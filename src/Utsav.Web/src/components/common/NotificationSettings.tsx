import { useEffect, useState } from "react";
import { Bell, BellOff, BellRing } from "lucide-react";
import { toast } from "sonner";
import {
  getExistingSubscription, isPushSupported, needsInstallForPush,
  sendTestPush, subscribeToPush, unsubscribeFromPush,
} from "@/lib/push";
import { Button } from "@/components/ui/button";

type State = "checking" | "unsupported" | "needs-install" | "blocked" | "off" | "on";

const COPY: Record<State, string> = {
  checking: "",
  unsupported: "This browser doesn't support push notifications.",
  "needs-install": "Add Utsav to your home screen first — iOS only allows notifications for installed apps.",
  blocked: "Blocked in this browser's site settings — turn it back on there to re-enable.",
  off: "Get notified when a collection, expense, advance or contract changes.",
  on: "You'll be notified about transaction updates on this device.",
};

/** Lives in Settings, next to InstallSettingsRow — a per-device opt-in, so it can't live anywhere that isn't visited on the device itself. */
export function NotificationSettingsRow() {
  const [state, setState] = useState<State>("checking");
  const [busy, setBusy] = useState(false);

  async function refresh() {
    if (!isPushSupported()) {
      setState(needsInstallForPush() ? "needs-install" : "unsupported");
      return;
    }
    if (typeof Notification !== "undefined" && Notification.permission === "denied") {
      setState("blocked");
      return;
    }
    const subscription = await getExistingSubscription();
    setState(subscription ? "on" : "off");
  }

  useEffect(() => { refresh(); }, []);

  async function toggle() {
    setBusy(true);
    try {
      if (state === "on") {
        await unsubscribeFromPush();
        toast.success("Notifications off");
      } else {
        await subscribeToPush();
        toast.success("Notifications on");
      }
      await refresh();
    } catch (e) {
      toast.error((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function test() {
    setBusy(true);
    try {
      const { sent } = await sendTestPush();
      if (sent > 0) toast.success("Test sent — check this device");
      else toast.error("Couldn't reach this device — try turning notifications off and on again");
    } catch (e) {
      toast.error((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  if (state === "checking") {
    return null;
  }

  const canToggle = state === "on" || state === "off";

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border p-4">
      <div className="flex min-w-0 items-center gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
          {state === "on" ? <BellRing className="h-5 w-5" /> : <Bell className="h-5 w-5" />}
        </span>
        <div className="min-w-0">
          <p className="font-medium">Push notifications</p>
          <p className="text-sm text-muted-foreground">{COPY[state]}</p>
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-2">
        {state === "on" && (
          <Button variant="ghost" size="sm" disabled={busy} onClick={test}>Send test</Button>
        )}
        {canToggle && (
          <Button variant="outline" disabled={busy} onClick={toggle}>
            {state === "on" ? <><BellOff className="h-4 w-4" /> Turn off</> : <><Bell className="h-4 w-4" /> Turn on</>}
          </Button>
        )}
      </div>
    </div>
  );
}
