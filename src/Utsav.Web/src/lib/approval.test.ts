import { describe, expect, it } from "vitest";
import { approvedCollected, canEditOwnClaim, feastEnabled, isApproved, isOpen, statusLabel } from "./approval";

describe("approval helpers", () => {
  it("counts only the confirmed part of a bill's collected amount", () => {
    expect(approvedCollected({ collected: 500, pending_collected: 200 })).toBe(300);
    expect(approvedCollected({ collected: 500 })).toBe(500);
    expect(approvedCollected({ collected: 0, pending_collected: 0 })).toBe(0);
  });

  it("treats a row with no status as approved", () => {
    expect(isApproved({})).toBe(true);
    expect(isApproved({ status: "pending" })).toBe(false);
    expect(isApproved({ status: "rejected" })).toBe(false);
  });

  it("knows which statuses await a decision", () => {
    expect(isOpen("pending")).toBe(true);
    expect(isOpen("sent_back")).toBe(true);
    expect(isOpen("approved")).toBe(false);
    expect(isOpen("rejected")).toBe(false);
  });

  it("lets a member change only their own open claim", () => {
    expect(canEditOwnClaim({ status: "pending", recorded_by_user_id: "u1" }, "u1")).toBe(true);
    expect(canEditOwnClaim({ status: "sent_back", recorded_by_user_id: "u1" }, "u1")).toBe(true);
    expect(canEditOwnClaim({ status: "approved", recorded_by_user_id: "u1" }, "u1")).toBe(false);
    expect(canEditOwnClaim({ status: "pending", recorded_by_user_id: "u2" }, "u1")).toBe(false);
    expect(canEditOwnClaim({ status: "pending", recorded_by_user_id: "u1" }, null)).toBe(false);
  });

  it("shows Feast unless it was switched off", () => {
    expect(feastEnabled({ feast_enabled: true })).toBe(true);
    expect(feastEnabled({})).toBe(true);
    expect(feastEnabled(null)).toBe(true);
    expect(feastEnabled({ feast_enabled: false })).toBe(false);
  });

  it("has a label for every status", () => {
    expect(statusLabel("pending")).toBe("Awaiting approval");
    expect(statusLabel("sent_back")).toBe("Sent back");
  });
});
