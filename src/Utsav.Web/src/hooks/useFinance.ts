import { useQuery } from "@tanstack/react-query";
import { api, qs } from "@/lib/api";
import type {
  AdBookingWithAdvertiser, AdvanceWithBalance, AuditReport, BudgetVsActual, CashLedger,
  CollectionPaymentRow, ContractWithStatus, DateCollectionRow, DocumentFile, Expense, FeastExpense, FeastWithDonor, FundBreakdown, FundPosition, FundTransfer, MyAdvanceSummary,
  ModeCollectionRow, NotificationDetail, NotificationItem, Offering, PujaCalendarDay, PujaScheduleItem, StreamCollectionRow, SpecialDonation, SubscriptionCategory, SubscriptionWithDonor, Vendor,
  VendorIssue, WorkflowItem, WorkflowNote, YearCollectionPayment, ZoneCollectionRow,
} from "@/types/db";

/** One hook per resource — all scoped to the active puja year. */
function yearScoped<T>(key: string, path: string, yearId?: string | null) {
  return {
    queryKey: [key, yearId],
    enabled: Boolean(yearId),
    queryFn: () => api.get<T>(`${path}${qs({ year_id: yearId })}`),
  };
}

/**
 * Omitting `category` returns both sections — what the dashboard totals want.
 * The category is part of the query key so Membership and Special Donation
 * cache separately and a row moving between them refetches both.
 */
export const useSubscriptions = (yearId?: string | null, category?: SubscriptionCategory) =>
  useQuery({
    queryKey: ["subscriptions", yearId, category ?? null],
    enabled: Boolean(yearId),
    queryFn: () =>
      api.get<SubscriptionWithDonor[]>(`/subscriptions${qs({ year_id: yearId, category })}`),
  });

export const useFeasts = (yearId?: string | null) =>
  useQuery(yearScoped<FeastWithDonor[]>("feasts", "/feasts", yearId));

export const useAdBookings = (yearId?: string | null) =>
  useQuery(yearScoped<AdBookingWithAdvertiser[]>("ad_bookings", "/ad_bookings", yearId));

export const useSpecialDonations = (yearId?: string | null) =>
  useQuery(yearScoped<SpecialDonation[]>("special_donations", "/special_donations", yearId));

export const useOfferings = (yearId?: string | null) =>
  useQuery(yearScoped<Offering[]>("offerings", "/offerings", yearId));

export const useExpenses = (yearId?: string | null) =>
  useQuery(yearScoped<Expense[]>("expenses", "/expenses", yearId));

export const useFeastExpenses = (yearId?: string | null) =>
  useQuery(yearScoped<FeastExpense[]>("feast_expenses", "/feast_expenses", yearId));

export const useBudget = (yearId?: string | null) =>
  useQuery(yearScoped<BudgetVsActual[]>("budget_lines", "/budget", yearId));

export const useCashLedger = (yearId?: string | null) =>
  useQuery(yearScoped<CashLedger[]>("cash_ledger", "/cash_ledger", yearId));

export const useFundTransfers = (yearId?: string | null) =>
  useQuery(yearScoped<FundTransfer[]>("fund_transfers", "/fund_transfers", yearId));

/** Where the money physically is — cash box vs bank/online. Computed server-side. */
export const useFundPosition = (yearId?: string | null) =>
  useQuery(yearScoped<FundPosition>("fund_position", "/fund_position", yearId));

/** Who the cash-in-hand / bank balances came from — fetched only once the breakdown dialog is actually opened. */
export const useFundPositionBreakdown = (yearId?: string | null, enabled = true) =>
  useQuery({
    queryKey: ["fund_position_breakdown", yearId],
    enabled: Boolean(yearId) && enabled,
    queryFn: () => api.get<FundBreakdown>(`/fund_position/breakdown${qs({ year_id: yearId })}`),
  });

export const useContracts = (yearId?: string | null) =>
  useQuery(yearScoped<ContractWithStatus[]>("contracts", "/contracts", yearId));

export const useAdvances = (yearId?: string | null) =>
  useQuery(yearScoped<AdvanceWithBalance[]>("advances", "/advances", yearId));

export const useMyAdvances = (yearId?: string | null) =>
  useQuery(yearScoped<MyAdvanceSummary | null>("my_advances", "/my/advances", yearId));

export const usePujaSchedule = (yearId?: string | null) =>
  useQuery(yearScoped<PujaScheduleItem[]>("puja_schedule", "/puja_schedule", yearId));

/** Festival day→date map for the year (Mahalaya … Dasami). */
export const usePujaCalendar = (yearId?: string | null) =>
  useQuery(yearScoped<PujaCalendarDay[]>("puja_calendar_days", "/puja_calendar_days", yearId));

export const useWorkflowItems = (yearId?: string | null) =>
  useQuery(yearScoped<WorkflowItem[]>("workflow_items", "/workflow_items", yearId));

export const useWorkflowNotes = (itemId?: string | null) =>
  useQuery({
    queryKey: ["workflow_notes", itemId],
    enabled: Boolean(itemId),
    queryFn: () => api.get<WorkflowNote[]>(`/workflow_items/${itemId}/notes`),
  });

// Vendors are org-scoped, not year-scoped — no year_id filter.
export const useVendors = () =>
  useQuery({ queryKey: ["vendors"], queryFn: () => api.get<Vendor[]>("/vendors") });

/**
 * The issue log — org-wide like vendors/contracts themselves, not year-scoped
 * (a vendor's track record matters across seasons). Omit both filters for the
 * full list the Vendors page's Issues tab wants; pass one to narrow to a
 * single vendor or contract.
 */
export const useVendorIssues = (vendorId?: string | null, contractId?: string | null) =>
  useQuery({
    queryKey: ["vendor_issues", vendorId ?? null, contractId ?? null],
    queryFn: () => api.get<VendorIssue[]>(`/vendor_issues${qs({ vendor_id: vendorId, contract_id: contractId })}`),
  });

// Documents can be year-scoped or standing (year_id null on the row) — the
// API includes both when a year is given, so no yearScoped() here.
export const useDocuments = (yearId?: string | null) =>
  useQuery({
    queryKey: ["documents", yearId ?? null],
    queryFn: () => api.get<DocumentFile[]>(`/documents${qs({ year_id: yearId })}`),
  });

// Committee-wide notification history — not year-scoped. Polled so the
// top-bar bell picks up new activity without a manual refresh.
export const useNotifications = () =>
  useQuery({
    queryKey: ["notifications"],
    queryFn: () => api.get<NotificationItem[]>("/notifications"),
    refetchInterval: 60_000,
  });

/** Who/when/page/record and the field-level diff behind one notification — fetched only once it's opened. */
export const useNotificationDetail = (id?: string | null, enabled = true) =>
  useQuery({
    queryKey: ["notification_detail", id],
    enabled: Boolean(id) && enabled,
    queryFn: () => api.get<NotificationDetail>(`/notifications/${id}/detail`),
  });

/** The dated part-payments behind one collection record. Only fetched when editing an existing record. */
export const useCollectionPayments = (parentType: string, parentId?: string | null) =>
  useQuery({
    queryKey: ["collection_payments", parentType, parentId],
    enabled: Boolean(parentId),
    queryFn: () =>
      api.get<CollectionPaymentRow[]>(`/collection_payments${qs({ parent_type: parentType, parent_id: parentId })}`),
  });

/**
 * Year's collections rolled up by income stream (membership / special / ad /
 * other), honouring an optional paid-on date range — the correct source for
 * a date-filtered "collected" figure, since a subscription/booking's own
 * `collected_on` only ever carries its *latest* instalment's date and would
 * mis-bucket any earlier one.
 */
export const useCollectionsByStream = (yearId?: string | null, from?: string, to?: string) =>
  useQuery({
    queryKey: ["collections_by_stream", yearId, from ?? null, to ?? null],
    enabled: Boolean(yearId),
    queryFn: () =>
      api.get<StreamCollectionRow[]>(`/collection_payments/by_stream${qs({ year_id: yearId, from: from || undefined, to: to || undefined })}`),
  });

/** Same date-range correctness as {@link useCollectionsByStream}, grouped by the donor's zone instead — feeds Zone-wise Collection. */
export const useCollectionsByZone = (yearId?: string | null, from?: string, to?: string) =>
  useQuery({
    queryKey: ["collections_by_zone", yearId, from ?? null, to ?? null],
    enabled: Boolean(yearId),
    queryFn: () =>
      api.get<ZoneCollectionRow[]>(`/collection_payments/by_zone${qs({ year_id: yearId, from: from || undefined, to: to || undefined })}`),
  });

/** Same date-range correctness as {@link useCollectionsByStream}, grouped by calendar day instead — feeds the Collection by Date report. */
export const useCollectionsByDate = (yearId?: string | null, from?: string, to?: string) =>
  useQuery({
    queryKey: ["collections_by_date", yearId, from ?? null, to ?? null],
    enabled: Boolean(yearId),
    queryFn: () =>
      api.get<DateCollectionRow[]>(`/collection_payments/by_date${qs({ year_id: yearId, from: from || undefined, to: to || undefined })}`),
  });

/** Same date-range correctness as {@link useCollectionsByStream}, grouped by payment mode instead — feeds the Collection by Type report. */
export const useCollectionsByMode = (yearId?: string | null, from?: string, to?: string) =>
  useQuery({
    queryKey: ["collections_by_mode", yearId, from ?? null, to ?? null],
    enabled: Boolean(yearId),
    queryFn: () =>
      api.get<ModeCollectionRow[]>(`/collection_payments/by_mode${qs({ year_id: yearId, from: from || undefined, to: to || undefined })}`),
  });

/**
 * Every dated instalment for a year, optionally narrowed to one parent_type —
 * lets a list page check "did this record have *any* payment in a date
 * range" instead of trusting the parent's single latest-instalment date.
 * See {@link useCollectionsByStream} for why that single date is unsafe.
 */
export const useCollectionPaymentsForYear = (yearId?: string | null, parentType?: string) =>
  useQuery({
    queryKey: ["collection_payments_for_year", yearId, parentType ?? null],
    enabled: Boolean(yearId),
    queryFn: () =>
      api.get<YearCollectionPayment[]>(`/collection_payments/for_year${qs({ year_id: yearId, parent_type: parentType })}`),
  });

/** The full CA / society year-end audit statement set, computed server-side. */
export const useAuditReport = (yearId?: string | null) =>
  useQuery(yearScoped<AuditReport>("audit_report", "/reports/audit", yearId));

// Small aggregate helper used across list pages / dashboard
export const sum = <T,>(rows: T[], pick: (r: T) => number | null | undefined) =>
  rows.reduce((acc, r) => acc + (Number(pick(r)) || 0), 0);
