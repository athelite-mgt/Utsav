import { useState } from "react";
import { Download, Share, Plus, X, Check, Smartphone } from "lucide-react";
import { toast } from "sonner";
import {
  dismissInstall, promptInstall, resetInstallDismissal, useInstallState,
} from "@/lib/pwa";
import { AppLogoTile } from "@/components/brand/Athelite";
import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";

/** The Share ▸ Add to Home Screen walkthrough — the only route iOS allows. */
function IosSteps({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const steps = [
    { icon: Share, text: "Tap the Share button in Safari's toolbar" },
    { icon: Plus, text: 'Scroll down and choose "Add to Home Screen"' },
    { icon: Check, text: 'Tap "Add" — Puja Finance appears on your home screen' },
  ];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Add to your home screen</DialogTitle>
          <DialogDescription>
            Three taps in Safari and it opens like a normal app — full screen, no address bar.
          </DialogDescription>
        </DialogHeader>
        <ol className="space-y-3">
          {steps.map((s, i) => (
            <li key={s.text} className="flex items-start gap-3">
              <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                <s.icon className="h-4 w-4" />
              </span>
              <span className="pt-1.5 text-sm">
                <span className="mr-1.5 font-semibold">{i + 1}.</span>{s.text}
              </span>
            </li>
          ))}
        </ol>
        <DialogFooter>
          <Button onClick={() => onOpenChange(false)} className="w-full">Got it</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Invitation to install, shown once the browser says installing is actually
 * possible. Deliberately a dismissible card rather than a modal — nobody
 * should have to dismiss a dialog to reach their accounts.
 */
export function InstallPrompt() {
  const { shouldOffer, canPrompt, needsManualSteps } = useInstallState();
  const [showSteps, setShowSteps] = useState(false);

  if (!shouldOffer) return null;

  async function install() {
    if (canPrompt) {
      const accepted = await promptInstall();
      if (accepted) toast.success("Installing — look for Puja Finance on your home screen");
      else dismissInstall();
      return;
    }
    setShowSteps(true);
  }

  return (
    <>
      {/* Sits clear of the mobile bottom nav — which is ~81px tall once the
          home-indicator safe area is counted, so 7rem leaves a real gap
          rather than resting on it. Hidden on desktop, where "install" isn't
          what anyone is there to do. */}
      <div className="fixed inset-x-0 bottom-28 z-40 px-3 lg:hidden">
        <div className="animate-rise mx-auto flex max-w-md items-center gap-3 rounded-2xl border border-border bg-card p-3 shadow-lg">
          <AppLogoTile className="h-11 w-11 shrink-0" />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold leading-tight">Install Puja Finance</p>
            <p className="mt-0.5 text-xs leading-snug text-muted-foreground">
              {needsManualSteps
                ? "Add it to your home screen for one-tap access."
                : "One tap from your home screen, works offline."}
            </p>
          </div>
          <Button size="sm" className="shrink-0" onClick={install}>
            <Download className="h-4 w-4" /> Install
          </Button>
          <button
            type="button"
            onClick={dismissInstall}
            aria-label="Not now"
            className="-mr-1 shrink-0 rounded-lg p-1.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>

      <IosSteps open={showSteps} onOpenChange={setShowSteps} />
    </>
  );
}

/**
 * The permanent entry point, for anyone who dismissed the card or is on a
 * desktop wanting it on their phone. Lives in Settings.
 */
export function InstallSettingsRow() {
  const { standalone, canPrompt, needsManualSteps } = useInstallState();
  const [showSteps, setShowSteps] = useState(false);
  const possible = canPrompt || needsManualSteps;

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border p-4">
        <div className="flex min-w-0 items-center gap-3">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Smartphone className="h-5 w-5" />
          </span>
          <div className="min-w-0">
            <p className="font-medium">Install on your phone</p>
            <p className="text-sm text-muted-foreground">
              {standalone
                ? "Already installed — you're using the app version."
                : possible
                  ? "Add Puja Finance to your home screen for one-tap access."
                  : "Open this page in Chrome or Safari on your phone to install it."}
            </p>
          </div>
        </div>
        {!standalone && (
          <Button
            variant="outline"
            disabled={!possible}
            onClick={async () => {
              resetInstallDismissal();
              if (canPrompt) {
                const accepted = await promptInstall();
                if (accepted) toast.success("Installing — look for Puja Finance on your home screen");
              } else {
                setShowSteps(true);
              }
            }}
          >
            <Download className="h-4 w-4" /> Install
          </Button>
        )}
      </div>

      <IosSteps open={showSteps} onOpenChange={setShowSteps} />
    </>
  );
}
