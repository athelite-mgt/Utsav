import { fmtDate } from "@/lib/format";

/** Raw DB column → the label a person reading an audit entry would expect. Falls back to Title Case. */
const FIELD_LABELS: Record<string, string> = {
  zone_id: "Zone",
  collector_id: "Collector",
  assignee_collector_id: "Assigned To",
  owner_collector_id: "Owner",
  issued_to_collector_id: "Issued To",
  vendor_id: "Vendor",
  bill_no: "Bill No",
  billed: "Billed",
  billed_on: "Billed On",
  collected: "Collected",
  collected_on: "Collected On",
  projected: "Projected",
  prior_billed: "Last Year Billed",
  due: "Due",
  mode: "Payment Mode",
  amount: "Amount",
  spent_on: "Date",
  details: "Details",
  comment: "Comment",
  comments: "Comments",
  category: "Category",
  forecast: "Forecast",
  contract_amount: "Contract Amount",
  advance_paid: "Advance Paid",
  status: "Status",
  title: "Title",
  name: "Name",
  contact: "Contact",
  value: "Value",
  offering_type: "Offering",
  puja_day: "Puja Day",
  received_date: "Received Date",
  expected_date: "Expected Date",
  received_by: "Received By",
  issued_to_name: "Issued To",
  issued_on: "Issued On",
  purpose: "Purpose",
  role: "Role",
  notes: "Notes",
  start_date: "Start Date",
  end_date: "End Date",
  ad_type: "Ad Type",
};

export function fieldLabel(key: string): string {
  return FIELD_LABELS[key] ?? key.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

/** "2026-08-27T09:14:22.123+00:00" → "27 Aug 2026, 2:44 PM" in local time. */
export function fmtStamp(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return `${fmtDate(iso)}, ${d.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })}`;
}

/** Money-ish and date-ish values read better trimmed; everything else prints as-is. */
export function fmtValue(v: unknown): string {
  if (v === null || v === undefined || v === "") return "—";
  if (typeof v === "boolean") return v ? "Yes" : "No";
  if (typeof v === "object") return JSON.stringify(v);
  const s = String(v);
  // A bare ISO date (yyyy-mm-dd or full timestamp) → friendly date.
  if (/^\d{4}-\d{2}-\d{2}([T ]|$)/.test(s)) {
    const nice = fmtDate(s);
    if (nice && nice !== s) return nice;
  }
  return s;
}
