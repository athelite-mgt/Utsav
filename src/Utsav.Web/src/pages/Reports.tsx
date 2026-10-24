import { useMemo, useState } from "react";
import { BarChart3, Download, FileDown, MapPin, ReceiptText, PiggyBank, Scale, HandCoins, FileCheck2, ListX, AlertCircle, Wallet, CalendarDays, Wallet2 } from "lucide-react";
import type { ComponentType } from "react";
import { useYear } from "@/context/YearContext";
import {
  useSubscriptions, useFeasts, useAdBookings, useSpecialDonations, useExpenses, useBudget,
  useCollectionsByStream, useCollectionsByZone, useCollectionsByDate, useCollectionsByMode, sum,
} from "@/hooks/useFinance";
import { useZones } from "@/hooks/useReference";
import { money, moneyCompact, pct, fmtDate } from "@/lib/format";
import { exportCsv } from "@/lib/exportCsv";
import { exportPdfTable } from "@/lib/exportPdf";
import { cn } from "@/lib/utils";
import { PageHeader } from "@/components/common/PageHeader";
import { StatCard } from "@/components/common/StatCard";
import { CardsSkeleton } from "@/components/common/states";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { OptionSelect } from "@/components/form/OptionSelect";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { GroupedGradientBars, RankedGradientBars, DimensionalDonut, type Series } from "@/components/charts/report-charts";
import { AuditReport } from "./reports/AuditReport";

type ReportKey =
  | "collection" | "zone" | "by_date" | "by_mode" | "expense" | "budget" | "netposition" | "audit"
  | "not_billed" | "due_list" | "donations";

interface ReportDef {
  key: ReportKey;
  label: string;
  short: string;
  icon: ComponentType<{ className?: string }>;
  /** Which filter controls this report responds to. */
  filters: { dates?: boolean; zone?: boolean; stream?: boolean };
  blurb: string;
}

const REPORTS: ReportDef[] = [
  { key: "collection", label: "Collection Summary", short: "Collection", icon: HandCoins,
    filters: { dates: true }, blurb: "Projected, billed and collected across every income stream." },
  { key: "zone", label: "Zone-wise Collection", short: "Zones", icon: MapPin,
    filters: { dates: true, zone: true }, blurb: "Collection broken down by para / zone." },
  { key: "by_date", label: "Collection by Date", short: "By Date", icon: CalendarDays,
    filters: { dates: true }, blurb: "Every income stream, day by day — download as PDF." },
  { key: "by_mode", label: "Collection by Type", short: "By Type", icon: Wallet2,
    filters: { dates: true }, blurb: "Cash, UPI, cheque, bank transfer — how the money actually came in." },
  { key: "expense", label: "Expense by Category", short: "Expenses", icon: ReceiptText,
    filters: { dates: true }, blurb: "Where the money went, category by category." },
  { key: "budget", label: "Budget vs Actual", short: "Budget", icon: PiggyBank,
    filters: {}, blurb: "Planned forecast against what was actually spent." },
  { key: "netposition", label: "Income vs Expense", short: "Net", icon: Scale,
    filters: { dates: true }, blurb: "The whole season at a glance — in, out and in hand." },
  { key: "not_billed", label: "Not Billed List", short: "Not Billed", icon: ListX,
    filters: { stream: true }, blurb: "Membership / Special Donation donors with nothing billed yet — download as PDF." },
  { key: "due_list", label: "Due List", short: "Due", icon: AlertCircle,
    filters: { stream: true }, blurb: "Billed donors who still owe a balance — download as PDF." },
  { key: "donations", label: "Donations (excl. zero)", short: "Donations", icon: Wallet,
    filters: { stream: true }, blurb: "Every donor who's actually paid something — download as PDF, zero-collected rows left out." },
  { key: "audit", label: "Audit Report (for CA)", short: "Audit", icon: FileCheck2,
    filters: {}, blurb: "Receipts & Payments, Income & Expenditure and Balance Sheet — the year-end set for your CA / society registration." },
];

const STREAM_OPTIONS = [
  { value: "all", label: "Membership + Special Donation" },
  { value: "membership", label: "Membership only" },
  { value: "special", label: "Special Donation only" },
];

interface Kpi { label: string; value: string; tone?: "default" | "primary" | "gold" | "success" | "destructive"; sub?: string }
type Column = { key: string; label: string; align?: "right"; money?: boolean };
type ChartSpec =
  | { kind: "grouped"; data: Array<Record<string, number | string>>; series: Series[] }
  | { kind: "ranked"; data: Array<{ name: string; value: number }>; caption: string }
  | { kind: "donut"; data: Array<{ name: string; value: number }>; centerLabel: string; centerValue?: number };

interface ReportView {
  kpis: Kpi[];
  chart: ChartSpec;
  columns: Column[];
  rows: Array<Record<string, string | number>>;
}

export function ReportsPage() {
  const { activeYearId, activeYear } = useYear();
  const [report, setReport] = useState<ReportKey>("collection");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [zoneId, setZoneId] = useState<string | null>(null);
  // For the Not Billed / Due / Donations list reports.
  const [stream, setStream] = useState("all");

  const subs = useSubscriptions(activeYearId);
  const feasts = useFeasts(activeYearId);
  const ads = useAdBookings(activeYearId);
  const others = useSpecialDonations(activeYearId);
  const expenses = useExpenses(activeYearId);
  const budget = useBudget(activeYearId);
  // The date-filtered "collected" figure comes from the real per-instalment
  // ledger, not each row's own collected_on — see useCollectionsByStream's
  // doc comment for why (a subscription's collected_on only ever carries its
  // *latest* instalment's date, which silently mis-bucketed anything paid on
  // a different day once a date filter was applied).
  const byStream = useCollectionsByStream(activeYearId, from, to);
  const byZone = useCollectionsByZone(activeYearId, from, to);
  const byDate = useCollectionsByDate(activeYearId, from, to);
  const byMode = useCollectionsByMode(activeYearId, from, to);
  const { data: zones = [] } = useZones();

  const def = REPORTS.find((r) => r.key === report)!;
  const loading = subs.isLoading || feasts.isLoading || ads.isLoading || others.isLoading || expenses.isLoading || budget.isLoading;

  const view = useMemo<ReportView>(() => {
    // The audit report renders its own dedicated component (server-computed),
    // so the generic KPI/chart/table view below isn't built for it.
    const emptyView: ReportView = { kpis: [], chart: { kind: "ranked", data: [], caption: "" }, columns: [], rows: [] };
    if (report === "audit") return emptyView;
    // A date is "in range" when it sits within whichever bounds are set;
    // rows without the relevant date drop out once any bound is applied.
    const inRange = (d: string | null | undefined) => {
      if (!from && !to) return true;
      const on = d?.slice(0, 10);
      if (!on) return false;
      if (from && on < from) return false;
      if (to && on > to) return false;
      return true;
    };

    const allSubs = subs.data ?? [];
    const memberSubs = allSubs.filter((s) => s.category !== "special");
    const specialSubs = allSubs.filter((s) => s.category === "special");
    const adRows = ads.data ?? [];
    const otherRows = others.data ?? [];
    const feastRows = feasts.data ?? [];

    // Billed/projected/due are point-in-time stock figures, not events, so
    // they're never date-gated — only "collected" (an actual payment event)
    // is, and it comes from the per-instalment ledger below rather than each
    // row's own collected_on (which only ever holds its *latest* instalment's
    // date — see useCollectionsByStream/useCollectionsByZone).
    const collectedByStream = new Map<string, number>();
    for (const r of byStream.data ?? []) collectedByStream.set(r.stream, Number(r.collected) || 0);

    switch (report) {
      case "collection": {
        const streams = [
          { name: "Membership", key: "membership", rows: memberSubs },
          { name: "Special Donation", key: "special", rows: specialSubs },
          { name: "Feast", key: "feast", rows: feastRows },
          { name: "Advertisements", key: "ad", rows: adRows },
          { name: "Other Incomes", key: "other", rows: otherRows },
        ].map((s) => ({
          name: s.name,
          // Feast has no projection field (no yearly-raise concept to project
          // from) — `billed` kept in the annotation purely so TS sees a
          // property in common with every row shape and doesn't flag the
          // (deliberately all-optional) type as unrelated to FeastWithDonor.
          projected: sum(s.rows, (r: { projected?: number; billed?: number }) => r.projected),
          billed: sum(s.rows, (r: { billed?: number }) => r.billed),
          collected: collectedByStream.get(s.key) ?? 0,
          due: sum(s.rows, (r: { due?: number }) => r.due),
        }));
        const totalCollected = sum(streams, (s) => s.collected);
        const totalBilled = sum(streams, (s) => s.billed);
        const totalProjected = sum(streams, (s) => s.projected);
        return {
          kpis: [
            { label: "Projected", value: money(totalProjected) },
            { label: "Billed", value: money(totalBilled), tone: "primary" },
            { label: "Collected", value: money(totalCollected), tone: "success" },
            { label: "Realised", value: `${pct(totalCollected, totalProjected || totalBilled || 1)}%`, sub: "of projected" },
          ],
          chart: {
            kind: "grouped",
            data: streams,
            series: [
              { key: "projected", name: "Projected", colorIndex: 2 },
              { key: "billed", name: "Billed", colorIndex: 0 },
              { key: "collected", name: "Collected", colorIndex: 1 },
            ],
          },
          columns: [
            { key: "name", label: "Stream" },
            { key: "projected", label: "Projected", align: "right", money: true },
            { key: "billed", label: "Billed", align: "right", money: true },
            { key: "collected", label: "Collected", align: "right", money: true },
            { key: "due", label: "Due", align: "right", money: true },
          ],
          rows: streams,
        };
      }

      case "zone": {
        // billed/due/donor-count still come from the current subscription
        // rows (stock figures, zone filter applied there); collected comes
        // from the date-scoped by-zone ledger and is merged in by name.
        const pool = memberSubs
          .concat(specialSubs)
          .filter((s) => !zoneId || s.donor?.zone_id === zoneId);
        const groups = new Map<string, { name: string; billed: number; due: number; donors: number }>();
        for (const s of pool) {
          const name = (s.donor?.zone_id && zones.find((z) => z.id === s.donor?.zone_id)?.name) || "No zone";
          const g = groups.get(name) ?? { name, billed: 0, due: 0, donors: 0 };
          g.billed += Number(s.billed) || 0;
          g.due += Number(s.due) || 0;
          g.donors += 1;
          groups.set(name, g);
        }
        const collectedByZone = new Map((byZone.data ?? []).map((z) => [z.zone_name, Number(z.collected) || 0]));
        const zoneFilterName = zoneId ? zones.find((z) => z.id === zoneId)?.name : null;
        const rows = [...groups.values()]
          .map((g) => ({ ...g, collected: collectedByZone.get(g.name) ?? 0 }))
          .filter((g) => !zoneFilterName || g.name === zoneFilterName)
          .sort((a, b) => b.collected - a.collected);
        return {
          kpis: [
            { label: "Zones", value: String(rows.length), tone: "primary" },
            { label: "Collected", value: money(sum(rows, (r) => r.collected)), tone: "success" },
            { label: "Still due", value: money(sum(rows, (r) => r.due)), tone: "destructive" },
            { label: "Donors", value: String(sum(rows, (r) => r.donors)) },
          ],
          chart: { kind: "ranked", data: rows.map((r) => ({ name: r.name, value: r.collected })), caption: "Collected by zone" },
          columns: [
            { key: "name", label: "Zone" },
            { key: "donors", label: "Donors", align: "right" },
            { key: "billed", label: "Billed", align: "right", money: true },
            { key: "collected", label: "Collected", align: "right", money: true },
            { key: "due", label: "Due", align: "right", money: true },
          ],
          rows,
        };
      }

      case "by_date": {
        // Pivot the (date, stream, collected) rows from the server into one
        // row per day with a column per stream, so the committee can see
        // which day brought in what across Membership, Special Donation,
        // Ads and Other Income at a glance.
        const byDay = new Map<string, { date: string; membership: number; special: number; feast: number; ad: number; other: number }>();
        for (const r of byDate.data ?? []) {
          const day = byDay.get(r.paid_on) ?? { date: r.paid_on, membership: 0, special: 0, feast: 0, ad: 0, other: 0 };
          day[r.stream] += Number(r.collected) || 0;
          byDay.set(r.paid_on, day);
        }
        const rows = [...byDay.values()]
          .map((d) => ({ ...d, total: d.membership + d.special + d.feast + d.ad + d.other }))
          .sort((a, b) => a.date.localeCompare(b.date));
        const totalCollected = sum(rows, (r) => r.total);
        return {
          kpis: [
            { label: "Days with collection", value: String(rows.length), tone: "primary" },
            { label: "Total collected", value: money(totalCollected), tone: "success" },
            { label: "Best day", value: rows.length ? money(Math.max(...rows.map((r) => r.total))) : money(0) },
            { label: "Avg / day", value: money(rows.length ? totalCollected / rows.length : 0) },
          ],
          chart: { kind: "ranked", data: rows.map((r) => ({ name: fmtDate(r.date), value: r.total })), caption: "Collected per day" },
          columns: [
            { key: "date", label: "Date" },
            { key: "membership", label: "Membership", align: "right", money: true },
            { key: "special", label: "Special Donation", align: "right", money: true },
            { key: "feast", label: "Feast", align: "right", money: true },
            { key: "ad", label: "Ads", align: "right", money: true },
            { key: "other", label: "Other Income", align: "right", money: true },
            { key: "total", label: "Total", align: "right", money: true },
          ],
          rows,
        };
      }

      case "by_mode": {
        // Same pivot as "by_date" — one row per payment mode, a column per
        // income stream — so the committee can see how much actually came
        // in as cash versus UPI/bank versus still-pending, not just totals.
        const byModeMap = new Map<string, { mode: string; membership: number; special: number; feast: number; ad: number; other: number }>();
        for (const r of byMode.data ?? []) {
          const row = byModeMap.get(r.mode) ?? { mode: r.mode, membership: 0, special: 0, feast: 0, ad: 0, other: 0 };
          row[r.stream] += Number(r.collected) || 0;
          byModeMap.set(r.mode, row);
        }
        const rows = [...byModeMap.values()]
          .map((d) => ({ ...d, total: d.membership + d.special + d.feast + d.ad + d.other }))
          .sort((a, b) => b.total - a.total);
        const totalCollected = sum(rows, (r) => r.total);
        return {
          kpis: [
            { label: "Types used", value: String(rows.length), tone: "primary" },
            { label: "Total collected", value: money(totalCollected), tone: "success" },
            { label: "Most used", value: rows[0] ? moneyCompact(rows[0].total) : money(0), sub: rows[0]?.mode },
            { label: "Via cash", value: `${pct(sum(rows.filter((r) => r.mode === "Cash"), (r) => r.total), totalCollected || 1)}%`, sub: "of total" },
          ],
          chart: { kind: "ranked", data: rows.map((r) => ({ name: r.mode, value: r.total })), caption: "Collected per type" },
          columns: [
            { key: "mode", label: "Type" },
            { key: "membership", label: "Membership", align: "right", money: true },
            { key: "special", label: "Special Donation", align: "right", money: true },
            { key: "feast", label: "Feast", align: "right", money: true },
            { key: "ad", label: "Ads", align: "right", money: true },
            { key: "other", label: "Other Income", align: "right", money: true },
            { key: "total", label: "Total", align: "right", money: true },
          ],
          rows,
        };
      }

      case "expense": {
        const forecastByCat = new Map<string, number>();
        for (const b of budget.data ?? []) forecastByCat.set(b.category, Number(b.forecast) || 0);
        const pool = (expenses.data ?? []).filter((e) => inRange(e.spent_on));
        const groups = new Map<string, { name: string; actual: number; entries: number }>();
        for (const e of pool) {
          const name = e.category ?? "Uncategorised";
          const g = groups.get(name) ?? { name, actual: 0, entries: 0 };
          g.actual += Number(e.amount) || 0;
          g.entries += 1;
          groups.set(name, g);
        }
        const rows = [...groups.values()]
          .map((g) => {
            const forecast = forecastByCat.get(g.name) ?? 0;
            return { ...g, forecast, variance: forecast - g.actual };
          })
          .sort((a, b) => b.actual - a.actual);
        const totalActual = sum(rows, (r) => r.actual);
        return {
          kpis: [
            { label: "Total spent", value: money(totalActual), tone: "primary" },
            { label: "Categories", value: String(rows.length) },
            { label: "Entries", value: String(sum(rows, (r) => r.entries)) },
            { label: "Largest", value: rows[0] ? moneyCompact(rows[0].actual) : "—", sub: rows[0]?.name },
          ],
          chart: { kind: "ranked", data: rows.map((r) => ({ name: r.name, value: r.actual })), caption: "Spending by category" },
          columns: [
            { key: "name", label: "Category" },
            { key: "entries", label: "Entries", align: "right" },
            { key: "forecast", label: "Budget", align: "right", money: true },
            { key: "actual", label: "Actual", align: "right", money: true },
            { key: "variance", label: "Under / (over)", align: "right", money: true },
          ],
          rows,
        };
      }

      case "budget": {
        const rows = (budget.data ?? []).map((b) => ({
          name: b.category,
          forecast: Number(b.forecast) || 0,
          contracted: Number(b.contracted) || 0,
          actual: Number(b.actual) || 0,
          remaining: Number(b.remaining) || 0,
        }));
        const totalForecast = sum(rows, (r) => r.forecast);
        const totalActual = sum(rows, (r) => r.actual);
        return {
          kpis: [
            { label: "Forecast", value: money(totalForecast) },
            { label: "Contracted", value: money(sum(rows, (r) => r.contracted)) },
            { label: "Actual", value: money(totalActual), tone: "primary" },
            {
              label: totalActual > totalForecast ? "Over budget" : "Under budget",
              value: moneyCompact(Math.abs(totalForecast - totalActual)),
              tone: totalActual > totalForecast ? "destructive" : "success",
            },
          ],
          chart: {
            kind: "grouped",
            data: [...rows].sort((a, b) => b.actual - a.actual).slice(0, 10),
            series: [
              { key: "forecast", name: "Forecast", colorIndex: 2 },
              { key: "actual", name: "Actual", colorIndex: 1 },
            ],
          },
          columns: [
            { key: "name", label: "Category" },
            { key: "forecast", label: "Forecast", align: "right", money: true },
            { key: "contracted", label: "Contracted", align: "right", money: true },
            { key: "actual", label: "Actual", align: "right", money: true },
            { key: "remaining", label: "Remaining", align: "right", money: true },
          ],
          rows,
        };
      }

      case "not_billed":
      case "due_list":
      case "donations": {
        // Not Billed deliberately excludes Not Eligible donors — they're
        // missing a bill on purpose (excluded from billing this year), not
        // by oversight, so they don't belong on a "needs follow-up" list.
        const pool = (stream === "special" ? specialSubs : stream === "membership" ? memberSubs : memberSubs.concat(specialSubs))
          .filter((s) => report !== "not_billed" || (!(Number(s.billed) > 0) && !s.not_eligible))
          .filter((s) => report !== "due_list" || Number(s.due) > 0)
          .filter((s) => report !== "donations" || Number(s.collected) > 0)
          .map((s) => ({
            name: [s.donor?.name, s.donor?.surname].filter(Boolean).join(" ") || "—",
            zone: (s.donor?.zone_id && zones.find((z) => z.id === s.donor?.zone_id)?.name) || "—",
            bill_no: s.bill_no || "—",
            billed: Number(s.billed) || 0,
            collected: Number(s.collected) || 0,
            due: Number(s.due) || 0,
          }))
          .sort((a, b) => a.name.localeCompare(b.name));
        return {
          kpis: [
            { label: "Donors", value: String(pool.length), tone: "primary" },
            { label: "Billed", value: money(sum(pool, (r) => r.billed)) },
            { label: "Collected", value: money(sum(pool, (r) => r.collected)), tone: "success" },
            { label: "Due", value: money(sum(pool, (r) => r.due)), tone: pool.some((r) => r.due > 0) ? "destructive" : "default" },
          ],
          chart: { kind: "ranked", data: pool.slice(0, 15).map((r) => ({ name: r.name, value: report === "not_billed" ? 0 : report === "due_list" ? r.due : r.collected })), caption: "Top 15 shown" },
          columns: [
            { key: "name", label: "Donor" },
            { key: "zone", label: "Zone" },
            { key: "bill_no", label: "Bill No" },
            { key: "billed", label: "Billed", align: "right", money: true },
            { key: "collected", label: "Collected", align: "right", money: true },
            { key: "due", label: "Due", align: "right", money: true },
          ],
          rows: pool,
        };
      }

      case "netposition":
      default: {
        const collected = (byStream.data ?? []).reduce((acc, r) => acc + (Number(r.collected) || 0), 0);
        const spent = sum((expenses.data ?? []).filter((e) => inRange(e.spent_on)), (r) => r.amount);
        const balance = collected - spent;
        const rows = [
          { name: "Collected", value: collected },
          { name: "Spent", value: spent },
          { name: "In hand", value: balance },
        ];
        return {
          kpis: [
            { label: "Collected", value: money(collected), tone: "success" },
            { label: "Spent", value: money(spent), tone: "primary" },
            { label: "In hand", value: money(balance), tone: balance >= 0 ? "gold" : "destructive" },
            { label: "Spent ratio", value: `${pct(spent, collected || 1)}%`, sub: "of collected" },
          ],
          chart: { kind: "donut", data: [{ name: "Spent", value: spent }, { name: "In hand", value: Math.max(0, balance) }], centerLabel: "Collected", centerValue: collected },
          columns: [
            { key: "name", label: "Measure" },
            { key: "value", label: "Amount", align: "right", money: true },
          ],
          rows,
        };
      }
    }
  }, [report, from, to, zoneId, stream, subs.data, feasts.data, ads.data, others.data, expenses.data, budget.data, byStream.data, byZone.data, byDate.data, byMode.data, zones]);

  const handleExport = () => {
    const label = REPORTS.find((r) => r.key === report)!.short.toLowerCase();
    exportCsv(
      `report-${label}-${new Date().toISOString().slice(0, 10)}`,
      view.rows.map((row) =>
        Object.fromEntries(view.columns.map((c) => [c.label, c.money ? Number(row[c.key] ?? 0) : row[c.key] ?? ""]))
      )
    );
  };

  const handleExportPdf = () => {
    exportPdfTable({
      filename: `report-${def.short.toLowerCase()}-${new Date().toISOString().slice(0, 10)}`,
      title: def.label,
      subtitle: activeYear ? `Season ${activeYear.year}${stream !== "all" ? ` · ${STREAM_OPTIONS.find((s) => s.value === stream)?.label}` : ""}` : undefined,
      columns: view.columns,
      rows: view.rows,
      meta: view.kpis.map((k) => `${k.label}: ${k.value}`),
    });
  };

  return (
    <div className="space-y-5">
      <PageHeader
        title="Reports"
        description={activeYear ? `Season ${activeYear.year} — pick a report and refine it below` : "Pick a report and refine it below"}
        icon={<BarChart3 />}
        actions={
          // The audit report carries its own PDF / CSV controls.
          report !== "audit" ? (
            <>
              <Button variant="outline" size="icon" onClick={handleExport} title="Export this report to CSV">
                <Download className="h-4 w-4" />
              </Button>
              <Button variant="outline" size="icon" onClick={handleExportPdf} title="Download this report as PDF">
                <FileDown className="h-4 w-4" />
              </Button>
            </>
          ) : null
        }
      />

      {/* Report picker — horizontally scrollable pills so it survives a narrow phone. */}
      <div className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1">
        {REPORTS.map((r) => {
          const active = r.key === report;
          return (
            <button
              key={r.key}
              type="button"
              onClick={() => setReport(r.key)}
              className={cn(
                "flex shrink-0 items-center gap-2 rounded-full border px-3.5 py-2 text-sm font-medium transition-all duration-300",
                active
                  ? "border-primary bg-primary text-primary-foreground shadow-card"
                  : "border-border bg-card text-muted-foreground hover:border-primary/40 hover:text-foreground"
              )}
            >
              <r.icon className="h-4 w-4" />
              {r.short}
            </button>
          );
        })}
      </div>

      {/* Contextual filter bar — only the controls this report actually uses. */}
      {(def.filters.dates || def.filters.stream || def.filters.zone) && (
        <div className="flex flex-wrap items-center gap-2 rounded-xl border border-border bg-card/60 p-3">
          {def.filters.dates && (
            <div className="flex items-center gap-1.5">
              <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Dates</span>
              <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="From date" className="w-40" />
              <span className="text-sm text-muted-foreground">to</span>
              <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="To date" className="w-40" />
            </div>
          )}
          {def.filters.stream && (
            <OptionSelect
              value={stream} onChange={(v) => setStream(v ?? "all")}
              options={STREAM_OPTIONS}
              className="w-56"
            />
          )}
          {def.filters.zone && (
            <OptionSelect
              value={zoneId} onChange={setZoneId} allowClear
              options={zones.map((z) => ({ value: z.id, label: z.name }))}
              placeholder="All zones" className="w-44"
            />
          )}
          {(from || to || zoneId || stream !== "all") && (
            <Button variant="ghost" size="sm" className="h-9 text-xs"
              onClick={() => { setFrom(""); setTo(""); setZoneId(null); setStream("all"); }}>
              Reset filters
            </Button>
          )}
        </div>
      )}

      {report === "audit" ? (
        <AuditReport yearId={activeYearId} />
      ) : loading ? (
        <CardsSkeleton />
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            {view.kpis.map((k, i) => (
              <div key={k.label} className="animate-rise" style={{ animationDelay: `${i * 60}ms` }}>
                <StatCard label={k.label} value={k.value} tone={k.tone} sub={k.sub} />
              </div>
            ))}
          </div>

          <Card className="animate-rise" style={{ animationDelay: "120ms" }}>
            <CardHeader>
              <CardTitle>{def.label}</CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">{def.blurb}</p>
            </CardHeader>
            <CardContent>
              {view.chart.kind === "grouped" && <GroupedGradientBars data={view.chart.data} series={view.chart.series} />}
              {view.chart.kind === "ranked" && <RankedGradientBars data={view.chart.data} />}
              {view.chart.kind === "donut" && <DimensionalDonut data={view.chart.data} centerLabel={view.chart.centerLabel} centerValue={view.chart.centerValue} />}
            </CardContent>
          </Card>

          <Card className="animate-rise" style={{ animationDelay: "180ms" }}>
            <Table>
              <TableHeader>
                <TableRow>
                  {view.columns.map((c) => (
                    <TableHead key={c.key} className={c.align === "right" ? "text-right" : undefined}>{c.label}</TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {view.rows.map((row, i) => (
                  <TableRow key={i}>
                    {view.columns.map((c) => {
                      const raw = row[c.key];
                      const negative = c.money && Number(raw) < 0;
                      return (
                        <TableCell key={c.key} className={cn(c.align === "right" && "text-right tabular", negative && "text-destructive")}>
                          {c.money ? money(Number(raw) || 0) : String(raw ?? "—")}
                        </TableCell>
                      );
                    })}
                  </TableRow>
                ))}
                {view.rows.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={view.columns.length} className="py-10 text-center text-sm text-muted-foreground">
                      Nothing matches this selection.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </Card>
        </>
      )}
    </div>
  );
}
