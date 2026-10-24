/**
 * What a page's "Projected" summary card should show, for the income streams
 * that have no rollover formula of their own (Advertisements, Other Incomes).
 * Membership and Special Donation don't need this — their `projected` field
 * is already populated for every row from the moment the year is created
 * (see AdminEndpoints.RolloverAsync), so summing it directly is enough.
 *
 * Ads and Other Incomes start each year with nothing carried over, so until
 * someone types a Projected figure into a booking, the honest "expected"
 * number is what was actually billed last year, not zero.
 */
export interface ProjectionResult {
  value: number;
  /** True when nothing has been projected yet this year and this is last year's billed total instead. */
  fromPriorYear: boolean;
}

export function projectedWithFallback(
  currentRows: Array<{ projected?: number | null }>,
  priorRows: Array<{ billed?: number | null }>
): ProjectionResult {
  const currentTotal = currentRows.reduce((s, r) => s + (Number(r.projected) || 0), 0);
  if (currentTotal > 0) {
    return { value: currentTotal, fromPriorYear: false };
  }

  const priorBilled = priorRows.reduce((s, r) => s + (Number(r.billed) || 0), 0);
  return { value: priorBilled, fromPriorYear: priorBilled > 0 };
}
