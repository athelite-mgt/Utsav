import { Hourglass, LogOut, RefreshCw } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { BrandMark } from "@/components/layout/NavContent";

/**
 * Shown after a successful sign-in whose committee membership isn't
 * 'active' yet — either a brand-new request (status 'pending') or,
 * defensively, no request at all (status 'none'). Signing in never grants
 * access by itself; an admin has to approve from Settings > Access Requests.
 */
export function PendingApproval() {
  const { user, signOut, refreshSession } = useAuth();

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4 py-10">
      <div className="w-full max-w-md rounded-2xl border border-border bg-card p-6 text-center shadow-card sm:p-8">
        <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-gold/10 text-gold">
          <Hourglass className="h-7 w-7" />
        </div>
        <h1 className="font-display text-xl font-bold">Waiting for approval</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Signed in as <span className="font-medium text-foreground">{user?.email}</span>. A committee admin
          needs to approve your access before you can see anything — they'll do this from
          Settings &gt; Access Requests.
        </p>
        <div className="mt-5 flex flex-col gap-2 sm:flex-row">
          <Button variant="outline" className="flex-1" onClick={() => refreshSession()}>
            <RefreshCw /> Check again
          </Button>
          <Button variant="ghost" className="flex-1" onClick={() => signOut()}>
            <LogOut /> Sign out
          </Button>
        </div>
        <div className="mt-6 flex justify-center opacity-70">
          <BrandMark compact />
        </div>
      </div>
    </div>
  );
}
