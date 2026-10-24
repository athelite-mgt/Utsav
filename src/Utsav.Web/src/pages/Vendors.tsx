import { useEffect, useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Truck, Plus, Search, Pencil, Trash2, Lock, FileText, AlertTriangle,
} from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useVendors, useContracts, useVendorIssues, sum } from "@/hooks/useFinance";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import { api } from "@/lib/api";
import { money, fmtDate, pct } from "@/lib/format";
import { onInvalidFocus } from "@/lib/formErrors";
import { cn } from "@/lib/utils";
import type { ContractStatus, ContractWithStatus, Vendor, VendorIssue, VendorIssueStatus } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MoneyInput } from "@/components/ui/money-input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Field } from "@/components/common/Field";
import { LookupSelect, OptionSelect } from "@/components/form/OptionSelect";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

const STATUS_TONE: Record<ContractStatus, "default" | "success" | "destructive" | "gold"> = {
  draft: "default", active: "gold", completed: "success", cancelled: "destructive",
};

// ---------------- Vendors ----------------
interface VendorForm {
  name: string; category: string | null; contact: string; email: string; notes: string;
}
const emptyVendor: VendorForm = { name: "", category: null, contact: "", email: "", notes: "" };

function VendorDialog({
  open, onOpenChange, vendor,
}: { open: boolean; onOpenChange: (v: boolean) => void; vendor?: Vendor | null }) {
  const { create, update } = useEntityMutations("vendors", ["vendors"], "Vendor");
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<VendorForm>({ defaultValues: emptyVendor });

  useEffect(() => {
    reset(vendor
      ? { name: vendor.name, category: vendor.category, contact: vendor.contact ?? "", email: vendor.email ?? "", notes: vendor.notes ?? "" }
      : emptyVendor);
  }, [vendor, open, reset]);

  async function onSubmit(v: VendorForm) {
    const payload = { ...v, contact: v.contact || null, email: v.email || null, notes: v.notes || null };
    if (vendor) await update.mutateAsync({ id: vendor.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{vendor ? "Edit vendor" : "Add vendor"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="Name" required>
            <Input {...register("name", { required: true })} placeholder="Vendor / supplier name" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Category">
              <LookupSelect category="vendor_category" value={watch("category")}
                onChange={(v) => setValue("category", v)} placeholder="Select category" allowClear />
            </Field>
            <Field label="Contact">
              <Input {...register("contact")} placeholder="Phone number" />
            </Field>
          </div>
          <Field label="Email">
            <Input type="email" {...register("email")} placeholder="vendor@example.com" />
          </Field>
          <Field label="Notes">
            <Textarea {...register("notes")} rows={2} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>
              {vendor ? "Save changes" : "Add vendor"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function VendorsTab({ canWrite }: { canWrite: boolean }) {
  const { data = [], isLoading, isError, error } = useVendors();
  const { remove } = useEntityMutations("vendors", ["vendors"], "Vendor");
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<Vendor | null>(null);

  const categories = useMemo(() => [...new Set(data.map((v) => v.category).filter(Boolean))] as string[], [data]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((v) => {
      if (q && !v.name.toLowerCase().includes(q) && !(v.category ?? "").toLowerCase().includes(q)) return false;
      if (category && v.category !== category) return false;
      return true;
    });
  }, [data, search, category]);

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative flex-1 sm:max-w-sm">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search vendors…" className="pl-9" />
        </div>
        <FilterMenu activeCount={category ? 1 : 0} onClear={() => setCategory(null)}>
          <FilterField label="Category">
            <select value={category ?? ""} onChange={(e) => setCategory(e.target.value || null)} className="h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm">
              <option value="">All categories</option>
              {categories.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </FilterField>
        </FilterMenu>
        {canWrite && (
          <Button onClick={() => { setEditing(null); setOpen(true); }}>
            <Plus /> <span className="hidden sm:inline">Add vendor</span>
          </Button>
        )}
      </div>

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<Truck />} title="No vendors"
            description={canWrite ? "Add the pandal, decoration, catering and other suppliers you work with." : "None added yet."}
            action={canWrite && <Button onClick={() => { setEditing(null); setOpen(true); }}><Plus /> Add vendor</Button>} />
        ) : (
          <Card className="divide-y divide-border">
            {filtered.map((v) => (
              <div
                key={v.id}
                className={cn("flex items-center gap-3 p-3.5 sm:px-4", canWrite && "cursor-pointer hover:bg-muted/40")}
                onClick={() => { if (canWrite) { setEditing(v); setOpen(true); } }}
              >
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <Truck className="h-5 w-5" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate font-medium">{v.name}</p>
                    {v.category && <Badge variant="outline" className="hidden sm:inline-flex">{v.category}</Badge>}
                  </div>
                  <p className="truncate text-xs text-muted-foreground">
                    {[v.contact, v.email].filter(Boolean).join(" · ") || "No contact on file"}
                  </p>
                </div>
                {canWrite && (
                  <div className="flex shrink-0 gap-0.5" onClick={(e) => e.stopPropagation()}>
                    <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(v); setOpen(true); }}>
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <ConfirmDialog title="Remove vendor?" description="Contracts already linked to this vendor keep their history."
                      destructive confirmLabel="Delete"
                      onConfirm={() => remove.mutateAsync(v.id)}
                      trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                  </div>
                )}
              </div>
            ))}
          </Card>
        )}
      <VendorDialog open={open} onOpenChange={setOpen} vendor={editing} />
    </div>
  );
}

// ---------------- Contracts ----------------
interface ContractForm {
  vendor_id: string; title: string; category: string | null;
  contract_amount: number; start_date: string; end_date: string;
  status: ContractStatus; terms: string; notes: string;
}
const emptyContract: ContractForm = {
  vendor_id: "", title: "", category: null, contract_amount: 0,
  start_date: "", end_date: "", status: "draft", terms: "", notes: "",
};

function ContractDialog({
  open, onOpenChange, contract, yearId, vendors,
}: { open: boolean; onOpenChange: (v: boolean) => void; contract?: ContractWithStatus | null; yearId: string; vendors: Vendor[] }) {
  const qc = useQueryClient();
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<ContractForm>({ defaultValues: emptyContract });

  useEffect(() => {
    reset(contract
      ? {
          vendor_id: contract.vendor_id, title: contract.title, category: contract.category,
          contract_amount: contract.contract_amount, start_date: contract.start_date ?? "",
          end_date: contract.end_date ?? "", status: contract.status,
          terms: contract.terms ?? "", notes: contract.notes ?? "",
        }
      : emptyContract);
  }, [contract, open, reset]);

  async function onSubmit(v: ContractForm) {
    const payload = {
      ...v, year_id: yearId, contract_amount: v.contract_amount || 0,
      start_date: v.start_date || null, end_date: v.end_date || null,
      terms: v.terms || null, notes: v.notes || null,
      updated_at: new Date().toISOString(),
    };
    if (contract) await api.patch(`/contracts/${contract.id}`, payload);
    else await api.post("/contracts", payload);
    qc.invalidateQueries({ queryKey: ["contracts"] });
    toast.success(contract ? "Contract updated" : "Contract added");
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{contract ? "Edit contract" : "New contract"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="Vendor" required>
            <OptionSelect value={watch("vendor_id")} onChange={(v) => setValue("vendor_id", v ?? "")}
              options={vendors.map((v) => ({ value: v.id, label: v.name }))} placeholder="Select vendor" />
          </Field>
          <Field label="Title" required>
            <Input {...register("title", { required: true })} placeholder="e.g. Pandal decoration 2025" />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            {/* Matches expense_category, not vendor_category — this is which
                Budget line the contract's committed amount counts against
                (see v_budget_vs_actual), not what kind of supplier this is
                (that's the vendor's own category, set on the Vendor itself). */}
            <Field label="Budget category" hint="Which budget line this contract counts against">
              <LookupSelect category="expense_category" value={watch("category")}
                onChange={(v) => setValue("category", v)} placeholder="Select category" allowClear />
            </Field>
            <Field label="Contract amount" required>
              <MoneyInput step="0.01" {...register("contract_amount", { valueAsNumber: true, required: true })} />
            </Field>
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Start date">
              <Input type="date" {...register("start_date")} />
            </Field>
            <Field label="End date">
              <Input type="date" {...register("end_date")} />
            </Field>
            <Field label="Status">
              <OptionSelect value={watch("status")} onChange={(v) => setValue("status", (v ?? "draft") as ContractStatus)}
                options={[
                  { value: "draft", label: "Draft" }, { value: "active", label: "Active" },
                  { value: "completed", label: "Completed" }, { value: "cancelled", label: "Cancelled" },
                ]} />
            </Field>
          </div>
          <Field label="Terms">
            <Textarea {...register("terms")} rows={2} placeholder="Payment terms, deliverables…" />
          </Field>
          <Field label="Notes">
            <Textarea {...register("notes")} rows={2} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit">{contract ? "Save changes" : "Create contract"}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

const CONTRACT_STATUS_OPTIONS: { value: ContractStatus; label: string }[] = [
  { value: "draft", label: "Draft" }, { value: "active", label: "Active" },
  { value: "completed", label: "Completed" }, { value: "cancelled", label: "Cancelled" },
];

function ContractsTab({ canWrite }: { canWrite: boolean }) {
  const { activeYearId } = useYear();
  const { data = [], isLoading, isError, error } = useContracts(activeYearId);
  const { data: vendors = [] } = useVendors();
  const qc = useQueryClient();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<ContractStatus | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<ContractWithStatus | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((c) => {
      if (q && !c.title.toLowerCase().includes(q) && !c.vendor_name.toLowerCase().includes(q) && !(c.category ?? "").toLowerCase().includes(q)) return false;
      if (status && c.status !== status) return false;
      return true;
    });
  }, [data, search, status]);

  const totalCommitted = sum(filtered, (c) => c.contract_amount);
  const totalPaid = sum(filtered, (c) => c.paid);
  const totalOutstanding = sum(filtered, (c) => c.outstanding);

  async function remove(id: string) {
    await api.del(`/contracts/${id}`);
    qc.invalidateQueries({ queryKey: ["contracts"] });
    toast.success("Contract deleted");
  }

  return (
    <div className="space-y-4">
      <SummaryBar items={[
        { label: "Committed", value: totalCommitted, tone: "primary" },
        { label: "Paid", value: totalPaid, tone: "success" },
        { label: "Outstanding", value: totalOutstanding, tone: totalOutstanding > 0 ? "destructive" : "success" },
        { label: "Contracts", value: filtered.length, isMoney: false },
      ]} />

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-1 flex-col gap-2 sm:flex-row">
          <div className="relative flex-1 sm:max-w-sm">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search contracts…" className="pl-9" />
          </div>
          <FilterMenu activeCount={status ? 1 : 0} onClear={() => setStatus(null)}>
            <FilterField label="Status">
              <select value={status ?? ""} onChange={(e) => setStatus((e.target.value || null) as ContractStatus | null)} className="h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm">
                <option value="">All statuses</option>
                {CONTRACT_STATUS_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </FilterField>
          </FilterMenu>
        </div>
        {canWrite && (
          <div className="flex flex-col items-end gap-1.5">
            <Button disabled={vendors.length === 0} title={vendors.length === 0 ? "Add a vendor first" : undefined}
              onClick={() => { setEditing(null); setOpen(true); }}>
              <Plus /> <span className="hidden sm:inline">New contract</span>
            </Button>
            {vendors.length === 0 && (
              <p className="text-xs text-muted-foreground">Add a vendor first, then create a contract against them.</p>
            )}
          </div>
        )}
      </div>

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<FileText />} title="No contracts"
            description={canWrite ? "Track vendor agreements, amounts and how much has been paid." : "None added yet."} />
        ) : (
          <Card className="divide-y divide-border">
            {filtered.map((c) => {
              const paidPct = pct(c.paid, c.contract_amount || 1);
              return (
                <div key={c.id} className="space-y-2 p-3.5 sm:px-4">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <p className="truncate font-medium">{c.title}</p>
                        <Badge variant={STATUS_TONE[c.status]} className="capitalize">{c.status}</Badge>
                      </div>
                      <p className="truncate text-xs text-muted-foreground">
                        {c.vendor_name} {c.category ? `· ${c.category}` : ""}
                        {c.end_date ? ` · due ${fmtDate(c.end_date)}` : ""}
                      </p>
                    </div>
                    {canWrite && (
                      <div className="flex shrink-0 gap-0.5">
                        <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(c); setOpen(true); }}>
                          <Pencil className="h-4 w-4" />
                        </Button>
                        <ConfirmDialog title="Delete contract?" destructive confirmLabel="Delete"
                          description="Expenses already recorded against this contract are kept."
                          onConfirm={() => remove(c.id)}
                          trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                      </div>
                    )}
                  </div>
                  <div className="flex items-center gap-3">
                    <Progress value={Math.min(paidPct, 100)} className="h-2 flex-1" />
                    <p className="shrink-0 text-xs tabular text-muted-foreground">
                      {money(c.paid)} / {money(c.contract_amount)}
                    </p>
                  </div>
                </div>
              );
            })}
          </Card>
        )}
      {activeYearId && <ContractDialog open={open} onOpenChange={setOpen} contract={editing} yearId={activeYearId} vendors={vendors} />}
    </div>
  );
}

// ---------------- Vendor issues ----------------
const today = () => new Date().toISOString().slice(0, 10);

const ISSUE_STATUS_OPTIONS: { value: VendorIssueStatus; label: string }[] = [
  { value: "open", label: "Open" }, { value: "resolved", label: "Resolved" },
];

interface IssueForm {
  vendor_id: string; contract_id: string | null; issue_type: string | null;
  description: string; occurred_on: string; status: VendorIssueStatus;
}
const emptyIssue: IssueForm = {
  vendor_id: "", contract_id: null, issue_type: null, description: "", occurred_on: today(), status: "open",
};

function IssueDialog({
  open, onOpenChange, issue, vendors, contracts,
}: { open: boolean; onOpenChange: (v: boolean) => void; issue?: VendorIssue | null; vendors: Vendor[]; contracts: ContractWithStatus[] }) {
  const { create, update } = useEntityMutations("vendor_issues", ["vendor_issues"], "Issue");
  const { register, handleSubmit, reset, watch, setValue, setFocus } = useForm<IssueForm>({ defaultValues: emptyIssue });

  useEffect(() => {
    reset(issue
      ? {
          vendor_id: issue.vendor_id, contract_id: issue.contract_id, issue_type: issue.issue_type,
          description: issue.description, occurred_on: issue.occurred_on ?? today(), status: issue.status,
        }
      : emptyIssue);
  }, [issue, open, reset]);

  const vendorId = watch("vendor_id");
  const vendorContracts = useMemo(() => contracts.filter((c) => c.vendor_id === vendorId), [contracts, vendorId]);

  async function onSubmit(v: IssueForm) {
    const payload = { ...v, contract_id: v.contract_id || null, occurred_on: v.occurred_on || null };
    if (issue) await update.mutateAsync({ id: issue.id, values: payload });
    else await create.mutateAsync(payload);
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader><DialogTitle>{issue ? "Edit issue" : "Log an issue"}</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit, onInvalidFocus(setFocus))} className="space-y-4">
          <Field label="Vendor" required>
            <OptionSelect value={watch("vendor_id")}
              onChange={(v) => { setValue("vendor_id", v ?? ""); setValue("contract_id", null); }}
              options={vendors.map((v) => ({ value: v.id, label: v.name }))} placeholder="Select vendor" />
          </Field>
          <Field label="Contract" hint="Only if this happened under a specific contract, not the vendor relationship generally">
            <OptionSelect value={watch("contract_id")} onChange={(v) => setValue("contract_id", v)}
              options={vendorContracts.map((c) => ({ value: c.id, label: c.title }))}
              placeholder={vendorId ? "No specific contract" : "Select a vendor first"} allowClear />
          </Field>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Issue type" required>
              <LookupSelect category="vendor_issue_type" value={watch("issue_type")}
                onChange={(v) => setValue("issue_type", v)} placeholder="Select type" />
            </Field>
            <Field label="Date" required>
              <Input type="date" {...register("occurred_on", { required: true })} />
            </Field>
          </div>
          <Field label="Description" required>
            <Textarea {...register("description", { required: true })} rows={3} placeholder="What happened…" />
          </Field>
          <Field label="Status">
            <OptionSelect value={watch("status")} onChange={(v) => setValue("status", (v ?? "open") as VendorIssueStatus)}
              options={ISSUE_STATUS_OPTIONS} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={create.isPending || update.isPending}>
              {issue ? "Save changes" : "Log issue"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function IssuesTab({ canWrite }: { canWrite: boolean }) {
  const { data: vendors = [] } = useVendors();
  const { activeYearId } = useYear();
  const { data: contracts = [] } = useContracts(activeYearId);
  const { data = [], isLoading, isError, error } = useVendorIssues();
  const { remove } = useEntityMutations("vendor_issues", ["vendor_issues"], "Issue");
  const [search, setSearch] = useState("");
  const [vendorFilter, setVendorFilter] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<VendorIssueStatus | null>(null);
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState<VendorIssue | null>(null);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return data.filter((i) => {
      if (q && !i.description.toLowerCase().includes(q) && !i.vendor_name.toLowerCase().includes(q)
        && !i.issue_type.toLowerCase().includes(q)) return false;
      if (vendorFilter && i.vendor_id !== vendorFilter) return false;
      if (statusFilter && i.status !== statusFilter) return false;
      return true;
    });
  }, [data, search, vendorFilter, statusFilter]);

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-1 flex-col gap-2 sm:flex-row">
          <div className="relative flex-1 sm:max-w-sm">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search issues…" className="pl-9" />
          </div>
          <FilterMenu activeCount={(vendorFilter ? 1 : 0) + (statusFilter ? 1 : 0)}
            onClear={() => { setVendorFilter(null); setStatusFilter(null); }}>
            <FilterField label="Vendor">
              <select value={vendorFilter ?? ""} onChange={(e) => setVendorFilter(e.target.value || null)}
                className="h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm">
                <option value="">All vendors</option>
                {vendors.map((v) => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </FilterField>
            <FilterField label="Status">
              <select value={statusFilter ?? ""} onChange={(e) => setStatusFilter((e.target.value || null) as VendorIssueStatus | null)}
                className="h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm">
                <option value="">All statuses</option>
                {ISSUE_STATUS_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </FilterField>
          </FilterMenu>
        </div>
        {canWrite && (
          <div className="flex flex-col items-end gap-1.5">
            <Button disabled={vendors.length === 0} title={vendors.length === 0 ? "Add a vendor first" : undefined}
              onClick={() => { setEditing(null); setOpen(true); }}>
              <Plus /> <span className="hidden sm:inline">Log issue</span>
            </Button>
          </div>
        )}
      </div>

      {isLoading ? <TableSkeleton />
        : isError ? <ErrorState message={(error as Error)?.message} />
        : filtered.length === 0 ? (
          <EmptyState icon={<AlertTriangle />} title="No issues logged"
            description={canWrite
              ? "Keep a record of delays, quality problems or disputes — useful when it's time to decide on renewing a vendor."
              : "None logged yet."} />
        ) : (
          <Card className="divide-y divide-border">
            {filtered.map((i) => (
              <div key={i.id} className="flex items-start justify-between gap-3 p-3.5 sm:px-4">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <p className="truncate font-medium">{i.vendor_name}</p>
                    <Badge variant="outline">{i.issue_type}</Badge>
                    <Badge variant={i.status === "open" ? "destructive" : "success"} className="capitalize">{i.status}</Badge>
                  </div>
                  <p className="mt-0.5 text-sm text-muted-foreground">{i.description}</p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {i.occurred_on ? fmtDate(i.occurred_on) : "No date"}{i.contract_title ? ` · ${i.contract_title}` : ""}
                  </p>
                </div>
                {canWrite && (
                  <div className="flex shrink-0 gap-0.5">
                    <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setEditing(i); setOpen(true); }}>
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <ConfirmDialog title="Delete this issue?" destructive confirmLabel="Delete"
                      onConfirm={() => remove.mutateAsync(i.id)}
                      trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                  </div>
                )}
              </div>
            ))}
          </Card>
        )}
      <IssueDialog open={open} onOpenChange={setOpen} issue={editing} vendors={vendors} contracts={contracts} />
    </div>
  );
}

// ---------------- Page ----------------
export function VendorsPage() {
  const { can } = useAuth();
  const canWrite = can("write_finance");

  return (
    <div className="space-y-5">
      <PageHeader title="Vendors & Contracts" description="Suppliers you work with and what's owed" icon={<Truck />} />

      {!canWrite && (
        <p className="flex items-center gap-2 rounded-lg bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
          <Lock className="h-3.5 w-3.5" /> View only — vendors and contracts are managed by finance and above.
        </p>
      )}

      <Tabs defaultValue="contracts">
        <TabsList>
          <TabsTrigger value="contracts" className="gap-1.5"><FileText className="h-4 w-4" /> Contracts</TabsTrigger>
          <TabsTrigger value="vendors" className="gap-1.5"><Truck className="h-4 w-4" /> Vendors</TabsTrigger>
          <TabsTrigger value="issues" className="gap-1.5"><AlertTriangle className="h-4 w-4" /> Issues</TabsTrigger>
        </TabsList>
        <TabsContent value="contracts"><ContractsTab canWrite={canWrite} /></TabsContent>
        <TabsContent value="vendors"><VendorsTab canWrite={canWrite} /></TabsContent>
        <TabsContent value="issues"><IssuesTab canWrite={canWrite} /></TabsContent>
      </Tabs>
    </div>
  );
}
