import { useState, type CSSProperties } from "react";
import { toast } from "sonner";
import { ArrowRight, Eye, EyeOff, Loader2, ShieldCheck } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { AppLogoTile, PoweredByAthelite } from "@/components/brand/Athelite";
import { GoogleSignInButton } from "@/components/auth/GoogleSignInButton";
import { MicrosoftSignInButton } from "@/components/auth/MicrosoftSignInButton";
import { InstallPrompt } from "@/components/common/InstallPrompt";

const googleEnabled = Boolean(import.meta.env.VITE_GOOGLE_CLIENT_ID);
const microsoftEnabled = Boolean(
  import.meta.env.VITE_ENTRA_CLIENT_ID && import.meta.env.VITE_ENTRA_TENANT_ID
);
const ssoEnabled = googleEnabled || microsoftEnabled;

/** Staggered entrance — each block lands just after the one above it. */
const rise = (delayMs: number): CSSProperties => ({ animationDelay: `${delayMs}ms` });

type Mode = "in" | "up";

export function Login() {
  const { signIn, signUp } = useAuth();
  const [mode, setMode] = useState<Mode>("in");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  // Held while the Google exchange is in flight so the email form can't be
  // used to fire a second, competing sign-in.
  const [googleBusy, setGoogleBusy] = useState(false);
  const locked = busy || googleBusy;
  const signingUp = mode === "up";

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (locked) return;
    setBusy(true);
    const { error } = signingUp
      ? await signUp(email, password, name)
      : await signIn(email, password);
    setBusy(false);
    if (error) return toast.error(error);
    if (signingUp) toast.success("Account created — request sent to the committee admin");
  }

  return (
    <div className="relative flex min-h-[100dvh] flex-col overflow-hidden bg-background">
      {/* Ambient wash. Two very faint, very slow blooms of the accent colour —
          enough to stop a plain white screen feeling flat, quiet enough that
          nothing competes with the form. Purely decorative. */}
      <div aria-hidden className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute -left-32 -top-40 h-[28rem] w-[28rem] animate-drift rounded-full bg-primary/[0.07] blur-3xl" />
        <div className="absolute -bottom-48 -right-32 h-[32rem] w-[32rem] animate-drift-slow rounded-full bg-gold/[0.09] blur-3xl" />
      </div>

      <main className="relative flex flex-1 flex-col items-center justify-center px-6 py-10 pb-safe pt-safe">
        <div className="w-full max-w-[26rem]">
          {/* Masthead */}
          <header className="animate-rise text-center" style={rise(0)}>
            <AppLogoTile className="mx-auto h-16 w-16 shadow-card" />
            <h1 className="mt-6 font-display text-[1.75rem] font-bold leading-tight tracking-tight sm:text-3xl">
              {signingUp ? "Join your committee" : "Utsav"}
            </h1>
            <p className="mx-auto mt-2 max-w-[22rem] text-[15px] leading-relaxed text-muted-foreground">
              {signingUp
                ? "Create an account and an admin will approve your access."
                : "Collections, expenses and budget for your puja committee."}
            </p>
          </header>

          {/* Segmented control */}
          <div
            className="animate-rise mt-8 grid grid-cols-2 gap-1 rounded-2xl bg-muted p-1"
            style={rise(70)}
            role="tablist"
            aria-label="Sign in or create an account"
          >
            {([["in", "Sign in"], ["up", "Create account"]] as const).map(([value, label]) => (
              <button
                key={value}
                type="button"
                role="tab"
                aria-selected={mode === value}
                onClick={() => setMode(value)}
                className={cn(
                  "rounded-xl py-2.5 text-sm font-medium transition-all duration-200",
                  mode === value
                    ? "bg-card text-foreground shadow-soft"
                    : "text-muted-foreground hover:text-foreground"
                )}
              >
                {label}
              </button>
            ))}
          </div>

          {/* Single sign-on. Google first: it is how almost every existing
              member already signs in. */}
          {ssoEnabled && (
            <div className="animate-rise mt-5" style={rise(130)}>
              {googleEnabled && <GoogleSignInButton onPendingChange={setGoogleBusy} />}
              {microsoftEnabled && (
                <div className={googleEnabled ? "mt-3" : undefined}>
                  <MicrosoftSignInButton onPendingChange={setGoogleBusy} />
                </div>
              )}
              <div className="my-5 flex items-center gap-3">
                <div className="h-px flex-1 bg-border" />
                <span className="text-xs text-muted-foreground">or with email</span>
                <div className="h-px flex-1 bg-border" />
              </div>
            </div>
          )}

          {/* Email form. One form for both modes — swapping fields in place
              keeps the layout from jumping, and the browser keeps whatever
              the user already typed. */}
          <form onSubmit={onSubmit} className="animate-rise space-y-3.5" style={rise(190)}>
            {signingUp && (
              <div className="space-y-1.5">
                <Label htmlFor="login-name">Full name</Label>
                <Input
                  id="login-name" required autoComplete="name" value={name} disabled={locked}
                  onChange={(e) => setName(e.target.value)} placeholder="Your name"
                  className="h-12 rounded-xl px-4 text-base"
                />
              </div>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="login-email">Email</Label>
              <Input
                id="login-email" type="email" required autoComplete="email"
                inputMode="email" autoCapitalize="none" spellCheck={false}
                value={email} disabled={locked}
                onChange={(e) => setEmail(e.target.value)} placeholder="you@example.com"
                className="h-12 rounded-xl px-4 text-base"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="login-password">Password</Label>
              <div className="relative">
                <Input
                  id="login-password"
                  type={showPassword ? "text" : "password"}
                  required
                  minLength={signingUp ? 6 : undefined}
                  autoComplete={signingUp ? "new-password" : "current-password"}
                  value={password} disabled={locked}
                  onChange={(e) => setPassword(e.target.value)} placeholder="••••••••"
                  className="h-12 rounded-xl px-4 pr-12 text-base"
                />
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? "Hide password" : "Show password"}
                  className="absolute right-1 top-1 flex h-10 w-10 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:text-foreground"
                >
                  {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
              {signingUp && <p className="text-xs text-muted-foreground">At least 6 characters.</p>}
            </div>

            <Button
              type="submit"
              disabled={locked}
              className="h-12 w-full rounded-xl text-base font-semibold transition-transform active:scale-[0.98]"
            >
              {busy ? <Loader2 className="animate-spin" /> : null}
              {busy
                ? (signingUp ? "Creating account…" : "Signing in…")
                : (signingUp ? "Create account" : "Sign in")}
              {!busy && <ArrowRight className="h-4 w-4" />}
            </Button>
          </form>

          <p
            className="animate-rise mt-5 flex items-start justify-center gap-1.5 text-center text-xs leading-relaxed text-muted-foreground"
            style={rise(250)}
          >
            <ShieldCheck className="mt-px h-3.5 w-3.5 shrink-0" />
            <span>
              {signingUp
                ? "A committee admin approves every new member before any figures are visible."
                : "Access is approved per member — nothing is public."}
            </span>
          </p>

          <PoweredByAthelite className="animate-rise mt-10" style={rise(320)} />
        </div>
      </main>

      {/* Same install banner AppShell shows once signed in — a first-time
          visitor should be able to install before they've even logged in,
          not only after. */}
      <InstallPrompt />
    </div>
  );
}
