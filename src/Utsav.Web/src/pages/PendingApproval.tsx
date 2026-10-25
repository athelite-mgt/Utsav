import { Hourglass, LogOut, RefreshCw } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { JoinCommitteeForm } from "@/components/auth/JoinCommitteeForm";
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
  const { user, memberships, isPlatformAdmin, signOut, refreshSession } = useAuth();
  const pending = memberships.filter((m) => m.status === "pending");

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

        <div className="mt-5">
          <JoinCommitteeForm />
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
