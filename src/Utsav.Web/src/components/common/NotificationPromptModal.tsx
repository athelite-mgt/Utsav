import { useEffect, useState } from "react";
import { toast } from "sonner";
import { BellRing } from "lucide-react";
import { getExistingSubscription, isPushSupported, needsInstallForPush, subscribeToPush } from "@/lib/push";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog";

/**
 * Asks on every fresh app open, not just once — mounted in AppShell, which
 * remounts per page load/app open but not per in-app navigation. Deliberately
 * no permanent dismissal: Boss wants this to keep asking members who haven't
 * turned it on, rather than a one-shot InstallPrompt-style card. Skipped
 * entirely for states where a prompt can't help (already on, blocked at the
 * browser level, unsupported, or iOS not yet installed) — see refresh().
 */
export function NotificationPromptModal() {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      if (!isPushSupported() || needsInstallForPush()) return;
      if (typeof Notification !== "undefined" && Notification.permission === "denied") return;
      const subscription = await getExistingSubscription();
      if (!subscription) setOpen(true);
    })();
  }, []);

  async function turnOn() {
    setBusy(true);
    try {
      await subscribeToPush();
      toast.success("Notifications on");
      setOpen(false);
    } catch (e) {
      toast.error((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <BellRing className="h-6 w-6" />
          </span>
          <DialogTitle className="text-center">Stay in the loop</DialogTitle>
          <DialogDescription className="text-center">
            Get notified the moment a collection, expense, advance or contract changes — no need to keep checking.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="sm:flex-col sm:gap-2">
          <Button className="w-full" disabled={busy} onClick={turnOn}>
            {busy ? "Turning on…" : "Turn on notifications"}
          </Button>
          <Button variant="ghost" className="w-full" disabled={busy} onClick={() => setOpen(false)}>
            Not now
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
