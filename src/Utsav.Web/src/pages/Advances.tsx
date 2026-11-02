import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  HandCoins, Plus, Search, Pencil, Lock, CheckCircle2, Undo2, Receipt, Trash2,
} from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useAdvances, sum } from "@/hooks/useFinance";
import { useCollectors } from "@/hooks/useReference";
import { api } from "@/lib/api";
import { money, fmtDate, pct } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { cn } from "@/lib/utils";
import type { AdvanceSettlement, AdvanceStatus, AdvanceWithBalance } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { useLookups } from "@/hooks/useReference";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Field } from "@/components/common/Field";
import { LookupSelect, OptionSelect } from "@/components/form/OptionSelect";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

const STATUS_TONE: Record<AdvanceStatus, "default" | "success" | "destructive" | "gold"> = {
  open: "gold", settled: "success", cancelled: "destructive",
};

// ---------------- Issue / edit an advance ----------------
interface AdvanceForm {
  issued_to_collector_id: string | null; amount: number; issued_on: string; purpose: string; mode: string | null;
}
const emptyAdvance: AdvanceForm = { issued_to_collector_id: null, amount: 0, issued_on: "", purpose: "", mode: null };

function AdvanceDialog({
  open, onOpenChange, advance, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; advance?: AdvanceWithBalance | null; yearId: string }) {
  const qc = useQueryClient();
  const { data: collectors = [] } = useCollectors();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<AdvanceForm>({ defaultValues: emptyAdvance });

  useEffect(() => {
    reset(advance
      ? { issued_to_collector_id: advance.issued_to_collector_id, amount: advance.amount, issued_on: advance.issued_on ?? "", purpose: advance.purpose ?? "", mode: advance.mode }
      : emptyAdvance);
  }, [advance, open, reset]);

  async function onSubmit(v: AdvanceForm) {
    if (!v.issued_to_collector_id) { toast.error("Pick who received the cash"); return; }
    const member = collectors.find((c) => c.id === v.issued_to_collector_id);
    const payload = {
      ...v, year_id: yearId, amount: v.amount || 0, purpose: v.purpose || null, issued_on: v.issued_on || null,
      issued_to_name: member?.name ?? "",
    };
    if (advance) await api.patch(`/advances/${advance.id}`, payload);
    else await api.post("/advances", payload);
    qc.invalidateQueries({ queryKey: ["advances"] });
    toast.success(advance ? "Advance updated" : "Advance issued");
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{advance ? "Edit advance" : "Issue cash advance"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="Issued to" required>
            <OptionSelect value={watch("issued_to_collector_id")}
              onChange={(v) => setValue("issued_to_collector_id", v)}
              options={collectors.map((c) => ({ value: c.id, label: c.name }))}
              placeholder="Select a member" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Amount" required>
              <MoneyInput step="0.01" {...register("amount", { valueAsNumber: true, required: true })} />
            </Field>
            <Field label="Date issued">
              <Input type="date" {...register("issued_on")} />
            </Field>
          </div>
          <Field label="Purpose">
            <Input {...register("purpose")} placeholder="What's this cash for?" />
          </Field>
          <Field label="Mode">
            <LookupSelect category="payment_mode" value={watch("mode")}
              onChange={(v) => setValue("mode", v)} placeholder="How was it handed over" allowClear />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit">{advance ? "Save changes" : "Issue advance"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ---------------- Settle dialog ----------------
interface SettleForm {
  kind: "expense" | "return";
  amount: number; settled_on: string;
  category: string | null; details: string; bill_no: string;
  note: string;
}
function emptySettle(maxAmount: number): SettleForm {
  return { kind: "expense", amount: Number(maxAmount.toFixed(2)), settled_on: "", category: null, details: "", bill_no: "", note: "" };
}

function SettleDialog({
  open, onOpenChange, advance,
}: { open: boolean; onOpenChange: (v: boolean) => void; advance: AdvanceWithBalance | null }) {
  const qc = useQueryClient();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<SettleForm>({ defaultValues: emptySettle(0) });

  const { data: settlements = [] } = useQuery({
    queryKey: ["advance_settlements", advance?.id],
    queryFn: () => api.get<AdvanceSettlement[]>(`/advances/${advance!.id}/settlements`),
    enabled: Boolean(advance),
  });

  useEffect(() => {
    if (advance) reset(emptySettle(advance.outstanding));
  }, [advance, open, reset]);

  const kind = watch("kind");

  async function onSubmit(v: SettleForm) {
    if (!advance) return;
    const payload: Record<string, unknown> = { kind: v.kind, amount: v.amount || 0, settled_on: v.settled_on || null };
    if (v.kind === "expense") {
      payload.category = v.category;
      payload.details = v.details || null;
      payload.bill_no = v.bill_no || null;
    } else {
      payload.note = v.note || null;
    }
    await api.post(`/advances/${advance.id}/settle`, payload);
    qc.invalidateQueries({ queryKey: ["advances"] });
    qc.invalidateQueries({ queryKey: ["advance_settlements", advance.id] });
    qc.invalidateQueries({ queryKey: ["expenses"] });
    toast.success("Settlement recorded");
    reset(emptySettle(0));
  }

  async function removeSettlement(id: string) {
    if (!advance) return;
    await api.del(`/advance_settlements/${id}`);
    qc.invalidateQueries({ queryKey: ["advances"] });
    qc.invalidateQueries({ queryKey: ["advance_settlements", advance.id] });
    qc.invalidateQueries({ queryKey: ["expenses"] });
    toast.success("Settlement removed");
  }

  if (!advance) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Settle advance — {advance.issued_to_name}</DialogTitle>
        </DialogHeader>

        <div className="rounded-lg border border-border bg-muted/40 p-3 text-sm">
          <div className="flex justify-between"><span className="text-muted-foreground">Issued</span><span className="tabular font-medium">{money(advance.amount)}</span></div>
          <div className="flex justify-between"><span className="text-muted-foreground">Spent (bills)</span><span className="tabular">{money(advance.spent)}</span></div>
          <div className="flex justify-between"><span className="text-muted-foreground">Returned</span><span className="tabular">{money(advance.returned)}</span></div>
          <div className="mt-1 flex justify-between border-t border-border pt-1 font-semibold">
            <span>Outstanding</span>
            <span className={advance.outstanding > 0 ? "text-destructive" : "text-success"}>{money(advance.outstanding)}</span>
          </div>
        </div>

        {settlements.length > 0 && (
          <div className="space-y-1.5">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Settlement history</p>
            {settlements.map((s) => (
              <div key={s.id} className="flex items-center justify-between rounded-lg bg-muted/40 px-3 py-2 text-sm">
                <div className="flex items-center gap-2">
                  {s.kind === "expense" ? <Receipt className="h-3.5 w-3.5 text-muted-foreground" /> : <Undo2 className="h-3.5 w-3.5 text-muted-foreground" />}
                  <span>{s.kind === "expense" ? "Bill" : "Cash returned"}</span>
                  {s.settled_on && <span className="text-xs text-muted-foreground">· {fmtDate(s.settled_on)}</span>}
                </div>
                <div className="flex items-center gap-2">
                  <span className="tabular font-medium">{money(s.amount)}</span>
                  <ConfirmDialog title="Remove settlement?" destructive confirmLabel="Remove"
                    description={s.kind === "expense" ? "This also deletes the linked expense entry." : undefined}
                    onConfirm={() => removeSettlement(s.id)}
                    trigger={<Button variant="ghost" size="icon" className="h-6 w-6 text-destructive"><Trash2 className="h-3.5 w-3.5" /></Button>} />
                </div>
              </div>
            ))}
          </div>
        )}

        {advance.status !== "settled" && advance.outstanding > 0.01 ? (
          <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-3 border-t border-border pt-4">
            <div className="flex gap-2">
              <Button type="button" size="sm" variant={kind === "expense" ? "default" : "outline"} onClick={() => setValue("kind", "expense")} className="flex-1">
                <Receipt className="h-3.5 w-3.5" /> Bill received
              </Button>
              <Button type="button" size="sm" variant={kind === "return" ? "default" : "outline"} onClick={() => setValue("kind", "return")} className="flex-1">
                <Undo2 className="h-3.5 w-3.5" /> Cash returned
              </Button>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Amount" required>
                <MoneyInput step="0.01" max={advance.outstanding}
                  {...register("amount", { valueAsNumber: true, required: true })} />
              </Field>
              <Field label="Date">
                <Input type="date" {...register("settled_on")} />
              </Field>
            </div>
            {kind === "expense" ? (
              <>
                <Field label="Expense category" required>
                  <LookupSelect category="expense_category" value={watch("category")}
                    onChange={(v) => setValue("category", v)} placeholder="Select category" />
                </Field>
                <div className="grid gap-3 sm:grid-cols-2">
                  <Field label="Details">
                    <Input {...register("details")} placeholder="What was bought" />
                  </Field>
                  <Field label="Bill no.">
                    <Input {...register("bill_no")} />
                  </Field>
                </div>
              </>
            ) : (
              <Field label="Note">
                <Textarea {...register("note")} rows={2} placeholder="Optional note" />
              </Field>
            )}
            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Close</Button>
              <Button type="submit">{kind === "expense" ? "Record bill" : "Record return"}</Button>
            </DialogFooter>
          </form>
        ) : (
          <div className="flex items-center justify-center gap-2 rounded-lg bg-success/10 py-3 text-sm font-medium text-success">
            <CheckCircle2 className="h-4 w-4" /> Fully settled
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ---------------- Page ----------------
export function AdvancesPage() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_advances");
  const { data = [], isLoading, isError, error } = useAdvances(activeYearId);
  const { data: modes = [] } = useLookups("payment_mode");
  const [search, setSearch] = useState("");
  const [mode, setMode] = useState<string | null>(null);
  const [tab, setTab] = useState<"open" | "settled">("open");
  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState<AdvanceWithBalance | null>(null);
  const [settleOpen, setSettleOpen] = useState(false);
  const [settling, setSettling] = useState<AdvanceWithBalance | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((a) => {
      if (tab === "open" && a.status !== "open") return false;
      if (tab === "settled" && a.status === "open") return false;
      if (q && !a.issued_to_name.toLowerCase().includes(q) && !(a.purpose ?? "").toLowerCase().includes(q)) return false;
      if (mode && a.mode !== mode) return false;
      return true;
    });
  }, [data, search, tab, mode]);

  const totalOutstanding = sum(data.filter((a) => a.status === "open"), (a) => a.outstanding);
  const totalIssued = sum(data, (a) => a.amount);
  const openCount = data.filter((a) => a.status === "open").length;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Cash Advances" description="Float given out before the bill comes in" icon={<HandCoins />}
        actions={canWrite && (
          <Button onClick={() => { setEditing(null); setEditOpen(true); }}>
            <Plus /> <span className="hidden sm:inline">Issue advance</span>
          </Button>
        )}
      />

      <SummaryBar items={[
        { label: "Issued (all time)", value: totalIssued, tone: "primary" },
        { label: "Outstanding", value: totalOutstanding, tone: totalOutstanding > 0 ? "destructive" : "success" },
        { label: "Open advances", value: openCount, isMoney: false },
        { label: "Total advances", value: data.length, isMoney: false },
      ]} />

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <Tabs value={tab} onValueChange={(v) => setTab(v as "open" | "settled")}>
          <TabsList>
            <TabsTrigger value="open">Open</TabsTrigger>
            <TabsTrigger value="settled">Settled</TabsTrigger>
          </TabsList>
        </Tabs>
        <div className="flex gap-2">
          <div className="relative sm:w-64">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search…" className="pl-9" />
          </div>
          <FilterMenu activeCount={mode ? 1 : 0} onClear={() => setMode(null)}>
            <FilterField label="Mode">
              <select value={mode ?? ""} onChange={(e) => setMode(e.target.value || null)} className="h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm">
                <option value="">All modes</option>
                {modes.map((l) => <option key={l.id} value={l.label}>{l.label}</option>)}
              </select>
            </FilterField>
          </FilterMenu>
        </div>
      </div>

      {!canWrite && (
        <p className="flex items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
          <Lock className="h-3.5 w-3.5" /> View only — advances are managed by the committee admin.
        </p>
      )}

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<HandCoins />} title={tab === "open" ? "No open advances" : "No settled advances"}
            description={canWrite ? "Issue cash to a member before they collect a bill for it." : "Nothing here yet."} />
        ) : (
          <Card className="divide-y divide-border">
            {filtered.map((a) => {
              const settledPct = pct(a.settled, a.amount || 1);
              return (
                <div
                  key={a.id}
                  className={cn("space-y-2 p-3.5 sm:px-4", canWrite && "cursor-pointer hover:bg-muted/40")}
                  onClick={() => { if (canWrite) { setEditing(a); setEditOpen(true); } }}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <p className="truncate font-medium">{a.issued_to_name}</p>
                        <Badge variant={STATUS_TONE[a.status]} className="capitalize">{a.status}</Badge>
                      </div>
                      <p className="truncate text-xs text-muted-foreground">
                        {a.purpose || "No purpose noted"} {a.issued_on ? `· ${fmtDate(a.issued_on)}` : ""}
                      </p>
                    </div>
                    <div className="flex shrink-0 items-center gap-1" onClick={(e) => e.stopPropagation()}>
                      {canWrite && a.status === "open" && (
                        <Button size="sm" variant="outline" onClick={() => { setSettling(a); setSettleOpen(true); }}>
                          Settle
                        </Button>
                      )}
                      {canWrite && (
                        <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(a); setEditOpen(true); }}>
                          <Pencil className="h-4 w-4" />
                        </Button>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-3">
                    <Progress value={Math.min(settledPct, 100)} className="h-2 flex-1" />
                    <p className="shrink-0 text-xs tabular text-muted-foreground">
                      {money(a.settled)} / {money(a.amount)}
                      {a.outstanding > 0.01 && <span className="ml-1 text-destructive">· {money(a.outstanding)} due</span>}
                    </p>
                  </div>
                </div>
              );
            })}
          </Card>
        )}

      {activeYearId && <AdvanceDialog open={editOpen} onOpenChange={setEditOpen} advance={editing} yearId={activeYearId} />}
      {/* Re-derive from the live list every render, not the frozen snapshot
          captured when "Settle" was clicked — otherwise the balance summary
          inside the dialog goes stale after the first settlement. */}
      <SettleDialog open={settleOpen} onOpenChange={setSettleOpen}
        advance={(settling && data.find((a) => a.id === settling.id)) ?? settling} />
    </div>
  );
}
