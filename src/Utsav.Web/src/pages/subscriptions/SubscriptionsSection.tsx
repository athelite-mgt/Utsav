import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Plus, Search, Download, FileDown, Pencil, Trash2, ArrowLeftRight, ChevronDown, ChevronUp, Zap } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useSubscriptions, useCollectionPayments, useCollectionPaymentsForYear, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { useIsDesktop } from "@/hooks/useMediaQuery";
import { useLookups, useZones } from "@/hooks/useReference";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Field } from "@/components/common/Field";
import { money, initials } from "@/lib/format";
import { exportCsv } from "@/lib/exportCsv";
import { exportPdfTable } from "@/lib/exportPdf";
import { cn } from "@/lib/utils";
import type { SubscriptionCategory, SubscriptionWithDonor } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { OptionSelect, LookupSelect } from "@/components/form/OptionSelect";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, SortableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useSort } from "@/hooks/useSort";
import { SubscriptionDialog } from "./SubscriptionDialog";

const today = () => new Date().toISOString().slice(0, 10);

/**
 * One tap to clear a due in full, for the common case — collector hands
 * over cash/UPI for exactly what's owed, nothing partial, nothing to
 * re-type. Everything except the mode is implied: full due, today, added
 * to whatever instalments already exist rather than replacing them.
 */
function QuickCollectDialog({
  sub, onOpenChange,
}: { sub: SubscriptionWithDonor | null; onOpenChange: (v: boolean) => void }) {
  const { data: existingPayments } = useCollectionPayments("subscription", sub?.id);
  const { update } = useEntityMutations("subscriptions", ["subscriptions", "collection_payments", "fund_position"], "Collection");
  const [mode, setMode] = useState<string | null>("Cash");

  useEffect(() => { if (sub) setMode("Cash"); }, [sub]);

  const loading = Boolean(sub) && existingPayments === undefined;

  async function confirm() {
    if (!sub) return;
    const existing = (existingPayments ?? []).map((p) => ({ amount: Number(p.amount) || 0, paid_on: p.paid_on, mode: p.mode }));
    const all = [...existing, { amount: sub.due, paid_on: today(), mode }];
    const latest = [...all].sort((a, b) => ((a.paid_on ?? "") < (b.paid_on ?? "") ? 1 : -1))[0];
    const collected = sum(all, (p) => p.amount);

    await update.mutateAsync({
      id: sub.id,
      values: {
        collected,
        mode: latest.mode,
        collected_on: latest.paid_on,
        updated_at: new Date().toISOString(),
        year_id: sub.year_id,
        payments: all,
      },
    });
    onOpenChange(false);
  }

  return (
    <Dialog open={Boolean(sub)} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Collect {sub ? money(sub.due) : ""}?</DialogTitle>
          <DialogDescription>
            Records {sub?.donor?.name}'s full outstanding due as paid today. How did it come in?
          </DialogDescription>
        </DialogHeader>
        <Field label="Mode">
          <LookupSelect category="payment_mode" value={mode} onChange={setMode} placeholder="Mode" />
        </Field>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={update.isPending}>
            Cancel
          </Button>
          <Button type="button" onClick={confirm} disabled={loading || update.isPending || !mode}>
            {update.isPending ? "Saving…" : loading ? "Loading…" : "Collect"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

const PAGE_SIZE = 50;

/** Where a row goes when the move button is used, and what to call it there. */
const OTHER_SECTION: Record<SubscriptionCategory, { category: SubscriptionCategory; label: string }> = {
  membership: { category: "special", label: "Special Donation" },
  special: { category: "membership", label: "Membership" },
};

/**
 * Nothing billed yet is *not* the same as "cleared" — keep those visually
 * neutral. A donor marked Not Eligible takes priority over everything else:
 * they're a deliberate exception (moved away, deceased, excused), not an
 * accidentally-missed bill, so they get their own red flag rather than
 * blending into "Not billed" or "Cleared".
 */
function StatusBadge({ sub }: { sub: SubscriptionWithDonor }) {
  if (sub.not_eligible) return <Badge variant="destructive">Not eligible</Badge>;
  if (!sub.billed && !sub.collected) return <Badge variant="outline">Not billed</Badge>;
  if (sub.due > 0) return <Badge variant="destructive">{money(sub.due)}</Badge>;
  return <Badge variant="success">Cleared</Badge>;
}

export interface SubscriptionsSectionProps {
  category: SubscriptionCategory;
  title: string;
  description: string;
  icon: ReactNode;
  /** Base name for the exported CSV; the date is appended. */
  exportName: string;
  addLabel: string;
}

/**
 * Membership and Special Donation are the same records — one `subscriptions`
 * table split by `category` — so they are one screen rendered twice rather
 * than two implementations kept in step by hand. The only behavioural
 * difference is the projection raise, which applies to the membership round
 * only (see SubscriptionDialog and AdminEndpoints.RolloverAsync).
 */
export function SubscriptionsSection({
  category, title, description, icon, exportName, addLabel,
}: SubscriptionsSectionProps) {
  const { activeYearId, activeYear } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_income");
  const isDesktop = useIsDesktop();
  const { data = [], isLoading, isError, error } = useSubscriptions(activeYearId, category);
  const { remove, update } = useEntityMutations("subscriptions", ["subscriptions"], "Donor");
  const { data: zones = [] } = useZones();
  const { data: modes = [] } = useLookups("payment_mode");
  // Raw dated instalments for the whole year, so the collected-date and Mode
  // filters below can check every payment a donor made, not just the parent
  // row's single latest-instalment date/mode (see useCollectionPaymentsForYear).
  const { data: yearPayments = [] } = useCollectionPaymentsForYear(activeYearId, "subscription");
  const paymentDatesByParent = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const p of yearPayments) {
      if (!p.paid_on) continue;
      const list = map.get(p.parent_id);
      if (list) list.push(p.paid_on);
      else map.set(p.parent_id, [p.paid_on]);
    }
    return map;
  }, [yearPayments]);
  const paymentModesByParent = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const p of yearPayments) {
      if (!p.mode) continue;
      const list = map.get(p.parent_id);
      if (list) list.push(p.mode);
      else map.set(p.parent_id, [p.mode]);
    }
    return map;
  }, [yearPayments]);

  const [search, setSearch] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [zoneId, setZoneId] = useState<string | null>(null);
  const [modeFilter, setModeFilter] = useState<string | null>(null);
  const [visible, setVisible] = useState(PAGE_SIZE);
  // Collapsed by default on mobile so the numbers don't push the list below
  // the fold; open by default on desktop, where there's room for both.
  const [summaryOpen, setSummaryOpen] = useState(isDesktop);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<SubscriptionWithDonor | null>(null);
  // One delete and one move dialog for the whole page, rather than a pair
  // mounted per row.
  const [pendingDelete, setPendingDelete] = useState<SubscriptionWithDonor | null>(null);
  const [pendingMove, setPendingMove] = useState<SubscriptionWithDonor | null>(null);
  const [pendingQuickCollect, setPendingQuickCollect] = useState<SubscriptionWithDonor | null>(null);

  const target = OTHER_SECTION[category];

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    // A search term that's purely numeric also matches on the exact billed
    // amount, so typing "500" finds both a donor named nothing-like-that and
    // whoever was billed exactly ₹500 — one box instead of a separate field.
    const qAmount = q !== "" && !Number.isNaN(Number(q)) ? Number(q) : null;
    return data.filter((s) => {
      const fullName = `${s.donor?.name ?? ""} ${s.donor?.surname ?? ""}`.toLowerCase();
      // Bill numbers are searched for both years — collectors working off
      // last year's register know the old number, not this year's.
      const bills = `${s.bill_no ?? ""} ${s.prior_bill_no ?? ""}`.toLowerCase();
      if (q && !fullName.includes(q) && !bills.includes(q) && !(qAmount !== null && s.billed === qAmount)) return false;
      // Date range works on the collection date — "who paid in this window".
      // A donor can have several dated instalments, so this checks every
      // payment they made, not just the parent row's single latest-instalment
      // date (collected_on) — matching against that alone silently dropped a
      // donor whose matching instalment wasn't their most recent one. ISO
      // date strings compare lexicographically, so a plain slice to the day
      // is enough; a row with nothing collected yet has no instalments and
      // is excluded whenever either bound is set.
      if (fromDate || toDate) {
        const dates = paymentDatesByParent.get(s.id) ?? [];
        const hasMatch = dates.some((d) => {
          const on = d.slice(0, 10);
          if (fromDate && on < fromDate) return false;
          if (toDate && on > toDate) return false;
          return true;
        });
        if (!hasMatch) return false;
      }
      if (status === "due" && s.due <= 0) return false;
      if (status === "paid" && s.due > 0) return false;
      // Not billed = nothing billed or collected yet (matches StatusBadge's
      // definition — billed_on itself isn't a signal since the dialog
      // prefills it to today for every new record regardless of amount),
      // AND not flagged Not Eligible — that donor isn't missing a bill by
      // oversight, they're deliberately excluded from billing this year, so
      // listing them here would read as "needs follow-up" when it doesn't.
      if (status === "not_billed" && (s.billed || s.collected || s.not_eligible)) return false;
      if (zoneId && s.donor?.zone_id !== zoneId) return false;
      // Checks every instalment's mode, same reasoning as the date filter —
      // the parent's own `mode` only ever holds the latest one.
      if (modeFilter && !(paymentModesByParent.get(s.id) ?? []).includes(modeFilter)) return false;
      return true;
    });
  }, [data, search, fromDate, toDate, status, zoneId, modeFilter, paymentDatesByParent, paymentModesByParent]);

  // The summary bar's "Collected" figure: with no date filter, each row's own
  // `collected` (lifetime total) is correct and cheapest. Once a date range is
  // set, the whole point is "what came in during this window" — summing the
  // same lifetime totals would silently include instalments paid outside the
  // range. Re-summed from the raw per-instalment ledger instead, restricted
  // to the dates in range AND the donors the other filters already narrowed to.
  const filteredIds = useMemo(() => new Set(filtered.map((s) => s.id)), [filtered]);
  const rangeCollected = useMemo(() => {
    if (!fromDate && !toDate) return sum(filtered, (r) => r.collected);
    return sum(
      yearPayments.filter((p) => {
        if (!filteredIds.has(p.parent_id)) return false;
        const on = p.paid_on?.slice(0, 10);
        if (!on) return false;
        if (fromDate && on < fromDate) return false;
        if (toDate && on > toDate) return false;
        return true;
      }),
      (p) => p.amount,
    );
  }, [fromDate, toDate, filtered, filteredIds, yearPayments]);

  const { sorted, sortKey, sortDir, toggleSort } = useSort<SubscriptionWithDonor>(filtered, {
    donor: (s) => `${s.donor?.name ?? ""} ${s.donor?.surname ?? ""}`.trim().toLowerCase(),
    bill_no: (s) => s.bill_no,
    prior_bill_no: (s) => s.prior_bill_no,
    projected: (s) => s.projected,
    billed: (s) => s.billed,
    collected: (s) => s.collected,
    due: (s) => s.due,
    mode: (s) => s.mode,
    collector: (s) => s.collector?.name ?? null,
  });

  const page = sorted.slice(0, visible);
  const resetPaging = () => setVisible(PAGE_SIZE);

  const openAdd = () => { setEditing(null); setDialogOpen(true); };
  const openEdit = (s: SubscriptionWithDonor) => { setEditing(s); setDialogOpen(true); };

  const handleExport = () =>
    exportCsv(
      `${exportName}-${new Date().toISOString().slice(0, 10)}`,
      sorted.map((s) => ({
        Name: `${s.donor?.name ?? ""} ${s.donor?.surname ?? ""}`.trim(),
        Contact: s.donor?.contact ?? "",
        BillNo: s.bill_no ?? "",
        LastYearBillNo: s.prior_bill_no ?? "",
        LastYearBilled: s.prior_year_billed ?? s.prior_billed ?? 0,
        Projected: s.projected,
        Billed: s.billed,
        BilledOn: s.billed_on ?? "",
        Collected: s.collected,
        Due: s.due,
        Mode: s.mode ?? "",
        CollectedOn: s.collected_on ?? "",
        CollectedBy: s.collector?.name ?? "",
      }))
    );

  const handleExportPdf = () =>
    exportPdfTable({
      filename: `${exportName}-${new Date().toISOString().slice(0, 10)}`,
      title,
      subtitle: activeYear ? `Season ${activeYear.year}` : undefined,
      columns: [
        { key: "name", label: "Donor" },
        { key: "bill_no", label: "Bill No" },
        { key: "billed", label: "Billed", align: "right" },
        { key: "collected", label: "Collected", align: "right" },
        { key: "due", label: "Due", align: "right" },
        { key: "mode", label: "Mode" },
        { key: "status", label: "Status" },
      ],
      rows: sorted.map((s) => ({
        name: `${s.donor?.name ?? ""} ${s.donor?.surname ?? ""}`.trim(),
        bill_no: s.bill_no ?? "—",
        billed: s.billed,
        collected: s.collected,
        due: s.due,
        mode: s.mode ?? "—",
        status: s.not_eligible ? "Not eligible" : !s.billed && !s.collected ? "Not billed" : s.due > 0 ? "Due" : "Cleared",
      })),
    });

  const moveButton = (s: SubscriptionWithDonor) => (
    <Button
      variant="ghost" size="icon" className="h-8 w-8"
      onClick={(e) => { e.stopPropagation(); setPendingMove(s); }}
      title={`Move to ${target.label}`}
      aria-label={`Move ${s.donor?.name} to ${target.label}`}
    >
      <ArrowLeftRight className="h-4 w-4" />
    </Button>
  );

  const quickCollectButton = (s: SubscriptionWithDonor) =>
    s.due > 0 && (
      <Button
        variant="ghost" size="icon" className="h-8 w-8 text-success"
        onClick={(e) => { e.stopPropagation(); setPendingQuickCollect(s); }}
        title={`Quick collect ${money(s.due)}`}
        aria-label={`Quick collect ${money(s.due)} from ${s.donor?.name}`}
      >
        <Zap className="h-4 w-4" />
      </Button>
    );

  return (
    <div className="space-y-5">
      <PageHeader
        title={title}
        description={description}
        icon={icon}
        actions={
          <>
            <Button
              variant="outline" size="icon" onClick={() => setSummaryOpen((v) => !v)}
              title={summaryOpen ? "Hide summary" : "Show summary"}
              aria-expanded={summaryOpen} aria-label="Toggle summary"
              // Cards start collapsed on mobile to save space — a few gentle
              // pulses on arrival flag that there's a button here at all,
              // rather than leaving the totals silently hidden.
              className={!isDesktop && !summaryOpen ? "animate-attention" : undefined}
            >
              {summaryOpen ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
            </Button>
            <Button variant="outline" size="icon" onClick={handleExport} title="Export CSV">
              <Download className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" onClick={handleExportPdf} title="Download as PDF">
              <FileDown className="h-4 w-4" />
            </Button>
            {canWrite && (
              <Button onClick={openAdd}>
                <Plus /> <span className="hidden sm:inline">{addLabel}</span>
              </Button>
            )}
          </>
        }
      />

      {summaryOpen && (
        <SummaryBar
          items={[
            { label: "Projected", value: sum(filtered, (r) => r.projected) },
            { label: "Billed", value: sum(filtered, (r) => r.billed) },
            { label: "Collected", value: rangeCollected, tone: "success" },
            { label: "Due", value: sum(filtered, (r) => r.due), tone: "destructive" },
          ]}
        />
      )}

      {/* Filters — search stays visible; everything else lives in the Filters dropdown so the bar never wraps. */}
      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => { setSearch(e.target.value); resetPaging(); }}
            placeholder="Search name, bill no., or exact billed amount…"
            className="pl-9"
          />
        </div>
        <FilterMenu
          activeCount={(status ? 1 : 0) + (zoneId ? 1 : 0) + (modeFilter ? 1 : 0) + (fromDate || toDate ? 1 : 0)}
          onClear={() => { setStatus(null); setZoneId(null); setModeFilter(null); setFromDate(""); setToDate(""); resetPaging(); }}
        >
          <FilterField label="Status">
            <OptionSelect
              value={status}
              onChange={(v) => { setStatus(v); resetPaging(); }}
              allowClear
              options={[
                { value: "due", label: "Has due" },
                { value: "paid", label: "Cleared" },
                { value: "not_billed", label: "Not billed" },
              ]}
              placeholder="All status"
              className="w-full"
            />
          </FilterField>
          <FilterField label="Zone">
            <OptionSelect
              value={zoneId}
              onChange={(v) => { setZoneId(v); resetPaging(); }}
              allowClear
              options={zones.map((z) => ({ value: z.id, label: z.name }))}
              placeholder="All zones"
              className="w-full"
            />
          </FilterField>
          <FilterField label="Collection type">
            <OptionSelect
              value={modeFilter}
              onChange={(v) => { setModeFilter(v); resetPaging(); }}
              allowClear
              options={modes.map((m) => ({ value: m.label, label: m.label }))}
              placeholder="All types"
              className="w-full"
            />
          </FilterField>
          <FilterField label="Collected date range">
            <div className="flex items-center gap-1.5">
              <Input
                type="date" value={fromDate} aria-label="Collected from" className="min-w-0 flex-1"
                onChange={(e) => { setFromDate(e.target.value); resetPaging(); }}
              />
              <span className="shrink-0 text-xs text-muted-foreground">to</span>
              <Input
                type="date" value={toDate} aria-label="Collected until" className="min-w-0 flex-1"
                onChange={(e) => { setToDate(e.target.value); resetPaging(); }}
              />
            </div>
          </FilterField>
        </FilterMenu>
      </div>

      {isLoading ? (
        <TableSkeleton />
      ) : isError ? (
        <ErrorState message={(error as Error)?.message} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={icon}
          title="No donors found"
          description={data.length ? "Try adjusting filters." : `Add your first donor, or move one over from ${target.label}.`}
          action={canWrite && <Button onClick={openAdd}><Plus /> {addLabel}</Button>}
        />
      ) : (
        <>
          {isDesktop ? (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <SortableHead sortKey="donor" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Donor</SortableHead>
                    <SortableHead sortKey="bill_no" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Bill no.</SortableHead>
                    <SortableHead sortKey="prior_bill_no" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Last yr bill</SortableHead>
                    <SortableHead sortKey="projected" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Projected</SortableHead>
                    <SortableHead sortKey="billed" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Billed</SortableHead>
                    <SortableHead sortKey="collected" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Collected</SortableHead>
                    <SortableHead sortKey="due" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Due</SortableHead>
                    <SortableHead sortKey="mode" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Mode</SortableHead>
                    <SortableHead sortKey="collector" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Collector</SortableHead>
                    <TableHead className="w-28"></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {page.map((s) => (
                    <TableRow
                      key={s.id}
                      className={cn(canWrite && "cursor-pointer", s.not_eligible && "bg-destructive/10 hover:bg-destructive/15")}
                      onClick={() => canWrite && openEdit(s)}
                    >
                      <TableCell>
                        <div className="flex items-center gap-2.5">
                          <Avatar className="h-8 w-8">
                            <AvatarFallback>{initials(`${s.donor?.name} ${s.donor?.surname ?? ""}`)}</AvatarFallback>
                          </Avatar>
                          <div className="min-w-0">
                            <p className="truncate font-medium">{s.donor?.name} {s.donor?.surname}</p>
                            {s.donor?.contact && (
                              <p className="text-xs text-muted-foreground">{s.donor.contact}</p>
                            )}
                          </div>
                        </div>
                      </TableCell>
                      <TableCell className="tabular">{s.bill_no ?? "—"}</TableCell>
                      <TableCell className="tabular text-muted-foreground">{s.prior_bill_no ?? "—"}</TableCell>
                      <TableCell className="text-right tabular text-muted-foreground">{money(s.projected)}</TableCell>
                      <TableCell className="text-right tabular">{money(s.billed)}</TableCell>
                      <TableCell className="text-right tabular font-medium">{money(s.collected)}</TableCell>
                      <TableCell className="text-right"><StatusBadge sub={s} /></TableCell>
                      <TableCell>{s.mode ? <Badge variant="outline">{s.mode}</Badge> : "—"}</TableCell>
                      <TableCell className="text-sm text-muted-foreground">{s.collector?.name ?? "—"}</TableCell>
                      <TableCell>
                        {/* The row itself opens the editor, so the buttons in
                            here must not also trigger it on their way up. */}
                        <div className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
                          {canWrite && (
                            <>
                              {quickCollectButton(s)}
                              {moveButton(s)}
                              <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(s)}
                                aria-label={`Edit ${s.donor?.name}`}>
                                <Pencil className="h-4 w-4" />
                              </Button>
                              <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"
                                onClick={() => setPendingDelete(s)} aria-label={`Delete ${s.donor?.name}`}>
                                <Trash2 className="h-4 w-4" />
                              </Button>
                            </>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Card>
          ) : (
            <div className="space-y-2.5">
              {page.map((s) => (
                <Card
                  key={s.id}
                  className={cn("p-4", s.not_eligible && "border-destructive/30 bg-destructive/10")}
                  onClick={() => canWrite && openEdit(s)}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex min-w-0 items-center gap-2.5">
                      <Avatar className="h-9 w-9">
                        <AvatarFallback>{initials(`${s.donor?.name} ${s.donor?.surname ?? ""}`)}</AvatarFallback>
                      </Avatar>
                      <div className="min-w-0">
                        <p className="truncate font-medium">{s.donor?.name} {s.donor?.surname}</p>
                        <p className="text-xs text-muted-foreground">
                          {s.bill_no ? `Bill ${s.bill_no}` : "No bill"}
                          {s.prior_bill_no ? ` · last yr ${s.prior_bill_no}` : ""}
                          {s.collector?.name ? ` · ${s.collector.name}` : ""}
                        </p>
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-1">
                      <StatusBadge sub={s} />
                      {canWrite && quickCollectButton(s)}
                      {canWrite && moveButton(s)}
                    </div>
                  </div>
                  <div className="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 border-t border-border pt-3 text-sm">
                    <span className="text-muted-foreground">
                      Projected <span className="tabular font-medium text-foreground">{money(s.projected)}</span>
                    </span>
                    <span className="text-muted-foreground">
                      Billed <span className="tabular font-medium text-foreground">{money(s.billed)}</span>
                    </span>
                    <span className="text-muted-foreground">
                      Collected <span className="tabular font-medium text-foreground">{money(s.collected)}</span>
                    </span>
                    {s.mode && <Badge variant="outline">{s.mode}</Badge>}
                  </div>
                </Card>
              ))}
            </div>
          )}

          {visible < filtered.length && (
            <div className="flex flex-col items-center gap-2 pt-1">
              <p className="text-sm text-muted-foreground">
                Showing {page.length} of {filtered.length}
              </p>
              <Button variant="outline" onClick={() => setVisible((v) => v + PAGE_SIZE)}>
                Show {Math.min(PAGE_SIZE, filtered.length - visible)} more
              </Button>
            </div>
          )}
        </>
      )}

      {activeYearId && (
        <SubscriptionDialog open={dialogOpen} onOpenChange={setDialogOpen} sub={editing} yearId={activeYearId}
          category={category} projectionPct={activeYear?.projection_pct ?? 6.5} />
      )}

      <QuickCollectDialog sub={pendingQuickCollect} onOpenChange={(o) => !o && setPendingQuickCollect(null)} />

      <Dialog open={Boolean(pendingMove)} onOpenChange={(o) => !o && setPendingMove(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Move to {target.label}?</DialogTitle>
            <DialogDescription>
              {pendingMove?.donor?.name} keeps their bill number, amounts and
              last year's history — only which section they appear in changes.
              You can move them back at any time.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPendingMove(null)}>Cancel</Button>
            <Button
              disabled={update.isPending}
              onClick={async () => {
                if (pendingMove) {
                  await update.mutateAsync({
                    id: pendingMove.id,
                    values: { category: target.category, updated_at: new Date().toISOString() },
                  });
                }
                setPendingMove(null);
              }}
            >
              {update.isPending ? "Moving…" : `Move to ${target.label}`}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={Boolean(pendingDelete)} onOpenChange={(o) => !o && setPendingDelete(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Remove this donor from the year?</DialogTitle>
            <DialogDescription>
              This deletes {pendingDelete?.donor?.name}'s record for the current
              puja year. Their details stay available for other years.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPendingDelete(null)}>Cancel</Button>
            <Button
              variant="destructive"
              disabled={remove.isPending}
              onClick={async () => {
                if (pendingDelete) await remove.mutateAsync(pendingDelete.id);
                setPendingDelete(null);
              }}
            >
              {remove.isPending ? "Removing…" : "Remove"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
