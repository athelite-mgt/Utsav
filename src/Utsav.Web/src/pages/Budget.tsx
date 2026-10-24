import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { PiggyBank, Plus, Pencil, Trash2, Lock, TrendingUp } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useBudget, useSubscriptions, useAdBookings, useSpecialDonations, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { money, pct } from "@/lib/format";
import { projectedWithFallback } from "@/lib/projection";
import { cn } from "@/lib/utils";
import type { BudgetVsActual } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { LookupSelect } from "@/components/form/OptionSelect";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { BudgetBars } from "@/components/charts/BudgetBars";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Table, TableBody, TableCell, TableHead, SortableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useSort } from "@/hooks/useSort";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

interface FormValues { category: string | null; notes: string; forecast: number; contract_amount: number }
const empty: FormValues = { category: null, notes: "", forecast: 0, contract_amount: 0 };

function BudgetDialog({
  open, onOpenChange, line, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; line?: BudgetVsActual | null; yearId: string }) {
  const { create, update } = useEntityMutations("budget_lines", ["budget_lines"], "Budget line");
  const { register, handleSubmit, reset, watch, setValue } = useForm<FormValues>({ defaultValues: empty });

  useEffect(() => {
    reset(line ? { category: line.category, notes: line.notes ?? "", forecast: line.forecast, contract_amount: line.contract_amount } : empty);
  }, [line, open, reset]);

  async function onSubmit(v: FormValues) {
    const payload = { category: v.category, notes: v.notes || null, forecast: v.forecast || 0, contract_amount: v.contract_amount || 0, year_id: yearId };
    if (line) await update.mutateAsync({ id: line.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{line ? "Edit budget line" : "Add budget line"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          <Field label="Category" required>
            <LookupSelect category="expense_category" value={watch("category")} onChange={(v) => setValue("category", v)} placeholder="Select category" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Forecast (planned)"><MoneyInput step="1" {...register("forecast", { valueAsNumber: true })} /></Field>
            <Field label="Contract amount"><MoneyInput step="1" {...register("contract_amount", { valueAsNumber: true })} /></Field>
          </div>
          <Field label="Notes"><Input {...register("notes")} placeholder="Optional" /></Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>{line ? "Save" : "Add line"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ---------------- Expense budget ----------------
function ExpenseBudgetTab({ yearId }: { yearId: string | null }) {
  const { can } = useAuth();
  const canWrite = can("write_finance");
  const { data = [], isLoading } = useBudget(yearId);
  const { remove } = useEntityMutations("budget_lines", ["budget_lines"], "Budget line");
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<BudgetVsActual | null>(null);

  const totalPrior = sum(data, (r) => r.prior_actual);
  const totalForecast = sum(data, (r) => r.forecast);
  const totalActual = sum(data, (r) => r.actual);
  const remaining = totalForecast - totalActual;

  const chartData = data.map((b) => ({
    name: b.category, forecast: Number(b.forecast) || 0, actual: Number(b.actual) || 0, prior: Number(b.prior_actual) || 0,
  }));

  const { sorted, sortKey, sortDir, toggleSort } = useSort<BudgetVsActual>(data, {
    category: (b) => b.category,
    prior_actual: (b) => b.prior_actual,
    forecast: (b) => b.forecast,
    contract_amount: (b) => b.contract_amount,
    actual: (b) => b.actual,
    remaining: (b) => b.remaining,
  });

  return (
    <div className="space-y-5">
      {canWrite && (
        <div className="flex justify-end">
          <Button onClick={() => { setEditing(null); setOpen(true); }}>
            <Plus /> <span className="hidden sm:inline">Add line</span>
          </Button>
        </div>
      )}

      <SummaryBar items={[
        { label: "Last Year Actual", value: totalPrior },
        { label: "This Year Budget", value: totalForecast },
        { label: "This Year Actual", value: totalActual, tone: "primary" },
        { label: "Remaining", value: remaining, tone: remaining >= 0 ? "success" : "destructive" },
      ]} />

      <Card>
        <CardHeader><CardTitle>Last year vs this year's budget vs actual</CardTitle></CardHeader>
        <CardContent><BudgetBars data={chartData} max={12} /></CardContent>
      </Card>

      {!canWrite && (
        <p className="flex items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
          <Lock className="h-3.5 w-3.5" /> View only — budget is managed by the committee admin.
        </p>
      )}

      {isLoading ? <TableSkeleton />
        : data.length === 0 ? (
          <EmptyState icon={<PiggyBank />} title="No budget lines"
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add line</Button>} />
        ) : (
          <Card>
            <Table>
              <TableHeader>
                <TableRow>
                  <SortableHead sortKey="category" activeKey={sortKey} dir={sortDir} onSort={toggleSort}>Category</SortableHead>
                  <SortableHead sortKey="prior_actual" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Last Year Actual</SortableHead>
                  <SortableHead sortKey="forecast" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Forecast</SortableHead>
                  <SortableHead sortKey="contract_amount" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Contract Amount</SortableHead>
                  <SortableHead sortKey="actual" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Actual</SortableHead>
                  <SortableHead sortKey="remaining" activeKey={sortKey} dir={sortDir} onSort={toggleSort} className="text-right">Remaining</SortableHead>
                  {canWrite && <TableHead className="w-20"></TableHead>}
                </TableRow>
              </TableHeader>
              <TableBody>
                {sorted.map((b) => {
                  const used = pct(Number(b.actual), Number(b.forecast) || 1);
                  const over = Number(b.actual) > Number(b.forecast);
                  return (
                    <TableRow
                      key={b.id}
                      className={canWrite ? "cursor-pointer" : undefined}
                      onClick={() => { if (canWrite) { setEditing(b); setOpen(true); } }}
                    >
                      <TableCell>
                        <p className="font-medium">{b.category}</p>
                        {b.notes && <p className="text-xs text-muted-foreground">{b.notes}</p>}
                      </TableCell>
                      <TableCell className="text-right tabular text-muted-foreground">{money(b.prior_actual)}</TableCell>
                      <TableCell className="text-right tabular">{money(b.forecast)}</TableCell>
                      {/* The value the line itself carries (what the editor sets),
                          shown in normal weight like Forecast beside it. Real
                          signed vendor contracts, if any, sit under it as a
                          muted hint so both figures are visible without being
                          confused for one another. */}
                      <TableCell className="text-right tabular">
                        {money(b.contract_amount)}
                        {Number(b.contracted) > 0 && (
                          <span className="block text-xs text-muted-foreground">{money(b.contracted)} in contracts</span>
                        )}
                      </TableCell>
                      <TableCell className={cn("text-right tabular font-medium", used > 100 && "text-destructive")}>
                        {money(b.actual)}
                      </TableCell>
                      <TableCell className={cn("text-right tabular", over ? "text-destructive" : "text-success")}>
                        {over ? `(${money(Math.abs(b.remaining))})` : money(b.remaining)}
                      </TableCell>
                      {canWrite && (
                        <TableCell>
                          {/* The row opens the editor; these must not re-trigger it. */}
                          <div className="flex justify-end gap-0.5" onClick={(e) => e.stopPropagation()}>
                            <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(b); setOpen(true); }}>
                              <Pencil className="h-4 w-4" />
                            </Button>
                            <ConfirmDialog title="Delete budget line?" destructive confirmLabel="Delete"
                              onConfirm={() => remove.mutateAsync(b.id)}
                              trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                          </div>
                        </TableCell>
                      )}
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </Card>
        )}

      {yearId && <BudgetDialog open={open} onOpenChange={setOpen} line={editing} yearId={yearId} />}
    </div>
  );
}

// ---------------- Income projection (shared by the page-top summary and the Income Budget tab) ----------------
// Read-only rollup — there's nothing to "add" here, it's a direct summary
// of what the income pages already have on file, so it can never drift from
// those pages. Membership and Special Donation always have a `projected`
// figure (rollover fills it in for every row); Advertisements and Other
// Incomes don't carry over between years, so the same last-year-billed
// fallback used on those pages' own summary cards applies here too — the
// two views would otherwise disagree about what "projected" means.
function useIncomeProjectionRows(yearId: string | null) {
  const { previousYearId } = useYear();
  const subs = useSubscriptions(yearId);
  const ads = useAdBookings(yearId);
  const priorAds = useAdBookings(previousYearId);
  const otherIncomes = useSpecialDonations(yearId);
  const priorOther = useSpecialDonations(previousYearId);

  const allSubs = subs.data ?? [];
  const memberSubs = allSubs.filter((x) => x.category !== "special");
  const specialSubs = allSubs.filter((x) => x.category === "special");
  const adProjection = projectedWithFallback(ads.data ?? [], priorAds.data ?? []);
  const otherProjection = projectedWithFallback(otherIncomes.data ?? [], priorOther.data ?? []);

  const rows = [
    {
      name: "Membership",
      projected: sum(memberSubs, (x) => x.projected),
      billed: sum(memberSubs, (x) => x.billed),
      collected: sum(memberSubs, (x) => x.collected),
      fromPriorYear: false,
    },
    {
      name: "Special Donation",
      projected: sum(specialSubs, (x) => x.projected),
      billed: sum(specialSubs, (x) => x.billed),
      collected: sum(specialSubs, (x) => x.collected),
      fromPriorYear: false,
    },
    {
      name: "Advertisements",
      projected: adProjection.value,
      billed: sum(ads.data ?? [], (x) => x.billed),
      collected: sum(ads.data ?? [], (x) => x.collected),
      fromPriorYear: adProjection.fromPriorYear,
    },
    {
      name: "Other Incomes",
      projected: otherProjection.value,
      billed: sum(otherIncomes.data ?? [], (x) => x.billed),
      collected: sum(otherIncomes.data ?? [], (x) => x.collected),
      fromPriorYear: otherProjection.fromPriorYear,
    },
  ];

  return { rows, loading: subs.isLoading || ads.isLoading || otherIncomes.isLoading };
}

/** Total projected income + the per-stream split, pinned above the tabs so it's visible without picking Income Budget. */
function IncomeProjectionSummary({ yearId }: { yearId: string | null }) {
  const { rows, loading } = useIncomeProjectionRows(yearId);
  const total = sum(rows, (r) => r.projected);
  const fallbackStreams = rows.filter((r) => r.fromPriorYear).map((r) => r.name);

  if (loading) {
    return <div className="h-[124px] animate-pulse rounded-xl bg-muted/60" />;
  }

  return (
    <Card className="p-5">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Total Projected Income</p>
      <p className="mt-1 text-2xl font-bold tabular tracking-tight sm:text-3xl">{money(total)}</p>
      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {rows.map((r) => (
          <div key={r.name}>
            <p className="truncate text-xs text-muted-foreground">{r.name}</p>
            <p className="font-semibold tabular">{money(r.projected)}</p>
          </div>
        ))}
      </div>
      {fallbackStreams.length > 0 && (
        <p className="mt-3 text-[11px] text-muted-foreground">
          {fallbackStreams.join(" and ")} {fallbackStreams.length > 1 ? "haven't" : "hasn't"} been projected for this
          year yet, so last year's billed total is shown until they are.
        </p>
      )}
    </Card>
  );
}

function IncomeBudgetTab({ yearId }: { yearId: string | null }) {
  const { rows, loading } = useIncomeProjectionRows(yearId);
  const totalProjected = sum(rows, (r) => r.projected);
  const totalBilled = sum(rows, (r) => r.billed);
  const totalCollected = sum(rows, (r) => r.collected);

  return (
    <div className="space-y-5">
      <SummaryBar items={[
        { label: "Projected", value: totalProjected },
        { label: "Billed", value: totalBilled },
        { label: "Collected", value: totalCollected, tone: "success" },
        { label: "Of Projected", value: totalProjected > 0 ? `${pct(totalCollected, totalProjected)}%` : "—", isMoney: false },
      ]} />

      {loading ? <TableSkeleton /> : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Source</TableHead>
                <TableHead className="text-right">Projected</TableHead>
                <TableHead className="text-right">Billed</TableHead>
                <TableHead className="text-right">Collected</TableHead>
                <TableHead className="text-right">Of Projected</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => (
                <TableRow key={r.name}>
                  <TableCell className="font-medium">
                    {r.name}
                    {r.fromPriorYear && <span className="ml-1.5 text-xs font-normal text-muted-foreground">(last year)</span>}
                  </TableCell>
                  <TableCell className="text-right tabular text-muted-foreground">{money(r.projected)}</TableCell>
                  <TableCell className="text-right tabular">{money(r.billed)}</TableCell>
                  <TableCell className="text-right tabular font-medium text-success">{money(r.collected)}</TableCell>
                  <TableCell className="text-right tabular">{r.projected > 0 ? `${pct(r.collected, r.projected)}%` : "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
      )}
      <p className="text-xs text-muted-foreground">
        Projected figures come straight from each donor/advertiser/donation record — edit them on the Membership,
        Special Donation, Advertisements or Other Incomes pages. Advertisements and Other Incomes fall back to last
        year's billed total until this year has projections of its own.
      </p>
    </div>
  );
}

// ---------------- Page ----------------
export function BudgetPage() {
  const { activeYearId } = useYear();

  return (
    <div className="space-y-5">
      <PageHeader title="Budget" description="Planned vs actual, income vs expense" icon={<PiggyBank />} />

      <IncomeProjectionSummary yearId={activeYearId} />

      <Tabs defaultValue="expense">
        <TabsList>
          <TabsTrigger value="expense" className="gap-1.5"><PiggyBank className="h-4 w-4" /> Expense Budget</TabsTrigger>
          <TabsTrigger value="income" className="gap-1.5"><TrendingUp className="h-4 w-4" /> Income Budget</TabsTrigger>
        </TabsList>
        <TabsContent value="expense"><ExpenseBudgetTab yearId={activeYearId} /></TabsContent>
        <TabsContent value="income"><IncomeBudgetTab yearId={activeYearId} /></TabsContent>
      </Tabs>
    </div>
  );
}
