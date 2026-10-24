import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { Wallet, Plus, Pencil, Trash2, ArrowDownLeft, ArrowUpRight, Lock } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useCashLedger, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { money, fmtDate } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { cn } from "@/lib/utils";
import type { CashLedger } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

interface FormValues { party: string; direction: "in" | "out"; magnitude: number; entry_date: string; note: string }
const empty: FormValues = { party: "", direction: "in", magnitude: 0, entry_date: "", note: "" };

function CashDialog({
  open, onOpenChange, row, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; row?: CashLedger | null; yearId: string }) {
  const { create, update } = useEntityMutations("cash_ledger", ["cash_ledger"], "Entry");
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: empty });

  useEffect(() => {
    reset(row ? {
      party: row.party, direction: row.amount >= 0 ? "in" : "out", magnitude: Math.abs(row.amount),
      entry_date: row.entry_date ?? "", note: row.note ?? "",
    } : empty);
  }, [row, open, reset]);

  async function onSubmit(v: FormValues) {
    const signed = (v.direction === "out" ? -1 : 1) * Math.abs(Number(v.magnitude) || 0);
    const payload = { year_id: yearId, party: v.party.trim(), amount: signed, entry_date: v.entry_date || null, note: v.note || null };
    if (row) await update.mutateAsync({ id: row.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  const dir = watch("direction");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{row ? "Edit entry" : "Add cash entry"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="Party / person" required><Input {...register("party", { required: true })} placeholder="Who received / gave" /></Field>
          <div className="grid grid-cols-2 gap-2">
            <button type="button" onClick={() => setValue("direction", "in")}
              className={cn("flex items-center justify-center gap-2 rounded-lg border p-3 text-sm font-medium transition-colors",
                dir === "in" ? "border-success bg-success/10 text-success" : "border-border text-muted-foreground")}>
              <ArrowDownLeft className="h-4 w-4" /> Into fund
            </button>
            <button type="button" onClick={() => setValue("direction", "out")}
              className={cn("flex items-center justify-center gap-2 rounded-lg border p-3 text-sm font-medium transition-colors",
                dir === "out" ? "border-destructive bg-destructive/10 text-destructive" : "border-border text-muted-foreground")}>
              <ArrowUpRight className="h-4 w-4" /> Out / held
            </button>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Amount" required><Input type="number" step="1" {...register("magnitude", { valueAsNumber: true, required: true })} /></Field>
            <Field label="Date"><Input type="date" {...register("entry_date")} /></Field>
          </div>
          <Field label="Note"><Input {...register("note")} /></Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>{row ? "Save" : "Add entry"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function CashLedgerPage() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_finance");
  const { data = [], isLoading } = useCashLedger(activeYearId);
  const { remove } = useEntityMutations("cash_ledger", ["cash_ledger"], "Entry");
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<CashLedger | null>(null);

  const inflow = useMemo(() => sum(data.filter((r) => r.amount > 0), (r) => r.amount), [data]);
  const outflow = useMemo(() => sum(data.filter((r) => r.amount < 0), (r) => r.amount), [data]);
  const net = inflow + outflow;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Cash Ledger" description="Petty cash & who's holding money" icon={<Wallet />}
        actions={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> <span className="hidden sm:inline">Add entry</span></Button>}
      />

      <SummaryBar items={[
        { label: "Into Fund", value: inflow, tone: "success" },
        { label: "Out / Held", value: Math.abs(outflow), tone: "destructive" },
        { label: "Net Position", value: net, tone: net >= 0 ? "success" : "destructive" },
        { label: "Entries", value: data.length, isMoney: false },
      ]} />

      {!canWrite && (
        <p className="flex items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
          <Lock className="h-3.5 w-3.5" /> View only — cash ledger is managed by the committee admin.
        </p>
      )}

      {isLoading ? <TableSkeleton />
        : data.length === 0 ? (
          <EmptyState icon={<Wallet />} title="No cash entries"
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add entry</Button>} />
        ) : (
          <Card className="divide-y divide-border">
            {data.map((r) => {
              const isIn = r.amount >= 0;
              return (
                <div key={r.id} className="flex items-center gap-3 p-3.5 sm:px-4">
                  <div className={cn("flex h-10 w-10 shrink-0 items-center justify-center rounded-lg",
                    isIn ? "bg-success/12 text-success" : "bg-destructive/12 text-destructive")}>
                    {isIn ? <ArrowDownLeft className="h-5 w-5" /> : <ArrowUpRight className="h-5 w-5" />}
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{r.party}</p>
                    <p className="truncate text-xs text-muted-foreground">{r.note || "—"}{r.entry_date ? ` · ${fmtDate(r.entry_date)}` : ""}</p>
                  </div>
                  <p className={cn("shrink-0 font-semibold tabular", isIn ? "text-success" : "text-destructive")}>
                    {isIn ? "+" : "−"}{money(Math.abs(r.amount))}
                  </p>
                  {canWrite && (
                    <div className="flex shrink-0 gap-0.5">
                      <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(r); setOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                      <ConfirmDialog title="Delete entry?" destructive confirmLabel="Delete"
                        onConfirm={() => remove.mutateAsync(r.id)}
                        trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                    </div>
                  )}
                </div>
              );
            })}
          </Card>
        )}

      {activeYearId && <CashDialog open={open} onOpenChange={setOpen} row={editing} yearId={activeYearId} />}
    </div>
  );
}
