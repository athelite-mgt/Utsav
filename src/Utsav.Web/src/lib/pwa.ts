import { useSyncExternalStore } from "react";

/**
 * Install-to-home-screen support.
 *
 * Chrome fires `beforeinstallprompt` once, early — often before React has
 * mounted — and the event is only usable if you hold on to it. So it's
 * captured here at module load and published to components through a tiny
 * store, rather than in a hook that might mount too late to hear it.
 *
 * iOS Safari implements none of this: there's no event and no programmatic
 * install, only the Share ▸ Add to Home Screen gesture. So iOS gets the same
 * card with instructions instead of a button that can't work.
 */

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

const DISMISS_KEY = "puja-install-dismissed";
/** How long a dismissal is respected before the card may reappear. */
const DISMISS_DAYS = 21;

let deferred: BeforeInstallPromptEvent | null = null;
let installed = false;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());

if (typeof window !== "undefined") {
  window.addEventListener("beforeinstallprompt", (e) => {
    e.preventDefault(); // suppress Chrome's own mini-infobar; we show our own card
    deferred = e as BeforeInstallPromptEvent;
    emit();
  });
  window.addEventListener("appinstalled", () => {
    deferred = null;
    installed = true;
    try { localStorage.removeItem(DISMISS_KEY); } catch { /* private mode */ }
    emit();
  });
}

/** Already running as an installed app — nothing left to offer. */
export function isStandalone(): boolean {
  if (typeof window === "undefined") return false;
  return (
    window.matchMedia("(display-mode: standalone)").matches ||
    // iOS Safari's non-standard flag, the only signal available there.
    (navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

export function isIos(): boolean {
  if (typeof navigator === "undefined") return false;
  return (
    /iphone|ipad|ipod/i.test(navigator.userAgent) ||
    // iPadOS 13+ reports itself as a Mac; the touch points give it away.
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1)
  );
}

/** Firefox and in-app browsers can't install at all — don't nag them. */
export function canInstallOnThisBrowser(): boolean {
  return Boolean(deferred) || isIos();
}

function dismissedRecently(): boolean {
  try {
    const at = Number(localStorage.getItem(DISMISS_KEY));
    if (!at) return false;
    return Date.now() - at < DISMISS_DAYS * 864e5;
  } catch {
    return false;
  }
}

export function dismissInstall() {
  try { localStorage.setItem(DISMISS_KEY, String(Date.now())); } catch { /* private mode */ }
  emit();
}

/** Clears the dismissal so the card comes back — used by Settings. */
export function resetInstallDismissal() {
  try { localStorage.removeItem(DISMISS_KEY); } catch { /* private mode */ }
  emit();
}

/** Shows Chrome's install dialog. Returns false where that isn't possible. */
export async function promptInstall(): Promise<boolean> {
  if (!deferred) return false;
  const evt = deferred;
  deferred = null; // a captured prompt is single-use
  emit();
  await evt.prompt();
  const { outcome } = await evt.userChoice;
  return outcome === "accepted";
}

const subscribe = (fn: () => void) => {
  listeners.add(fn);
  return () => { listeners.delete(fn); };
};

export interface InstallState {
  /** Chrome handed us a usable prompt. */
  canPrompt: boolean;
  /** iOS — instructions rather than a button. */
  needsManualSteps: boolean;
  /** Running as an installed app already. */
  standalone: boolean;
  /** Whether the invitation card should be shown unprompted. */
  shouldOffer: boolean;
}

let snapshot: InstallState = buildState();

function buildState(): InstallState {
  const standalone = installed || isStandalone();
  const canPrompt = Boolean(deferred);
  const needsManualSteps = !standalone && !canPrompt && isIos();
  return {
    canPrompt,
    needsManualSteps,
    standalone,
    shouldOffer: !standalone && (canPrompt || needsManualSteps) && !dismissedRecently(),
  };
}

// useSyncExternalStore compares snapshots by identity, so a new object every
// read would loop forever — rebuild only when something actually differs.
function getSnapshot(): InstallState {
  const next = buildState();
  const changed = (Object.keys(next) as (keyof InstallState)[]).some((k) => next[k] !== snapshot[k]);
  if (changed) snapshot = next;
  return snapshot;
}

export function useInstallState(): InstallState {
  return useSyncExternalStore(subscribe, getSnapshot, () => snapshot);
}
