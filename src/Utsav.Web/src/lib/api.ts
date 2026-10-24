/**
 * Thin fetch wrapper for the API.
 *
 * Auth is an httpOnly cookie the server sets: there is deliberately no token
 * here for any script to read, store or leak, so a cross-site-scripting bug
 * cannot be turned into a stolen session. What the page does hold, in memory
 * only, is the CSRF token the server hands back on sign-in and on /auth/me.
 * Cookies are sent automatically, which is exactly why every state-changing
 * request has to prove it came from this page by echoing that token in a
 * header another site cannot add.
 */
let csrfToken: string | null = null;

export const setCsrfToken = (token: string | null) => {
  csrfToken = token;
};

const CSRF_HEADER = "X-Utsav-Csrf";

/** Headers for any request the app makes itself, including the ones that do not go through request(). */
export function authHeaders(method = "GET"): Record<string, string> {
  const safe = method === "GET" || method === "HEAD" || method === "OPTIONS";
  return !safe && csrfToken ? { [CSRF_HEADER]: csrfToken } : {};
}

export class ApiError extends Error {
  status: number;
  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}

/**
 * Cap how many API calls are in flight at once.
 *
 * Opening the dashboard kicks off ~10 queries simultaneously. On Netlify
 * each one can land on its own cold function instance, and each instance
 * wants its own database connection — against a Supabase pooler that allows
 * 15 clients in session mode across the entire deployment and refuses,
 * rather than queues, past that. The result was a couple of tiles failing
 * on almost every load.
 *
 * Four at a time keeps the whole page comfortably inside that ceiling.
 * Nothing is dropped, only staggered, and since each call is fast the page
 * still finishes in about the same time.
 *
 * (The deployment-side fix is Supabase's transaction-mode pooler on port
 * 6543, which is built for serverless and holds far more clients. This cap
 * is what makes the app well-behaved either way.)
 */
const MAX_IN_FLIGHT = 4;
let inFlight = 0;
const waiting: (() => void)[] = [];

function acquire(): Promise<void> {
  if (inFlight < MAX_IN_FLIGHT) {
    inFlight++;
    return Promise.resolve();
  }
  return new Promise((resolve) => waiting.push(resolve));
}

function release() {
  const next = waiting.shift();
  if (next) next(); // hands the slot straight over, so inFlight stays put
  else inFlight--;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  await acquire();
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...authHeaders(method),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } finally {
    release();
  }

  if (!res.ok) {
    let message = res.statusText;
    try {
      message = (await res.json())?.error ?? message;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(message, res.status);
  }
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
}

/** Build a query string, skipping null/undefined values. */
export function qs(params: Record<string, string | number | null | undefined>) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== null && v !== undefined && v !== "") p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : "";
}

/** Multipart upload — no JSON here, so it bypasses request()'s Content-Type/body handling entirely. */
async function upload<T>(path: string, form: FormData): Promise<T> {
  await acquire();
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method: "POST",
      headers: authHeaders("POST"),
      body: form,
    });
  } finally {
    release();
  }

  if (!res.ok) {
    let message = res.statusText;
    try {
      message = (await res.json())?.error ?? message;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(message, res.status);
  }
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
}

export const api = {
  get: <T,>(path: string) => request<T>("GET", path),
  post: <T,>(path: string, body?: unknown) => request<T>("POST", path, body),
  patch: <T,>(path: string, body?: unknown) => request<T>("PATCH", path, body),
  del: <T,>(path: string) => request<T>("DELETE", path),
  upload: <T,>(path: string, form: FormData) => upload<T>(path, form),
};
