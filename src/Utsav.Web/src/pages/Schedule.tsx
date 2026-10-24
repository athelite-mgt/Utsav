import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { CalendarClock, Plus, Pencil, Trash2, User, Phone, Clock } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { usePujaSchedule } from "@/hooks/useFinance";
import { useCollectors, useLookups } from "@/hooks/useReference";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { fmtDate } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import type { PujaScheduleItem } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { OptionSelect, LookupSelect } from "@/components/form/OptionSelect";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { PujaCalendarCard } from "./schedule/PujaCalendarCard";

interface FormValues {
  puja_day: string; event_date: string; start_time: string; end_time: string; charge_title: string;
  assignee_name: string; assignee_collector_id: string | null; contact: string; notes: string;
}
function emptyForm(defaultDay: string): FormValues {
  return {
    puja_day: defaultDay, event_date: "", start_time: "", end_time: "", charge_title: "",
    assignee_name: "", assignee_collector_id: null, contact: "", notes: "",
  };
}

/** "18:30" -> "6:30 PM" — falls back to the raw value for anything unparseable. */
function fmtTime(t: string | null | undefined) {
  if (!t) return null;
  const [h, m] = t.split(":").map(Number);
  if (Number.isNaN(h) || Number.isNaN(m)) return t;
  const period = h >= 12 ? "PM" : "AM";
  const h12 = h % 12 || 12;
  return `${h12}:${String(m).padStart(2, "0")} ${period}`;
}

function ScheduleDialog({
  open, onOpenChange, item, yearId, defaultDay,
}: { open: boolean; onOpenChange: (v: boolean) => void; item?: PujaScheduleItem | null; yearId: string; defaultDay: string }) {
  const { create, update } = useEntityMutations("puja_schedule", ["puja_schedule"], "Charge");
  const { data: collectors = [] } = useCollectors();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: emptyForm(defaultDay) });

  useEffect(() => {
    reset(item
      ? {
          puja_day: item.puja_day, event_date: item.event_date ?? "",
          start_time: item.start_time ?? "", end_time: item.end_time ?? "", charge_title: item.charge_title,
          assignee_name: item.assignee_name ?? "", assignee_collector_id: item.assignee_collector_id,
          contact: item.contact ?? "", notes: item.notes ?? "",
        }
      : emptyForm(defaultDay));
  }, [item, open, defaultDay, reset]);

  async function onSubmit(v: FormValues) {
    const payload = {
      ...v, year_id: yearId, event_date: v.event_date || null,
      start_time: v.start_time || null, end_time: v.end_time || null,
      assignee_name: v.assignee_name || null, contact: v.contact || null, notes: v.notes || null,
    };
    if (item) await update.mutateAsync({ id: item.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{item ? "Edit charge" : "Add charge"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Puja day" required>
              <LookupSelect category="puja_day" value={watch("puja_day")} onChange={(v) => setValue("puja_day", v ?? defaultDay)} placeholder="Day" />
            </Field>
            <Field label="Date">
              <Input type="date" {...register("event_date")} />
            </Field>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Start time"><Input type="time" {...register("start_time")} /></Field>
            <Field label="End time"><Input type="time" {...register("end_time")} /></Field>
          </div>
          <Field label="Charge / duty" required>
            <Input {...register("charge_title", { required: true })} placeholder="e.g. Bhog distribution, Gate management" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Assigned to">
              <OptionSelect value={watch("assignee_collector_id")}
                onChange={(v) => {
                  setValue("assignee_collector_id", v);
                  const c = collectors.find((x) => x.id === v);
                  if (c) setValue("assignee_name", c.name);
                }}
                options={collectors.map((c) => ({ value: c.id, label: c.name }))}
                placeholder="Pick a collector" allowClear />
            </Field>
            <Field label="Or type a name" hint="If they're not in the collector list">
              <Input {...register("assignee_name")} placeholder="Name" />
            </Field>
          </div>
          <Field label="Contact">
            <Input {...register("contact")} placeholder="Phone number" />
          </Field>
          <Field label="Notes">
            <Textarea {...register("notes")} rows={2} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>
              {item ? "Save changes" : "Add charge"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function SchedulePage() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_workflow");
  const { data = [], isLoading, isError, error } = usePujaSchedule(activeYearId);
  const { data: days = [] } = useLookups("puja_day");
  const { remove } = useEntityMutations("puja_schedule", ["puja_schedule"], "Charge");
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<PujaScheduleItem | null>(null);
  const [addDay, setAddDay] = useState("");

  const dayNames = useMemo(() => days.map((d) => d.label), [days]);
  const firstDay = dayNames[0] ?? "";

  // Time-bound sections: within each day, charges with a start time sort
  // chronologically first; anything with no time noted falls to the end
  // rather than being sorted as if it were midnight.
  const byDay = useMemo(() => {
    const groups = new Map<string, PujaScheduleItem[]>();
    for (const day of dayNames) groups.set(day, []);
    for (const item of data) {
      if (!groups.has(item.puja_day)) groups.set(item.puja_day, []);
      groups.get(item.puja_day)!.push(item);
    }
    for (const items of groups.values()) {
      items.sort((a, b) => {
        if (!a.start_time && !b.start_time) return 0;
        if (!a.start_time) return 1;
        if (!b.start_time) return -1;
        return a.start_time.localeCompare(b.start_time);
      });
    }
    return groups;
  }, [data, dayNames]);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Puja Schedule" description="Who's in charge of what, on which day" icon={<CalendarClock />}
        actions={canWrite && (
          <Button onClick={() => { setEditing(null); setAddDay(firstDay); setOpen(true); }}>
            <Plus /> <span className="hidden sm:inline">Add charge</span>
          </Button>
        )}
      />

      {activeYearId && <PujaCalendarCard yearId={activeYearId} canWrite={canWrite} />}

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : data.length === 0 ? (
          <EmptyState icon={<CalendarClock />} title="No charges scheduled yet"
            description={canWrite
              ? "Assign duties like gate management, bhog distribution or lighting to each puja day, with a time if it's time-bound."
              : "Nothing scheduled yet."}
            action={canWrite && <Button onClick={() => { setEditing(null); setAddDay(firstDay); setOpen(true); }}><Plus /> Add charge</Button>} />
        ) : (
          <div className="space-y-5">
            {[...byDay.entries()].filter(([, items]) => items.length > 0).map(([day, items]) => (
              <div key={day}>
                <div className="mb-2 flex items-center justify-between">
                  <h3 className="font-display text-sm font-semibold text-foreground">{day}</h3>
                  {canWrite && (
                    <Button variant="ghost" size="sm" className="h-7 gap-1 text-xs"
                      onClick={() => { setEditing(null); setAddDay(day); setOpen(true); }}>
                      <Plus className="h-3.5 w-3.5" /> Add
                    </Button>
                  )}
                </div>
                <Card className="divide-y divide-border">
                  {items.map((it) => {
                    const timeRange = it.start_time
                      ? `${fmtTime(it.start_time)}${it.end_time ? ` – ${fmtTime(it.end_time)}` : ""}`
                      : null;
                    return (
                      <div key={it.id} className="flex items-center gap-3 p-3.5 sm:px-4">
                        {timeRange ? (
                          <div className="flex h-10 w-16 shrink-0 flex-col items-center justify-center rounded-lg bg-primary/10 text-primary">
                            <Clock className="h-3.5 w-3.5" />
                            <span className="mt-0.5 text-center text-[10px] font-semibold leading-none tabular">{timeRange}</span>
                          </div>
                        ) : (
                          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                            <User className="h-5 w-5" />
                          </div>
                        )}
                        <div className="min-w-0 flex-1">
                          <p className="truncate font-medium">{it.charge_title}</p>
                          <p className="truncate text-xs text-muted-foreground">
                            {it.collector_name || it.assignee_name || "Unassigned"}
                            {it.event_date ? ` · ${fmtDate(it.event_date)}` : ""}
                          </p>
                        </div>
                        {it.contact && (
                          <Badge variant="outline" className="hidden shrink-0 gap-1 sm:inline-flex">
                            <Phone className="h-3 w-3" /> {it.contact}
                          </Badge>
                        )}
                        <div className="flex shrink-0 gap-0.5">
                          {canWrite && (
                            <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(it); setOpen(true); }}>
                              <Pencil className="h-4 w-4" />
                            </Button>
                          )}
                          {canWrite && (
                            <ConfirmDialog title="Remove charge?" destructive confirmLabel="Delete"
                              onConfirm={() => remove.mutateAsync(it.id)}
                              trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                          )}
                        </div>
                      </div>
                    );
                  })}
                </Card>
              </div>
            ))}
          </div>
        )}

      {activeYearId && <ScheduleDialog open={open} onOpenChange={setOpen} item={editing} yearId={activeYearId} defaultDay={addDay || firstDay} />}
    </div>
  );
}
