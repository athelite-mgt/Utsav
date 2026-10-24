import { useEffect, useRef, useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { useAuth } from "@/context/AuthContext";

// Google Identity Services attaches itself to window; there's no official
// npm type package for the GSI script, so this is deliberately minimal —
// just the shape this component actually calls.
declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: {
            client_id: string;
            callback: (response: { credential: string }) => void;
          }) => void;
          renderButton: (parent: HTMLElement, options: Record<string, unknown>) => void;
        };
      };
    };
  }
}

const CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID;

/**
 * Renders nothing if no Client ID is configured — Google sign-in is opt-in
 * per deployment (see .env.example), never a hard requirement to run the app.
 *
 * `onPendingChange` lets the host page grey out the rest of its form while
 * the exchange is in flight.
 */
export function GoogleSignInButton({ onPendingChange }: { onPendingChange?: (v: boolean) => void }) {
  const { signInWithGoogle } = useAuth();
  const containerRef = useRef<HTMLDivElement>(null);
  const [pending, setPending] = useState(false);
  // Long enough that a first-request cold start (the serverless function has
  // to wake up and reach Postgres) stops looking like a hang.
  const [slow, setSlow] = useState(false);

  // GSI keeps whichever callback it was initialised with. Holding the live
  // one behind a ref means initialize/renderButton run exactly once, instead
  // of re-running — and re-rendering Google's iframe — on every render of
  // the auth context.
  const handlers = useRef({ signInWithGoogle, onPendingChange });
  handlers.current = { signInWithGoogle, onPendingChange };

  useEffect(() => {
    if (!CLIENT_ID) return;
    let cancelled = false;
    let interval: ReturnType<typeof setInterval> | undefined;

    function render() {
      if (cancelled || !containerRef.current || !window.google) return;
      window.google.accounts.id.initialize({
        client_id: CLIENT_ID!,
        callback: async (response) => {
          // From here until the API answers, nothing visible happens on
          // Google's side — the account chooser has already closed. Without
          // this the page just sits there and people click again.
          setPending(true);
          handlers.current.onPendingChange?.(true);
          const slowTimer = setTimeout(() => setSlow(true), 4000);
          try {
            const { error } = await handlers.current.signInWithGoogle(response.credential);
            if (error) toast.error(error);
          } finally {
            clearTimeout(slowTimer);
            // On success this component is already being unmounted (App
            // swaps Login out for the dashboard), so these only really
            // matter on the error path — where the button must come back.
            setSlow(false);
            setPending(false);
            handlers.current.onPendingChange?.(false);
          }
        },
      });
      // GSI renders the button at a fixed pixel width, not a responsive
      // one — a hardcoded width wider than the card (e.g. on narrow mobile
      // screens) makes it overflow/misalign instead of centering. Measure
      // the actual container instead, capped at Google's documented max.
      const width = Math.min(400, containerRef.current.offsetWidth || 336);
      window.google.accounts.id.renderButton(containerRef.current, {
        type: "standard",
        theme: "outline",
        size: "large",
        text: "continue_with",
        shape: "rectangular",
        width,
      });
    }

    if (window.google) {
      render();
    } else {
      // The GSI <script> in index.html loads async — poll briefly until
      // it's ready rather than depending on a load-event race.
      interval = setInterval(() => {
        if (window.google) {
          clearInterval(interval);
          render();
        }
      }, 100);
    }
    return () => { cancelled = true; if (interval) clearInterval(interval); };
  }, []);

  if (!CLIENT_ID) return null;

  return (
    <div className="relative">
      {/* No forced child width here — GSI sets its own inline width on the
          div it renders (matching the `width` option above) to keep the
          iframe inside correctly sized; overriding that with a CSS width
          fought Google's own centering and pushed the button off to one
          side. It stays mounted while pending so Google's iframe isn't
          torn down mid-flight; the overlay covers it instead. */}
      <div ref={containerRef} className="flex justify-center" />

      {pending && (
        <div
          className="absolute inset-0 flex flex-col items-center justify-center gap-1 rounded-lg bg-card/95"
          role="status"
          aria-live="polite"
        >
          <span className="flex items-center gap-2 text-sm font-medium">
            <Loader2 className="h-4 w-4 animate-spin text-primary" />
            Signing you in…
          </span>
          {slow && (
            <span className="text-xs text-muted-foreground">Waking the server, one moment</span>
          )}
        </div>
      )}
    </div>
  );
}
