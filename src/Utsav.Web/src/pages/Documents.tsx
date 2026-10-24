import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { FileText, Plus, Search, Download, Trash2, FileArchive } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useDocuments } from "@/hooks/useFinance";
import { api, getToken } from "@/lib/api";
import { fmtDate } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { cn } from "@/lib/utils";
import type { DocumentFile } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

// Mirrors Utsav.Api.Endpoints.DocumentEndpoints.Categories exactly.
const CATEGORIES = ["Puja Permission", "Contract", "Other"];

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

async function downloadDocument(doc: DocumentFile) {
  const token = getToken();
  const res = await fetch(`/api/documents/${doc.id}/download`, {
    headers: token ? { Authorization: `Bearer ${token}` } : undefined,
  });
  if (!res.ok) {
    toast.error("Download failed — the file may be missing from storage.");
    return;
  }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = window.document.createElement("a");
  a.href = url;
  a.download = doc.file_name;
  a.click();
  URL.revokeObjectURL(url);
}

interface FormValues {
  title: string;
  category: string;
  notes: string;
  standing: string; // "year" | "standing" — kept as a string for the Select
}
const empty: FormValues = { title: "", category: CATEGORIES[0], notes: "", standing: "year" };

function UploadDialog({
  open, onOpenChange, yearId, yearName,
}: { open: boolean; onOpenChange: (v: boolean) => void; yearId: string | null; yearName: string }) {
  const qc = useQueryClient();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<FormValues>({ defaultValues: empty });
  const [file, setFile] = useState<File | null>(null);

  const upload = useMutation({
    mutationFn: async (v: FormValues) => {
      if (!file) throw new Error("Choose a file first");
      const form = new FormData();
      form.append("file", file);
      form.append("title", v.title.trim());
      form.append("category", v.category);
      if (v.notes.trim()) form.append("notes", v.notes.trim());
      if (v.standing === "year" && yearId) form.append("year_id", yearId);
      return api.upload("/documents", form);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["documents"] });
      toast.success("Document uploaded");
      reset(empty);
      setFile(null);
      onOpenChange(false);
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <Dialog open={open} onOpenChange={(v) => { onOpenChange(v); if (!v) { reset(empty); setFile(null); } }}>
      <DialogContent>
        <DialogHeader><DialogTitle>Upload document</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit((v) => upload.mutate(v), onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="File" required>
            <input
              type="file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-foreground file:mr-3 file:rounded-md file:border-0 file:bg-primary file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-primary-foreground"
            />
          </Field>
          <Field label="Title" required>
            <Input {...register("title", { required: true })} placeholder="e.g. Police NOC 2026" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Category">
              <Select value={watch("category")} onValueChange={(v) => setValue("category", v)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {CATEGORIES.map((c) => <SelectItem key={c} value={c}>{c}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
            <Field label="Applies to">
              <Select value={watch("standing")} onValueChange={(v) => setValue("standing", v)}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="year">{yearName}</SelectItem>
                  <SelectItem value="standing">All years</SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
          <Field label="Notes"><Input {...register("notes")} placeholder="Optional" /></Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={upload.isPending || !file}>
              {upload.isPending ? "Uploading…" : "Upload"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function DocumentsPage() {
  const { activeYearId, years } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_finance");
  const { data = [], isLoading, isError, error } = useDocuments(activeYearId);
  const qc = useQueryClient();
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const [open, setOpen] = useState(false);

  const remove = useMutation<void, Error, string>({
    mutationFn: async (id) => { await api.del(`/documents/${id}`); },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["documents"] }); toast.success("Document deleted"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((d) => {
      if (q && !d.title.toLowerCase().includes(q) && !(d.notes ?? "").toLowerCase().includes(q)) return false;
      if (category && d.category !== category) return false;
      return true;
    });
  }, [data, search, category]);

  const yearName = years?.find((y) => y.id === activeYearId)?.name ?? "this year";
  const categoriesInUse = new Set(data.map((d) => d.category).filter(Boolean));

  return (
    <div className="space-y-5">
      <PageHeader
        title="Documents" description="Puja permissions and contract paperwork" icon={<FileArchive />}
        actions={canWrite && (
          <Button onClick={() => setOpen(true)}><Plus /> <span className="hidden sm:inline">Upload document</span></Button>
        )}
      />

      <SummaryBar items={[
        { label: "Total Files", value: data.length, isMoney: false },
        { label: "Permissions", value: data.filter((d) => d.category === "Puja Permission").length, isMoney: false },
        { label: "Contracts", value: data.filter((d) => d.category === "Contract").length, isMoney: false },
        { label: "Categories", value: categoriesInUse.size, isMoney: false },
      ]} />

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search documents…" className="pl-9" />
        </div>
        <FilterMenu activeCount={category ? 1 : 0} onClear={() => setCategory(null)}>
          <FilterField label="Category">
            <select
              value={category ?? ""}
              onChange={(e) => setCategory(e.target.value || null)}
              className="h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm"
            >
              <option value="">All categories</option>
              {CATEGORIES.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </FilterField>
        </FilterMenu>
      </div>

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<FileText />} title="No documents"
            description={canWrite ? "Upload your first permit or contract." : "Nothing uploaded yet."}
            action={canWrite && <Button onClick={() => setOpen(true)}><Plus /> Upload document</Button>} />
        ) : (
          <Card className="divide-y divide-border">
            {filtered.map((d) => (
              <div key={d.id} className="flex items-center gap-3 p-3.5 sm:px-4">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <FileText className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <p className="truncate font-medium">{d.title}</p>
                    {d.category && <Badge variant="secondary">{d.category}</Badge>}
                  </div>
                  <p className="truncate text-xs text-muted-foreground">
                    {d.file_name} · {formatSize(d.size_bytes)} · {fmtDate(d.created_at)}
                    {d.uploaded_by_name ? ` · by ${d.uploaded_by_name}` : ""}
                    {d.notes ? ` · ${d.notes}` : ""}
                  </p>
                </div>
                <div className="flex shrink-0 gap-0.5">
                  <Button variant="ghost" size="icon" className="h-8 w-8" title="Download"
                    onClick={() => downloadDocument(d)}>
                    <Download className="h-4 w-4" />
                  </Button>
                  {canWrite && (
                    <ConfirmDialog title="Delete document?" destructive confirmLabel="Delete"
                      onConfirm={() => remove.mutateAsync(d.id)}
                      trigger={<Button variant="ghost" size="icon" className={cn("h-8 w-8 text-destructive")}><Trash2 className="h-4 w-4" /></Button>} />
                  )}
                </div>
              </div>
            ))}
          </Card>
        )}

      {canWrite && <UploadDialog open={open} onOpenChange={setOpen} yearId={activeYearId} yearName={yearName} />}
    </div>
  );
}
