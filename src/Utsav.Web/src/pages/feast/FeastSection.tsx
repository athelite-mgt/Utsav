import { useEffect, useMemo, useState } from "react";
import { Plus, Search, Download, FileDown, Pencil, Trash2, ChevronDown, ChevronUp, Zap, UtensilsCrossed } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useFeasts, useCollectionPayments, useCollectionPaymentsForYear, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { useIsDesktop } from "@/hooks/useMediaQuery";
import { useLookups, useZones } from "@/hooks/useReference";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Field } from "@/components/common/Field";
import { money, initials } from "@/lib/format";
import { exportCsv } from "@/lib/exportCsv";
import { exportPdfTable } from "@/lib/exportPdf";
import { cn } from "@/lib/utils";
import type { FeastWithDonor } from "@/types/db";
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
import { FeastDialog } from "./FeastDialog";

const today = () => new Date().toISOString().slice(0, 10);

/** One tap to clear a due in full — same shortcut as Membership/Special Donation's QuickCollectDialog. */
function QuickCollectDialog({
  feast, onOpenChange,
}: { feast: FeastWithDonor | null; onOpenChange: (v: boolean) => void }) {
  const { data: existingPayments } = useCollectionPayments("feast", feast?.id);
  const { update } = useEntityMutations("feasts", ["feasts", "collection_payments", "fund_position"], "Collection");
  const [mode, setMode] = useState<string | null>("Cash");

  useEffect(() => { if (feast) setMode("Cash"); }, [feast]);

  const loading = Boolean(feast) && existingPayments === undefined;

  async function confirm() {
    if (!feast) return;
    const existing = (existingPayments ?? []).map((p) => ({ amount: Number(p.amount) || 0, paid_on: p.paid_on, mode: p.mode }));
    const all = [...existing, { amount: feast.due, paid_on: today(), mode }];
    const latest = [...all].sort((a, b) => ((a.paid_on ?? "") < (b.paid_on ?? "") ? 1 : -1))[0];
    const collected = sum(all, (p) => p.amount);

    await update.mutateAsync({
      id: feast.id,
      values: {
        collected,
        mode: latest.mode,
        collected_on: latest.paid_on,
        updated_at: new Date().toISOString(),
        year_id: feast.year_id,
        payments: all,
      },
    });
    onOpenChange(false);
  }

  return (
    <Dialog open={Boolean(feast)} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Collect {feast ? money(feast.due) : ""}?</DialogTitle>
          <DialogDescription>
            Records {feast?.donor?.name}'s full outstanding due as paid today. How did it come in?
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

function StatusBadge({ feast }: { feast: FeastWithDonor }) {
  if (feast.not_eligible) return <Badge variant="destructive">Not eligible</Badge>;
  if (!feast.billed && !feast.collected) return <Badge variant="outline">Not billed</Badge>;
  if (feast.due > 0) return <Badge variant="destructive">{money(feast.due)}</Badge>;
  return <Badge variant="success">Cleared</Badge>;
}

/**
 * Feast collection — same donor/bill-no/instalment mechanics as Membership
 * and Special Donation (see SubscriptionsSection), its own page since Feast
 * lives in its own table rather than a subscriptions `category`. No move-
 * between-sections control and no Projected column: feast has nothing to
 * move to, and no yearly-raise/rollover concept to project from.
 */
export function FeastSection() {
  const { activeYearId, activeYear } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_income");
  const isDesktop = useIsDesktop();
  const { data = [], isLoading, isError, error } = useFeasts(activeYearId);
  const { remove, update } = useEntityMutations("feasts", ["feasts"], "Donor");
  const { data: zones = [] } = useZones();
  const { data: modes = [] } = useLookups("payment_mode");
  const { data: yearPayments = [] } = useCollectionPaymentsForYear(activeYearId, "feast");
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
  const [summaryOpen, setSummaryOpen] = useState(isDesktop);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<FeastWithDonor | null>(null);
  const [pendingDelete, setPendingDelete] = useState<FeastWithDonor | null>(null);
  const [pendingQuickCollect, setPendingQuickCollect] = useState<FeastWithDonor | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const qAmount = q !== "" && !Number.isNaN(Number(q)) ? Number(q) : null;
    return data.filter((f) => {
      const fullName = `${f.donor?.name ?? ""} ${f.donor?.surname ?? ""}`.toLowerCase();
      const bills = `${f.bill_no ?? ""}`.toLowerCase();
      if (q && !fullName.includes(q) && !bills.includes(q) && !(qAmount !== null && f.billed === qAmount)) return false;
      if (fromDate || toDate) {
        const dates = paymentDatesByParent.get(f.id) ?? [];
        const hasMatch = dates.some((d) => {
          const on = d.slice(0, 10);
          if (fromDate && on < fromDate) return false;
          if (toDate && on > toDate) return false;
          return true;
        });
        if (!hasMatch) return false;
      }
      if (status === "due" && f.due <= 0) return false;
      if (status === "paid" && f.due > 0) return false;
      if (status === "not_billed" && (f.billed || f.collected || f.not_eligible)) return false;
      if (zoneId && f.donor?.zone_id !== zoneId) return false;
      if (modeFilter && !(paymentModesByParent.get(f.id) ?? []).includes(modeFilter)) return false;
      return true;
    });
  }, [data, search, fromDate, toDate, status, zoneId, modeFilter, paymentDatesByParent, paymentModesByParent]);

  const filteredIds = useMemo(() => new Set(filtered.map((f) => f.id)), [filtered]);
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

  const { sorted, sortKey, sortDir, toggleSort } = useSort<FeastWithDonor>(filtered, {
    donor: (f) => `${f.donor?.name ?? ""} ${f.donor?.surname ?? ""}`.trim().toLowerCase(),
    bill_no: (f) => f.bill_no,
    billed: (f) => f.billed,
    collected: (f) => f.collected,
    due: (f) => f.due,
    mode: (f) => f.mode,
    collector: (f) => f.collector?.name ?? null,
  });

  const page = sorted.slice(0, visible);
  const resetPaging = () => setVisible(PAGE_SIZE);

  const openAdd = () => { setEditing(null); setDialogOpen(true); };
  const openEdit = (f: FeastWithDonor) => { setEditing(f); setDialogOpen(true); };

  const handleExport = () =>
    exportCsv(
      `feast-${new Date().toISOString().slice(0, 10)}`,
      sorted.map((f) => ({
        Name: `${f.donor?.name ?? ""} ${f.donor?.surname ?? ""}`.trim(),
        Contact: f.donor?.contact ?? "",
        BillNo: f.bill_no ?? "",
        Billed: f.billed,
        BilledOn: f.billed_on ?? "",
        Collected: f.collected,
        Due: f.due,
        Mode: f.mode ?? "",
        CollectedOn: f.collected_on ?? "",
        CollectedBy: f.collector?.name ?? "",
      }))
    );

  const handleExportPdf = () =>
    exportPdfTable({
      filename: `feast-${new Date().toISOString().slice(0, 10)}`,
      title: "Feast",
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
      rows: sorted.map((f) => ({
        name: `${f.donor?.name ?? ""} ${f.donor?.surname ?? ""}`.trim(),
        bill_no: f.bill_no ?? "—",
        billed: f.billed,
        collected: f.collected,
        due: f.due,
        mode: f.mode ?? "—",
        status: f.not_eligible ? "Not eligible" : !f.billed && !f.collected ? "Not billed" : f.due > 0 ? "Due" : "Cleared",
      })),
    });

  const quickCollectButton = (f: FeastWithDonor) =>
    f.due > 0 && (
      <Button
        variant="ghost" size="icon" className="h-8 w-8 text-success"
        onClick={(e) => { e.stopPropagation(); setPendingQuickCollect(f); }}
        title={`Quick collect ${money(f.due)}`}
        aria-label={`Quick collect ${money(f.due)} from ${f.donor?.name}`}
      >
        <Zap className="h-4 w-4" />
      </Button>
    );

  return (
    <div className="space-y-5">
      <PageHeader
        title="Feast"
        description="Feast donor collection"
        icon={<UtensilsCrossed />}
        actions={
          <>
            <Button
              variant="outline" size="icon" onClick={() => setSummaryOpen((v) => !v)}
              title={summaryOpen ? "Hide summary" : "Show summary"}
              aria-expanded={summaryOpen} aria-label="Toggle summary"
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
                <Plus /> <span className="hidden sm:inline">Add donor</span>
              </Button>
            )}
          </>
        }
      />

      {summaryOpen && (
        <SummaryBar
          items={[
            { label: "Billed", value: sum(filtered, (r) => r.billed) },
            { label: "Collected", value: rangeCollected, tone: "success" },
            { label: "Due", value: sum(filtered, (r) => r.due), tone: "destructive" },
          ]}
        />
      )}

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
          icon={<UtensilsCrossed />}
          title="No donors found"
          description={data.length ? "Try adjusting filters." : "Add your first feast donor."}
          action={canWrite && <Button onClick={openAdd}><Plus /> Add donor</Button>}
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
                    <SortableHead sortKey="billed" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Billed</SortableHead>
                    <SortableHead sortKey="collected" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Collected</SortableHead>
                    <SortableHead sortKey="due" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Due</SortableHead>
                    <SortableHead sortKey="mode" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Mode</SortableHead>
                    <SortableHead sortKey="collector" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Collector</SortableHead>
                    <TableHead className="w-28"></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {page.map((f) => (
                    <TableRow
                      key={f.id}
                      className={cn(canWrite && "cursor-pointer", f.not_eligible && "bg-destructive/10 hover:bg-destructive/15")}
                      onClick={() => canWrite && openEdit(f)}
                    >
                      <TableCell>
                        <div className="flex items-center gap-2.5">
                          <Avatar className="h-8 w-8">
                            <AvatarFallback>{initials(`${f.donor?.name} ${f.donor?.surname ?? ""}`)}</AvatarFallback>
                          </Avatar>
                          <div className="min-w-0">
                            <p className="truncate font-medium">{f.donor?.name} {f.donor?.surname}</p>
                            {f.donor?.contact && (
                              <p className="text-xs text-muted-foreground">{f.donor.contact}</p>
                            )}
                          </div>
                        </div>
                      </TableCell>
                      <TableCell className="tabular">{f.bill_no ?? "—"}</TableCell>
                      <TableCell className="text-right tabular">{money(f.billed)}</TableCell>
                      <TableCell className="text-right tabular font-medium">{money(f.collected)}</TableCell>
                      <TableCell className="text-right"><StatusBadge feast={f} /></TableCell>
                      <TableCell>{f.mode ? <Badge variant="outline">{f.mode}</Badge> : "—"}</TableCell>
                      <TableCell className="text-sm text-muted-foreground">{f.collector?.name ?? "—"}</TableCell>
                      <TableCell>
                        <div className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
                          {canWrite && (
                            <>
                              {quickCollectButton(f)}
                              <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(f)}
                                aria-label={`Edit ${f.donor?.name}`}>
                                <Pencil className="h-4 w-4" />
                              </Button>
                              <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"
                                onClick={() => setPendingDelete(f)} aria-label={`Delete ${f.donor?.name}`}>
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
              {page.map((f) => (
                <Card
                  key={f.id}
                  className={cn("p-4", f.not_eligible && "border-destructive/30 bg-destructive/10")}
                  onClick={() => canWrite && openEdit(f)}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex min-w-0 items-center gap-2.5">
                      <Avatar className="h-9 w-9">
                        <AvatarFallback>{initials(`${f.donor?.name} ${f.donor?.surname ?? ""}`)}</AvatarFallback>
                      </Avatar>
                      <div className="min-w-0">
                        <p className="truncate font-medium">{f.donor?.name} {f.donor?.surname}</p>
                        <p className="text-xs text-muted-foreground">
                          {f.bill_no ? `Bill ${f.bill_no}` : "No bill"}
                          {f.collector?.name ? ` · ${f.collector.name}` : ""}
                        </p>
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-1">
                      <StatusBadge feast={f} />
                      {canWrite && quickCollectButton(f)}
                    </div>
                  </div>
                  <div className="mt-3 flex flex-wrap items-center justify-between gap-x-4 gap-y-1 border-t border-border pt-3 text-sm">
                    <span className="text-muted-foreground">
                      Billed <span className="tabular font-medium text-foreground">{money(f.billed)}</span>
                    </span>
                    <span className="text-muted-foreground">
                      Collected <span className="tabular font-medium text-foreground">{money(f.collected)}</span>
                    </span>
                    {f.mode && <Badge variant="outline">{f.mode}</Badge>}
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
        <FeastDialog open={dialogOpen} onOpenChange={setDialogOpen} feast={editing} yearId={activeYearId} />
      )}

      <QuickCollectDialog feast={pendingQuickCollect} onOpenChange={(o) => !o && setPendingQuickCollect(null)} />

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
