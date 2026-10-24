import { useMemo, useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { History as HistoryIcon, AlertTriangle, Search } from "lucide-react";
import { FilterMenu, FilterField } from "@/components/common/FilterMenu";
import { cn } from "@/lib/utils";
import { api, qs } from "@/lib/api";
import { useYear } from "@/context/YearContext";
import { useIsDesktop } from "@/hooks/useMediaQuery";
import { fieldLabel, fmtStamp, fmtValue } from "@/lib/auditFormat";
import type { AuditRow, AuditChange, AuditPage } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { OptionSelect } from "@/components/form/OptionSelect";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

/**
 * Filter options are keyed by the raw table name (what the API stores as
 * entityType); the label is what a committee member recognises. The "Page"
 * column itself comes pre-labelled from the server, so this map only drives
 * the dropdown.
 */
const ENTITY_OPTIONS = [
  { value: "subscriptions", label: "Membership / Special Donation" },
  { value: "special_donations", label: "Other Incomes" },
  { value: "collection_payments", label: "Collections" },
  { value: "ad_bookings", label: "Advertising" },
  { value: "offerings", label: "Offerings" },
  { value: "expenses", label: "Expenses" },
  { value: "budget_lines", label: "Budget" },
  { value: "advances", label: "Cash Advances" },
  { value: "contracts", label: "Vendor Contracts" },
  { value: "contract_payments", label: "Contract Payments" },
  { value: "vendors", label: "Vendors" },
  { value: "donors", label: "Donors" },
  { value: "documents", label: "Documents" },
  { value: "puja_years", label: "Puja Year" },
  { value: "org_members", label: "Members & Roles" },
];

/** One flat row per changed field (updates), or a single row for a create/delete. */
interface DisplayRow {
  rowKey: string;
  entry: AuditRow;
  field: string;
  previous: string;
  current: string;
  kind: "update" | "created" | "deleted";
}

function toDisplayRows(entry: AuditRow): DisplayRow[] {
  const kind: DisplayRow["kind"] =
    entry.action === "Created" ? "created" : entry.action === "Deleted" ? "deleted" : "update";

  const changes = entry.changes ?? [];
  if (changes.length === 0) {
    // Nothing field-level to show (e.g. the pre-image couldn't be captured,
    // or every changed column was plumbing) — still worth one summary line
    // so the entry isn't invisible.
    return [{ rowKey: entry.id, entry, field: "—", previous: "—", current: kind === "update" ? "—" : entry.action, kind }];
  }
  // A create has no "before" (reads null → value) and a delete has no
  // "after" (reads value → null) — same field-by-field shape as an update,
  // so a settlement or any other created/deleted record shows its actual
  // amount/date/etc. right in the table, not just the bare word "Created".
  return changes.map((c: AuditChange) => ({
    rowKey: `${entry.id}-${c.field}`,
    entry,
    field: fieldLabel(c.field),
    previous: fmtValue(c.from),
    current: fmtValue(c.to),
    kind,
  }));
}

function EntryDialog({ entry, onClose }: { entry: AuditRow | null; onClose: () => void }) {
  return (
    <Dialog open={Boolean(entry)} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-md">
        {entry && (
          <>
            <DialogHeader>
              <DialogTitle>{entry.page}{entry.record ? ` · ${entry.record}` : ""}</DialogTitle>
            </DialogHeader>
            <div className="space-y-2 text-sm">
              <p className="text-muted-foreground">
                {entry.action} · {fmtStamp(entry.timestamp)} · {entry.who}
                {entry.role ? ` · ${entry.role}` : ""}
              </p>
              {entry.outcome === "Failure" && (
                <p className="flex items-center gap-1.5 rounded-lg bg-destructive/10 p-2.5 text-xs text-destructive">
                  <AlertTriangle className="h-3.5 w-3.5 shrink-0" /> {entry.error_message}
                </p>
              )}
              {entry.changes.length > 0 && (
                <div className="rounded-lg border border-border">
                  {entry.changes.map((c) => (
                    <div key={c.field} className="flex justify-between gap-3 border-b border-border px-3 py-1.5 last:border-0">
                      <span className="text-muted-foreground">{fieldLabel(c.field)}</span>
                      <span className="text-right">{fmtValue(c.from)} → <span className="font-medium">{fmtValue(c.to)}</span></span>
                    </div>
                  ))}
                </div>
              )}
              <p className="text-[11px] text-muted-foreground">
                {entry.request_method} {entry.request_path}
                {entry.ip_address ? ` · ${entry.ip_address}` : ""}
              </p>
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

export function HistoryPage() {
  const { activeYear } = useYear();
  const isDesktop = useIsDesktop();
  const [entityType, setEntityType] = useState<string | null>(null);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<AuditRow | null>(null);

  const { data, isLoading, isError, error, fetchNextPage, hasNextPage, isFetchingNextPage } =
    useInfiniteQuery({
      queryKey: ["audit", entityType, from, to],
      initialPageParam: null as string | null,
      queryFn: ({ pageParam }) =>
        api.get<AuditPage>(`/audit${qs({
          entityType,
          // The API compares against a UTC instant; a bare date input is local
          // midnight, and "to" should include the whole day the user picked.
          from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
          to: to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined,
          limit: 50,
          continuationToken: pageParam,
        })}`),
      getNextPageParam: (last) => last.continuation_token ?? undefined,
    });

  const entries = useMemo(() => data?.pages.flatMap((p) => p.items) ?? [], [data]);
  const allRows = useMemo(() => entries.flatMap(toDisplayRows), [entries]);
  const rows = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return allRows;
    return allRows.filter((r) =>
      r.entry.who.toLowerCase().includes(q) ||
      r.entry.page.toLowerCase().includes(q) ||
      (r.entry.record ?? "").toLowerCase().includes(q) ||
      r.field.toLowerCase().includes(q));
  }, [allRows, search]);

  return (
    <div className="space-y-5">
      <PageHeader
        title="History"
        description="Who changed what, and when — newest first"
        icon={<HistoryIcon />}
      />

      <div className="flex flex-col gap-2 sm:flex-row">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search who, page, record or field…" className="pl-9" />
        </div>
        <FilterMenu
          activeCount={(entityType ? 1 : 0) + (from || to ? 1 : 0)}
          onClear={() => { setEntityType(null); setFrom(""); setTo(""); }}
        >
          <FilterField label="Record type">
            <OptionSelect
              value={entityType}
              onChange={setEntityType}
              allowClear
              options={ENTITY_OPTIONS}
              placeholder="All records"
              className="w-full"
            />
          </FilterField>
          <FilterField label="Date range">
            <div className="flex gap-2">
              <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="From date" className="min-w-0 flex-1" />
              <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="To date" className="min-w-0 flex-1" />
            </div>
          </FilterField>
        </FilterMenu>
      </div>

      {/* The trail is committee-wide, not year-scoped — worth saying, since
          every other page follows the year switcher. */}
      {activeYear && (
        <p className="text-xs text-muted-foreground">
          Showing changes across all puja years, not just {activeYear.year}.
        </p>
      )}

      {isLoading ? (
        <TableSkeleton />
      ) : isError || !data ? (
        // `!data` matters: if the audit store is unreachable the query never
        // yields a page, and without this the screen would fall through to the
        // empty state below — telling a committee admin "no changes recorded"
        // during an outage, when the truth is we couldn't read the trail.
        <ErrorState message={(error as Error)?.message ?? "Couldn't load the history right now."} />
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<HistoryIcon />}
          title="No changes recorded"
          description="Nothing matches these filters yet. Edits made from now on will appear here."
        />
      ) : (
        <>
          {isDesktop ? (
            <Card>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Date &amp; time</TableHead>
                    <TableHead>Who</TableHead>
                    <TableHead>Page</TableHead>
                    <TableHead>Record</TableHead>
                    <TableHead>Field</TableHead>
                    <TableHead>Previous</TableHead>
                    <TableHead>Current</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((r) => {
                    const isDelete = r.kind === "deleted";
                    return (
                      <TableRow
                        key={r.rowKey}
                        className={cn("cursor-pointer", isDelete && "bg-destructive/5 hover:bg-destructive/10")}
                        onClick={() => setSelected(r.entry)}
                      >
                        <TableCell className={cn("whitespace-nowrap text-sm", isDelete ? "text-destructive" : "text-muted-foreground")}>
                          {fmtStamp(r.entry.timestamp)}
                        </TableCell>
                        <TableCell className={cn("text-sm", isDelete && "text-destructive")}>{r.entry.who}</TableCell>
                        <TableCell className={cn("text-sm", isDelete && "text-destructive")}>{r.entry.page}</TableCell>
                        <TableCell className={cn("max-w-[12rem] truncate text-sm font-medium", isDelete && "text-destructive")}>
                          {r.entry.record ?? "—"}
                        </TableCell>
                        <TableCell className={cn("text-sm", isDelete && "text-destructive")}>{r.field}</TableCell>
                        <TableCell className={cn("max-w-[14rem] truncate text-sm", isDelete ? "text-destructive" : "text-muted-foreground")}>
                          {r.previous}
                        </TableCell>
                        <TableCell className={cn("max-w-[14rem] truncate text-sm", isDelete ? "text-destructive" : "text-foreground")}>
                          {r.field === "—"
                            ? <Badge variant={isDelete ? "destructive" : "success"}>{r.current}</Badge>
                            : <span className="font-medium">{r.current}</span>}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </Card>
          ) : (
            <div className="space-y-2">
              {rows.map((r) => {
                const isDelete = r.kind === "deleted";
                return (
                  <Card
                    key={r.rowKey}
                    className={cn("p-3.5", isDelete && "border-destructive/30 bg-destructive/5")}
                    onClick={() => setSelected(r.entry)}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <p className={cn("min-w-0 flex-1 truncate text-sm font-medium", isDelete && "text-destructive")}>
                        {r.entry.page}
                      </p>
                      {r.field === "—" && (
                        <Badge variant={isDelete ? "destructive" : "success"} className="shrink-0">{r.current}</Badge>
                      )}
                      <span className="shrink-0 text-xs text-muted-foreground">{fmtStamp(r.entry.timestamp)}</span>
                    </div>
                    {r.entry.record && (
                      <p className={cn("mt-0.5 truncate text-xs font-medium", isDelete && "text-destructive")}>{r.entry.record}</p>
                    )}
                    <p className="mt-1 truncate text-xs text-muted-foreground">{r.entry.who}</p>
                    {r.field !== "—" && (
                      <p className="mt-1.5 truncate text-xs">
                        <span className="font-medium">{r.field}</span>: {r.previous} → <span className="font-medium">{r.current}</span>
                      </p>
                    )}
                  </Card>
                );
              })}
            </div>
          )}

          {hasNextPage && (
            <div className="flex justify-center pt-1">
              <Button variant="outline" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
                {isFetchingNextPage ? "Loading…" : "Load older changes"}
              </Button>
            </div>
          )}
        </>
      )}

      <EntryDialog entry={selected} onClose={() => setSelected(null)} />
    </div>
  );
}
