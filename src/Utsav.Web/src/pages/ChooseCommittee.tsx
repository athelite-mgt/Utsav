import { useState } from "react";
import { LogOut } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { BrandMark } from "@/components/layout/NavContent";
import { CommitteeAvatar } from "@/components/brand/CommitteeAvatar";

/**
 * Shown to a user who belongs to more than one committee and has not yet
 * picked which one they are working in. A user with a single committee never
 * sees this: the session is pinned for them at sign-in.
 */
export function ChooseCommittee() {
  const { user, memberships, switchCommittee, signOut } = useAuth();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const active = memberships.filter((m) => m.status === "active");
  const pending = memberships.filter((m) => m.status === "pending");

  async function choose(orgId: string) {
    setBusy(orgId);
    setError(null);
    const result = await switchCommittee(orgId);
    if (result.error) {
      setError(result.error);
      setBusy(null);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4 py-10">
      <div className="w-full max-w-md rounded-2xl border border-border bg-card p-6 shadow-card sm:p-8">
        <h1 className="font-display text-xl font-bold">Choose a committee</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Signed in as <span className="font-medium text-foreground">{user?.email}</span>. You belong to more
          than one — pick the one you're working in. You can switch any time from the sidebar.
        </p>

        {error && <p className="mt-3 text-sm text-destructive">{error}</p>}

        <ul className="mt-5 space-y-2">
          {active.map((m) => (
            <li key={m.org_id}>
              <button
                type="button"
                onClick={() => choose(m.org_id)}
                disabled={busy !== null}
                className="flex w-full items-center gap-3 rounded-xl border border-border bg-background p-3 text-left transition-colors hover:bg-accent disabled:opacity-60"
              >
                <CommitteeAvatar name={m.name} logoUrl={m.logo_url} accentColour={m.accent_colour} />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-semibold">{m.name}</span>
                  <span className="block text-xs capitalize text-muted-foreground">{m.role}</span>
                </span>
                {busy === m.org_id && <span className="text-xs text-muted-foreground">Opening…</span>}
              </button>
            </li>
          ))}
        </ul>

        {pending.length > 0 && (
          <p className="mt-4 text-xs text-muted-foreground">
            Awaiting approval at {pending.map((m) => m.name).join(", ")}.
          </p>
        )}

        <div className="mt-5 flex items-center justify-between">
          <BrandMark compact />
          <Button variant="ghost" size="sm" onClick={() => signOut()}>
            <LogOut /> Sign out
          </Button>
        </div>
      </div>
    </div>
  );
}
