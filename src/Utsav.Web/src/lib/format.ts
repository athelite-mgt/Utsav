import { format, parseISO, isValid } from "date-fns";

const inr = new Intl.NumberFormat("en-IN", {
  style: "currency",
  currency: "INR",
  maximumFractionDigits: 0,
});
const inrPaise = new Intl.NumberFormat("en-IN", {
  style: "currency",
  currency: "INR",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** ₹ 1,23,456 — Indian grouping, no paise by default. */
export function money(v: number | null | undefined, paise = false): string {
  const n = Number(v ?? 0);
  if (Number.isNaN(n)) return "₹ 0";
  return (paise ? inrPaise : inr).format(n).replace("₹", "₹ ");
}

/** Compact for tight KPI displays: ₹1.2L, ₹12.5k */
export function moneyCompact(v: number | null | undefined): string {
  const n = Number(v ?? 0);
  if (Math.abs(n) >= 1e7) return `₹${(n / 1e7).toFixed(2)}Cr`;
  if (Math.abs(n) >= 1e5) return `₹${(n / 1e5).toFixed(2)}L`;
  if (Math.abs(n) >= 1e3) return `₹${(n / 1e3).toFixed(1)}k`;
  return `₹${n}`;
}

export function numberIN(v: number | null | undefined): string {
  return new Intl.NumberFormat("en-IN").format(Number(v ?? 0));
}

export function pct(part: number, whole: number): number {
  if (!whole) return 0;
  return Math.round((part / whole) * 100);
}

export function fmtDate(d: string | null | undefined, f = "dd MMM yyyy"): string {
  if (!d) return "—";
  const parsed = typeof d === "string" ? parseISO(d) : d;
  return isValid(parsed) ? format(parsed, f) : "—";
}

export function initials(name?: string | null): string {
  if (!name) return "?";
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? "")
    .join("");
}
