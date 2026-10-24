import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { isPast, isToday, parseISO } from "date-fns";
import {
  ListChecks, Plus, Pencil, Trash2, Circle, CircleDot, CircleAlert, CircleCheck, Send,
  Check, User, CalendarDays, Search,
} from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useWorkflowItems, useWorkflowNotes } from "@/hooks/useFinance";
import { useCollectors, useLookups } from "@/hooks/useReference";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { api } from "@/lib/api";
import { fmtDate } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { cn } from "@/lib/utils";
import type { WorkflowItem, WorkflowStatus } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Field } from "@/components/common/Field";
import { OptionSelect, LookupSelect } from "@/components/form/OptionSelect";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

const STATUS_META: Record<WorkflowStatus, { label: string; icon: typeof Circle; tone: "default" | "gold" | "destructive" | "success" }> = {
  not_started: { label: "Not started", icon: Circle, tone: "default" },
  in_progress: { label: "In progress", icon: CircleDot, tone: "gold" },
  blocked: { label: "Blocked", icon: CircleAlert, tone: "destructive" },
  done: { label: "Done", icon: CircleCheck, tone: "success" },
};
const STATUS_ORDER: WorkflowStatus[] = ["not_started", "in_progress", "blocked", "done"];

function isOverdue(item: WorkflowItem) {
  if (item.status === "done" || !item.due_date) return false;
  const d = parseISO(item.due_date);
  return isPast(d) && !isToday(d);
}

// ---------------- Activity log ----------------
// Author always comes from the signed-in session server-side — there is
// deliberately no "who" field here for the user to fill in themselves.
function WorkflowNotes({ itemId }: { itemId: string }) {
  const qc = useQueryClient();
  const { data = [], isLoading } = useWorkflowNotes(itemId);
  const [text, setText] = useState("");

  const add = useMutation({
    mutationFn: (note: string) => api.post(`/workflow_items/${itemId}/notes`, { note }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["workflow_notes", itemId] });
      setText("");
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <div className="space-y-3 border-t border-border pt-4">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Activity log</p>
      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No notes yet.</p>
      ) : (
        <div className="max-h-48 space-y-2 overflow-y-auto rounded-lg bg-muted/40 p-3">
          {data.map((n) => (
            <div key={n.id} className="text-sm">
              <div className="flex items-baseline gap-2">
                <span className="font-medium">{n.author_name ?? "Unknown"}</span>
                <span className="text-xs text-muted-foreground">{fmtDate(n.created_at, "dd MMM, h:mm a")}</span>
              </div>
              <p className="text-foreground">{n.note}</p>
            </div>
          ))}
        </div>
      )}
      <div className="flex gap-2">
        <Textarea value={text} onChange={(e) => setText(e.target.value)} rows={2}
          placeholder="Add a status update or note…" />
        <Button type="button" size="icon" className="shrink-0" disabled={!text.trim() || add.isPending}
          onClick={() => add.mutate(text.trim())}>
          <Send className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}

interface FormValues {
  title: string; category: string | null; owner_name: string; owner_collector_id: string | null;
  status: WorkflowStatus; due_date: string; notes: string;
}
const emptyForm: FormValues = {
  title: "", category: null, owner_name: "", owner_collector_id: null,
  status: "not_started", due_date: "", notes: "",
};

function WorkflowDialog({
  open, onOpenChange, item, yearId,
}: { open: boolean; onOpenChange: (v: boolean) => void; item?: WorkflowItem | null; yearId: string }) {
  const { create, update } = useEntityMutations("workflow_items", ["workflow_items"], "Task");
  const { data: collectors = [] } = useCollectors();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: emptyForm });

  useEffect(() => {
    reset(item
      ? {
          title: item.title, category: item.category, owner_name: item.owner_name ?? "",
          owner_collector_id: item.owner_collector_id, status: item.status,
          due_date: item.due_date ?? "", notes: item.notes ?? "",
        }
      : emptyForm);
  }, [item, open, reset]);

  async function onSubmit(v: FormValues) {
    const wasDone = item?.status === "done";
    const nowDone = v.status === "done";
    const payload = {
      ...v, year_id: yearId, category: v.category || null, owner_name: v.owner_name || null,
      due_date: v.due_date || null, notes: v.notes || null,
      completed_date: nowDone ? (item?.completed_date ?? new Date().toISOString().slice(0, 10)) : (wasDone ? null : item?.completed_date ?? null),
      updated_at: new Date().toISOString(),
    };
    if (item) await update.mutateAsync({ id: item.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{item ? "Edit task" : "Add task"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="Task" required>
            <Input {...register("title", { required: true })} placeholder="e.g. Book pandal, confirm dhaki" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Category">
              <LookupSelect category="expense_category" value={watch("category")} onChange={(v) => setValue("category", v)}
                placeholder="Select category" allowClear />
            </Field>
            <Field label="Due date">
              <Input type="date" {...register("due_date")} />
            </Field>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Owner">
              <OptionSelect value={watch("owner_collector_id")}
                onChange={(v) => {
                  setValue("owner_collector_id", v);
                  const c = collectors.find((x) => x.id === v);
                  if (c) setValue("owner_name", c.name);
                }}
                options={collectors.map((c) => ({ value: c.id, label: c.name }))}
                placeholder="Pick a collector" allowClear />
            </Field>
            <Field label="Status">
              <OptionSelect value={watch("status")} onChange={(v) => setValue("status", (v ?? "not_started") as WorkflowStatus)}
                options={STATUS_ORDER.map((s) => ({ value: s, label: STATUS_META[s].label }))} />
            </Field>
          </div>
          {/* Named apart from the activity log below on purpose — this is
              the standing description of the task, that is the running
              commentary on it. Two boxes both called "notes" was half the
              confusion on this screen. */}
          <Field label="Details" hint="What this task involves. For progress updates, use the activity log below.">
            <Textarea {...register("notes")} rows={2} placeholder="Any specifics, contacts, amounts…" />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>
              {item ? "Save changes" : "Add task"}
            </Button>
          </DialogFooter>
        </form>
        {item && <WorkflowNotes itemId={item.id} />}
      </DialogContent>
    </Dialog>
  );
}

// ---------------- One task row ----------------
// Two explicit controls, no hidden behaviour: a tick box that means "done"
// and a labelled status menu for everything in between. The old single
// circle silently cycled not started → in progress → blocked → done on each
// click, which is why nobody could tell what pressing it would do.
function TaskRow({
  item, canWrite, onEdit, onStatus, onDelete,
}: {
  item: WorkflowItem;
  canWrite: boolean;
  onEdit: () => void;
  onStatus: (s: WorkflowStatus) => void;
  onDelete: () => void | Promise<void>;
}) {
  const done = item.status === "done";
  const overdue = isOverdue(item);
  const owner = item.collector_name || item.owner_name;

  return (
    <div className="flex items-start gap-3 p-3.5 sm:items-center sm:px-4">
      <button
        type="button"
        onClick={() => onStatus(done ? "not_started" : "done")}
        aria-pressed={done}
        aria-label={done ? `Mark "${item.title}" as not done` : `Mark "${item.title}" as done`}
        title={done ? "Mark as not done" : "Mark as done"}
        className={cn(
          "mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-md border-2 transition-colors sm:mt-0",
          done
            ? "border-success bg-success text-white"
            : "border-muted-foreground/35 hover:border-primary hover:bg-primary/5"
        )}
      >
        {done && <Check className="h-4 w-4" strokeWidth={3} />}
      </button>

      <div className="min-w-0 flex-1">
        <p className={cn("font-medium", done && "text-muted-foreground line-through")}>
          {item.title}
        </p>
        <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
          <span className="inline-flex items-center gap-1">
            <User className="h-3 w-3" />
            {owner || "Unassigned"}
          </span>
          {item.due_date && (
            <span className={cn("inline-flex items-center gap-1", overdue && "font-medium text-destructive")}>
              <CalendarDays className="h-3 w-3" />
              {overdue ? "Overdue — " : "Due "}{fmtDate(item.due_date)}
            </span>
          )}
          {item.category && <Badge variant="outline" className="font-normal">{item.category}</Badge>}
          {/* The status select doesn't fit a phone row, so state is shown
              here instead; changing it there happens via Edit. */}
          {!done && (
            <Badge variant={STATUS_META[item.status].tone === "default" ? "outline" : STATUS_META[item.status].tone}
              className="sm:hidden">
              {STATUS_META[item.status].label}
            </Badge>
          )}
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-1">
        <OptionSelect
          value={item.status}
          onChange={(v) => onStatus((v ?? "not_started") as WorkflowStatus)}
          options={STATUS_ORDER.map((s) => ({ value: s, label: STATUS_META[s].label }))}
          className="hidden h-8 w-[132px] text-xs sm:flex"
        />
        <Button variant="ghost" size="icon" className="h-8 w-8" onClick={onEdit} aria-label={`Edit ${item.title}`}>
          <Pencil className="h-4 w-4" />
        </Button>
        {canWrite && (
          <ConfirmDialog title="Delete task?" destructive confirmLabel="Delete" onConfirm={onDelete}
            trigger={
              <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive" aria-label={`Delete ${item.title}`}>
                <Trash2 className="h-4 w-4" />
              </Button>
            } />
        )}
      </div>
    </div>
  );
}

export function WorkflowPage() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_workflow");
  const { data = [], isLoading, isError, error } = useWorkflowItems(activeYearId);
  const { remove, update } = useEntityMutations("workflow_items", ["workflow_items"], "Task");
  const { data: collectors = [] } = useCollectors();
  const { data: categories = [] } = useLookups("expense_category");
  const [statusFilter, setStatusFilter] = useState<WorkflowStatus | "all">("all");
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const [ownerId, setOwnerId] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<WorkflowItem | null>(null);

  const byStatus = statusFilter === "all" ? data : data.filter((w) => w.status === statusFilter);
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return byStatus.filter((w) => {
      if (q && !w.title.toLowerCase().includes(q) && !(w.owner_name ?? "").toLowerCase().includes(q)) return false;
      if (category && w.category !== category) return false;
      if (ownerId && w.owner_collector_id !== ownerId) return false;
      return true;
    });
  }, [byStatus, search, category, ownerId]);

  const counts = Object.fromEntries(
    STATUS_ORDER.map((s) => [s, data.filter((w) => w.status === s).length])
  ) as Record<WorkflowStatus, number>;
  const doneCount = counts.done;
  const overdueCount = data.filter(isOverdue).length;
  const progressPct = data.length ? Math.round((doneCount / data.length) * 100) : 0;

  // With no filter on, the list is grouped by status so an "All" view reads
  // as a board — outstanding work at the top, finished work parked at the
  // bottom — instead of one undifferentiated column of tasks.
  const groups =
    statusFilter === "all"
      ? STATUS_ORDER.map((s) => ({ status: s, items: filtered.filter((w) => w.status === s) })).filter((g) => g.items.length)
      : [{ status: statusFilter, items: filtered }];

  function setStatus(item: WorkflowItem, next: WorkflowStatus) {
    update.mutate({
      id: item.id,
      values: {
        status: next,
        completed_date: next === "done" ? new Date().toISOString().slice(0, 10) : null,
        updated_at: new Date().toISOString(),
      },
    });
  }

  const addButton = (
    <Button onClick={() => { setEditing(null); setOpen(true); }}>
      <Plus /> <span className="hidden sm:inline">Add task</span>
    </Button>
  );

  return (
    <div className="space-y-5">
      <PageHeader
        title="Task Tracker"
        description="Everything the committee needs to get done — who owns it, and by when"
        icon={<ListChecks />}
        actions={addButton}
      />

      <SummaryBar items={[
        { label: "Completed", value: `${doneCount} / ${data.length}`, isMoney: false, tone: "success" },
        { label: "In progress", value: counts.in_progress, isMoney: false, tone: "primary" },
        { label: "Blocked", value: counts.blocked, isMoney: false, tone: counts.blocked > 0 ? "destructive" : "default" },
        { label: "Overdue", value: overdueCount, isMoney: false, tone: overdueCount > 0 ? "destructive" : "default" },
      ]} />

      {data.length > 0 && (
        <Card className="p-4">
          <div className="mb-2 flex items-baseline justify-between text-sm">
            <span className="font-medium">Overall progress</span>
            <span className="tabular text-muted-foreground">{progressPct}% complete</span>
          </div>
          <Progress value={progressPct} indicatorClassName={progressPct === 100 ? "bg-success" : undefined} />
        </Card>
      )}

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search tasks…" className="pl-9" />
        </div>
        <FilterMenu
          activeCount={(category ? 1 : 0) + (ownerId ? 1 : 0)}
          onClear={() => { setCategory(null); setOwnerId(null); }}
        >
          <FilterField label="Category">
            <OptionSelect value={category} onChange={setCategory} allowClear
              options={categories.map((l) => ({ value: l.label, label: l.label }))}
              placeholder="All categories" className="w-full" />
          </FilterField>
          <FilterField label="Owner">
            <OptionSelect value={ownerId} onChange={setOwnerId} allowClear
              options={collectors.map((c) => ({ value: c.id, label: c.name }))}
              placeholder="All owners" className="w-full" />
          </FilterField>
        </FilterMenu>
      </div>

      <Tabs value={statusFilter} onValueChange={(v) => setStatusFilter(v as WorkflowStatus | "all")}>
        <TabsList className="flex h-auto w-full flex-wrap justify-start gap-1 p-1">
          <TabsTrigger value="all">All ({data.length})</TabsTrigger>
          {STATUS_ORDER.map((s) => (
            <TabsTrigger key={s} value={s}>{STATUS_META[s].label} ({counts[s]})</TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<ListChecks />}
            title={data.length ? "Nothing in this status" : "No tasks yet"}
            description={data.length
              ? "Every task is sitting under a different status — switch tabs or view All."
              : "Add the things that have to happen before puja: book the pandal, order the idol, confirm the dhaki — each with an owner and a due date."}
            action={data.length ? undefined : addButton} />
        ) : (
          <div className="space-y-4">
            {groups.map((g) => {
              const meta = STATUS_META[g.status];
              const Icon = meta.icon;
              return (
                <div key={g.status}>
                  <div className="mb-1.5 flex items-center gap-2 px-1">
                    <Icon className={cn(
                      "h-4 w-4",
                      meta.tone === "success" && "text-success",
                      meta.tone === "gold" && "text-gold",
                      meta.tone === "destructive" && "text-destructive",
                      meta.tone === "default" && "text-muted-foreground"
                    )} />
                    <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                      {meta.label} · {g.items.length}
                    </p>
                  </div>
                  <Card className="divide-y divide-border">
                    {g.items.map((w) => (
                      <TaskRow
                        key={w.id} item={w} canWrite={canWrite}
                        onEdit={() => { setEditing(w); setOpen(true); }}
                        onStatus={(s) => setStatus(w, s)}
                        onDelete={async () => { await remove.mutateAsync(w.id); }}
                      />
                    ))}
                  </Card>
                </div>
              );
            })}
          </div>
        )}

      {activeYearId && <WorkflowDialog open={open} onOpenChange={setOpen} item={editing} yearId={activeYearId} />}
    </div>
  );
}
