import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { useQuery, useQueryClient, useMutation } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Settings as Cog, Plus, Trash2, ShieldCheck, X, CalendarDays, Users, MapPin,
  ListChecks, UserCog, Building2, ArrowRightCircle, Check, UserPlus, Ban, Smartphone, Megaphone,
  KeyRound, Copy, Pencil,
} from "lucide-react";
import { api, qs } from "@/lib/api";
import { parseHex } from "@/lib/brand";
import { nextYearName } from "@/lib/yearName";
import { CommitteeAvatar } from "@/components/brand/CommitteeAvatar";
import { useAuth } from "@/context/AuthContext";
import { useEntityMutations } from "@/hooks/useEntityMutations";
import type { AccessRequest, AppRole, Collector, Lookup, LookupCategory, PujaYear, User, Zone } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Field } from "@/components/common/Field";
import { EmptyState } from "@/components/common/states";
import { InstallSettingsRow } from "@/components/common/InstallPrompt";
import { NotificationSettingsRow } from "@/components/common/NotificationSettings";
import { AnnouncementSettings } from "@/components/common/AnnouncementSettings";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";

const ROLE_OPTIONS: { value: AppRole; label: string }[] = [
  { value: "admin", label: "Admin" },
  { value: "secretariat", label: "Secretariat" },
  { value: "finance", label: "Finance" },
  { value: "executive", label: "Executive" },
  { value: "viewer", label: "Viewer" },
];

const LOOKUP_CATEGORIES: { value: LookupCategory; label: string }[] = [
  { value: "payment_mode", label: "Payment modes" },
  { value: "expense_category", label: "Expense categories" },
  { value: "ad_type", label: "Ad types" },
  { value: "ad_status", label: "Ad workflow status" },
  { value: "offering_type", label: "Offering types" },
  { value: "puja_day", label: "Puja days" },
  { value: "donation_purpose", label: "Donation purposes" },
  { value: "vendor_category", label: "Vendor categories" },
  { value: "vendor_issue_type", label: "Vendor issue types" },
];

function AddRow({ placeholder, onAdd }: { placeholder: string; onAdd: (v: string) => Promise<void> | void }) {
  const [v, setV] = useState("");
  return (
    <form
      className="flex gap-2"
      onSubmit={async (e) => { e.preventDefault(); if (!v.trim()) return; await onAdd(v.trim()); setV(""); }}
    >
      <Input value={v} onChange={(e) => setV(e.target.value)} placeholder={placeholder} />
      <Button type="submit" size="icon" className="shrink-0"><Plus className="h-4 w-4" /></Button>
    </form>
  );
}

// ---------------- Rollover ----------------
interface RolloverForm { year: number; name: string; target: number; projection_pct: number }

function RolloverDialog({
  open, onOpenChange, sourceYear,
}: { open: boolean; onOpenChange: (v: boolean) => void; sourceYear: PujaYear }) {
  const qc = useQueryClient();
  const { register, handleSubmit, reset } = useForm<RolloverForm>({
    defaultValues: {
      year: sourceYear.year + 1,
      name: nextYearName(sourceYear.name, sourceYear.year + 1),
      target: sourceYear.target,
      projection_pct: sourceYear.projection_pct,
    },
  });

  useEffect(() => {
    reset({
      year: sourceYear.year + 1,
      name: nextYearName(sourceYear.name, sourceYear.year + 1),
      target: sourceYear.target,
      projection_pct: sourceYear.projection_pct,
    });
  }, [sourceYear, open, reset]);

  const rollover = useMutation({
    mutationFn: (v: RolloverForm) => api.post<{ year: PujaYear; carried: number }>(
      `/puja_years/${sourceYear.id}/rollover`, v
    ),
    onSuccess: ({ carried }) => {
      qc.invalidateQueries({ queryKey: ["puja_years"] });
      toast.success(`New year created — ${carried} donor${carried === 1 ? "" : "s"} carried forward`);
      onOpenChange(false);
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Start {sourceYear.year + 1}</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-muted-foreground">
          Every donor from <strong>{sourceYear.name ?? sourceYear.year}</strong> is carried forward: last year's
          billed amount becomes this year's "last year billed", and a projected ask is suggested using the raise
          below. Nothing is billed or collected automatically.
        </p>
        <form onSubmit={handleSubmit((v) => rollover.mutate(v))} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Year" required>
              <Input type="number" step="1" {...register("year", { valueAsNumber: true, required: true })} />
            </Field>
            <Field label="Target (₹)">
              <Input type="number" step="1" {...register("target", { valueAsNumber: true })} />
            </Field>
          </div>
          <Field label="Name">
            <Input {...register("name")} />
          </Field>
          <Field label="Projection rate (%)" hint="Applied to last year's billed amount to suggest each ask">
            <Input type="number" step="0.1" {...register("projection_pct", { valueAsNumber: true })} />
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={rollover.isPending}>
              {rollover.isPending ? "Creating…" : "Create year"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ---------------- Year ----------------
function YearSettings() {
  const qc = useQueryClient();
  const { data: years = [] } = useQuery({
    queryKey: ["puja_years"],
    queryFn: () => api.get<PujaYear[]>("/puja_years"),
  });
  const [rolloverFrom, setRolloverFrom] = useState<PujaYear | null>(null);

  const update = useMutation({
    mutationFn: ({ id, values }: { id: string; values: Partial<PujaYear> }) =>
      api.patch(`/puja_years/${id}`, values),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["puja_years"] }); toast.success("Year updated"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const setActive = useMutation({
    mutationFn: async (id: string) => {
      // Only one active year at a time.
      await Promise.all(years.map((y) => api.patch(`/puja_years/${y.id}`, { is_active: y.id === id })));
    },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["puja_years"] }); toast.success("Active year changed"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  const newestYear = years[0]; // ordered by year DESC from the API

  return (
    <div className="space-y-3">
      {years.map((y) => (
        <Card key={y.id}>
          {/* items-start, not items-end: the Raise field's hint line makes its
              column taller than Name/Target's, so bottom-aligning would shift
              those inputs down and out of line with it. Every label here is a
              single line, so top-aligning keeps all three inputs level; the
              action buttons get a matching sm:pt-6 to sit at that same line
              since they have no label of their own. */}
          <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:items-start sm:flex-wrap">
            <Field label="Name" className="flex-1 min-w-[10rem]">
              <Input defaultValue={y.name ?? ""}
                onBlur={(e) => e.target.value !== y.name && update.mutate({ id: y.id, values: { name: e.target.value } })} />
            </Field>
            <Field label="Target (₹)" className="sm:w-36">
              <Input type="number" defaultValue={y.target}
                onBlur={(e) => Number(e.target.value) !== y.target && update.mutate({ id: y.id, values: { target: Number(e.target.value) } })} />
            </Field>
            <Field label="Raise (%)" hint="Used to suggest next year's ask" className="sm:w-28">
              <Input type="number" step="0.1" defaultValue={y.projection_pct}
                onBlur={(e) => Number(e.target.value) !== y.projection_pct && update.mutate({ id: y.id, values: { projection_pct: Number(e.target.value) } })} />
            </Field>
            {/* Brought forward from last year's close — folded into the fund
                position, not counted as this year's collection. The hints spell
                that out so nobody mistakes it for income. */}
            <Field label="Opening cash (₹)" hint="In hand from last year" className="sm:w-32">
              <Input type="number" step="1" defaultValue={y.opening_cash_balance}
                onBlur={(e) => Number(e.target.value) !== y.opening_cash_balance && update.mutate({ id: y.id, values: { opening_cash_balance: Number(e.target.value) } })} />
            </Field>
            <Field label="Opening bank (₹)" hint="In account from last year" className="sm:w-32">
              <Input type="number" step="1" defaultValue={y.opening_bank_balance}
                onBlur={(e) => Number(e.target.value) !== y.opening_bank_balance && update.mutate({ id: y.id, values: { opening_bank_balance: Number(e.target.value) } })} />
            </Field>
            <div className="flex items-center gap-2 sm:pt-6">
              {y.is_active
                ? <Badge variant="success" className="h-9 px-3"><ShieldCheck className="mr-1 h-3.5 w-3.5" /> Active</Badge>
                : <Button variant="outline" onClick={() => setActive.mutate(y.id)}>Set active</Button>}
              {y.id === newestYear?.id && (
                <Button variant="outline" onClick={() => setRolloverFrom(y)}>
                  <ArrowRightCircle className="h-4 w-4" /> Start {y.year + 1}
                </Button>
              )}
            </div>
          </CardContent>
        </Card>
      ))}
      {rolloverFrom && (
        <RolloverDialog open={Boolean(rolloverFrom)} onOpenChange={(v) => !v && setRolloverFrom(null)} sourceYear={rolloverFrom} />
      )}
    </div>
  );
}

// ---------------- Collectors ----------------
// Linking a collector to a login lets that member's own sign-in be matched
// back to advances issued to them (see the Dashboard "your advances" card)
// — it's optional, and most collectors never need one.
function CollectorSettings() {
  const qc = useQueryClient();
  const { data = [] } = useQuery({
    queryKey: ["collectors"],
    queryFn: () => api.get<Collector[]>("/collectors"),
  });
  const { data: members = [] } = useQuery({
    queryKey: ["users"],
    queryFn: () => api.get<User[]>("/users"),
  });
  const { create, remove } = useEntityMutations("collectors", ["collectors"], "Collector");
  const setLogin = useMutation({
    mutationFn: ({ id, user_id }: { id: string; user_id: string | null }) =>
      api.patch(`/collectors/${id}`, { user_id }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["collectors"] }); toast.success("Login updated"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <div className="space-y-3">
      <AddRow placeholder="New collector name…" onAdd={(name) => create.mutateAsync({ name })} />
      <Card className="divide-y divide-border">
        {data.length === 0 && <p className="p-4 text-sm text-muted-foreground">No collectors yet.</p>}
        {data.map((c) => (
          <div key={c.id} className="flex flex-wrap items-center justify-between gap-2 p-3">
            <span className="font-medium">{c.name}</span>
            <div className="flex items-center gap-2">
              <Select value={c.user_id ?? "none"}
                onValueChange={(v) => setLogin.mutate({ id: c.id, user_id: v === "none" ? null : v })}>
                <SelectTrigger className="h-8 w-48 text-xs"><SelectValue placeholder="No login linked" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">No login linked</SelectItem>
                  {members.map((m) => <SelectItem key={m.id} value={m.id}>{m.full_name ?? m.email}</SelectItem>)}
                </SelectContent>
              </Select>
              <ConfirmDialog title="Remove collector?" destructive confirmLabel="Delete"
                onConfirm={() => remove.mutateAsync(c.id)}
                trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
            </div>
          </div>
        ))}
      </Card>
    </div>
  );
}

// ---------------- Zones ----------------
function ZoneSettings() {
  const { data = [] } = useQuery({
    queryKey: ["zones"],
    queryFn: () => api.get<Zone[]>("/zones"),
  });
  const { create, update, remove } = useEntityMutations("zones", ["zones"], "Zone");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editValue, setEditValue] = useState("");

  function startEdit(z: Zone) {
    setEditingId(z.id);
    setEditValue(z.name);
  }

  async function saveEdit(id: string) {
    const name = editValue.trim();
    if (!name) return;
    await update.mutateAsync({ id, values: { name } });
    setEditingId(null);
  }

  return (
    <div className="space-y-3">
      <AddRow placeholder="New zone / lane…" onAdd={(name) => create.mutateAsync({ name })} />
      <Card className="divide-y divide-border">
        {data.length === 0 && <p className="p-4 text-sm text-muted-foreground">No zones yet.</p>}
        {data.map((z) => (
          <div key={z.id} className="flex items-center justify-between gap-2 p-3">
            {editingId === z.id ? (
              <form
                className="flex flex-1 items-center gap-2"
                onSubmit={(e) => { e.preventDefault(); saveEdit(z.id); }}
              >
                <Input
                  autoFocus
                  value={editValue}
                  onChange={(e) => setEditValue(e.target.value)}
                  onKeyDown={(e) => { if (e.key === "Escape") setEditingId(null); }}
                  className="h-8"
                />
                <Button type="submit" size="icon" className="h-8 w-8 shrink-0" disabled={!editValue.trim() || update.isPending}>
                  <Check className="h-4 w-4" />
                </Button>
                <Button type="button" variant="ghost" size="icon" className="h-8 w-8 shrink-0" onClick={() => setEditingId(null)}>
                  <X className="h-4 w-4" />
                </Button>
              </form>
            ) : (
              <>
                <span className="font-medium">{z.name}</span>
                <div className="flex items-center gap-1">
                  <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => startEdit(z)}>
                    <Pencil className="h-4 w-4" />
                  </Button>
                  <ConfirmDialog title="Remove zone?" destructive confirmLabel="Delete"
                    onConfirm={() => remove.mutateAsync(z.id)}
                    trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Trash2 className="h-4 w-4" /></Button>} />
                </div>
              </>
            )}
          </div>
        ))}
      </Card>
    </div>
  );
}

// ---------------- Lookups ----------------
function LookupSettings() {
  const [cat, setCat] = useState<LookupCategory>("payment_mode");
  const qc = useQueryClient();
  const { data = [] } = useQuery({
    queryKey: ["lookups", cat],
    queryFn: () => api.get<Lookup[]>(`/lookups${qs({ category: cat })}`),
  });

  const refresh = () => qc.invalidateQueries({ queryKey: ["lookups"] });

  const add = useMutation<void, Error, string>({
    mutationFn: async (label) => {
      await api.post("/lookups", { category: cat, label, sort_order: data.length + 1 });
    },
    onSuccess: () => { refresh(); toast.success("Added"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });
  const del = useMutation<void, Error, string>({
    mutationFn: async (id) => { await api.del(`/lookups/${id}`); },
    onSuccess: () => { refresh(); toast.success("Removed"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <div className="space-y-3">
      <Select value={cat} onValueChange={(v) => setCat(v as LookupCategory)}>
        <SelectTrigger className="w-full sm:w-64"><SelectValue /></SelectTrigger>
        <SelectContent>
          {LOOKUP_CATEGORIES.map((c) => <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>)}
        </SelectContent>
      </Select>
      <AddRow placeholder="Add option…" onAdd={(label) => add.mutateAsync(label)} />
      <div className="flex flex-wrap gap-2">
        {data.map((l) => (
          <span key={l.id} className="inline-flex items-center gap-1.5 rounded-full border border-border bg-card py-1 pl-3 pr-1.5 text-sm">
            {l.label}
            <button onClick={() => del.mutate(l.id)} className="rounded-full p-0.5 text-muted-foreground hover:bg-destructive/10 hover:text-destructive">
              <X className="h-3.5 w-3.5" />
            </button>
          </span>
        ))}
        {data.length === 0 && <p className="text-sm text-muted-foreground">No options in this list.</p>}
      </div>
    </div>
  );
}

// ---------------- Users / roles ----------------
function UserSettings() {
  const qc = useQueryClient();
  const { user } = useAuth();
  const { data = [] } = useQuery({
    queryKey: ["users"],
    queryFn: () => api.get<User[]>("/users"),
  });
  const setRole = useMutation({
    mutationFn: ({ id, role }: { id: string; role: AppRole }) =>
      api.patch(`/users/${id}`, { role }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["users"] }); toast.success("Role updated"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <Card className="divide-y divide-border">
      {data.length === 0 && <p className="p-4 text-sm text-muted-foreground">No members yet.</p>}
      {data.map((p) => (
        <div key={p.id} className="flex items-center justify-between gap-3 p-3.5">
          <div className="min-w-0">
            <p className="truncate font-medium">
              {p.full_name ?? "Unnamed"}
              {p.id === user?.id && <span className="ml-2 text-xs text-muted-foreground">(you)</span>}
            </p>
            <p className="truncate text-xs text-muted-foreground">{p.email}</p>
          </div>
          <Select value={p.role ?? "viewer"} onValueChange={(role) => setRole.mutate({ id: p.id, role: role as AppRole })}>
            <SelectTrigger className="w-36"><SelectValue /></SelectTrigger>
            <SelectContent>
              {ROLE_OPTIONS.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}
            </SelectContent>
          </Select>
        </div>
      ))}
    </Card>
  );
}

// ---------------- Committee ----------------
// This committee's own name, brand colour and logo. Each committee on the
// platform sets its own; changing them here never touches another's.
function CommitteeSettings() {
  const { org, refreshSession } = useAuth();
  const [name, setName] = useState(org?.name ?? "");
  const [colour, setColour] = useState(org?.accent_colour ?? "");
  const fileInput = useRef<HTMLInputElement>(null);

  useEffect(() => setName(org?.name ?? ""), [org?.name]);
  useEffect(() => setColour(org?.accent_colour ?? ""), [org?.accent_colour]);

  const onDone = (message: string) => async () => { await refreshSession(); toast.success(message); };
  const onFail = (e: unknown) => toast.error((e as Error).message);

  const rename = useMutation({
    mutationFn: (v: string) => api.patch("/organizations/current", { name: v }),
    onSuccess: onDone("Committee renamed"),
    onError: onFail,
  });
  const recolour = useMutation({
    mutationFn: (v: string | null) => api.patch("/organizations/current", { accent_colour: v }),
    onSuccess: onDone("Brand colour saved"),
    onError: onFail,
  });
  const uploadLogo = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append("file", file);
      return api.upload("/organizations/current/logo", form);
    },
    onSuccess: onDone("Logo updated"),
    onError: onFail,
  });
  const removeLogo = useMutation({
    mutationFn: () => api.del("/organizations/current/logo"),
    onSuccess: onDone("Logo removed"),
    onError: onFail,
  });

  const colourValid = colour === "" || parseHex(colour) !== null;
  const colourChanged = (colour || null) !== (org?.accent_colour ?? null);

  return (
    <Card className="space-y-5 p-4">
      <div className="flex items-center gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Building2 className="h-4.5 w-4.5" />
        </div>
        <Field label="Committee name" className="flex-1">
          <div className="flex gap-2">
            <Input value={name} onChange={(e) => setName(e.target.value)} />
            <Button className="shrink-0" disabled={!name.trim() || name === org?.name || rename.isPending}
              onClick={() => rename.mutate(name.trim())}>
              {rename.isPending ? "Saving…" : "Save"}
            </Button>
          </div>
        </Field>
      </div>

      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Brand colour" hint="Buttons, links and highlights. It is adjusted automatically so text stays readable.">
          <div className="flex items-center gap-2">
            <input
              type="color"
              aria-label="Pick a brand colour"
              value={parseHex(colour) ? colour : "#1d4ed8"}
              onChange={(e) => setColour(e.target.value)}
              className="h-10 w-12 shrink-0 cursor-pointer rounded-lg border border-input bg-card p-1"
            />
            <Input value={colour} onChange={(e) => setColour(e.target.value.trim())} placeholder="Default"
              className="font-mono" aria-invalid={!colourValid} />
            <Button className="shrink-0" disabled={!colourValid || !colourChanged || recolour.isPending}
              onClick={() => recolour.mutate(colour || null)}>
              Save
            </Button>
          </div>
          {!colourValid && <p className="mt-1 text-xs text-destructive">Use a hex colour such as #b45309.</p>}
          {org?.accent_colour && (
            <Button variant="link" size="sm" className="mt-1 h-auto p-0" onClick={() => recolour.mutate(null)}>
              Use the default colour
            </Button>
          )}
        </Field>

        <Field label="Logo" hint="PNG, JPEG, GIF or WebP, up to 512 KB. It appears in the header and on your reports.">
          <div className="flex items-center gap-3">
            <CommitteeAvatar name={org?.name ?? "?"} logoUrl={org?.logo_url} accentColour={org?.accent_colour} className="h-12 w-12 text-lg" />
            <input
              ref={fileInput}
              type="file"
              accept="image/png,image/jpeg,image/gif,image/webp"
              className="hidden"
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (file) uploadLogo.mutate(file);
                e.target.value = "";
              }}
            />
            <Button variant="outline" disabled={uploadLogo.isPending} onClick={() => fileInput.current?.click()}>
              {uploadLogo.isPending ? "Uploading…" : org?.logo_url ? "Replace" : "Upload"}
            </Button>
            {org?.logo_url && (
              <Button variant="ghost" disabled={removeLogo.isPending} onClick={() => removeLogo.mutate()}>Remove</Button>
            )}
          </div>
        </Field>
      </div>
    </Card>
  );
}

// ---------------- Access requests ----------------
interface ApiKeyRow {
  id: string;
  name: string;
  prefix: string;
  created_at: string;
  last_used_at: string | null;
  revoked_at: string | null;
  created_by_name: string | null;
}

/**
 * Issue and revoke the credentials an external agent uses to pull the audit
 * PDF on a schedule. The plaintext key exists only in the create response —
 * the server stores a hash — so it's shown once here and never again; a lost
 * key is revoked and reissued.
 */
function ApiKeySettings() {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [justCreated, setJustCreated] = useState<{ name: string; key: string } | null>(null);

  const { data: keys = [], isLoading } = useQuery<ApiKeyRow[]>({
    queryKey: ["api_keys"],
    queryFn: () => api.get<ApiKeyRow[]>("/api_keys"),
  });

  const create = useMutation({
    mutationFn: (n: string) => api.post<{ name: string; key: string }>("/api_keys", { name: n }),
    onSuccess: (res) => {
      setJustCreated(res);
      setName("");
      qc.invalidateQueries({ queryKey: ["api_keys"] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const revoke = useMutation({
    mutationFn: (id: string) => api.del<void>(`/api_keys/${id}`),
    onSuccess: () => {
      toast.success("Key revoked");
      qc.invalidateQueries({ queryKey: ["api_keys"] });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const endpoint = `${window.location.origin}/api/reports/audit.pdf`;

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-border bg-muted/40 p-3 text-sm">
        <p className="font-medium">Audit report endpoint</p>
        <code className="mt-1 block break-all text-xs text-muted-foreground">GET {endpoint}</code>
        <p className="mt-2 text-xs text-muted-foreground">
          Send the key as <code>X-Api-Key</code> (or <code>Authorization: Bearer</code>). Returns the
          current season&apos;s audit PDF; add <code>?year_id=…</code> for a specific year. A key is
          read-only and works on this endpoint alone — it cannot change anything.
        </p>
      </div>

      {justCreated && (
        <div className="space-y-2 rounded-lg border border-primary/40 bg-primary/5 p-3">
          <p className="text-sm font-medium">New key for “{justCreated.name}” — copy it now</p>
          <div className="flex gap-2">
            <Input readOnly value={justCreated.key} className="font-mono text-xs" onFocus={(e) => e.currentTarget.select()} />
            <Button type="button" variant="outline" size="icon" title="Copy"
              onClick={() => { navigator.clipboard.writeText(justCreated.key); toast.success("Copied"); }}>
              <Copy className="h-4 w-4" />
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            This is the only time it can be shown. Store it in the agent&apos;s config now.
          </p>
          <Button type="button" variant="ghost" size="sm" onClick={() => setJustCreated(null)}>Done</Button>
        </div>
      )}

      <AddRow placeholder="Name this key, e.g. Hermes agent" onAdd={async (v) => { setName(v); await create.mutateAsync(v); }} />
      {name && create.isPending && <p className="text-xs text-muted-foreground">Creating…</p>}

      {isLoading ? <p className="text-sm text-muted-foreground">Loading…</p>
        : keys.length === 0 ? <EmptyState icon={<KeyRound />} title="No API keys" description="Create one to let an agent fetch the audit report." />
        : (
          <Card className="divide-y divide-border">
            {keys.map((k) => (
              <div key={k.id} className="flex items-center gap-3 p-3">
                <KeyRound className={`h-4 w-4 shrink-0 ${k.revoked_at ? "text-muted-foreground" : "text-primary"}`} />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">
                    {k.name} {k.revoked_at && <Badge variant="outline" className="ml-1">Revoked</Badge>}
                  </p>
                  <p className="truncate font-mono text-xs text-muted-foreground">{k.prefix}…</p>
                  <p className="text-xs text-muted-foreground">
                    {k.created_by_name ? `Created by ${k.created_by_name}` : "Created"}
                    {" · "}
                    {k.last_used_at ? `last used ${new Date(k.last_used_at).toLocaleString()}` : "never used"}
                  </p>
                </div>
                {!k.revoked_at && (
                  <ConfirmDialog title="Revoke this key?" destructive confirmLabel="Revoke"
                    description="Any agent using it stops working immediately. This cannot be undone — issue a new key instead."
                    onConfirm={() => revoke.mutateAsync(k.id)}
                    trigger={<Button variant="ghost" size="icon" className="h-8 w-8 text-destructive"><Ban className="h-4 w-4" /></Button>} />
                )}
              </div>
            ))}
          </Card>
        )}
    </div>
  );
}

function AccessRequestsSettings() {
  const qc = useQueryClient();
  const { data = [] } = useQuery({
    queryKey: ["access_requests"],
    queryFn: () => api.get<AccessRequest[]>("/access_requests"),
  });
  const [roleFor, setRoleFor] = useState<Record<string, AppRole>>({});

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["access_requests"] });
    qc.invalidateQueries({ queryKey: ["users"] });
  };
  const approve = useMutation({
    mutationFn: ({ id, role }: { id: string; role: AppRole }) =>
      api.post(`/access_requests/${id}/approve`, { role }),
    onSuccess: () => { refresh(); toast.success("Access granted"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });
  const deny = useMutation({
    mutationFn: (id: string) => api.post(`/access_requests/${id}/deny`),
    onSuccess: () => { refresh(); toast.success("Request denied"); },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  if (data.length === 0) {
    return (
      <EmptyState icon={<UserPlus />} title="No pending requests"
        description="When someone signs in for the first time, their request to join shows up here." />
    );
  }

  return (
    <Card className="divide-y divide-border">
      {data.map((r) => {
        const role = roleFor[r.member_id] ?? "viewer";
        return (
          <div key={r.member_id} className="flex flex-col gap-3 p-3.5 sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <p className="truncate font-medium">{r.full_name ?? "Unnamed"}</p>
              <p className="truncate text-xs text-muted-foreground">
                {r.email} · signed in with {r.auth_provider === "google" ? "Google" : "email & password"}
              </p>
            </div>
            <div className="flex shrink-0 items-center gap-2">
              <Select value={role} onValueChange={(v) => setRoleFor((s) => ({ ...s, [r.member_id]: v as AppRole }))}>
                <SelectTrigger className="w-32"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {ROLE_OPTIONS.map((opt) => <SelectItem key={opt.value} value={opt.value}>{opt.label}</SelectItem>)}
                </SelectContent>
              </Select>
              <Button size="sm" disabled={approve.isPending}
                onClick={() => approve.mutate({ id: r.member_id, role })}>
                <Check className="h-3.5 w-3.5" /> Approve
              </Button>
              <Button size="sm" variant="ghost" className="text-destructive" disabled={deny.isPending}
                onClick={() => deny.mutate(r.member_id)}>
                <Ban className="h-3.5 w-3.5" /> Deny
              </Button>
            </div>
          </div>
        );
      })}
    </Card>
  );
}

export function SettingsPage() {
  const { can, role } = useAuth();
  const manageSettings = can("manage_settings");
  const manageUsers = can("manage_users");
  const isAdmin = role === "admin";

  // App.tsx's SettingsRoute already keeps anyone without either permission
  // off this page entirely; this only decides which tabs someone who does
  // have one of the two (but maybe not both — Secretariat has manage_users,
  // not manage_settings) actually gets to see.
  const tabs = [
    manageSettings && { v: "year", label: "Puja Year", icon: CalendarDays, node: <YearSettings /> },
    manageUsers && { v: "requests", label: "Access Requests", icon: UserPlus, node: <AccessRequestsSettings /> },
    manageUsers && { v: "users", label: "Users & Roles", icon: UserCog, node: <UserSettings /> },
    manageSettings && { v: "collectors", label: "Collectors", icon: Users, node: <CollectorSettings /> },
    manageSettings && { v: "zones", label: "Zones", icon: MapPin, node: <ZoneSettings /> },
    manageSettings && { v: "lists", label: "Dropdown Lists", icon: ListChecks, node: <LookupSettings /> },
    manageSettings && { v: "committee", label: "Committee", icon: Building2, node: <CommitteeSettings /> },
    manageSettings && { v: "api", label: "API Keys", icon: KeyRound, node: <ApiKeySettings /> },
    // Admin-only, and literally by role (not a permission) — matches the
    // server's AuthorizationPolicies.Admin gate on POST /api/push/announce.
    isAdmin && { v: "announce", label: "Announce", icon: Megaphone, node: <AnnouncementSettings /> },
    { v: "app", label: "App", icon: Smartphone, node: <div className="space-y-3"><InstallSettingsRow /><NotificationSettingsRow /></div> },
  ].filter((t): t is Exclude<typeof t, false> => Boolean(t));

  return (
    <div className="space-y-5">
      <PageHeader title="Settings" description="Manage your committee configuration" icon={<Cog />} />
      <Tabs defaultValue={tabs[0]?.v}>
        <TabsList className="flex h-auto w-full flex-wrap justify-start gap-1 overflow-x-auto p-1">
          {tabs.map((t) => (
            <TabsTrigger key={t.v} value={t.v} className="gap-1.5">
              <t.icon className="h-4 w-4" /> <span>{t.label}</span>
            </TabsTrigger>
          ))}
        </TabsList>
        {tabs.map((t) => (
          <TabsContent key={t.v} value={t.v}>
            <Card>
              <CardHeader>
                <CardTitle>{t.label}</CardTitle>
                <CardDescription>Changes apply across the app immediately.</CardDescription>
              </CardHeader>
              <CardContent>{t.node}</CardContent>
            </Card>
          </TabsContent>
        ))}
      </Tabs>
    </div>
  );
}
