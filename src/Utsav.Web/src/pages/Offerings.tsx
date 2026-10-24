import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { Gift, Plus, Search, Check, Pencil, Trash2, Clock } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useOfferings } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { fmtDate, initials, money } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { cn } from "@/lib/utils";
import type { Offering } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Field } from "@/components/common/Field";
import { LookupSelect, OptionSelect } from "@/components/form/OptionSelect";
import { useLookups } from "@/hooks/useReference";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

interface FormValues {
  name: string; contact: string; offering_type: string | null; puja_day: string | null; value: number;
  details: string; expected_date: string; received_date: string; received_by: string;
}
const empty: FormValues = { name: "", contact: "", offering_type: null, puja_day: null, value: 0, details: "", expected_date: "", received_date: "", received_by: "" };

function OfferingDialog({
  open, onOpenChange, row, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; row?: Offering | null; yearId: string }) {
  const { create, update } = useEntityMutations("offerings", ["offerings"], "Offering");
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: empty });

  useEffect(() => {
    reset(row ? {
      name: row.name, contact: row.contact ?? "", offering_type: row.offering_type, puja_day: row.puja_day,
      value: row.value ?? 0, details: row.details ?? "", expected_date: row.expected_date ?? "", received_date: row.received_date ?? "",
      received_by: row.received_by ?? "",
    } : empty);
  }, [row, open, reset]);

  async function onSubmit(v: FormValues) {
    const payload = { year_id: yearId, name: v.name.trim(), contact: v.contact || null, offering_type: v.offering_type,
      puja_day: v.puja_day, value: v.value || null, details: v.details || null, expected_date: v.expected_date || null,
      received_date: v.received_date || null, received_by: v.received_by || null,
      status: v.received_date ? "received" : "pledged" };
    if (row) await update.mutateAsync({ id: row.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader><DialogTitle>{row ? "Edit offering" : "Add offering"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Name" required><Input {...register("name", { required: true })} /></Field>
            <Field label="Contact"><Input {...register("contact")} inputMode="tel" /></Field>
            <Field label="Offering"><LookupSelect category="offering_type" value={watch("offering_type")} onChange={(v) => setValue("offering_type", v)} placeholder="Type" allowClear /></Field>
            <Field label="Puja day"><LookupSelect category="puja_day" value={watch("puja_day")} onChange={(v) => setValue("puja_day", v)} placeholder="Day" allowClear /></Field>
            <Field label="Value" hint="Estimated value — used for next year's budget planning">
              <MoneyInput step="0.01" {...register("value", { valueAsNumber: true })} />
            </Field>
          </div>
          <Field label="Details"><Input {...register("details")} placeholder="e.g. Sasthi - Adhibas" /></Field>
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Expected date"><Input type="date" {...register("expected_date")} /></Field>
            <Field label="Received date"><Input type="date" {...register("received_date")} /></Field>
            <Field label="Received by"><Input {...register("received_by")} /></Field>
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>{row ? "Save" : "Add"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function OfferingsPage() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_income");
  const { data = [], isLoading } = useOfferings(activeYearId);
  const { update, remove } = useEntityMutations("offerings", ["offerings"], "Offering");
  const { data: types = [] } = useLookups("offering_type");
  const { data: days = [] } = useLookups("puja_day");
  const [search, setSearch] = useState("");
  const [offeringType, setOfferingType] = useState<string | null>(null);
  const [pujaDay, setPujaDay] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Offering | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((r) => {
      if (q && !r.name.toLowerCase().includes(q) && !(r.offering_type ?? "").toLowerCase().includes(q)) return false;
      if (offeringType && r.offering_type !== offeringType) return false;
      if (pujaDay && r.puja_day !== pujaDay) return false;
      if (status && r.status !== status) return false;
      return true;
    });
  }, [data, search, offeringType, pujaDay, status]);

  const received = data.filter((r) => r.status === "received").length;
  const totalValue = data.reduce((acc, r) => acc + (Number(r.value) || 0), 0);

  const markReceived = (r: Offering) =>
    update.mutate({ id: r.id, values: { status: "received", received_date: r.received_date ?? new Date().toISOString().slice(0, 10) } });

  return (
    <div className="space-y-5">
      <PageHeader
        title="Offerings" description="In-kind gifts — sarees, dhuti, vog & flowers" icon={<Gift />}
        actions={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> <span className="hidden sm:inline">Add offering</span></Button>}
      />

      <SummaryBar items={[
        { label: "Total Pledged", value: data.length, isMoney: false },
        { label: "Received", value: received, isMoney: false, tone: "success" },
        { label: "Pending", value: data.length - received, isMoney: false, tone: "destructive" },
        { label: "Total Value", value: totalValue, tone: "primary" },
      ]} />

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search offerings…" className="pl-9" />
        </div>
        <FilterMenu
          activeCount={(offeringType ? 1 : 0) + (pujaDay ? 1 : 0) + (status ? 1 : 0)}
          onClear={() => { setOfferingType(null); setPujaDay(null); setStatus(null); }}
        >
          <FilterField label="Offering type">
            <OptionSelect value={offeringType} onChange={setOfferingType} allowClear
              options={types.map((l) => ({ value: l.label, label: l.label }))}
              placeholder="All types" className="w-full" />
          </FilterField>
          <FilterField label="Puja day">
            <OptionSelect value={pujaDay} onChange={setPujaDay} allowClear
              options={days.map((l) => ({ value: l.label, label: l.label }))}
              placeholder="All days" className="w-full" />
          </FilterField>
          <FilterField label="Status">
            <OptionSelect value={status} onChange={setStatus} allowClear
              options={[{ value: "received", label: "Received" }, { value: "pledged", label: "Pending" }]}
              placeholder="All" className="w-full" />
          </FilterField>
        </FilterMenu>
      </div>

      {isLoading ? <TableSkeleton />
        : filtered.length === 0 ? (
          <EmptyState icon={<Gift />} title="No offerings"
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add offering</Button>} />
        ) : (
          <div className="grid gap-2.5 sm:grid-cols-2">
            {filtered.map((r) => (
              <Card
                key={r.id}
                className={cn("p-4", canWrite && "cursor-pointer hover:bg-muted/40")}
                onClick={() => { if (canWrite) { setEditing(r); setOpen(true); } }}
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="flex min-w-0 items-center gap-2.5">
                    <Avatar className="h-9 w-9"><AvatarFallback>{initials(r.name)}</AvatarFallback></Avatar>
                    <div className="min-w-0">
                      <p className="truncate font-semibold">{r.name}</p>
                      <div className="mt-0.5 flex flex-wrap items-center gap-1.5">
                        {r.offering_type && <Badge variant="gold">{r.offering_type}</Badge>}
                        {r.puja_day && <Badge variant="secondary">{r.puja_day}</Badge>}
                        {!!r.value && <Badge variant="outline">{money(r.value)}</Badge>}
                      </div>
                    </div>
                  </div>
                  {r.status === "received"
                    ? <Badge variant="success"><Check className="mr-1 h-3 w-3" /> Received</Badge>
                    : <Badge variant="outline"><Clock className="mr-1 h-3 w-3" /> Pending</Badge>}
                </div>
                <div className="mt-3 flex items-center justify-between border-t border-border pt-3">
                  <p className="text-xs text-muted-foreground">
                    {r.details || "—"}{r.received_by ? ` · by ${r.received_by}` : ""}
                    {r.received_date ? ` · ${fmtDate(r.received_date)}` : ""}
                  </p>
                  {canWrite && (
                    <div className="flex shrink-0 gap-0.5" onClick={(e) => e.stopPropagation()}>
                      {r.status !== "received" && (
                        <Button variant="ghost" size="icon" className="h-8 w-8 text-success" title="Mark received" onClick={() => markReceived(r)}>
                          <Check className="h-4 w-4" />
                        </Button>
                      )}
                      <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(r); setOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                      <ConfirmDialog title="Delete offering?" destructive confirmLabel="Delete"
                        onConfirm={() => remove.mutateAsync(r.id)}
                        trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                    </div>
                  )}
                </div>
              </Card>
            ))}
          </div>
        )}

      {activeYearId && <OfferingDialog open={open} onOpenChange={setOpen} row={editing} yearId={activeYearId} />}
    </div>
  );
}
