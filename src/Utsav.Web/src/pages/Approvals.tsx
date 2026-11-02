import { useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { ClipboardCheck, CheckCheck, HandCoins, ReceiptText, Wallet, Undo2 } from "lucide-react";
import { api } from "@/lib/api";
import { useAuth } from "@/context/AuthContext";
import { useYear } from "@/context/YearContext";
import { useApprovals, useMyApprovals, useExpenses } from "@/hooks/useFinance";
import { money, fmtDate } from "@/lib/format";
import { isOpen, statusLabel } from "@/lib/approval";
import type { ApprovalItem, Expense } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { EmptyState, ErrorState, TableSkeleton } from "@/components/common/states";
import { ApprovalBadge } from "@/components/finance/ApprovalBadge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { MoneyInput } from "@/components/ui/money-input";
import { Field } from "@/components/common/Field";
import { LookupSelect } from "@/components/form/OptionSelect";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";

/** Everything these actions can change, so every figure on every page is fresh afterwards. */
function useRefreshAll() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries();
}

type Action = "approve" | "send_back" | "reject";

function ReviewDialog({ item, onClose }: { item: ApprovalItem | null; onClose: () => void }) {
  const refresh = useRefreshAll();
  const [amount, setAmount] = useState<number | null>(null);
  const [note, setNote] = useState("");

  const decide = useMutation<void, Error, Action>({
    mutationFn: async (action) => {
      await api.post(`/approvals/${item!.kind}/${item!.id}/${action}`, {
        ...(action === "approve" && amount != null && amount !== item!.amount ? { amount } : {}),
        ...(note.trim() ? { note: note.trim() } : {}),
      });
    },
    onSuccess: (_d, action) => {
      toast.success(action === "approve" ? "Approved" : action === "send_back" ? "Sent back" : "Rejected");
      refresh();
      onClose();
    },
    onError: (e) => toast.error(e.message),
  });

  if (!item) return null;
  const value = amount ?? item.amount;
  const less = value < item.amount;
  const needsNote = !note.trim();

  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>{item.label || item.source}</DialogTitle></DialogHeader>
        <div className="space-y-4">
          <p className="text-sm text-muted-foreground">
            {item.source} · recorded by <span className="font-medium text-foreground">{item.recorded_by_name ?? "unknown"}</span>
            {item.on_date ? ` · ${fmtDate(item.on_date)}` : ""}{item.mode ? ` · ${item.mode}` : ""}
          </p>
          <Field label={item.kind === "income" ? "Amount received" : "Amount approved"}>
            <MoneyInput step="0.01" value={value} onChange={(e) => setAmount(Number(e.target.value))} />
          </Field>
          {less && (
            <p className="rounded-lg bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
              {item.kind === "income"
                ? `${money(item.amount - value)} stays with ${item.recorded_by_name ?? "the member"} until it is handed over.`
                : item.paid_from === "own_pocket"
                  ? `${money(item.amount - value)} will not be reimbursed.`
                  : `${money(item.amount - value)} is dropped from this expense.`}
            </p>
          )}
          <Field label="Note (required to send back or reject)">
            <Textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} placeholder="Why?" />
          </Field>
        </div>
        <DialogFooter className="gap-2 sm:justify-between">
          <div className="flex gap-2">
            <Button variant="outline" disabled={decide.isPending || needsNote} onClick={() => decide.mutate("send_back")}>Send back</Button>
            <Button variant="outline" className="text-destructive" disabled={decide.isPending || needsNote} onClick={() => decide.mutate("reject")}>Reject</Button>
          </div>
          <Button disabled={decide.isPending || value <= 0 || value > item.amount} onClick={() => decide.mutate("approve")}>
            Approve {money(value)}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RefundDialog({ expense, onClose }: { expense: Expense | null; onClose: () => void }) {
  const refresh = useRefreshAll();
  const [on, setOn] = useState(new Date().toISOString().slice(0, 10));
  const [mode, setMode] = useState<string | null>(null);
  const refund = useMutation<void, Error>({
    mutationFn: async () => { await api.post(`/expenses/${expense!.id}/refund`, { refunded_on: on, mode }); },
    onSuccess: () => { toast.success("Marked as refunded"); refresh(); onClose(); },
    onError: (e) => toast.error(e.message),
  });
  if (!expense) return null;
  return (
    <Dialog open onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader><DialogTitle>Pay back {expense.recorded_by_name ?? "member"}</DialogTitle></DialogHeader>
        <p className="text-sm text-muted-foreground">{money(expense.amount)} · {expense.details || expense.category}</p>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Paid back on"><Input type="date" value={on} onChange={(e) => setOn(e.target.value)} /></Field>
          <Field label="Paid from">
            <LookupSelect category="payment_mode" value={mode} onChange={setMode} placeholder="Cash or bank" />
          </Field>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button disabled={!mode || !on || refund.isPending} onClick={() => refund.mutate()}>Record refund</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ItemRow({ item, onReview }: { item: ApprovalItem; onReview?: (i: ApprovalItem) => void }) {
  return (
    <div className="flex items-center gap-3 p-3.5 sm:px-4">
      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
        {item.kind === "income" ? <HandCoins className="h-4 w-4" /> : <ReceiptText className="h-4 w-4" />}
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="truncate font-medium">{item.label || item.source}</p>
          <Badge variant="outline">{item.source}</Badge>
          {item.paid_from === "own_pocket" && <Badge variant="outline">Paid from own pocket</Badge>}
          <ApprovalBadge status={item.status} />
        </div>
        <p className="truncate text-xs text-muted-foreground">
          {item.recorded_by_name ?? "unknown"} · {item.on_date ? fmtDate(item.on_date) : "no date"}{item.mode ? ` · ${item.mode}` : ""} · {item.year}
        </p>
        {item.note && <p className="mt-0.5 text-xs italic text-muted-foreground">“{item.note}”</p>}
      </div>
      <p className="shrink-0 font-semibold tabular">{money(item.amount)}</p>
      {onReview && item.status === "pending" && (
        <Button size="sm" onClick={() => onReview(item)}>Review</Button>
      )}
    </div>
  );
}

function Queue() {
  const refresh = useRefreshAll();
  const { data = [], isLoading, isError, error } = useApprovals();
  const [reviewing, setReviewing] = useState<ApprovalItem | null>(null);

  const byMember = useMemo(() => {
    const groups = new Map<string, ApprovalItem[]>();
    for (const item of data) {
      const key = item.recorded_by_name ?? "Unknown";
      groups.set(key, [...(groups.get(key) ?? []), item]);
    }
    return [...groups.entries()].sort((a, b) => b[1].length - a[1].length);
  }, [data]);

  const approveAll = useMutation<{ approved: number; total: number }, Error, string>({
    mutationFn: (name) => api.post("/approvals/approve_member", { name }),
    onSuccess: (r) => { toast.success(`Approved ${r.approved} item${r.approved === 1 ? "" : "s"} · ${money(r.total)}`); refresh(); },
    onError: (e) => toast.error(e.message),
  });

  if (isLoading) return <TableSkeleton />;
  if (isError) return <ErrorState message={(error as Error)?.message} />;
  if (data.length === 0) {
    return <EmptyState icon={<CheckCheck />} title="Nothing waiting" description="Everything recorded has been dealt with." />;
  }

  return (
    <div className="space-y-4">
      {byMember.map(([name, items]) => {
        const pending = items.filter((i) => i.status === "pending");
        return (
          <Card key={name}>
            <div className="flex items-center justify-between gap-3 border-b border-border p-3.5 sm:px-4">
              <div>
                <p className="font-semibold">{name}</p>
                <p className="text-xs text-muted-foreground">
                  {items.length} item{items.length === 1 ? "" : "s"} · {money(items.reduce((a, i) => a + i.amount, 0))}
                </p>
              </div>
              {pending.length > 1 && (
                <Button size="sm" variant="outline" disabled={approveAll.isPending} onClick={() => approveAll.mutate(name)}>
                  <CheckCheck /> Approve all {pending.length}
                </Button>
              )}
            </div>
            <div className="divide-y divide-border">
              {items.map((i) => <ItemRow key={`${i.kind}-${i.id}`} item={i} onReview={setReviewing} />)}
            </div>
          </Card>
        );
      })}
      <ReviewDialog item={reviewing} onClose={() => setReviewing(null)} />
    </div>
  );
}

/** Approved claims a member paid for themselves that the committee has not paid back yet. */
function OwedSection() {
  const { activeYearId } = useYear();
  const { data = [] } = useExpenses(activeYearId);
  const [paying, setPaying] = useState<Expense | null>(null);
  const owed = data.filter((e) => e.status === "approved" && e.paid_from === "own_pocket" && !e.refunded_on);
  if (owed.length === 0) return null;
  return (
    <section className="space-y-2">
      <h2 className="flex items-center gap-2 text-sm font-semibold"><Wallet className="h-4 w-4" /> Owed to members</h2>
      <Card className="divide-y divide-border">
        {owed.map((e) => (
          <div key={e.id} className="flex items-center gap-3 p-3.5 sm:px-4">
            <div className="min-w-0 flex-1">
              <p className="truncate font-medium">{e.recorded_by_name ?? "Member"}</p>
              <p className="truncate text-xs text-muted-foreground">{e.details || e.category} · {fmtDate(e.spent_on)}</p>
            </div>
            <p className="font-semibold tabular">{money(e.amount)}</p>
            <Button size="sm" variant="outline" onClick={() => setPaying(e)}><Undo2 /> Mark refunded</Button>
          </div>
        ))}
      </Card>
      <RefundDialog expense={paying} onClose={() => setPaying(null)} />
    </section>
  );
}

function MyItems() {
  const { data = [], isLoading } = useMyApprovals();
  if (isLoading || data.length === 0) return null;
  return (
    <section className="space-y-2">
      <h2 className="text-sm font-semibold">Your submissions</h2>
      <p className="text-xs text-muted-foreground">
        Waiting for Finance to confirm. Sent-back items can be fixed on their own page and will go back into the queue.
      </p>
      <Card className="divide-y divide-border">
        {data.map((i) => <ItemRow key={`${i.kind}-${i.id}`} item={i} />)}
      </Card>
    </section>
  );
}

export function ApprovalsPage() {
  const { can } = useAuth();
  const approver = can("approve_money");
  const { data = [] } = useApprovals(approver);
  const open = data.filter((i) => isOpen(i.status)).length;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Approvals" icon={<ClipboardCheck />}
        description={approver
          ? `Money your members have recorded${open ? ` · ${open} waiting` : ""} — nothing reaches the fund until you confirm it`
          : "Your payments and expenses, and what Finance has decided about them"}
      />
      {approver && <Queue />}
      {approver && <OwedSection />}
      <MyItems />
      {!approver && <p className="text-xs text-muted-foreground">Only Finance or Admin can approve. {statusLabel("pending")} entries are not in the fund yet.</p>}
    </div>
  );
}
