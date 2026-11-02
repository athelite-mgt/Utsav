import { Plus, Trash2, UserCheck } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { money } from "@/lib/format";
import { MoneyInput } from "@/components/ui/money-input";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { LookupSelect } from "@/components/form/OptionSelect";

/** One editable instalment row in a collection. `key` is a stable client id for list rendering. */
export interface PaymentDraft {
  key: string;
  amount: number;
  paid_on: string;
  mode: string | null;
}

const today = () => new Date().toISOString().slice(0, 10);

let seq = 0;
export function newPayment(mode: string | null = "Cash"): PaymentDraft {
  seq += 1;
  return { key: `p-${Date.now()}-${seq}`, amount: 0, paid_on: today(), mode };
}

/**
 * The dated part-payment ledger for a collection — each instalment its own
 * amount, date and mode, so "₹500 cash on Sasthi, ₹500 GPay on Nabami" is
 * recorded faithfully rather than collapsed into one figure. "Collected by" is
 * shown once, disabled, as the signed-in user: collections are attributed to
 * whoever recorded them in the app, never a free-typed name. Rows stack on
 * mobile so nothing is cramped.
 */
export function CollectionPaymentsField({
  payments, onChange,
}: {
  payments: PaymentDraft[];
  onChange: (next: PaymentDraft[]) => void;
}) {
  const { user } = useAuth();
  const total = payments.reduce((acc, p) => acc + (Number(p.amount) || 0), 0);

  const update = (key: string, patch: Partial<PaymentDraft>) =>
    onChange(payments.map((p) => (p.key === key ? { ...p, ...patch } : p)));
  const remove = (key: string) => onChange(payments.filter((p) => p.key !== key));
  const add = () => onChange([...payments, newPayment()]);

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Collections</p>
        <p className="text-xs text-muted-foreground">
          Total collected: <span className="font-semibold text-foreground">{money(total)}</span>
        </p>
      </div>

      {payments.length === 0 && (
        <p className="rounded-lg border border-dashed border-border px-3 py-2 text-xs text-muted-foreground">
          No collection recorded yet. Add an instalment when money comes in.
        </p>
      )}

      {payments.map((p) => (
        <div key={p.key} className="grid grid-cols-2 gap-2 rounded-lg border border-border p-2 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end sm:border-0 sm:p-0">
          <label className="space-y-1">
            <span className="text-[11px] text-muted-foreground sm:hidden">Amount</span>
            <MoneyInput step="1" value={p.amount || ""} onChange={(e) => update(p.key, { amount: Number(e.target.value) || 0 })} placeholder="Amount" />
          </label>
          <label className="space-y-1">
            <span className="text-[11px] text-muted-foreground sm:hidden">Date</span>
            <Input type="date" value={p.paid_on} onChange={(e) => update(p.key, { paid_on: e.target.value })} />
          </label>
          <label className="col-span-2 space-y-1 sm:col-span-1">
            <span className="text-[11px] text-muted-foreground sm:hidden">Mode</span>
            <LookupSelect category="payment_mode" value={p.mode} onChange={(v) => update(p.key, { mode: v })} placeholder="Mode" />
          </label>
          <Button type="button" variant="ghost" className="col-span-2 h-10 w-full justify-center gap-1.5 px-3 text-destructive sm:col-span-1 sm:w-10 sm:shrink-0 sm:px-0"
            onClick={() => remove(p.key)} aria-label="Remove instalment">
            <Trash2 className="h-4 w-4" />
            <span className="sm:hidden">Remove instalment</span>
          </Button>
        </div>
      ))}

      <div className="flex flex-wrap items-center justify-between gap-2 pt-0.5">
        <Button type="button" variant="outline" size="sm" onClick={add}>
          <Plus className="h-4 w-4" /> Add instalment
        </Button>
        <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <UserCheck className="h-3.5 w-3.5" />
          Recorded by {user?.full_name ?? user?.email ?? "you"} · automatic
        </span>
      </div>
    </div>
  );
}
