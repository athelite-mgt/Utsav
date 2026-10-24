import { useEffect, useRef, useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";

const CLIENT_ID = import.meta.env.VITE_ENTRA_CLIENT_ID;
const TENANT_ID = import.meta.env.VITE_ENTRA_TENANT_ID;

/**
 * The Microsoft logo. Four squares, drawn rather than fetched, so the button
 * doesn't depend on a remote asset loading before it looks right.
 */
function MicrosoftLogo() {
  return (
    <svg viewBox="0 0 21 21" className="h-4 w-4" aria-hidden focusable="false">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}

/**
 * MSAL tracks "a popup/redirect is in flight" in sessionStorage under this
 * key — shared across every PublicClientApplication instance in the tab,
 * not per-instance. If a previous attempt is interrupted in a way that skips
 * MSAL's own cleanup (the popup blocked outright, the tab was backgrounded,
 * a second click opened a nested popup mid-flight), this flag is left set
 * and every future attempt fails immediately with interaction_in_progress —
 * not intermittently, but every single time, since sessionStorage survives
 * reloads within the tab. There's no public MSAL API to clear it; this is
 * the key MSAL itself reads and writes.
 */
const MSAL_INTERACTION_KEY = "msal.interaction.status";

function clearStuckInteractionFlag() {
  sessionStorage.removeItem(MSAL_INTERACTION_KEY);
}

/**
 * Sign in with a Microsoft account.
 *
 * MSAL is loaded on demand rather than at module scope: it is a sizeable
 * dependency that only matters the moment somebody actually presses this
 * button, and pulling it into the initial bundle would slow the first paint
 * of a login screen most people reach through Google instead.
 *
 * Renders nothing unless configured, matching GoogleSignInButton — a
 * committee that hasn't set up Entra simply doesn't see the option.
 */
export function MicrosoftSignInButton({ onPendingChange }: { onPendingChange?: (v: boolean) => void }) {
  const { signInWithMicrosoft } = useAuth();
  const [pending, setPending] = useState(false);
  // Held across clicks so a second press reuses the initialised instance
  // instead of building a fresh one.
  const msalRef = useRef<import("@azure/msal-browser").PublicClientApplication | null>(null);
  // A second, *synchronous* lock alongside the `pending` state. `pending` is
  // React state — it updates on the next render, not immediately — so two
  // clicks arriving in the same event-loop turn (a fast double-click, or the
  // browser occasionally double-firing a click event) can both read
  // `pending === false` and both call loginPopup, which is exactly what
  // produced block_nested_popups. A ref updates immediately, closing that
  // window.
  const lockRef = useRef(false);

  // A flag left over from a previous tab session (the popup and its opener
  // don't reliably share sessionStorage resolution, so an interrupted flow
  // can leave this set indefinitely) shouldn't cost a fresh page load its
  // first attempt. No interaction can genuinely be "in progress" the moment
  // this component mounts.
  useEffect(() => {
    clearStuckInteractionFlag();
  }, []);

  if (!CLIENT_ID || !TENANT_ID) return null;

  async function getMsal() {
    if (msalRef.current) return msalRef.current;
    const { PublicClientApplication } = await import("@azure/msal-browser");
    const instance = new PublicClientApplication({
      auth: {
        clientId: CLIENT_ID!,
        // Boss chose work/school *and* personal Microsoft accounts, so the
        // authority has to be one that accepts both. Pointing this at the
        // tenant GUID instead would turn away every outlook.com address.
        authority: "https://login.microsoftonline.com/common",
        redirectUri: window.location.origin,
      },
      cache: { cacheLocation: "sessionStorage" },
    });
    await instance.initialize();
    msalRef.current = instance;
    return instance;
  }

  async function attemptLogin(msal: import("@azure/msal-browser").PublicClientApplication) {
    // Popup rather than redirect: the login page holds unsaved form state,
    // and a full-page redirect would throw it away on the way back.
    const result = await msal.loginPopup({ scopes: ["openid", "profile", "email"] });
    if (!result.idToken) throw new Error("Microsoft returned no identity token");
    return result.idToken;
  }

  async function onClick() {
    if (lockRef.current) return;
    lockRef.current = true;
    setPending(true);
    onPendingChange?.(true);
    try {
      const msal = await getMsal();

      let idToken: string;
      try {
        idToken = await attemptLogin(msal);
      } catch (e) {
        const code = (e as { errorCode?: string })?.errorCode;
        // A genuinely stuck flag from an earlier interrupted attempt — clear
        // it and retry exactly once, rather than leaving the user stuck on
        // an error they have no way to act on themselves.
        if (code === "interaction_in_progress") {
          clearStuckInteractionFlag();
          idToken = await attemptLogin(msal);
        } else {
          throw e;
        }
      }

      const { error } = await signInWithMicrosoft(idToken);
      if (error) toast.error(error);
    } catch (e) {
      const code = (e as { errorCode?: string })?.errorCode;
      const message = e instanceof Error ? e.message : "Microsoft sign-in failed";
      // Closing the popup, or the nested-popup collision itself, is a
      // deliberate or self-correcting outcome — not worth shouting about.
      if (!/user_cancelled|popup_window_error|user_closed/i.test(message) && code !== "block_nested_popups") {
        toast.error(message);
      }
      // Whatever went wrong, don't leave the flag set for the next attempt.
      clearStuckInteractionFlag();
    } finally {
      lockRef.current = false;
      setPending(false);
      onPendingChange?.(false);
    }
  }

  return (
    <Button
      type="button"
      variant="outline"
      onClick={onClick}
      disabled={pending}
      className="h-11 w-full gap-2 rounded-lg text-sm font-medium"
    >
      {pending ? <Loader2 className="h-4 w-4 animate-spin" /> : <MicrosoftLogo />}
      {pending ? "Signing you in…" : "Continue with Microsoft"}
    </Button>
  );
}
