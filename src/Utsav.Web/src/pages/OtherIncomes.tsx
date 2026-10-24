import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { Sparkles, Plus, Search, Download, Pencil, Trash2 } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useSpecialDonations, useCollectionPayments, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { money, fmtDate, initials } from "@/lib/format";
import { exportCsv } from "@/lib/exportCsv";
import { onInvalidFocus } from "@/lib/formErrors";
import { projectedWithFallback } from "@/lib/projection";
import type { SpecialDonation } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Textarea } from "@/components/ui/textarea";
import { Card } from "@/components/ui/card";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Field } from "@/components/common/Field";
import { OptionSelect } from "@/components/form/OptionSelect";
import { useLookups } from "@/hooks/useReference";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { CollectionPaymentsField, type PaymentDraft } from "@/components/finance/CollectionPaymentsField";

interface FormValues { name: string; contact: string; bill_no: string; projected: number; billed: number; comments: string }
const empty: FormValues = { name: "", contact: "", bill_no: "", projected: 0, billed: 0, comments: "" };

function SpecialDialog({
  open, onOpenChange, row, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; row?: SpecialDonation | null; yearId: string }) {
  const { create, update } = useEntityMutations("special_donations", ["special_donations"], "Other income");
  const { register, handleSubmit, reset, watch, setFocus } = useForm<FormValues>({ defaultValues: empty });
  const { data: existingPayments } = useCollectionPayments("special_donation", row?.id);
  const [payments, setPayments] = useState<PaymentDraft[]>([]);

  useEffect(() => {
    reset(row ? {
      name: row.name, contact: row.contact ?? "", bill_no: row.bill_no ?? "", projected: Math.round(row.projected ?? 0), billed: row.billed,
      comments: row.comments ?? "",
    } : empty);
    if (!row) setPayments([]);
  }, [row, open, reset]);

  useEffect(() => {
    if (!open || !row) return;
    if (existingPayments) {
      setPayments(existingPayments.map((p, i) => ({
        key: `e-${p.id}-${i}`, amount: Number(p.amount) || 0, paid_on: p.paid_on ?? new Date().toISOString().slice(0, 10), mode: p.mode,
      })));
    }
  }, [existingPayments, open, row]);

  const collected = payments.reduce((acc, p) => acc + (Number(p.amount) || 0), 0);
  // Don't let a save land before existing instalments hydrate (would wipe them).
  const paymentsHydrating = Boolean(row) && existingPayments === undefined;

  async function onSubmit(v: FormValues) {
    if (paymentsHydrating) return;
    const dated = payments.filter((p) => (Number(p.amount) || 0) !== 0).sort((a, b) => (a.paid_on < b.paid_on ? 1 : -1));
    const latest = dated[0];
    const payload = { year_id: yearId, name: v.name.trim(), contact: v.contact || null, bill_no: v.bill_no || null,
      projected: v.projected || 0, billed: v.billed || 0, collected, mode: latest?.mode ?? null, collected_on: latest?.paid_on ?? null,
      comments: v.comments || null, updated_at: new Date().toISOString(),
      payments: dated.map((p) => ({ amount: Number(p.amount) || 0, paid_on: p.paid_on || null, mode: p.mode })) };
    if (row) await update.mutateAsync({ id: row.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  const due = (Number(watch("billed")) || 0) - collected;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader><DialogTitle>{row ? "Edit entry" : "Add other income"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Source / name" required><Input {...register("name", { required: true })} /></Field>
            <Field label="Contact"><Input {...register("contact")} inputMode="tel" /></Field>
          </div>

          <div className="space-y-4 rounded-xl border border-border bg-muted/30 p-4">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">This year</p>
            <div className="grid gap-4 sm:grid-cols-3">
              <Field label="Projected"><MoneyInput step="1" {...register("projected", { valueAsNumber: true })} /></Field>
              <Field label="Bill no."><Input {...register("bill_no")} /></Field>
              <Field label="Billed"><MoneyInput step="1" {...register("billed", { valueAsNumber: true })} /></Field>
            </div>
            <Field label="Due" hint={due > 0 ? "Outstanding" : "Cleared"}><Input readOnly value={money(due)} className="bg-muted text-muted-foreground font-semibold" /></Field>
            <CollectionPaymentsField payments={payments} onChange={setPayments} />
          </div>

          <Field label="Comments"><Textarea {...register("comments")} rows={2} /></Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending || paymentsHydrating}>{paymentsHydrating ? "Loading…" : row ? "Save" : "Add"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function OtherIncomesPage() {
  const { activeYearId, previousYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_income");
  const { data = [], isLoading } = useSpecialDonations(activeYearId);
  const { data: priorData = [] } = useSpecialDonations(previousYearId);
  const { remove } = useEntityMutations("special_donations", ["special_donations"], "Other income");
  const { data: modes = [] } = useLookups("payment_mode");
  const [search, setSearch] = useState("");
  const [mode, setMode] = useState<string | null>(null);
  const [dueFilter, setDueFilter] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<SpecialDonation | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((r) => {
      if (q && !r.name.toLowerCase().includes(q)) return false;
      if (mode && r.mode !== mode) return false;
      if (dueFilter === "due" && r.due <= 0) return false;
      if (dueFilter === "cleared" && r.due > 0) return false;
      return true;
    });
  }, [data, search, mode, dueFilter]);

  // These rows aren't linked to a durable donor/advertiser record, so there's
  // no per-entry carry-forward — only a page-level placeholder: last year's
  // billed total, until this year's entries add up to something themselves.
  const projection = projectedWithFallback(filtered, priorData);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Other Incomes" description="Income that isn't membership or advertising" icon={<Sparkles />}
        actions={
          <>
            <Button variant="outline" size="icon" title="Export CSV"
              onClick={() => exportCsv("other-incomes", filtered.map((r) => ({
                Name: r.name, Contact: r.contact, Billed: r.billed, Collected: r.collected, Due: r.due, Mode: r.mode,
              })))}>
              <Download className="h-4 w-4" />
            </Button>
            {canWrite && (
              <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> <span className="hidden sm:inline">Add income</span></Button>
            )}
          </>
        }
      />

      <SummaryBar items={[
        { label: "Projected", value: projection.value, hint: projection.fromPriorYear ? "from last year's billed" : undefined },
        { label: "Billed", value: sum(filtered, (r) => r.billed) },
        { label: "Collected", value: sum(filtered, (r) => r.collected), tone: "success" },
        { label: "Due", value: sum(filtered, (r) => r.due), tone: "destructive" },
      ]} />

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search…" className="pl-9" />
        </div>
        <FilterMenu
          activeCount={(mode ? 1 : 0) + (dueFilter ? 1 : 0)}
          onClear={() => { setMode(null); setDueFilter(null); }}
        >
          <FilterField label="Mode">
            <OptionSelect value={mode} onChange={setMode} allowClear
              options={modes.map((l) => ({ value: l.label, label: l.label }))}
              placeholder="All modes" className="w-full" />
          </FilterField>
          <FilterField label="Due">
            <OptionSelect value={dueFilter} onChange={setDueFilter} allowClear
              options={[{ value: "due", label: "Has due" }, { value: "cleared", label: "Cleared" }]}
              placeholder="All" className="w-full" />
          </FilterField>
        </FilterMenu>
      </div>

      {isLoading ? <TableSkeleton />
        : filtered.length === 0 ? (
          <EmptyState icon={<Sparkles />} title="No other incomes recorded"
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add income</Button>} />
        ) : (
          <Card className="divide-y divide-border">
            {filtered.map((r) => (
              <div key={r.id} className="flex items-center gap-3 p-3.5 sm:px-4">
                <Avatar className="h-9 w-9"><AvatarFallback>{initials(r.name)}</AvatarFallback></Avatar>
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium">{r.name}</p>
                  <p className="truncate text-xs text-muted-foreground">
                    {r.bill_no ? `Bill ${r.bill_no}` : "—"}{r.collected_on ? ` · ${fmtDate(r.collected_on)}` : ""}
                    {r.comments ? ` · ${r.comments}` : ""}
                  </p>
                </div>
                <div className="text-right">
                  <p className="font-semibold tabular text-success">{money(r.collected)}</p>
                  {r.due > 0 && <p className="text-xs text-destructive">Due {money(r.due)}</p>}
                </div>
                {canWrite && (
                  <div className="flex shrink-0 gap-0.5">
                    <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(r); setOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                    <ConfirmDialog title="Delete entry?" destructive confirmLabel="Delete"
                      onConfirm={() => remove.mutateAsync(r.id)}
                      trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                  </div>
                )}
              </div>
            ))}
          </Card>
        )}

      {activeYearId && <SpecialDialog open={open} onOpenChange={setOpen} row={editing} yearId={activeYearId} />}
    </div>
  );
}
