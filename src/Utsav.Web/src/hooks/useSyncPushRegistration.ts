import { useEffect } from "react";
import { syncPushRegistration } from "@/lib/push";

/**
 * Re-registers this device's existing push subscription with the active
 * committee. Never prompts: it only acts when notifications are already
 * granted and the browser already holds a subscription, so a user who has not
 * opted in is never nagged.
 */
export function useSyncPushRegistration(orgId: string | null) {
  useEffect(() => {
    if (!orgId) return;
    void syncPushRegistration().catch(() => {
      // Best effort: failing to refresh a registration must never surface as
      // an error on a page the user is trying to use.
    });
  }, [orgId]);
}
