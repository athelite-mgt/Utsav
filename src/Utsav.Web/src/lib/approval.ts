import type { ApprovalStatus, Organization } from "@/types/db";

/** Statuses that still await Finance (or the member's own fix). */
export const isOpen = (status?: ApprovalStatus | null): boolean => status === "pending" || status === "sent_back";

/** A row without a status predates approvals, so it counts as approved. */
export const isApproved = (row: { status?: ApprovalStatus | null }): boolean => (row.status ?? "approved") === "approved";

/**
 * What of a bill's collected amount has actually been confirmed. A bill shows
 * pending payments as collected so the donor is not chased twice, but the fund,
 * totals and reports count only the approved part.
 */
export const approvedCollected = (row: { collected: number; pending_collected?: number | null }): number =>
  (Number(row.collected) || 0) - (Number(row.pending_collected) || 0);

export const statusLabel = (status: ApprovalStatus): string => ({
  approved: "Approved",
  pending: "Awaiting approval",
  sent_back: "Sent back",
  rejected: "Rejected",
})[status];

/** Feast is on unless a committee has switched it off; absent (older payloads) means on. */
export const feastEnabled = (org: Pick<Organization, "feast_enabled"> | null | undefined): boolean =>
  org?.feast_enabled !== false;

/** Whether a member may still change or withdraw an expense: only their own, and only until it is approved. */
export const canEditOwnClaim = (
  row: { status?: ApprovalStatus | null; recorded_by_user_id?: string | null },
  userId?: string | null,
): boolean => isOpen(row.status) && !!userId && row.recorded_by_user_id === userId;
