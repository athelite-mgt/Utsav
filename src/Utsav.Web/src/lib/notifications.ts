import type { NotificationItem } from "@/types/db";

/**
 * "Unread" is tracked per-device in localStorage rather than server-side:
 * a committee member reads notifications on their own phone, and a shared
 * server-side read flag would mark them read for everyone the moment one
 * person opened the page. The stored value is the timestamp of the newest
 * notification the user has seen; anything newer counts as unread.
 */
const KEY = "utsav-notifications-seen";

export function getLastSeen(): number {
  const raw = localStorage.getItem(KEY);
  const n = raw ? Date.parse(raw) : 0;
  return Number.isNaN(n) ? 0 : n;
}

export function markAllSeen(items: NotificationItem[]): void {
  const newest = items.reduce((max, n) => Math.max(max, Date.parse(n.created_at) || 0), 0);
  if (newest > 0) localStorage.setItem(KEY, new Date(newest).toISOString());
}

export function unreadCount(items: NotificationItem[]): number {
  const seen = getLastSeen();
  return items.filter((n) => (Date.parse(n.created_at) || 0) > seen).length;
}
