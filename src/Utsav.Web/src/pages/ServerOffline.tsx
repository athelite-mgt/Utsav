import { ServerCrash, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";

export function ServerOffline() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4 py-10">
      <div className="w-full max-w-md rounded-2xl border border-border bg-card p-6 text-center shadow-card sm:p-8">
        <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-destructive/10 text-destructive">
          <ServerCrash className="h-7 w-7" />
        </div>
        <h1 className="font-display text-xl font-bold">Can't reach the API</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          The local server on port 5174 isn't responding. Start both the app and the
          API together with:
        </p>
        <pre className="mt-4 overflow-x-auto rounded-lg bg-muted px-4 py-3 text-left font-mono text-sm">
          npm run dev
        </pre>
        <p className="mt-3 text-xs text-muted-foreground">
          First time? Run <span className="font-mono text-foreground">npm run seed</span> once
          to build the database from your Excel workbook.
        </p>
        <Button className="mt-5 w-full" onClick={() => window.location.reload()}>
          <RefreshCw /> Retry
        </Button>
      </div>
    </div>
  );
}
