import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { CalendarDays, Plus, Pencil, Trash2 } from "lucide-react";
import { usePujaCalendar } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { fmtDate } from "@/lib/format";
import type { PujaCalendarDay } from "@/types/db";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field } from "@/components/common/Field";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

interface FormValues { label: string; event_date: string; note: string; sort_order: number }

/** "Sat" from a yyyy-MM-dd string, for the little weekday chip. */
function weekday(d: string | null) {
  if (!d) return null;
  const parsed = new Date(`${d}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toLocaleDateString(undefined, { weekday: "short" });
}

function DayDialog({
  open, onOpenChange, day, yearId, nextSort,
}: { open: boolean; onOpenChange: (v: boolean) => void; day?: PujaCalendarDay | null; yearId: string; nextSort: number }) {
  const { create, update } = useEntityMutations("puja_calendar_days", ["puja_calendar_days"], "Puja day");
  const { register, handleSubmit, reset } = useForm<FormValues>({ defaultValues: { label: "", event_date: "", note: "", sort_order: nextSort } });

  useEffect(() => {
    reset(day
      ? { label: day.label, event_date: day.event_date ?? "", note: day.note ?? "", sort_order: day.sort_order }
      : { label: "", event_date: "", note: "", sort_order: nextSort });
  }, [day, open, nextSort, reset]);

  async function onSubmit(v: FormValues) {
    const payload = { year_id: yearId, label: v.label.trim(), event_date: v.event_date || null, note: v.note || null, sort_order: Number(v.sort_order) || 0 };
    if (day) await update.mutateAsync({ id: day.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{day ? "Edit puja day" : "Add puja day"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Day" required><Input {...register("label", { required: true })} placeholder="e.g. Sasthi" /></Field>
            <Field label="Date"><Input type="date" {...register("event_date")} /></Field>
          </div>
          <Field label="Note" hint="e.g. Sandhi Puja window, or a ritual detail"><Input {...register("note")} /></Field>
          <Field label="Order"><Input type="number" step="1" {...register("sort_order", { valueAsNumber: true })} /></Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>{day ? "Save" : "Add"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The committee's festival calendar for the year — the canonical day→date map,
 * kept to whichever panjika they follow (Surja Siddhanta here). Distinct from
 * the duty roster below it on the page.
 */
export function PujaCalendarCard({ yearId, canWrite }: { yearId: string; canWrite: boolean }) {
  const { data = [], isLoading } = usePujaCalendar(yearId);
  const { remove } = useEntityMutations("puja_calendar_days", ["puja_calendar_days"], "Puja day");
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<PujaCalendarDay | null>(null);
  const nextSort = (data.reduce((m, d) => Math.max(m, d.sort_order), 0) || 0) + 1;

  if (isLoading) return null;
  if (data.length === 0 && !canWrite) return null;

  return (
    <Card className="p-4">
      <div className="mb-3 flex items-center justify-between">
        <h3 className="flex items-center gap-2 font-display text-sm font-semibold">
          <CalendarDays className="h-4 w-4 text-primary" /> Festival dates
        </h3>
        {canWrite && (
          <Button variant="ghost" size="sm" className="h-7 gap-1 text-xs" onClick={() => { setEditing(null); setOpen(true); }}>
            <Plus className="h-3.5 w-3.5" /> Add day
          </Button>
        )}
      </div>

      {data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No festival dates set yet.</p>
      ) : (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
          {data.map((d) => (
            <div key={d.id} className="group relative rounded-lg border border-border p-3">
              <p className="text-sm font-semibold">{d.label}</p>
              <p className="text-xs text-muted-foreground">
                {d.event_date ? fmtDate(d.event_date) : "—"}
                {weekday(d.event_date) ? ` · ${weekday(d.event_date)}` : ""}
              </p>
              {d.note && <p className="mt-1 text-[11px] leading-tight text-muted-foreground">{d.note}</p>}
              {canWrite && (
                <div className="absolute right-1 top-1 flex gap-0.5 opacity-0 transition-opacity group-hover:opacity-100">
                  <Button variant="ghost" size="icon" className="h-7 w-7" onClick={() => { setEditing(d); setOpen(true); }}>
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                  <ConfirmDialog title="Remove this day?" destructive confirmLabel="Delete"
                    onConfirm={() => remove.mutateAsync(d.id)}
                    trigger={<Button variant="ghost" size="icon" className="h-7 w-7 text-destructive"><Trash2 className="h-3.5 w-3.5" /></Button>} />
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      <DayDialog open={open} onOpenChange={setOpen} day={editing} yearId={yearId} nextSort={nextSort} />
    </Card>
  );
}
