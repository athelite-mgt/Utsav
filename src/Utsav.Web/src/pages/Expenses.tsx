import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { ReceiptText, Plus, Search, Download, FileDown, Pencil, Trash2, Lock, ArrowUpDown, ArrowUp, ArrowDown } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useExpenses, useBudget, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { useSort } from "@/hooks/useSort";
import { money, fmtDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import { exportCsv } from "@/lib/exportCsv";
import { exportPdfTable } from "@/lib/exportPdf";
import { onInvalidFocus } from "@/lib/formErrors";
import type { Expense } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { FilterMenu, FilterField, FILTER_INPUT_CLS as SELECT_CLS } from "@/components/common/FilterMenu";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { LookupSelect } from "@/components/form/OptionSelect";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";

const SORT_OPTIONS: { value: string; label: string }[] = [
  { value: "spent_on", label: "Date" },
  { value: "amount", label: "Amount" },
  { value: "category", label: "Category" },
];

interface FormValues {
  category: string | null;
  details: string;
  amount: number;
  spent_on: string;
  mode: string | null;
  comment: string;
  bill_no: string;
}
const empty: FormValues = { category: null, details: "", amount: 0, spent_on: "", mode: null, comment: "", bill_no: "" };

function ExpenseDialog({
  open, onOpenChange, expense, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; expense?: Expense | null; yearId: string }) {
  const { create, update } = useEntityMutations("expenses", ["expenses", "budget_lines"], "Expense");
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: empty });
  const { data: budgetLines = [] } = useBudget(yearId);
  const selectedCategory = watch("category");
  const budgetLine = budgetLines.find((b) => b.category === selectedCategory);

  useEffect(() => {
    reset(expense
      ? { category: expense.category, details: expense.details ?? "", amount: expense.amount,
          spent_on: expense.spent_on ?? "", mode: expense.mode, comment: expense.comment ?? "",
          bill_no: expense.bill_no ?? "" }
      : empty);
  }, [expense, open, reset]);

  function toPayload(v: FormValues) {
    return { ...v, year_id: yearId, amount: v.amount || 0, category: v.category, mode: v.mode,
      details: v.details || null, spent_on: v.spent_on || null, comment: v.comment || null,
      bill_no: v.bill_no || null };
  }

  async function onSubmit(v: FormValues) {
    if (expense) await update.mutateAsync({ id: expense.id, values: toPayload(v) });
    else await create.mutateAsync(toPayload(v));
    onOpenChange(false);
  }

  // Same category/date/mode carried forward, only the item-specific fields
  // (details, amount) clear — so entering ten similar Decoration receipts in
  // a row is ten amounts typed, not ten category pickers re-opened.
  async function onAddAndNew(v: FormValues) {
    await create.mutateAsync(toPayload(v));
    reset({ ...v, details: "", amount: 0 });
    setFocus("amount");
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{expense ? "Edit expense" : "Add expense"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Category" required>
              <LookupSelect category="expense_category" value={watch("category")}
                onChange={(v) => setValue("category", v)} placeholder="Select category" />
            </Field>
            <Field label="Amount" required>
              <MoneyInput step="0.01" {...register("amount", { valueAsNumber: true, required: true })} />
            </Field>
          </div>
          {selectedCategory && budgetLine && (
            <p className={cn(
              "text-xs",
              budgetLine.remaining < 0 ? "font-medium text-destructive" : "text-muted-foreground",
            )}>
              {selectedCategory} remaining: {" "}
              <span className="font-semibold">
                {budgetLine.remaining < 0 ? `(${money(Math.abs(budgetLine.remaining))} over)` : money(budgetLine.remaining)}
              </span>
              {" "}of {money(budgetLine.forecast)}
            </p>
          )}
          {selectedCategory && !budgetLine && (
            <p className="text-xs text-muted-foreground">No budget line set for this category yet.</p>
          )}
          <Field label="Details">
            <Input {...register("details")} placeholder="What was this for?" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Bill No / Voucher No">
              <Input {...register("bill_no")} placeholder="Optional" />
            </Field>
            <Field label="Date">
              <Input type="date" {...register("spent_on")} />
            </Field>
            <Field label="Mode">
              <LookupSelect category="payment_mode" value={watch("mode")}
                onChange={(v) => setValue("mode", v)} placeholder="Payment mode" allowClear />
            </Field>
          </div>
          <Field label="Comment">
            <Textarea {...register("comment")} rows={2} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            {!expense && (
              <Button type="button" variant="secondary" disabled={create.isPending}
                onClick={handleSubmit(onAddAndNew, onInvalidFocus(setFocus))}>
                Add &amp; new
              </Button>
            )}
            <Button type="submit" disabled={create.isPending || update.isPending}>
              {expense ? "Save changes" : "Add expense"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function ExpensesPage() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_finance");
  const { data = [], isLoading, isError, error } = useExpenses(activeYearId);
  const { remove } = useEntityMutations("expenses", ["expenses", "budget_lines"], "Expense");
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const [voucherFilter, setVoucherFilter] = useState<"all" | "missing" | "has">("all");
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Expense | null>(null);

  const categories = useMemo(
    () => [...new Set(data.map((e) => e.category).filter(Boolean))] as string[],
    [data]
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((e) => {
      if (q && !(e.details ?? "").toLowerCase().includes(q) && !(e.category ?? "").toLowerCase().includes(q)) return false;
      if (category && e.category !== category) return false;
      const hasVoucher = !!e.bill_no?.trim();
      if (voucherFilter === "missing" && hasVoucher) return false;
      if (voucherFilter === "has" && !hasVoucher) return false;
      return true;
    });
  }, [data, search, category, voucherFilter]);

  const total = sum(filtered, (e) => e.amount);
  const biggest = [...filtered].sort((a, b) => b.amount - a.amount)[0];

  const { sorted, sortKey, sortDir, toggleSort } = useSort<Expense>(filtered, {
    spent_on: (e) => e.spent_on,
    amount: (e) => e.amount,
    category: (e) => e.category,
  }, "spent_on");
  const SortIcon = sortKey ? (sortDir === "asc" ? ArrowUp : ArrowDown) : ArrowUpDown;
  const openEdit = (e: Expense) => { setEditing(e); setOpen(true); };

  return (
    <div className="space-y-5">
      <PageHeader
        title="Expenses" description="All committee spending" icon={<ReceiptText />}
        actions={canWrite && (
          <>
            <Button variant="outline" size="icon" title="Export CSV"
              onClick={() => exportCsv("expenses", sorted.map((e) => ({
                Category: e.category, Details: e.details, "Bill No": e.bill_no, Amount: e.amount, Date: e.spent_on, Mode: e.mode, Comment: e.comment,
              })))}>
              <Download className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" title="Download PDF"
              onClick={() => exportPdfTable({
                filename: "expenses",
                title: "Expenses",
                subtitle: `${filtered.length} entries · Total ${money(total)}`,
                columns: [
                  { key: "category", label: "Category" },
                  { key: "details", label: "Details" },
                  { key: "bill_no", label: "Bill No" },
                  { key: "date", label: "Date" },
                  { key: "mode", label: "Mode" },
                  { key: "amount", label: "Amount", align: "right" },
                ],
                rows: sorted.map((e) => ({
                  category: e.category ?? "Uncategorised", details: e.details ?? "—",
                  bill_no: e.bill_no ?? "—",
                  date: e.spent_on ? fmtDate(e.spent_on) : "—", mode: e.mode ?? "—", amount: e.amount,
                })),
              })}>
              <FileDown className="h-4 w-4" />
            </Button>
            <Button onClick={() => { setEditing(null); setOpen(true); }}>
              <Plus /> <span className="hidden sm:inline">Add expense</span>
            </Button>
          </>
        )}
      />

      <SummaryBar items={[
        { label: "Total Spent", value: total, tone: "primary" },
        { label: "Entries", value: filtered.length, isMoney: false },
        { label: "Categories", value: categories.length, isMoney: false },
        { label: "Largest", value: biggest?.amount ?? 0 },
      ]} />

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search expenses…" className="pl-9" />
        </div>
        <FilterMenu
          activeCount={(category ? 1 : 0) + (voucherFilter !== "all" ? 1 : 0)}
          onClear={() => { setCategory(null); setVoucherFilter("all"); }}
        >
          <FilterField label="Category">
            <select value={category ?? ""} onChange={(e) => setCategory(e.target.value || null)} className={SELECT_CLS}>
              <option value="">All categories</option>
              {categories.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </FilterField>
          <FilterField label="Voucher">
            <select value={voucherFilter} onChange={(e) => setVoucherFilter(e.target.value as typeof voucherFilter)} className={SELECT_CLS}>
              <option value="all">All vouchers</option>
              <option value="missing">Missing voucher</option>
              <option value="has">Has voucher</option>
            </select>
          </FilterField>
        </FilterMenu>
        <div className="flex gap-2">
          <select
            value={sortKey ?? ""}
            onChange={(e) => toggleSort(e.target.value)}
            className="h-10 rounded-lg border border-input bg-card px-3 text-sm sm:w-36"
            aria-label="Sort by"
          >
            {SORT_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
          <Button variant="outline" size="icon" onClick={() => sortKey && toggleSort(sortKey)} title="Toggle sort direction">
            <SortIcon className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {!canWrite && (
        <p className="flex items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
          <Lock className="h-3.5 w-3.5" /> View only — expenses are managed by the committee admin.
        </p>
      )}

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<ReceiptText />} title="No expenses"
            description={canWrite ? "Record your first expense." : "Nothing recorded yet."}
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add expense</Button>} />
        ) : (
          <Card className="divide-y divide-border">
            {sorted.map((e) => (
              <div
                key={e.id}
                className={cn("flex items-center gap-3 p-3.5 sm:px-4", canWrite && "cursor-pointer hover:bg-muted/40")}
                onClick={() => canWrite && openEdit(e)}
              >
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <ReceiptText className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate font-medium">{e.category ?? "Uncategorised"}</p>
                    {e.mode && <Badge variant="outline" className="hidden sm:inline-flex">{e.mode}</Badge>}
                  </div>
                  <p className="truncate text-xs text-muted-foreground">
                    {e.details || "—"} · {fmtDate(e.spent_on)}
                  </p>
                </div>
                <p className="shrink-0 font-semibold tabular">{money(e.amount)}</p>
                {canWrite && (
                  <div className="flex shrink-0 gap-0.5" onClick={(ev) => ev.stopPropagation()}>
                    <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => openEdit(e)}>
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <ConfirmDialog title="Delete expense?" destructive confirmLabel="Delete"
                      onConfirm={() => remove.mutateAsync(e.id)}
                      trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                  </div>
                )}
              </div>
            ))}
          </Card>
        )}

      {activeYearId && <ExpenseDialog open={open} onOpenChange={setOpen} expense={editing} yearId={activeYearId} />}
    </div>
  );
}
