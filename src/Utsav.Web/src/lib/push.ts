import { api } from "@/lib/api";

/**
 * Web Push — subscribe/unsubscribe against the browser's own push service,
 * authenticated with this server's VAPID keypair. No third-party
 * notification vendor sits behind any of this.
 */

export function isPushSupported(): boolean {
  return typeof window !== "undefined" && "serviceWorker" in navigator && "PushManager" in window;
}

/** iOS Safari only implements Web Push once the app is added to the Home Screen (16.4+). */
export function needsInstallForPush(): boolean {
  if (typeof navigator === "undefined") return false;
  const ios =
    /iphone|ipad|ipod/i.test(navigator.userAgent) ||
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  const standalone =
    window.matchMedia("(display-mode: standalone)").matches ||
    (navigator as Navigator & { standalone?: boolean }).standalone === true;
  return ios && !standalone;
}

/** The VAPID public key arrives base64url-encoded; subscribe() needs it as raw bytes. */
function urlBase64ToUint8Array(base64: string): Uint8Array<ArrayBuffer> {
  const padding = "=".repeat((4 - (base64.length % 4)) % 4);
  const safe = (base64 + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = atob(safe);
  // Allocated explicitly (not via .from()) so TS sees a plain ArrayBuffer
  // backing it, not the wider ArrayBufferLike that subscribe() rejects.
  const bytes = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
  return bytes;
}

export async function getExistingSubscription(): Promise<PushSubscription | null> {
  if (!isPushSupported()) return null;
  const registration = await navigator.serviceWorker.ready;
  return registration.pushManager.getSubscription();
}

export async function subscribeToPush(): Promise<void> {
  // Wire format is snake_case, like every other endpoint's JSON (see
  // Program.cs's global SnakeCaseLower policy) — this one was written
  // expecting camelCase and silently read undefined forever, so the
  // "not set up yet" error fired even once the server had a real key.
  const { public_key: publicKey } = await api.get<{ public_key: string | null }>("/push/vapid-public-key");
  if (!publicKey) {
    throw new Error("Push notifications aren't set up on the server yet.");
  }

  // Must follow directly from the user's own click — browsers refuse a
  // permission prompt that isn't triggered by a gesture.
  const permission = await Notification.requestPermission();
  if (permission !== "granted") {
    throw new Error(permission === "denied" ? "Notifications were blocked." : "Notifications weren't enabled.");
  }

  const registration = await navigator.serviceWorker.ready;
  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(publicKey),
  });

  // toJSON() is exactly {endpoint, keys: {p256dh, auth}} — the shape
  // PushEndpoints.SubscribeAsync expects, no reshaping needed.
  await api.post("/push/subscribe", subscription.toJSON());
}

/**
 * Registers the browser's existing subscription with the committee the user is
 * currently in. A device's registration belongs to one committee, so a member
 * of two needs it refreshed after switching or they would only hear about the
 * committee they happened to enable notifications in. A no-op unless the user
 * has already granted permission and the browser already holds a subscription.
 */
export async function syncPushRegistration(): Promise<void> {
  if (!isPushSupported() || typeof Notification === "undefined" || Notification.permission !== "granted") return;
  const subscription = await getExistingSubscription();
  if (!subscription) return;
  await api.post("/push/subscribe", subscription.toJSON());
}

export async function unsubscribeFromPush(): Promise<void> {
  const subscription = await getExistingSubscription();
  if (!subscription) return;
  await api.post("/push/unsubscribe", { endpoint: subscription.endpoint });
  await subscription.unsubscribe();
}

export function sendTestPush(): Promise<{ sent: number; removed: number }> {
  return api.post<{ sent: number; removed: number }>("/push/test");
}

/** Admin-only broadcast — every active member gets it, Viewer included (unlike a transaction update). */
export function sendAnnouncement(title: string, body: string, url?: string): Promise<{ ok: boolean }> {
  return api.post<{ ok: boolean }>("/push/announce", { title, body, url });
}
