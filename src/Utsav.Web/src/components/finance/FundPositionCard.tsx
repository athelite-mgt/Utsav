import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Banknote, Landmark, ArrowLeftRight, ArrowRight, Info, WalletCards,
} from "lucide-react";
import { api } from "@/lib/api";
import { useAuth } from "@/context/AuthContext";
import { useFundPosition, useFundPositionBreakdown } from "@/hooks/useFinance";
import { money, fmtDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import type { FundSide, TransferDirection } from "@/types/db";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Field } from "@/components/common/Field";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import {
  Tooltip, TooltipContent, TooltipTrigger,
} from "@/components/ui/tooltip";

interface FormValues {
  direction: TransferDirection;
  amount: number;
  transfer_date: string;
  reference: string;
  note: string;
}

const today = () => new Date().toISOString().slice(0, 10);

function TransferDialog({
  open, onOpenChange, yearId, cash, bank,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  yearId: string;
  cash: number;
  bank: number;
}) {
  const qc = useQueryClient();
  const { register, handleSubmit, reset, watch, setValue, formState } = useForm<FormValues>({
    defaultValues: { direction: "cash_to_bank", amount: 0, transfer_date: today(), reference: "", note: "" },
  });

  useEffect(() => {
    if (open) reset({ direction: "cash_to_bank", amount: 0, transfer_date: today(), reference: "", note: "" });
  }, [open, reset]);

  const save = useMutation({
    mutationFn: (v: FormValues) =>
      api.post("/fund_transfers", {
        year_id: yearId,
        direction: v.direction,
        amount: Math.abs(Number(v.amount) || 0),
        transfer_date: v.transfer_date || null,
        reference: v.reference || null,
        note: v.note || null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["fund_position"] });
      qc.invalidateQueries({ queryKey: ["fund_transfers"] });
      toast.success("Transfer recorded");
      onOpenChange(false);
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const direction = watch("direction");
  const amount = Number(watch("amount")) || 0;
  const fromBalance = direction === "cash_to_bank" ? cash : bank;
  // A warning, not a block: the balances are derived from what's been
  // entered so far, and a committee mid-catch-up can legitimately be
  // transferring money the app hasn't seen come in yet.
  const overdrawn = amount > fromBalance;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Transfer between cash and bank</DialogTitle>
          <DialogDescription>
            Moves money between the committee's own two pockets. This is not income
            or an expense — the fund total doesn't change.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit((v) => save.mutate(v))} className="space-y-4">
          <div className="grid grid-cols-2 gap-2">
            {([
              { value: "cash_to_bank", from: "Cash", to: "Bank", icon: Banknote },
              { value: "bank_to_cash", from: "Bank", to: "Cash", icon: Landmark },
            ] as const).map((o) => (
              <button
                key={o.value}
                type="button"
                onClick={() => setValue("direction", o.value)}
                className={cn(
                  "flex flex-col items-center gap-1 rounded-xl border p-3 text-sm font-medium transition-colors",
                  direction === o.value
                    ? "border-primary bg-primary/5 text-primary"
                    : "border-border text-muted-foreground hover:bg-accent"
                )}
              >
                <o.icon className="h-4 w-4" />
                <span className="flex items-center gap-1">
                  {o.from} <ArrowRight className="h-3 w-3" /> {o.to}
                </span>
              </button>
            ))}
          </div>

          <Field
            label="Amount"
            required
            error={formState.errors.amount && "Enter an amount greater than zero"}
            hint={`Available in ${direction === "cash_to_bank" ? "cash" : "bank"}: ${money(fromBalance)}`}
          >
            <Input
              type="number" step="1" inputMode="numeric" autoFocus
              {...register("amount", { valueAsNumber: true, required: true, min: 1 })}
            />
          </Field>

          {overdrawn && amount > 0 && (
            <p className="rounded-lg bg-gold/10 px-3 py-2 text-xs text-gold">
              That's more than the {direction === "cash_to_bank" ? "cash" : "bank"} balance
              this app knows about. Recording it anyway is fine — just check nothing's missing.
            </p>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Date"><Input type="date" {...register("transfer_date")} /></Field>
            <Field label="Reference" hint="Slip / UTR no.">
              <Input {...register("reference")} placeholder="Optional" />
            </Field>
          </div>

          <Field label="Note">
            <Input {...register("note")} placeholder="e.g. Deposited chanda collection" />
          </Field>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? "Recording…" : "Record transfer"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function SideTile({
  label, hint, side, icon, tone, onClick,
}: {
  label: string;
  hint: string;
  side: FundSide;
  icon: React.ReactNode;
  tone: "success" | "primary";
  onClick?: () => void;
}) {
  const net = side.transfersIn - side.transfersOut;
  return (
    <div
      className={cn("flex-1 rounded-xl border border-border p-4", onClick && "cursor-pointer transition-colors hover:bg-accent")}
      onClick={onClick}
      role={onClick ? "button" : undefined}
    >
      <div className="flex items-center gap-2">
        <span className={cn(
          "flex h-8 w-8 items-center justify-center rounded-lg [&_svg]:size-4",
          tone === "success" ? "bg-success/12 text-success" : "bg-primary/10 text-primary"
        )}>
          {icon}
        </span>
        <div className="min-w-0">
          <p className="truncate text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {label}
          </p>
          <p className="truncate text-[11px] text-muted-foreground">{hint}</p>
        </div>
      </div>

      <p className={cn(
        "mt-3 text-2xl font-bold tabular tracking-tight",
        side.balance < 0 && "text-destructive"
      )}>
        {money(side.balance)}
      </p>

      <dl className="mt-3 space-y-1 border-t border-border pt-2.5 text-xs">
        {side.opening !== 0 && (
          <div className="flex justify-between gap-2">
            <dt className="text-muted-foreground">Opening (last year)</dt>
            <dd className="tabular text-muted-foreground">{money(side.opening)}</dd>
          </div>
        )}
        <div className="flex justify-between gap-2">
          <dt className="text-muted-foreground">Received</dt>
          <dd className="tabular text-success">+{money(side.received)}</dd>
        </div>
        <div className="flex justify-between gap-2">
          <dt className="text-muted-foreground">Spent</dt>
          <dd className="tabular text-destructive">−{money(side.spent)}</dd>
        </div>
        {net !== 0 && (
          <div className="flex justify-between gap-2">
            <dt className="text-muted-foreground">Transfers</dt>
            <dd className={cn("tabular", net > 0 ? "text-success" : "text-destructive")}>
              {net > 0 ? "+" : "−"}{money(Math.abs(net))}
            </dd>
          </div>
        )}
        {side.advancesOut > 0 && (
          <div className="flex justify-between gap-2">
            <dt className="flex items-center gap-1 text-muted-foreground">
              Out with members
              <Tooltip>
                <TooltipTrigger asChild>
                  <span><Info className="h-3 w-3" /></span>
                </TooltipTrigger>
                <TooltipContent className="max-w-56">
                  Advances handed out and not yet settled. The money has left this
                  pocket, so it's deducted until a bill or a return comes back.
                </TooltipContent>
              </Tooltip>
            </dt>
            <dd className="tabular text-gold">−{money(side.advancesOut)}</dd>
          </div>
        )}
      </dl>
    </div>
  );
}

/**
 * The committee holds money in two places and needs to see both at a glance —
 * how much is physically in the box, how much is in the account — plus a way
 * to record moving it between them (a bank deposit, a withdrawal for pandal
 * payments). Payment mode on each income/expense row decides the side; see
 * server/paymentMode.mjs for how blank modes are treated.
 */
export function FundPositionCard({ yearId }: { yearId: string | null }) {
  const { can } = useAuth();
  const { data, isLoading } = useFundPosition(yearId);
  const [open, setOpen] = useState(false);
  const [breakdownSide, setBreakdownSide] = useState<"cash" | "bank" | null>(null);
  // Fetched only once a card is actually tapped — the aggregate figures
  // above cost nothing extra either way, but the full payer-by-payer list
  // isn't worth loading for every dashboard visit.
  const { data: breakdown, isLoading: breakdownLoading } = useFundPositionBreakdown(yearId, breakdownSide !== null);

  if (isLoading || !data) {
    return (
      <Card className="p-4 sm:p-5">
        <Skeleton className="h-5 w-40" />
        <div className="mt-4 flex flex-col gap-3 sm:flex-row">
          <Skeleton className="h-40 flex-1 rounded-xl" />
          <Skeleton className="h-40 flex-1 rounded-xl" />
        </div>
      </Card>
    );
  }

  const recent = data.recentTransfers ?? [];

  return (
    <Card className="p-4 sm:p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="font-display text-base font-semibold">Where the money is</h3>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {money(data.total.balance)} in the fund · cash box and bank account
          </p>
        </div>
        {can("write_finance") && yearId && (
          <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
            <ArrowLeftRight className="h-4 w-4" /> Transfer
          </Button>
        )}
      </div>

      <div className="mt-4 flex flex-col gap-3 sm:flex-row">
        <SideTile
          label="Cash in hand" hint="Notes in the box — tap for who" side={data.cash}
          icon={<Banknote />} tone="success" onClick={() => setBreakdownSide("cash")}
        />
        <SideTile
          label="Bank / online" hint="GPay, transfers, cheques — tap for who" side={data.bank}
          icon={<Landmark />} tone="primary" onClick={() => setBreakdownSide("bank")}
        />
      </div>

      {recent.length > 0 && (
        <div className="mt-4 border-t border-border pt-3">
          <p className="mb-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Recent transfers
          </p>
          <ul className="space-y-1.5">
            {recent.map((t, i) => (
              <li key={`${t.transfer_date}-${t.amount}-${i}`} className="flex items-center gap-2 text-sm">
                <ArrowLeftRight className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                <span className="min-w-0 flex-1 truncate text-muted-foreground">
                  {t.direction === "cash_to_bank" ? "Cash → Bank" : "Bank → Cash"}
                  {t.note ? ` · ${t.note}` : ""}
                  {t.transfer_date ? ` · ${fmtDate(t.transfer_date)}` : ""}
                </span>
                <span className="shrink-0 tabular font-medium">{money(t.amount)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}

      {data.total.advancesOut > 0 && (
        <p className="mt-3 flex items-center gap-2 rounded-lg bg-gold/10 px-3 py-2 text-xs text-gold">
          <WalletCards className="h-3.5 w-3.5 shrink-0" />
          {money(data.total.advancesOut)} is out with members as unsettled advances —
          already deducted above.
        </p>
      )}

      {yearId && (
        <TransferDialog
          open={open} onOpenChange={setOpen} yearId={yearId}
          cash={data.cash.balance} bank={data.bank.balance}
        />
      )}

      <Dialog open={breakdownSide !== null} onOpenChange={(o) => !o && setBreakdownSide(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Where {breakdownSide === "bank" ? "the bank balance" : "the cash"} came from</DialogTitle>
          </DialogHeader>
          {breakdownLoading ? (
            <Skeleton className="h-32 w-full rounded-lg" />
          ) : (
            (() => {
              const entries = (breakdownSide === "bank" ? breakdown?.bank : breakdown?.cash) ?? [];
              return entries.length === 0 ? (
                <p className="py-4 text-center text-sm text-muted-foreground">Nothing recorded on this side yet.</p>
              ) : (
                <div className="max-h-80 space-y-1.5 overflow-y-auto">
                  {entries.map((e) => (
                    <div key={e.name} className="flex items-center justify-between gap-3 rounded-lg bg-muted/40 px-3 py-2 text-sm">
                      <span className="min-w-0 truncate text-muted-foreground">{e.name}</span>
                      <span className="shrink-0 font-semibold tabular">{money(e.amount)}</span>
                    </div>
                  ))}
                </div>
              );
            })()
          )}
        </DialogContent>
      </Dialog>
    </Card>
  );
}
