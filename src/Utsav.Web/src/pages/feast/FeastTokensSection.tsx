import { useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Printer, Link2, Ban, QrCode, Loader2, Search, ListPlus } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useAuth } from "@/context/AuthContext";
import { useFeasts } from "@/hooks/useFinance";
import { api } from "@/lib/api";
import {
  useFeastTokens, useFeastTokenSummary, printFeastTokens, useAssignFeastToken, useVoidFeastToken,
} from "@/hooks/useFeastTokens";
import { PageHeader } from "@/components/common/PageHeader";
import { SummaryBar } from "@/components/common/SummaryBar";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { Field } from "@/components/common/Field";
import { SearchableSelect } from "@/components/form/SearchableSelect";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import type { FeastToken, FeastTokenStatus } from "@/types/db";

function StatusBadge({ status }: { status: FeastTokenStatus }) {
  switch (status) {
    case "unassigned": return <Badge variant="outline">Unassigned</Badge>;
    case "assigned": return <Badge variant="gold">Assigned</Badge>;
    case "redeemed": return <Badge variant="success">Redeemed</Badge>;
    case "void": return <Badge variant="destructive">Void</Badge>;
  }
}

/** Asks for a count, then downloads the sheet PDF directly — no intermediate preview page, matching the brief's "ask for the count, then provide the PDF" flow. */
function PrintDialog({ open, onOpenChange, yearId }: { open: boolean; onOpenChange: (v: boolean) => void; yearId: string }) {
  const [count, setCount] = useState("10");
  const [printing, setPrinting] = useState(false);
  const n = Number(count);
  const valid = Number.isInteger(n) && n >= 1 && n <= 2000;
  const qc = useQueryClient();

  async function handlePrint() {
    if (!valid) return;
    setPrinting(true);
    try {
      await printFeastTokens(yearId, n);
      // printFeastTokens is a plain blob download, not a useMutation — the
      // new rows it created server-side need an explicit refetch here, or
      // the list/summary below would keep showing the pre-print counts.
      await qc.invalidateQueries({ queryKey: ["feast_tokens"] });
      toast.success(`${n} token${n === 1 ? "" : "s"} printed`);
      onOpenChange(false);
    } catch (e) {
      toast.error((e as Error).message);
    } finally {
      setPrinting(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(v) => !printing && onOpenChange(v)}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Print feast tokens</DialogTitle>
          <DialogDescription>
            Ten per page, numbered to continue from wherever the last print run left off.
            Blank — assign each one to a donor after it's handed out.
          </DialogDescription>
        </DialogHeader>
        <Field label="How many tokens?">
          <Input
            type="number" min={1} max={2000} value={count}
            onChange={(e) => setCount(e.target.value)}
            autoFocus
          />
        </Field>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={printing}>
            Cancel
          </Button>
          <Button type="button" onClick={handlePrint} disabled={!valid || printing}>
            {printing ? <><Loader2 className="h-4 w-4 animate-spin" /> Generating…</> : <><Printer className="h-4 w-4" /> Print PDF</>}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The donor picker shared by single and range assignment — searchable (a
 * committee can have hundreds of names), shows how many tokens a donor
 * already holds so handing a second coupon to the same household is visibly
 * supported, and a query with no match offers "Add '<name>' as a new donor"
 * so a token can go to someone not already on the feast list, created with
 * just a name — everything else (contact, billing) gets filled in later
 * from the Feast page.
 */
function DonorField({ value, onChange, yearId }: { value: string | null; onChange: (v: string) => void; yearId: string }) {
  const { data: feasts = [] } = useFeasts(yearId);
  const { data: allTokens = [] } = useFeastTokens(yearId);
  const qc = useQueryClient();
  const [creatingDonor, setCreatingDonor] = useState(false);

  const tokenCountByFeast = useMemo(() => {
    const map = new Map<string, number>();
    for (const t of allTokens) {
      if (!t.feast_id) continue;
      map.set(t.feast_id, (map.get(t.feast_id) ?? 0) + 1);
    }
    return map;
  }, [allTokens]);

  const options = useMemo(
    () => feasts
      .slice()
      .sort((a, b) => `${a.donor?.name ?? ""}`.localeCompare(`${b.donor?.name ?? ""}`))
      .map((f) => {
        const count = tokenCountByFeast.get(f.id) ?? 0;
        return {
          value: f.id,
          label: `${f.donor?.name ?? "Unknown"} ${f.donor?.surname ?? ""}`.trim(),
          hint: count > 0 ? `${count} token${count === 1 ? "" : "s"} already` : undefined,
        };
      }),
    [feasts, tokenCountByFeast],
  );

  async function createDonorAndFeast(name: string) {
    setCreatingDonor(true);
    try {
      const donor = await api.post<{ id: string }>("/donors", { name });
      const feast = await api.post<{ id: string }>("/feasts", { donor_id: donor.id, year_id: yearId });
      await qc.invalidateQueries({ queryKey: ["feasts"] });
      onChange(feast.id);
      toast.success(`${name} added to this year's feast list`);
    } catch (e) {
      toast.error((e as Error).message);
    } finally {
      setCreatingDonor(false);
    }
  }

  return (
    <Field label="Donor (this feast season)">
      <SearchableSelect
        value={value}
        onChange={onChange}
        options={options}
        placeholder="Search a donor…"
        searchPlaceholder="Type a name…"
        emptyText="No donors yet — type a name to add one"
        onCreateNew={createDonorAndFeast}
        createLabel={(q) => `Add "${q}" as a new donor`}
        creating={creatingDonor}
        className="w-full"
      />
    </Field>
  );
}

/** Links a single printed token to whichever donor it ended up with, door to door. */
function AssignDialog({ token, onOpenChange, yearId }: { token: FeastToken | null; onOpenChange: (v: boolean) => void; yearId: string }) {
  const assign = useAssignFeastToken();
  const [feastId, setFeastId] = useState<string | null>(null);

  async function confirm() {
    if (!token || !feastId) return;
    await assign.mutateAsync({ code: token.code, feast_id: feastId });
    setFeastId(null);
    onOpenChange(false);
  }

  return (
    <Dialog open={Boolean(token)} onOpenChange={(v) => { if (!v) setFeastId(null); onOpenChange(v); }}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Assign {token?.code}</DialogTitle>
          <DialogDescription>Which donor did this coupon go to?</DialogDescription>
        </DialogHeader>
        {token && <DonorField value={feastId} onChange={setFeastId} yearId={yearId} />}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={assign.isPending}>
            Cancel
          </Button>
          <Button type="button" onClick={confirm} disabled={!feastId || assign.isPending}>
            {assign.isPending ? "Assigning…" : "Assign"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Assigns a contiguous block of unassigned tokens to one donor in a single
 * go — a family or a stall taking several coupons at once, instead of
 * opening the single-assign dialog once per token. From/To are picked from
 * the actual unassigned tokens (sorted by print sequence) rather than typed
 * codes, so the range can never reference a token that doesn't exist or is
 * already spoken for.
 */
function RangeAssignDialog({ open, onOpenChange, yearId }: { open: boolean; onOpenChange: (v: boolean) => void; yearId: string }) {
  const { data: allTokens = [] } = useFeastTokens(yearId);
  const qc = useQueryClient();
  const [fromId, setFromId] = useState<string | null>(null);
  const [toId, setToId] = useState<string | null>(null);
  const [feastId, setFeastId] = useState<string | null>(null);
  const [assigning, setAssigning] = useState(false);
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null);

  const unassigned = useMemo(
    () => allTokens.filter((t) => t.status === "unassigned").slice().sort((a, b) => a.seq_no - b.seq_no),
    [allTokens],
  );
  const tokenOptions = useMemo(() => unassigned.map((t) => ({ value: t.id, label: t.code })), [unassigned]);

  const fromToken = unassigned.find((t) => t.id === fromId);
  const toToken = unassigned.find((t) => t.id === toId);
  const rangeTokens = useMemo(() => {
    if (!fromToken || !toToken) return [];
    const lo = Math.min(fromToken.seq_no, toToken.seq_no);
    const hi = Math.max(fromToken.seq_no, toToken.seq_no);
    return unassigned.filter((t) => t.seq_no >= lo && t.seq_no <= hi);
  }, [fromToken, toToken, unassigned]);

  function reset() {
    setFromId(null);
    setToId(null);
    setFeastId(null);
    setProgress(null);
  }

  async function confirm() {
    if (!feastId || rangeTokens.length === 0) return;
    setAssigning(true);
    setProgress({ done: 0, total: rangeTokens.length });
    let ok = 0;
    let failed = 0;
    // Sequential, not Promise.all — each call still needs its own
    // optimistic-concurrency check server-side, and a clear running count
    // beats an all-or-nothing flash for what could be dozens of tokens.
    for (const t of rangeTokens) {
      try {
        await api.post("/feast_tokens/assign", { code: t.code, feast_id: feastId });
        ok++;
      } catch {
        failed++;
      }
      setProgress((p) => (p ? { ...p, done: p.done + 1 } : p));
    }
    await qc.invalidateQueries({ queryKey: ["feast_tokens"] });
    setAssigning(false);
    if (failed > 0) toast.error(`${ok} assigned, ${failed} couldn't be assigned`);
    else toast.success(`${ok} token${ok === 1 ? "" : "s"} assigned`);
    reset();
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) reset(); onOpenChange(v); }}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle>Assign a range of tokens</DialogTitle>
          <DialogDescription>
            Give a contiguous block of unassigned coupons to one donor — a family or a stall taking several at once.
          </DialogDescription>
        </DialogHeader>
        <div className="grid grid-cols-2 gap-3">
          <Field label="From token">
            <SearchableSelect value={fromId} onChange={setFromId} options={tokenOptions} placeholder="Start code…" searchPlaceholder="Search code…" className="w-full" />
          </Field>
          <Field label="To token">
            <SearchableSelect value={toId} onChange={setToId} options={tokenOptions} placeholder="End code…" searchPlaceholder="Search code…" className="w-full" />
          </Field>
        </div>
        {rangeTokens.length > 0 && (
          <p className="text-xs text-muted-foreground">
            {rangeTokens.length} token{rangeTokens.length === 1 ? "" : "s"}: {rangeTokens[0].code} – {rangeTokens[rangeTokens.length - 1].code}
          </p>
        )}
        <DonorField value={feastId} onChange={setFeastId} yearId={yearId} />
        {progress && (
          <p className="text-xs text-muted-foreground">Assigning {progress.done} / {progress.total}…</p>
        )}
        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={assigning}>
            Cancel
          </Button>
          <Button type="button" onClick={confirm} disabled={!feastId || rangeTokens.length === 0 || assigning}>
            {assigning ? "Assigning…" : `Assign ${rangeTokens.length || ""} token${rangeTokens.length === 1 ? "" : "s"}`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: "unassigned", label: "Unassigned" },
  { value: "assigned", label: "Assigned" },
  { value: "redeemed", label: "Redeemed" },
  { value: "void", label: "Void" },
];

/**
 * Print → assign → redeem → void, the whole token lifecycle in one place.
 * Redemption itself happens on the separate gate-facing Feast Entry page —
 * this page is the treasurer's desk, not the door.
 */
export function FeastTokensSection() {
  const { activeYearId } = useYear();
  const { can } = useAuth();
  const canWrite = can("write_income");
  const [status, setStatus] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const { data = [], isLoading, isError, error } = useFeastTokens(activeYearId, status);
  const { data: summary } = useFeastTokenSummary(activeYearId);
  const [printOpen, setPrintOpen] = useState(false);
  const [rangeOpen, setRangeOpen] = useState(false);
  const [assigning, setAssigning] = useState<FeastToken | null>(null);
  const [pendingVoid, setPendingVoid] = useState<FeastToken | null>(null);
  const voidToken = useVoidFeastToken();

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return data;
    return data.filter((t) =>
      t.code.toLowerCase().includes(q) ||
      `${t.donor_name ?? ""} ${t.donor_surname ?? ""}`.toLowerCase().includes(q));
  }, [data, search]);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Feast Tokens"
        description="Print, assign, and manage QR entry coupons"
        icon={<QrCode />}
        actions={canWrite && (
          <>
            <Button variant="outline" onClick={() => setRangeOpen(true)}>
              <ListPlus className="h-4 w-4" /> <span className="hidden sm:inline">Assign range</span>
            </Button>
            <Button onClick={() => setPrintOpen(true)}>
              <Printer className="h-4 w-4" /> <span className="hidden sm:inline">Print tokens</span>
            </Button>
          </>
        )}
      />

      {summary && (
        <SummaryBar
          items={[
            { label: "Unassigned", value: summary.unassigned, isMoney: false },
            { label: "Assigned", value: summary.assigned, isMoney: false },
            { label: "Redeemed", value: summary.redeemed, isMoney: false, tone: "success" },
            { label: "Void", value: summary.voided, isMoney: false, tone: "destructive" },
          ]}
        />
      )}

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap gap-2">
          <Button
            variant={status === null ? "default" : "outline"} size="sm"
            onClick={() => setStatus(null)}
          >
            All ({summary?.total ?? 0})
          </Button>
          {STATUS_FILTERS.map((f) => (
            <Button
              key={f.value} variant={status === f.value ? "default" : "outline"} size="sm"
              onClick={() => setStatus(status === f.value ? null : f.value)}
            >
              {f.label}
            </Button>
          ))}
        </div>
        <div className="relative sm:w-64">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search code or donor name…"
            className="pl-9"
          />
        </div>
      </div>

      {isLoading ? (
        <TableSkeleton />
      ) : isError ? (
        <ErrorState message={(error as Error)?.message} />
      ) : filtered.length === 0 ? (
        <EmptyState
          icon={<QrCode />}
          title={data.length === 0 ? "No tokens printed yet" : "No tokens match your search"}
          description={data.length === 0 ? "Print a batch of coupons to start handing them out door to door." : "Try a different code or donor name."}
          action={canWrite && data.length === 0 && <Button onClick={() => setPrintOpen(true)}><Printer className="h-4 w-4" /> Print tokens</Button>}
        />
      ) : (
        <Card>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Code</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Donor</TableHead>
                <TableHead>Assigned</TableHead>
                <TableHead>Redeemed</TableHead>
                <TableHead className="w-24"></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filtered.map((t) => {
                const rowOpensAssign = canWrite && t.status === "unassigned";
                return (
                  <TableRow
                    key={t.id}
                    className={cn(t.status === "void" && "opacity-60", rowOpensAssign && "cursor-pointer")}
                    onClick={() => rowOpensAssign && setAssigning(t)}
                  >
                    <TableCell className="tabular font-medium">{t.code}</TableCell>
                    <TableCell><StatusBadge status={t.status} /></TableCell>
                    <TableCell>
                      {t.donor_name ? `${t.donor_name} ${t.donor_surname ?? ""}`.trim() : <span className="text-muted-foreground">—</span>}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {t.assigned_at ? new Date(t.assigned_at).toLocaleDateString() : "—"}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {t.redeemed_at ? new Date(t.redeemed_at).toLocaleDateString() : "—"}
                      {t.redeemed_by_name && <span className="block text-xs">by {t.redeemed_by_name}</span>}
                    </TableCell>
                    <TableCell>
                      {canWrite && (
                        <div className="flex justify-end gap-1" onClick={(e) => e.stopPropagation()}>
                          {t.status === "unassigned" && (
                            <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => setAssigning(t)} aria-label={`Assign ${t.code}`}>
                              <Link2 className="h-4 w-4" />
                            </Button>
                          )}
                          {t.status !== "void" && (
                            <Button
                              variant="ghost" size="icon" className="h-8 w-8 text-destructive"
                              onClick={() => setPendingVoid(t)} aria-label={`Void ${t.code}`}
                            >
                              <Ban className="h-4 w-4" />
                            </Button>
                          )}
                        </div>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Card>
      )}

      {activeYearId && <PrintDialog open={printOpen} onOpenChange={setPrintOpen} yearId={activeYearId} />}
      {activeYearId && <RangeAssignDialog open={rangeOpen} onOpenChange={setRangeOpen} yearId={activeYearId} />}
      {activeYearId && <AssignDialog token={assigning} onOpenChange={(v) => !v && setAssigning(null)} yearId={activeYearId} />}

      <Dialog open={Boolean(pendingVoid)} onOpenChange={(o) => !o && setPendingVoid(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Void {pendingVoid?.code}?</DialogTitle>
            <DialogDescription>
              For a lost or damaged coupon. It can never be assigned or redeemed again.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPendingVoid(null)}>Cancel</Button>
            <Button
              variant="destructive"
              disabled={voidToken.isPending}
              onClick={async () => {
                if (pendingVoid) await voidToken.mutateAsync({ code: pendingVoid.code });
                setPendingVoid(null);
              }}
            >
              {voidToken.isPending ? "Voiding…" : "Void token"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
