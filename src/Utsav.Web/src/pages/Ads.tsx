import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Megaphone, Plus, Search, Download, Pencil, Trash2 } from "lucide-react";
import { api } from "@/lib/api";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useAdBookings, useCollectionPayments, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { money } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { exportCsv } from "@/lib/exportCsv";
import { projectedWithFallback } from "@/lib/projection";
import type { AdBookingWithAdvertiser } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { LookupSelect, OptionSelect } from "@/components/form/OptionSelect";
import { useLookups } from "@/hooks/useReference";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { CollectionPaymentsField, type PaymentDraft } from "@/components/finance/CollectionPaymentsField";

interface FormValues {
  name: string; contact: string; ad_type: string | null; projected: number; bill_no: string;
  billed: number; billed_on: string; status: string | null; comments: string;
}
const empty: FormValues = { name: "", contact: "", ad_type: null, projected: 0, bill_no: "", billed: 0, billed_on: "", status: null, comments: "" };

function AdDialog({
  open, onOpenChange, booking, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; booking?: AdBookingWithAdvertiser | null; yearId: string }) {
  const qc = useQueryClient();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: empty });
  const { data: existingPayments } = useCollectionPayments("ad_booking", booking?.id);
  const [payments, setPayments] = useState<PaymentDraft[]>([]);

  useEffect(() => {
    reset(booking ? {
      name: booking.advertiser?.name ?? "", contact: booking.advertiser?.contact ?? "",
      ad_type: booking.ad_type, projected: Math.round(booking.projected ?? 0), bill_no: booking.bill_no ?? "", billed: booking.billed, billed_on: booking.billed_on ?? "",
      status: booking.status, comments: booking.comments ?? "",
    } : empty);
    if (!booking) setPayments([]);
  }, [booking, open, reset]);

  useEffect(() => {
    if (!open || !booking) return;
    if (existingPayments) {
      setPayments(existingPayments.map((p, i) => ({
        key: `e-${p.id}-${i}`, amount: Number(p.amount) || 0, paid_on: p.paid_on ?? new Date().toISOString().slice(0, 10), mode: p.mode,
      })));
    }
  }, [existingPayments, open, booking]);

  const collected = payments.reduce((acc, p) => acc + (Number(p.amount) || 0), 0);
  // Don't let a save land before existing instalments hydrate (would wipe them).
  const paymentsHydrating = Boolean(booking) && existingPayments === undefined;

  const save = useMutation({
    mutationFn: async (v: FormValues) => {
      const advPayload = { name: v.name.trim(), contact: v.contact || null };
      const dated = payments.filter((p) => (Number(p.amount) || 0) !== 0).sort((a, b) => (a.paid_on < b.paid_on ? 1 : -1));
      const latest = dated[0];
      const bookPayload = {
        year_id: yearId, ad_type: v.ad_type, projected: v.projected || 0, bill_no: v.bill_no || null, billed: v.billed || 0,
        billed_on: v.billed_on || null,
        collected, mode: latest?.mode ?? null, status: v.status, collected_on: latest?.paid_on ?? null,
        comments: v.comments || null, updated_at: new Date().toISOString(),
        payments: dated.map((p) => ({ amount: Number(p.amount) || 0, paid_on: p.paid_on || null, mode: p.mode })),
      };
      if (booking) {
        await api.patch(`/advertisers/${booking.advertiser_id}`, advPayload);
        await api.patch(`/ad_bookings/${booking.id}`, bookPayload);
      } else {
        const adv = await api.post<{ id: string }>("/advertisers", advPayload);
        await api.post("/ad_bookings", { ...bookPayload, advertiser_id: adv.id });
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["ad_bookings"] });
      qc.invalidateQueries({ queryKey: ["collection_payments"] });
      qc.invalidateQueries({ queryKey: ["fund_position"] });
      toast.success(booking ? "Updated" : "Advertiser added"); onOpenChange(false);
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const due = (Number(watch("billed")) || 0) - collected;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader><DialogTitle>{booking ? "Edit advertiser" : "Add advertiser"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit((v) => { if (!paymentsHydrating) save.mutate(v); }, onInvalidFocus(setFocus))} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Advertiser name" required><Input {...register("name", { required: true })} placeholder="Business name" /></Field>
            <Field label="Contact"><Input {...register("contact")} inputMode="tel" placeholder="Phone" /></Field>
            <Field label="Ad type"><LookupSelect category="ad_type" value={watch("ad_type")} onChange={(v) => setValue("ad_type", v)} placeholder="Type" allowClear /></Field>
            <Field label="Status"><LookupSelect category="ad_status" value={watch("status")} onChange={(v) => setValue("status", v)} placeholder="Workflow" allowClear /></Field>
          </div>

          <div className="space-y-4 rounded-xl border border-border bg-muted/30 p-4">
            <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">This year</p>
            <div className="grid gap-4 sm:grid-cols-3">
              <Field label="Projected" hint="Expected ask for this year"><MoneyInput step="1" {...register("projected", { valueAsNumber: true })} /></Field>
              <Field label="Bill no."><Input {...register("bill_no")} /></Field>
              <Field label="Billed on"><Input type="date" {...register("billed_on")} /></Field>
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Billed"><MoneyInput step="1" {...register("billed", { valueAsNumber: true })} /></Field>
              <Field label="Due" hint={due > 0 ? "Outstanding" : "Cleared"}><Input readOnly value={money(due)} className="bg-muted text-muted-foreground font-semibold" /></Field>
            </div>
            <CollectionPaymentsField payments={payments} onChange={setPayments} />
          </div>

          <Field label="Comments"><Textarea {...register("comments")} rows={2} /></Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={save.isPending || paymentsHydrating}>{paymentsHydrating ? "Loading…" : booking ? "Save" : "Add"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function AdsPage() {
  const { activeYearId, previousYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_income");
  const { data = [], isLoading } = useAdBookings(activeYearId);
  const { data: priorData = [] } = useAdBookings(previousYearId);
  const { remove } = useEntityMutations("ad_bookings", ["ad_bookings"], "Advertiser");
  const { data: adTypes = [] } = useLookups("ad_type");
  const { data: adStatuses = [] } = useLookups("ad_status");
  const [search, setSearch] = useState("");
  const [adType, setAdType] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [dueFilter, setDueFilter] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<AdBookingWithAdvertiser | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((b) => {
      if (q && !(b.advertiser?.name ?? "").toLowerCase().includes(q)) return false;
      if (adType && b.ad_type !== adType) return false;
      if (status && b.status !== status) return false;
      if (dueFilter === "due" && b.due <= 0) return false;
      if (dueFilter === "cleared" && b.due > 0) return false;
      return true;
    });
  }, [data, search, adType, status, dueFilter]);

  // Ad bookings aren't carried over year to year (unlike subscriptions), so a
  // fresh year starts with no projected figure until someone books an ad —
  // last year's billed total is the honest placeholder until then.
  const projection = projectedWithFallback(filtered, priorData);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Advertisements" description="Souvenir ads — banners, leaflets & sponsors" icon={<Megaphone />}
        actions={
          <>
            <Button variant="outline" size="icon" title="Export CSV"
              onClick={() => exportCsv("advertisements", filtered.map((b) => ({
                Advertiser: b.advertiser?.name, Contact: b.advertiser?.contact, Type: b.ad_type,
                Billed: b.billed, BilledOn: b.billed_on, Collected: b.collected, Due: b.due, Status: b.status,
              })))}>
              <Download className="h-4 w-4" />
            </Button>
            {canWrite && (
              <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> <span className="hidden sm:inline">Add advertiser</span></Button>
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
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search advertisers…" className="pl-9" />
        </div>
        <FilterMenu
          activeCount={(adType ? 1 : 0) + (status ? 1 : 0) + (dueFilter ? 1 : 0)}
          onClear={() => { setAdType(null); setStatus(null); setDueFilter(null); }}
        >
          <FilterField label="Ad type">
            <OptionSelect value={adType} onChange={setAdType} allowClear
              options={adTypes.map((l) => ({ value: l.label, label: l.label }))}
              placeholder="All types" className="w-full" />
          </FilterField>
          <FilterField label="Status">
            <OptionSelect value={status} onChange={setStatus} allowClear
              options={adStatuses.map((l) => ({ value: l.label, label: l.label }))}
              placeholder="All statuses" className="w-full" />
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
          <EmptyState icon={<Megaphone />} title="No advertisers"
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add advertiser</Button>} />
        ) : (
          <div className="grid gap-2.5 sm:grid-cols-2">
            {filtered.map((b) => (
              <Card key={b.id} className="p-4">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="truncate font-semibold">{b.advertiser?.name}</p>
                    <div className="mt-1 flex flex-wrap gap-1.5">
                      {b.ad_type && <Badge variant="gold">{b.ad_type}</Badge>}
                      {b.status && <Badge variant="outline">{b.status}</Badge>}
                    </div>
                  </div>
                  {canWrite && (
                    <div className="flex shrink-0 gap-0.5">
                      <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(b); setOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                      <ConfirmDialog title="Delete advertiser?" destructive confirmLabel="Delete"
                        onConfirm={() => remove.mutateAsync(b.id)}
                        trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                    </div>
                  )}
                </div>
                <div className="mt-3 flex items-center justify-between border-t border-border pt-3 text-sm">
                  <span className="text-muted-foreground">Billed <span className="tabular font-medium text-foreground">{money(b.billed)}</span></span>
                  <span className="text-muted-foreground">Got <span className="tabular font-medium text-success">{money(b.collected)}</span></span>
                  {b.due > 0 ? <Badge variant="destructive">Due {money(b.due)}</Badge> : <Badge variant="success">Cleared</Badge>}
                </div>
              </Card>
            ))}
          </div>
        )}

      {activeYearId && <AdDialog open={open} onOpenChange={setOpen} booking={editing} yearId={activeYearId} />}
    </div>
  );
}
