/**
 * Thin fetch wrapper for the local Express + SQLite API.
 * Auth is a bearer token kept in localStorage.
 */
const TOKEN_KEY = "puja-token";

export const getToken = () => localStorage.getItem(TOKEN_KEY);
export const setToken = (t: string | null) =>
  t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY);

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
  const token = getToken();
  await acquire();
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method,
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
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
  const token = getToken();
  await acquire();
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method: "POST",
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
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
