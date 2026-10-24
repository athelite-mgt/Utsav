import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { api } from "@/lib/api";
import { useZones } from "@/hooks/useReference";
import { useFeasts, useCollectionPayments } from "@/hooks/useFinance";
import type { FeastWithDonor } from "@/types/db";
import { money } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Textarea } from "@/components/ui/textarea";
import { Field } from "@/components/common/Field";
import { OptionSelect } from "@/components/form/OptionSelect";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { Info } from "lucide-react";
import { CollectionPaymentsField, newPayment, type PaymentDraft } from "@/components/finance/CollectionPaymentsField";

interface FormValues {
  name: string;
  surname: string;
  contact: string;
  zone_id: string | null;
  bill_no: string;
  not_eligible: boolean;
  billed: number;
  billed_on: string;
  comments: string;
}

const today = () => new Date().toISOString().slice(0, 10);

const empty: FormValues = {
  name: "", surname: "", contact: "", zone_id: null,
  bill_no: "", not_eligible: false, billed: 0, billed_on: "", comments: "",
};

/**
 * Feast's add/edit form — same collection mechanics as SubscriptionDialog
 * (bill no., billed/due, dated instalments, Not Eligible) but no projection
 * or prior-year block, since feast collection starts fresh every year with
 * no rollover/raise concept to carry forward.
 */
export function FeastDialog({
  open, onOpenChange, feast, yearId,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  feast?: FeastWithDonor | null;
  yearId: string;
}) {
  const qc = useQueryClient();
  const { data: zones = [] } = useZones();
  // Already fetched (and cached) by the page this dialog opens from — reused
  // here purely to work out the next bill number, no extra request.
  const { data: yearFeasts = [] } = useFeasts(yearId);
  const { data: existingPayments } = useCollectionPayments("feast", feast?.id);
  const { register, handleSubmit, reset, watch, setValue, setError, clearErrors, setFocus, formState } = useForm<FormValues>({
    defaultValues: empty,
  });
  const [payments, setPayments] = useState<PaymentDraft[]>([]);
  const [billNoSuggested, setBillNoSuggested] = useState(false);
  const [confirmNotEligibleOpen, setConfirmNotEligibleOpen] = useState(false);

  function nextBillNo(): number {
    try {
      const max = (yearFeasts ?? [])
        .map((f) => Number(f.bill_no))
        .filter((n) => Number.isFinite(n) && n > 0)
        .reduce((acc, n) => Math.max(acc, n), 0);
      return max + 1;
    } catch {
      return 1;
    }
  }

  useEffect(() => {
    if (!open) return;
    if (feast) {
      const hasBillNo = Boolean(feast.bill_no && feast.bill_no.trim() !== "");
      setBillNoSuggested(!hasBillNo);
      reset({
        name: feast.donor?.name ?? "",
        surname: feast.donor?.surname ?? "",
        contact: feast.donor?.contact ?? "",
        zone_id: feast.donor?.zone_id ?? null,
        bill_no: hasBillNo ? feast.bill_no! : String(nextBillNo()),
        not_eligible: feast.not_eligible ?? false,
        billed: feast.billed ?? 0,
        billed_on: feast.billed_on ?? today(),
        comments: feast.comments ?? "",
      });
    } else {
      setBillNoSuggested(true);
      reset({ ...empty, bill_no: String(nextBillNo()), billed_on: today() });
      setPayments([newPayment()]);
    }
    // yearFeasts deliberately excluded: useFeasts() falls back to a fresh `[]`
    // literal on every render whenever its query has no data yet (including
    // while it's persistently erroring — e.g. the feasts table not existing
    // until its migration is applied), which is a new reference every time.
    // Depending on it here reset this form on every render once that
    // happened, wiping out whatever the user had just typed on every
    // keystroke. nextBillNo() still reads the latest yearFeasts from the
    // closure when the effect does run — it doesn't need to be a trigger.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [feast, open, reset]);

  useEffect(() => {
    if (!open || !feast) return;
    if (existingPayments) {
      setPayments(existingPayments.map((p, i) => ({
        key: `e-${p.id}-${i}`,
        amount: Number(p.amount) || 0,
        paid_on: p.paid_on ?? today(),
        mode: p.mode,
      })));
    }
  }, [existingPayments, open, feast]);

  const paymentsHydrating = Boolean(feast) && existingPayments === undefined;

  const billed = watch("billed");
  const notEligible = watch("not_eligible");
  const collected = payments.reduce((acc, p) => acc + (Number(p.amount) || 0), 0);
  const hasBillingDetails = (Number(billed) || 0) > 0 || collected > 0;
  const billNo = watch("bill_no");
  const duplicateBillNo = billNo?.trim()
    ? (yearFeasts ?? []).find((f) => f.id !== feast?.id && (f.bill_no ?? "").trim() === billNo.trim())
    : undefined;

  const save = useMutation({
    mutationFn: async (v: FormValues) => {
      const donorPayload = {
        name: v.name.trim(),
        surname: v.surname || null,
        contact: v.contact || null,
        zone_id: v.zone_id,
      };
      const dated = [...payments].filter((p) => (Number(p.amount) || 0) !== 0)
        .sort((a, b) => (a.paid_on < b.paid_on ? 1 : -1));
      const latest = dated[0];
      const feastPayload = {
        year_id: yearId,
        bill_no: v.bill_no || null,
        not_eligible: v.not_eligible,
        billed: v.billed || 0,
        billed_on: v.billed_on || null,
        collected,
        mode: latest?.mode ?? null,
        collected_on: latest?.paid_on ?? null,
        comments: v.comments || null,
        updated_at: new Date().toISOString(),
        payments: payments
          .filter((p) => (Number(p.amount) || 0) !== 0)
          .map((p) => ({ amount: Number(p.amount) || 0, paid_on: p.paid_on || null, mode: p.mode })),
      };

      if (feast) {
        await api.patch(`/donors/${feast.donor_id}`, donorPayload);
        await api.patch(`/feasts/${feast.id}`, feastPayload);
      } else {
        const donor = await api.post<{ id: string }>("/donors", donorPayload);
        await api.post("/feasts", { ...feastPayload, donor_id: donor.id });
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["feasts"] });
      qc.invalidateQueries({ queryKey: ["collection_payments"] });
      qc.invalidateQueries({ queryKey: ["fund_position"] });
      toast.success(feast ? "Donor updated" : "Donor added");
      onOpenChange(false);
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const due = (Number(billed) || 0) - collected;

  function onSubmit(v: FormValues) {
    if (paymentsHydrating) return;
    if (!v.zone_id) {
      setError("zone_id", { type: "required", message: "Lane is required" });
      toast.error("Lane is required");
      return;
    }
    save.mutate(v);
  }

  function requestMarkNotEligible() {
    if (hasBillingDetails) {
      toast.error("Clear this year's Billed and Collected amounts before marking as Not Eligible.");
      return;
    }
    setConfirmNotEligibleOpen(true);
  }

  function confirmMarkNotEligible() {
    setConfirmNotEligibleOpen(false);
    setValue("not_eligible", true);
    handleSubmit(onSubmit, onInvalidFocus(setFocus))();
  }

  function markEligibleAgain() {
    setValue("not_eligible", false);
    handleSubmit(onSubmit, onInvalidFocus(setFocus))();
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{feast ? "Edit donor & collection" : "Add donor"}</DialogTitle>
          <DialogDescription>Feast collection for this puja year.</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Name" required error={formState.errors.name && "Name is required"}>
              <Input {...register("name", { required: true })} placeholder="First name" />
            </Field>
            <Field label="Surname">
              <Input {...register("surname")} placeholder="Surname" />
            </Field>
            <Field label="Contact">
              <Input {...register("contact")} inputMode="tel" placeholder="Phone (optional)" />
            </Field>
            <Field label="Zone / Lane" required error={formState.errors.zone_id?.message}>
              <OptionSelect
                value={watch("zone_id")}
                onChange={(v) => { setValue("zone_id", v); if (v) clearErrors("zone_id"); }}
                options={zones.map((z) => ({ value: z.id, label: z.name }))}
                placeholder="Select zone"
                allowClear
              />
            </Field>
          </div>

          <div className="space-y-4 rounded-xl border border-border bg-muted/30 p-4">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">This year</p>

            <div className="grid grid-cols-2 gap-4">
              <Field
                label={
                  <span className="inline-flex items-center gap-1">
                    Bill no.
                    {billNoSuggested && (
                      <Tooltip>
                        <TooltipTrigger type="button" tabIndex={-1}>
                          <Info className="h-3.5 w-3.5 text-muted-foreground" />
                        </TooltipTrigger>
                        <TooltipContent>We suggested this number — next available bill no. this year.</TooltipContent>
                      </Tooltip>
                    )}
                  </span>
                }
                hint={duplicateBillNo ? `Already used by ${duplicateBillNo.donor?.name ?? "another donor"} — you can still save.` : undefined}
              >
                <Input
                  value={watch("bill_no")}
                  placeholder="e.g. 175"
                  className={duplicateBillNo ? "border-gold text-gold" : undefined}
                  onChange={(e) => { setBillNoSuggested(false); setValue("bill_no", e.target.value); }}
                />
              </Field>
              <Field label="Billed">
                <MoneyInput step="1" {...register("billed", { valueAsNumber: true })} />
              </Field>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <Field label="Billed on">
                <Input type="date" {...register("billed_on")} />
              </Field>
              <Field label="Due" hint={due > 0 ? "Outstanding" : "Cleared"}>
                <Input value={money(due)} readOnly className="bg-muted font-semibold text-muted-foreground" />
              </Field>
            </div>

            <CollectionPaymentsField payments={payments} onChange={setPayments} />
          </div>

          <Field label="Comments">
            <Textarea {...register("comments")} rows={2} placeholder="Any notes…" />
          </Field>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button
              type="button"
              variant="outline"
              className={notEligible ? undefined : "border-destructive/40 text-destructive hover:bg-destructive/5 hover:text-destructive"}
              onClick={notEligible ? markEligibleAgain : requestMarkNotEligible}
              disabled={save.isPending}
            >
              {notEligible ? "Mark eligible again" : "Mark not eligible"}
            </Button>
            <Button type="submit" disabled={save.isPending || paymentsHydrating}>
              {save.isPending ? "Saving…" : paymentsHydrating ? "Loading…" : feast ? "Save changes" : "Add donor"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>

      <Dialog open={confirmNotEligibleOpen} onOpenChange={setConfirmNotEligibleOpen}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Mark as Not Eligible?</DialogTitle>
            <DialogDescription>
              This saves {watch("name") || "this donor"} right away as not eligible for the feast
              this year — no bill will be raised. You can undo it later from this same button.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setConfirmNotEligibleOpen(false)} disabled={save.isPending}>
              Cancel
            </Button>
            <Button type="button" variant="destructive" onClick={confirmMarkNotEligible} disabled={save.isPending}>
              {save.isPending ? "Saving…" : "Yes, mark not eligible"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Dialog>
  );
}
