import { useState, type FormEvent } from "react";
import { Hourglass, KeyRound, LogOut, RefreshCw, Send } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { BrandMark } from "@/components/layout/NavContent";

/**
 * Shown after a successful sign-in that doesn't yet have an active committee
 * membership: either a request awaiting approval (status 'pending') or no
 * request at all (status 'none'). Signing in never grants access by itself; an
 * admin has to approve from Settings > Access Requests.
 *
 * There is no default committee, so someone with no request yet names the one
 * they want to join, by its short name (slug).
 */
export function PendingApproval() {
  const { user, memberships, isPlatformAdmin, signOut, refreshSession, requestAccess, redeemInvite } = useAuth();
  const [slug, setSlug] = useState("");
  const [code, setCode] = useState("");
  const [codeBusy, setCodeBusy] = useState(false);
  const [codeError, setCodeError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const pending = memberships.filter((m) => m.status === "pending");

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!slug.trim()) return;
    setBusy(true);
    setError(null);
    const result = await requestAccess(slug.trim());
    setBusy(false);
    if (result.error) setError(result.error);
    else setSlug("");
  }

  async function redeem(e: FormEvent) {
    e.preventDefault();
    if (!code.trim()) return;
    setCodeBusy(true);
    setCodeError(null);
    const result = await redeemInvite(code.trim());
    setCodeBusy(false);
    if (result.error) setCodeError(result.error);
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4 py-10">
      <div className="w-full max-w-md rounded-2xl border border-border bg-card p-6 text-center shadow-card sm:p-8">
        <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-gold/10 text-gold">
          <Hourglass className="h-7 w-7" />
        </div>
        <h1 className="font-display text-xl font-bold">
          {pending.length > 0 ? "Waiting for approval" : "Join a committee"}
        </h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Signed in as <span className="font-medium text-foreground">{user?.email}</span>.{" "}
          {pending.length > 0 ? (
            <>
              Your request to join{" "}
              <span className="font-medium text-foreground">{pending.map((m) => m.name).join(", ")}</span> is
              waiting. A committee admin needs to approve it from Settings &gt; Access Requests.
            </>
          ) : (
            <>Enter your committee's short name and its admin will be asked to approve you.</>
          )}
        </p>

        <form onSubmit={submit} className="mt-5 flex gap-2 text-left">
          <Input
            value={slug}
            onChange={(e) => setSlug(e.target.value)}
            placeholder="Committee short name, e.g. barrackpore-kali"
            aria-label="Committee short name"
            autoCapitalize="none"
            autoCorrect="off"
          />
          <Button type="submit" disabled={busy || !slug.trim()}>
            <Send /> Request
          </Button>
        </form>
        {error && <p className="mt-2 text-left text-sm text-destructive">{error}</p>}

        <div className="mt-6 border-t border-border pt-5 text-left">
          <p className="text-sm font-medium">Have an invite code?</p>
          <p className="mt-1 text-xs text-muted-foreground">
            A new committee's first administrator gets a one-time code. Use it while signed in with the
            email address it was issued for.
          </p>
          <form onSubmit={redeem} className="mt-3 flex gap-2">
            <Input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="Invite code"
              aria-label="Invite code"
              autoCapitalize="characters"
              autoCorrect="off"
              spellCheck={false}
              className="font-mono"
            />
            <Button type="submit" variant="secondary" disabled={codeBusy || !code.trim()}>
              <KeyRound /> Use code
            </Button>
          </form>
          {codeError && <p className="mt-2 text-sm text-destructive">{codeError}</p>}
        </div>

        <div className="mt-5 flex flex-col gap-2 sm:flex-row">
          <Button variant="outline" className="flex-1" onClick={() => refreshSession()}>
            <RefreshCw /> Check again
          </Button>
          <Button variant="ghost" className="flex-1" onClick={() => signOut()}>
            <LogOut /> Sign out
          </Button>
        </div>
        {isPlatformAdmin && (
          <p className="mt-4 text-xs text-muted-foreground">
            You're a platform operator. <a className="underline" href="/platform">Open the platform console</a>.
          </p>
        )}
        <div className="mt-6 flex justify-center opacity-70">
          <BrandMark compact />
        </div>
      </div>
    </div>
  );
}
