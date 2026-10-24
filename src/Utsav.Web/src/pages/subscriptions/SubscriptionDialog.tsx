import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { useZones } from "@/hooks/useReference";
import { useSubscriptions, useCollectionPayments } from "@/hooks/useFinance";
import type { SubscriptionCategory, SubscriptionWithDonor } from "@/types/db";
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
  prior_billed: number;
  projected: number;
  bill_no: string;
  not_eligible: boolean;
  billed: number;
  billed_on: string;
  comments: string;
}

const today = () => new Date().toISOString().slice(0, 10);

const empty: FormValues = {
  name: "", surname: "", contact: "", zone_id: null,
  prior_billed: 0, projected: 0, bill_no: "", not_eligible: false, billed: 0, billed_on: "", comments: "",
};

export function SubscriptionDialog({
  open, onOpenChange, sub, yearId, category, projectionPct = 6.5,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  sub?: SubscriptionWithDonor | null;
  yearId: string;
  /** Which section this row belongs to — decides whether the raise applies. */
  category: SubscriptionCategory;
  /** This puja year's configured raise — see Settings › Puja Year. */
  projectionPct?: number;
}) {
  const qc = useQueryClient();
  const { data: zones = [] } = useZones();
  // Already fetched (and cached) by the page this dialog opens from — reused
  // here purely to work out the next bill number, no extra request.
  const { data: yearSubs = [] } = useSubscriptions(yearId, category);
  // Existing dated instalments for this record, loaded only when editing.
  const { data: existingPayments } = useCollectionPayments("subscription", sub?.id);
  const { register, handleSubmit, reset, watch, setValue, setError, clearErrors, setFocus, formState } = useForm<FormValues>({
    defaultValues: empty,
  });
  const [payments, setPayments] = useState<PaymentDraft[]>([]);
  // Drives the "we suggested this" tooltip — cleared the moment the field is
  // hand-edited, so it never claims credit for a number the user typed.
  const [billNoSuggested, setBillNoSuggested] = useState(false);
  const [confirmNotEligibleOpen, setConfirmNotEligibleOpen] = useState(false);

  // Max bill number already in use this year, +1 — falls back to 1 if the
  // year has no parsable bill numbers yet (brand-new year, or every existing
  // one is non-numeric). Shared by both brand-new donors and existing donors
  // being billed for the first time (old back-entries that never got a
  // number assigned in a previous pass).
  function nextBillNo(): number {
    try {
      const max = (yearSubs ?? [])
        .map((s) => Number(s.bill_no))
        .filter((n) => Number.isFinite(n) && n > 0)
        .reduce((acc, n) => Math.max(acc, n), 0);
      return max + 1;
    } catch {
      return 1;
    }
  }

  useEffect(() => {
    if (!open) return;
    if (sub) {
      const hasBillNo = Boolean(sub.bill_no && sub.bill_no.trim() !== "");
      setBillNoSuggested(!hasBillNo);
      reset({
        name: sub.donor?.name ?? "",
        surname: sub.donor?.surname ?? "",
        contact: sub.donor?.contact ?? "",
        zone_id: sub.donor?.zone_id ?? null,
        prior_billed: sub.prior_billed ?? 0,
        projected: Math.round(sub.projected ?? 0),
        bill_no: hasBillNo ? sub.bill_no! : String(nextBillNo()),
        not_eligible: sub.not_eligible ?? false,
        billed: sub.billed ?? 0,
        billed_on: sub.billed_on ?? today(),
        comments: sub.comments ?? "",
      });
    } else {
      setBillNoSuggested(true);
      reset({ ...empty, bill_no: String(nextBillNo()), billed_on: today() });
      // One instalment ready to fill in rather than an empty list — most
      // donors pay something the moment they're added, and an empty-amount
      // row is dropped silently at save time if it's never touched (see
      // the save mutation's payments filter) so this costs nothing when
      // it's not needed.
      setPayments([newPayment()]);
    }
  }, [sub, open, reset, yearSubs]);

  // Hydrate the instalment rows once the existing payments arrive (edit mode).
  useEffect(() => {
    if (!open || !sub) return;
    if (existingPayments) {
      setPayments(existingPayments.map((p, i) => ({
        key: `e-${p.id}-${i}`,
        amount: Number(p.amount) || 0,
        paid_on: p.paid_on ?? today(),
        mode: p.mode,
      })));
    }
  }, [existingPayments, open, sub]);

  // When editing, the instalment rows arrive asynchronously; saving before they
  // land would persist collected=0 and wipe the record's collections. Block the
  // save until they've hydrated. (New records have nothing to load.)
  const paymentsHydrating = Boolean(sub) && existingPayments === undefined;

  const prior = watch("prior_billed");
  const billed = watch("billed");
  const projectedVal = watch("projected");
  const notEligible = watch("not_eligible");
  const collected = payments.reduce((acc, p) => acc + (Number(p.amount) || 0), 0);
  // Not Eligible is a "nothing happened this year" flag — a donor who's
  // already been asked for money or has paid something shouldn't also be
  // marked as skipped without first clearing those figures, or the two states
  // contradict each other in every report that reads them.
  const hasBillingDetails = (Number(billed) || 0) > 0 || collected > 0 || (Number(projectedVal) || 0) > 0;
  const billNo = watch("bill_no");
  // Warn, don't block — a correction (e.g. re-numbering after a mistake)
  // can legitimately land on a number that's about to be freed up by the
  // same save, and the committee knows their own paper register better
  // than this check does.
  const duplicateBillNo = billNo?.trim()
    ? (yearSubs ?? []).find((s) => s.id !== sub?.id && (s.bill_no ?? "").trim() === billNo.trim())
    : undefined;
  // Special donations are negotiated gift by gift, so the membership round's
  // yearly raise doesn't apply — the suggestion is simply last year's figure.
  const appliesRaise = category === "membership";
  const suggestion = appliesRaise
    ? Math.round((Number(prior) || 0) * (1 + projectionPct / 100))
    : Math.round(Number(prior) || 0);
  const suggestionHint = appliesRaise
    ? `Suggested (+${projectionPct}%): ${money(suggestion)}`
    : `Same as last year: ${money(suggestion)}`;
  const priorYearLabel = sub?.prior_year ? `Puja ${sub.prior_year}` : "Last year";

  const save = useMutation({
    mutationFn: async (v: FormValues) => {
      const donorPayload = {
        name: v.name.trim(),
        surname: v.surname || null,
        contact: v.contact || null,
        zone_id: v.zone_id,
      };
      // Latest instalment stands in for the parent's single collected_on/mode,
      // kept for back-compat; the real per-instalment detail rides in `payments`.
      const dated = [...payments].filter((p) => (Number(p.amount) || 0) !== 0)
        .sort((a, b) => (a.paid_on < b.paid_on ? 1 : -1));
      const latest = dated[0];
      const subPayload = {
        year_id: yearId,
        category,
        prior_billed: v.prior_billed || 0,
        projected: v.projected || 0,
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

      if (sub) {
        await api.patch(`/donors/${sub.donor_id}`, donorPayload);
        await api.patch(`/subscriptions/${sub.id}`, subPayload);
      } else {
        const donor = await api.post<{ id: string }>("/donors", donorPayload);
        await api.post("/subscriptions", { ...subPayload, donor_id: donor.id });
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["subscriptions"] });
      qc.invalidateQueries({ queryKey: ["collection_payments"] });
      qc.invalidateQueries({ queryKey: ["fund_position"] });
      toast.success(sub ? "Donor updated" : "Donor added");
      onOpenChange(false);
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const due = (Number(billed) || 0) - collected;

  function onSubmit(v: FormValues) {
    if (paymentsHydrating) return; // guard: don't overwrite unloaded collections
    // Lane/zone became mandatory going forward — doesn't retroactively
    // block records that already lack one until someone opens them again.
    if (!v.zone_id) {
      setError("zone_id", { type: "required", message: "Lane is required" });
      toast.error("Lane is required");
      return;
    }
    save.mutate(v);
  }

  const submitNow = handleSubmit(onSubmit, onInvalidFocus(setFocus));

  function requestMarkNotEligible() {
    if (hasBillingDetails) {
      toast.error("Clear this year's Projected, Billed and Collected amounts before marking as Not Eligible.");
      return;
    }
    setConfirmNotEligibleOpen(true);
  }

  function confirmMarkNotEligible() {
    setConfirmNotEligibleOpen(false);
    setValue("not_eligible", true);
    submitNow();
  }

  function markEligibleAgain() {
    setValue("not_eligible", false);
    submitNow();
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{sub ? "Edit donor & collection" : "Add donor"}</DialogTitle>
          <DialogDescription>
            {appliesRaise
              ? "Household membership subscription for this puja year."
              : "Special donation for this puja year — the yearly raise doesn't apply."}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-5">
          {/* Donor details */}
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

          {/* This year's billing + collections — Bill No/Billed and Billed
              on/Due pair up two-wide at every width (not just desktop), so
              the common path doesn't scroll through single-column fields on
              a phone; Projected sits last since it's a forward-looking
              estimate, not part of what's actually been billed yet. */}
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

            {/* Dated part-payments — mode / date / recorded-by all live here now. */}
            <CollectionPaymentsField payments={payments} onChange={setPayments} />

            <div className={cn("grid gap-4", !sub && "grid-cols-2")}>
              {!sub && (
                <Field label="Last year billed" hint={suggestionHint}>
                  <MoneyInput step="1" {...register("prior_billed", { valueAsNumber: true })} />
                </Field>
              )}
              <Field label="Projected" hint={sub ? suggestionHint : undefined}>
                <div className="flex gap-1.5">
                  <MoneyInput step="1" {...register("projected", { valueAsNumber: true })} />
                  <Button type="button" variant="outline" size="sm" className="shrink-0"
                    onClick={() => setValue("projected", suggestion)}>
                    Auto
                  </Button>
                </div>
              </Field>
            </div>
          </div>

          <Field label="Comments">
            <Textarea {...register("comments")} rows={2} placeholder="Any notes…" />
          </Field>

          {/* Last year — carried over by the year rollover, never typed in here. */}
          {sub && (
            <div className="rounded-xl border border-border bg-muted/30 p-4">
              <p className="mb-3 text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {priorYearLabel} · read-only
              </p>
              <div className="grid gap-4 sm:grid-cols-3">
                <Field label="Bill no.">
                  <Input value={sub.prior_bill_no ?? "—"} readOnly disabled className="bg-muted" />
                </Field>
                <Field label="Billed">
                  <Input value={money(sub.prior_year_billed ?? Number(prior) ?? 0)} readOnly disabled className="bg-muted tabular" />
                </Field>
                <Field label="Collected">
                  <Input value={sub.prior_year_collected != null ? money(sub.prior_year_collected) : "—"} readOnly disabled className="bg-muted tabular" />
                </Field>
              </div>
            </div>
          )}

          {/* DOM order Cancel → Not eligible → Save reads left-to-right on
              desktop (DialogFooter's sm:flex-row) and top-to-bottom as
              Save → Not eligible → Cancel on mobile (its flex-col-reverse) —
              the primary action lands first under a thumb either way. */}
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
              {save.isPending ? "Saving…" : paymentsHydrating ? "Loading…" : sub ? "Save changes" : "Add donor"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>

      <Dialog open={confirmNotEligibleOpen} onOpenChange={setConfirmNotEligibleOpen}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Mark as Not Eligible?</DialogTitle>
            <DialogDescription>
              This saves {watch("name") || "this donor"} right away as not eligible for{" "}
              {appliesRaise ? "membership" : "a special donation"} this year — no bill will be raised.
              You can undo it later from this same button.
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
