// Row types mirroring server/schema.sql — the shapes the API returns.

export type AppRole = "admin" | "secretariat" | "finance" | "executive" | "viewer";

/** Mirrors Utsav.Domain/Authorization/Permission.cs — see lib/permissions.ts. */
export type Permission =
  | "read"
  | "manage_settings"
  | "manage_users"
  | "write_income"
  | "write_finance"
  | "write_workflow"
  | "write_advances"
  | "read_audit"
  | "use_asura";

/** One before→after change within an update, as /api/audit returns it. */
export interface AuditChange {
  field: string;
  from: unknown;
  to: unknown;
}

/**
 * One display-ready history row, as GET /api/audit returns it. This is the
 * shape AuditEndpoints.ToView builds — deliberately not the raw stored
 * AuditEntry (which carries raw column names and before/after dicts). Every
 * property is snake_case to match the app-wide JSON policy.
 */
export interface AuditRow {
  id: string;
  timestamp: string;
  /** Who did it — full name, else email, else "System" for entries with no actor. */
  who: string;
  role: AppRole | null;
  /** Friendly page name, e.g. "Expenses", "Membership / Special Donation". */
  page: string;
  action: "Created" | "Updated" | "Deleted" | string;
  /** The specific record touched — donor name, expense details, vendor title. Null if it couldn't be resolved. */
  record: string | null;
  outcome: "Success" | "Failure";
  error_message: string | null;
  request_method: string | null;
  request_path: string | null;
  ip_address: string | null;
  /** Field-by-field changes for updates; empty for create/delete. */
  changes: AuditChange[];
}

export interface AuditPage {
  items: AuditRow[];
  continuation_token: string | null;
}

/** One dated instalment of a collection. Mirrors CollectionEndpoints.ListAsync. */
export interface CollectionPaymentRow {
  id: string;
  amount: number;
  paid_on: string | null;
  mode: string | null;
  recorded_by_name: string | null;
}

/** Collections for a year rolled up by income stream, date-range aware. Mirrors CollectionEndpoints.ByStreamAsync. */
export interface StreamCollectionRow {
  stream: "membership" | "special" | "ad" | "other" | "feast";
  collected: number;
}

/** Collections for a year rolled up by donor zone, date-range aware. Mirrors CollectionEndpoints.ByZoneAsync. */
export interface ZoneCollectionRow {
  zone_name: string;
  collected: number;
  /** Distinct donors with a payment in this zone (in the given range). */
  donors: number;
}

/** Collections for a year rolled up by calendar day + stream. Mirrors CollectionEndpoints.ByDateAsync. */
export interface DateCollectionRow {
  paid_on: string;
  stream: "membership" | "special" | "ad" | "other" | "feast";
  collected: number;
}

/** Collections for a year rolled up by payment mode + stream. Mirrors CollectionEndpoints.ByModeAsync. */
export interface ModeCollectionRow {
  mode: string;
  stream: "membership" | "special" | "ad" | "other" | "feast";
  collected: number;
}

/** One raw dated instalment for a year (optionally narrowed to a parent_type). Mirrors CollectionEndpoints.ForYearAsync. */
export interface YearCollectionPayment {
  parent_id: string;
  amount: number;
  paid_on: string | null;
  mode: string | null;
}

/** One entry on the Notifications page. Mirrors PushEndpoints.ListNotificationsAsync. */
export interface NotificationItem {
  id: string;
  title: string;
  body: string;
  /** In-app path this notification points at, e.g. "/expenses". */
  url: string;
  kind: "transaction" | "announcement" | string;
  actor_name: string | null;
  created_at: string;
}

/**
 * The field-level detail behind one notification — who/when/page/record and
 * what changed, correlated to the audit entry that caused it. Mirrors
 * PushEndpoints.GetDetailAsync; `found: false` for an announcement, a
 * notification with no recorded actor, or when nothing in the trail matched —
 * the page just falls back to the notification's own title/body then.
 */
export type NotificationDetail =
  | { found: false }
  | { found: true; entry: AuditRow };

// ---------------- CA / society audit report ----------------
/** One labelled money line in an audit statement. */
export interface AuditLine {
  label: string;
  amount: number;
}
export interface AuditBalance {
  cash: number;
  bank: number;
  total: number;
}
/** The full year-end statement set. Mirrors AuditReportEndpoint.AuditReportDto. */
export interface AuditReport {
  org_name: string;
  year_label: string;
  year: number;
  opening: AuditBalance;
  closing: AuditBalance;
  receipts_payments: {
    receipts: AuditLine[];
    payments: AuditLine[];
    total_receipts: number;
    total_payments: number;
  };
  income_expenditure: {
    income: AuditLine[];
    expenditure: AuditLine[];
    total_income: number;
    total_expenditure: number;
    surplus: number;
  };
  balance_sheet: {
    assets: AuditLine[];
    funds: AuditLine[];
    total_assets: number;
    total_funds: number;
  };
  notes: string[];
  annexures: {
    membership: IncomeAnnexureRow[];
    special: IncomeAnnexureRow[];
    ads: IncomeAnnexureRow[];
    other: IncomeAnnexureRow[];
    expenses: ExpenseAnnexureRow[];
  };
}

/** One donor/advertiser income row on a Membership/Special/Ads/Other annexure. Mirrors AuditReportEndpoint.IncomeAnnexureRow. */
export interface IncomeAnnexureRow {
  name: string;
  zone: string | null;
  bill_no: string | null;
  billed: number;
  billed_on: string | null;
  collected: number;
  collected_on: string | null;
  due: number;
  mode: string | null;
}

/**
 * One expense row on the Expenses annexure. Mirrors
 * AuditReportEndpoint.ExpenseAnnexureRow. `bill_no` is labelled "Voucher" on
 * screen/PDF/CSV — same data, display name only.
 */
export interface ExpenseAnnexureRow {
  category: string;
  details: string | null;
  bill_no: string | null;
  amount: number;
  spent_on: string | null;
  mode: string | null;
}

/** Where a signed-in user's committee access stands. See auth.mjs requestAccess(). */
export type AccessStatus = "active" | "pending" | "none";

export interface User {
  id: string;
  email: string;
  full_name: string | null;
  role: AppRole | null;
  created_at?: string;
}

export interface Organization {
  id: string;
  name: string;
  slug: string;
  city: string | null;
  /** Public URL of the committee's logo, or null to fall back to an initial. */
  logo_url: string | null;
  /** Brand colour as a #rgb / #rrggbb hex literal, or null for the default theme. */
  accent_colour: string | null;
}

/** A committee as the platform operator's directory lists it: never its books. */
export interface PlatformCommittee {
  id: string;
  name: string;
  slug: string;
  city: string | null;
  is_active: boolean;
  created_at: string;
  members: number;
  pending_invite: boolean;
}

/** What creating a committee returns. The invite code is shown once and never again. */
export interface CreatedCommittee {
  id: string;
  slug: string;
  invite_code: string;
  invite_expires_at: string;
}

/** One committee the signed-in user belongs to, as returned by /auth/me. */
export interface Membership {
  org_id: string;
  name: string;
  slug: string;
  logo_url: string | null;
  accent_colour: string | null;
  role: AppRole;
  status: AccessStatus;
}

/** A membership awaiting admin approval — see Settings > Access Requests. */
export interface AccessRequest {
  member_id: string;
  user_id: string;
  email: string;
  full_name: string | null;
  auth_provider: string;
  created_at: string;
}

export interface PujaYear {
  id: string;
  year: number;
  name: string | null;
  target: number;
  projection_pct: number;
  /** Cash in hand carried into this year from last year's close. */
  opening_cash_balance: number;
  /** Bank / online balance carried into this year from last year's close. */
  opening_bank_balance: number;
  is_active: boolean;
  notes: string | null;
  created_at: string;
}

export interface Zone {
  id: string;
  name: string;
  sort_order: number;
}

export interface Collector {
  id: string;
  name: string;
  phone: string | null;
  user_id: string | null;
  active: boolean;
  sort_order: number;
}

export type LookupCategory =
  | "payment_mode"
  | "expense_category"
  | "feast_expense_category"
  | "ad_type"
  | "ad_status"
  | "offering_type"
  | "puja_day"
  | "donation_purpose"
  | "vendor_category"
  | "vendor_issue_type";

export interface Lookup {
  id: string;
  category: LookupCategory;
  label: string;
  sort_order: number;
  active: boolean;
}

export interface Donor {
  id: string;
  name: string;
  surname: string | null;
  contact: string | null;
  zone_id: string | null;
  notes: string | null;
  created_at: string;
}

/**
 * Which section of the subscriptions table a row belongs to. Mirrors
 * Utsav.Infrastructure/Data/Entities/SubscriptionCategory.cs and the
 * CK_subscriptions_category check constraint.
 */
export type SubscriptionCategory = "membership" | "special";

export interface Subscription {
  id: string;
  donor_id: string;
  year_id: string;
  category: SubscriptionCategory;
  prior_billed: number;
  projected: number;
  bill_no: string | null;
  not_eligible: boolean;
  billed: number;
  billed_on: string | null;
  collected: number;
  due: number;
  mode: string | null;
  collected_on: string | null;
  collector_id: string | null;
  comments: string | null;
  history: Record<string, unknown> | null;
  created_at: string;
  updated_at: string;
}

export interface SubscriptionWithDonor extends Subscription {
  donor: Donor | null;
  collector: Collector | null;
  /** Read-only carry-over from the preceding puja year — see GET /api/subscriptions. */
  prior_year: number | null;
  prior_bill_no: string | null;
  prior_year_billed: number | null;
  prior_year_collected: number | null;
}

/**
 * Feast collection — donor-linked billing/instalments, same mechanics as
 * Subscription but its own table (see Utsav.Infrastructure/Data/Entities/Feast.cs)
 * and no prior-year carry-over: feast collection starts fresh, with no
 * rollover/raise concept to track.
 */
export interface Feast {
  id: string;
  donor_id: string;
  year_id: string;
  bill_no: string | null;
  not_eligible: boolean;
  billed: number;
  billed_on: string | null;
  collected: number;
  due: number;
  mode: string | null;
  collected_on: string | null;
  collector_id: string | null;
  comments: string | null;
  created_at: string;
  updated_at: string;
}

export interface FeastWithDonor extends Feast {
  donor: Donor | null;
  collector: Collector | null;
}

export type FeastTokenStatus = "unassigned" | "assigned" | "redeemed" | "void";

/**
 * One printed, physical feast entry coupon — see
 * Utsav.Infrastructure/Data/Entities/FeastToken.cs. `donor_name`/`donor_surname`
 * come along on every list/detail response once a token is assigned (left
 * join through feasts → donors), so the UI never needs a second round trip
 * just to show whose token this is.
 */
export interface FeastToken {
  id: string;
  year_id: string;
  seq_no: number;
  code: string;
  feast_id: string | null;
  status: FeastTokenStatus;
  assigned_at: string | null;
  redeemed_at: string | null;
  redeemed_by_user_id: string | null;
  redeemed_by_name: string | null;
  created_at: string;
  donor_name: string | null;
  donor_surname: string | null;
  donor_contact?: string | null;
}

export interface FeastTokenSummary {
  unassigned: number;
  assigned: number;
  redeemed: number;
  voided: number;
  total: number;
}

export interface Advertiser {
  id: string;
  name: string;
  contact: string | null;
  notes: string | null;
  created_at: string;
}

export interface AdBooking {
  id: string;
  advertiser_id: string;
  year_id: string;
  ad_type: string | null;
  projected: number;
  bill_no: string | null;
  billed: number;
  billed_on: string | null;
  collected: number;
  due: number;
  mode: string | null;
  status: string | null;
  collected_on: string | null;
  comments: string | null;
  created_at: string;
  updated_at: string;
}

export interface AdBookingWithAdvertiser extends AdBooking {
  advertiser: Advertiser | null;
}

export interface SpecialDonation {
  id: string;
  year_id: string;
  name: string;
  contact: string | null;
  bill_no: string | null;
  projected: number;
  billed: number;
  collected: number;
  due: number;
  mode: string | null;
  collected_on: string | null;
  comments: string | null;
  created_at: string;
  updated_at: string;
}

export interface Offering {
  id: string;
  year_id: string;
  name: string;
  contact: string | null;
  offering_type: string | null;
  puja_day: string | null;
  value: number | null;
  details: string | null;
  expected_date: string | null;
  received_date: string | null;
  received_by: string | null;
  status: string;
  created_at: string;
}

export interface Expense {
  id: string;
  year_id: string;
  category: string | null;
  details: string | null;
  amount: number;
  spent_on: string | null;
  mode: string | null;
  comment: string | null;
  vendor_id: string | null;
  contract_id: string | null;
  advance_id: string | null;
  bill_no: string | null;
  vendor_name?: string | null;
  created_at: string;
}

/** Spending against the feast pool — same shape as Expense minus contract/advance linkage, kept in its own table (see FeastExpense.cs) so it never touches the general fund's Budget/Expenses figures. */
export interface FeastExpense {
  id: string;
  year_id: string;
  category: string | null;
  details: string | null;
  amount: number;
  spent_on: string | null;
  mode: string | null;
  comment: string | null;
  vendor_id: string | null;
  bill_no: string | null;
  vendor_name?: string | null;
  created_at: string;
}

export interface BudgetLine {
  id: string;
  year_id: string;
  category: string;
  notes: string | null;
  forecast: number;
  contract_amount: number;
  sort_order: number;
  created_at: string;
}

export interface BudgetVsActual extends BudgetLine {
  actual: number;
  remaining: number;
  /** Actual spend in this category during the org's previous puja year (0 if none). */
  prior_actual: number;
  /** Live total of every non-cancelled vendor contract in this category — from the Vendors & Contracts page, not typed in here. */
  contracted: number;
}

export interface CashLedger {
  id: string;
  year_id: string;
  party: string;
  amount: number;
  entry_date: string | null;
  note: string | null;
  created_at: string;
}

// ---------------- Cash / bank position ----------------
export type TransferDirection = "cash_to_bank" | "bank_to_cash";

export interface FundTransfer {
  id: string;
  year_id: string;
  direction: TransferDirection;
  amount: number;
  transfer_date: string | null;
  reference: string | null;
  note: string | null;
  created_at: string;
}

export interface FundSide {
  /** Carried forward from last year's close — part of the balance, not this year's income. */
  opening: number;
  received: number;
  spent: number;
  transfersIn: number;
  transfersOut: number;
  /** Cash handed out as advances and not yet settled — out of the box, not spent. */
  advancesOut: number;
  balance: number;
}

export interface RecentTransfer {
  direction: TransferDirection;
  amount: number;
  note: string | null;
  transfer_date: string | null;
}

export interface FundPosition {
  cash: FundSide;
  bank: FundSide;
  /** Newest first — bundled in so the dashboard needs only this one call. */
  recentTransfers: RecentTransfer[];
  total: { opening: number; received: number; spent: number; advancesOut: number; balance: number };
}

/** One payer's total on one side of the fund — the "tap the card to see who" breakdown. */
export interface FundBreakdownEntry {
  name: string;
  amount: number;
}

export interface FundBreakdown {
  cash: FundBreakdownEntry[];
  bank: FundBreakdownEntry[];
}

// ---------------- Vendors & contracts ----------------
export interface Vendor {
  id: string;
  name: string;
  contact: string | null;
  email: string | null;
  address: string | null;
  gst_no: string | null;
  category: string | null;
  active: boolean;
  notes: string | null;
  created_at: string;
}

export type ContractStatus = "draft" | "active" | "completed" | "cancelled";

export interface ContractWithStatus {
  id: string;
  year_id: string;
  vendor_id: string;
  vendor_name: string;
  title: string;
  category: string | null;
  contract_amount: number;
  start_date: string | null;
  end_date: string | null;
  status: ContractStatus;
  terms: string | null;
  notes: string | null;
  paid: number;
  scheduled: number;
  outstanding: number;
  created_at: string;
}

export interface ContractPayment {
  id: string;
  contract_id: string;
  milestone: string | null;
  amount: number;
  due_date: string | null;
  paid_date: string | null;
  expense_id: string | null;
  status: "pending" | "paid" | "cancelled";
  note: string | null;
  created_at: string;
}

export type VendorIssueStatus = "open" | "resolved";

/** A logged problem with a vendor, optionally tied to one of their contracts. Mirrors VendorIssueEndpoints. */
export interface VendorIssue {
  id: string;
  vendor_id: string;
  vendor_name: string;
  contract_id: string | null;
  contract_title: string | null;
  issue_type: string;
  description: string;
  occurred_on: string | null;
  status: VendorIssueStatus;
  created_at: string;
}

// ---------------- Cash advances ----------------
export type AdvanceStatus = "open" | "settled" | "cancelled";

export interface AdvanceWithBalance {
  id: string;
  year_id: string;
  issued_to_name: string;
  issued_to_collector_id: string | null;
  amount: number;
  issued_on: string | null;
  purpose: string | null;
  issued_by: string | null;
  mode: string | null;
  status: AdvanceStatus;
  notes: string | null;
  settled: number;
  spent: number;
  returned: number;
  outstanding: number;
  created_at: string;
}

export interface AdvanceSettlement {
  id: string;
  advance_id: string;
  kind: "expense" | "return";
  expense_id: string | null;
  amount: number;
  settled_on: string | null;
  note: string | null;
  created_at: string;
}

// ---------------- Puja-day duty roster ----------------
export interface PujaScheduleItem {
  id: string;
  year_id: string;
  puja_day: string;
  event_date: string | null;
  start_time: string | null;
  end_time: string | null;
  charge_title: string;
  assignee_name: string | null;
  assignee_collector_id: string | null;
  collector_name?: string | null;
  contact: string | null;
  notes: string | null;
  sort_order: number;
  created_at: string;
}

/** One festival day with its calendar date. Mirrors PujaCalendarEndpoints.ListAsync. */
export interface PujaCalendarDay {
  id: string;
  year_id: string;
  label: string;
  event_date: string | null;
  note: string | null;
  sort_order: number;
}

// ---------------- Workflow / task tracker ----------------
export type WorkflowStatus = "not_started" | "in_progress" | "blocked" | "done";

export interface WorkflowItem {
  id: string;
  year_id: string;
  title: string;
  category: string | null;
  owner_name: string | null;
  owner_collector_id: string | null;
  collector_name?: string | null;
  status: WorkflowStatus;
  due_date: string | null;
  completed_date: string | null;
  notes: string | null;
  sort_order: number;
  created_at: string;
  updated_at: string;
}

/** null when the signed-in user's login isn't linked to any collector. */
export interface MyAdvanceSummary {
  count: number;
  outstanding: number;
}

/** A timestamped, author-attributed entry in a workflow item's activity log. */
export interface WorkflowNote {
  id: string;
  workflow_item_id: string;
  author_id: string | null;
  author_name: string | null;
  note: string;
  created_at: string;
}

/** Puja permission / contract paperwork. Mirrors Utsav.Api.Endpoints.DocumentEndpoints.ListAsync's projection. */
export interface DocumentFile {
  id: string;
  title: string;
  category: string | null;
  notes: string | null;
  file_name: string;
  content_type: string;
  size_bytes: number;
  created_at: string;
  year_id: string | null;
  contract_id: string | null;
  contract_title: string | null;
  uploaded_by_name: string | null;
}

