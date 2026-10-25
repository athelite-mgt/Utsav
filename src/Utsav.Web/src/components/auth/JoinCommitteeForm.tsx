import { useState, type FormEvent } from "react";
import { KeyRound, Send } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

/**
 * The two ways onto a committee: ask by its short name (an admin approves from
 * Settings > Access Requests), or redeem a one-time invite code issued for this
 * account's email. Shared by the page an account with no committee lands on and
 * by the dialog a committee member opens to join another one.
 */
export function JoinCommitteeForm({ onJoined }: { onJoined?: () => void }) {
  const { requestAccess, redeemInvite } = useAuth();
  const [slug, setSlug] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);
  const [code, setCode] = useState("");
  const [codeBusy, setCodeBusy] = useState(false);
  const [codeError, setCodeError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const wanted = slug.trim();
    if (!wanted) return;
    setBusy(true);
    setError(null);
    setSent(null);
    const result = await requestAccess(wanted);
    setBusy(false);
    if (result.error) {
      setError(result.error);
    } else {
      setSent(wanted);
      setSlug("");
    }
  }

  async function redeem(e: FormEvent) {
    e.preventDefault();
    if (!code.trim()) return;
    setCodeBusy(true);
    setCodeError(null);
    const result = await redeemInvite(code.trim());
    setCodeBusy(false);
    if (result.error) setCodeError(result.error);
    else onJoined?.();
  }

  return (
    <>
      <form onSubmit={submit} className="flex gap-2 text-left">
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
      {sent && (
        <p className="mt-2 text-left text-sm text-muted-foreground">
          Request sent to <span className="font-medium text-foreground">{sent}</span>. Its admin can approve it
          from Settings &gt; Access Requests.
        </p>
      )}

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
    </>
  );
}
