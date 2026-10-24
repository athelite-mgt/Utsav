import { FileDown, Download, AlertCircle } from "lucide-react";
import { useAuditReport } from "@/hooks/useFinance";
import { money, fmtDate } from "@/lib/format";
import { exportCsv } from "@/lib/exportCsv";
import { exportPdfSections, type PdfColumn } from "@/lib/exportPdf";
import type { AuditLine, ExpenseAnnexureRow, IncomeAnnexureRow } from "@/types/db";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { CardsSkeleton } from "@/components/common/states";

const MONEY_COLS = [{ key: "label", label: "Particulars" }, { key: "amount", label: "Amount", align: "right" as const }];
const toRows = (lines: AuditLine[]) => lines.map((l) => ({ label: l.label, amount: l.amount }));
const sum = <T,>(rows: T[], pick: (r: T) => number) => rows.reduce((acc, r) => acc + (pick(r) || 0), 0);

const incomeAnnexureCols = (withZone: boolean): PdfColumn[] => [
  { key: "name", label: withZone ? "Donor" : "Name" },
  ...(withZone ? [{ key: "zone", label: "Zone" }] : []),
  { key: "bill_no", label: "Bill No" },
  { key: "billed_on", label: "Billed Date" },
  { key: "billed", label: "Billed", align: "right" },
  { key: "collected_on", label: "Collected Date" },
  { key: "collected", label: "Collected", align: "right" },
  { key: "due", label: "Due", align: "right" },
  { key: "mode", label: "Mode" },
];
const expenseAnnexureCols: PdfColumn[] = [
  { key: "category", label: "Category" },
  { key: "details", label: "Details" },
  { key: "bill_no", label: "Voucher" },
  { key: "spent_on", label: "Date" },
  { key: "mode", label: "Mode" },
  { key: "amount", label: "Amount", align: "right" },
];

const incomeAnnexureRows = (rows: IncomeAnnexureRow[]) => rows.map((r) => ({
  name: r.name, zone: r.zone ?? "—", bill_no: r.bill_no ?? "—",
  billed_on: r.billed_on ? fmtDate(r.billed_on) : "—", billed: r.billed,
  collected_on: r.collected_on ? fmtDate(r.collected_on) : "—", collected: r.collected,
  due: r.due, mode: r.mode ?? "—",
}));
const expenseAnnexureRowsFor = (rows: ExpenseAnnexureRow[]) => rows.map((r) => ({
  category: r.category, details: r.details ?? "—",
  bill_no: r.bill_no ?? "—", spent_on: r.spent_on ? fmtDate(r.spent_on) : "—", mode: r.mode ?? "—", amount: r.amount,
}));

/**
 * The formal year-end statements a committee hands its CA / society registrar —
 * Receipts & Payments, Income & Expenditure, and a Balance Sheet — rendered as
 * clean double-column accounts. Everything is computed server-side (see
 * AuditReportEndpoint) so the three tie to each other and to the dashboard's
 * Fund Position. Download as a single PDF for the CA, or CSV for a spreadsheet.
 *
 * There's no in-app Print anymore — printing this page directly also printed
 * the left navigation and top bar around it (no print stylesheet strips the
 * app shell), so a PDF download that's already just the statements is both
 * simpler and reliably clean.
 */
export function AuditReport({ yearId }: { yearId?: string | null }) {
  const { data, isLoading, isError } = useAuditReport(yearId);

  if (isLoading) return <CardsSkeleton />;
  if (isError || !data)
    return (
      <Card className="p-6 text-center text-sm text-muted-foreground">
        Couldn't load the audit report. It needs the audit-read permission (Admin, Secretariat or Finance).
      </Card>
    );

  const exportAll = () => {
    const rows: Array<Record<string, string | number>> = [];
    const push = (section: string, side: string, l: AuditLine) =>
      rows.push({ Statement: section, Side: side, Particulars: l.label, Amount: l.amount });
    data.receipts_payments.receipts.forEach((l) => push("Receipts & Payments", "Receipts", l));
    data.receipts_payments.payments.forEach((l) => push("Receipts & Payments", "Payments", l));
    data.income_expenditure.income.forEach((l) => push("Income & Expenditure", "Income", l));
    data.income_expenditure.expenditure.forEach((l) => push("Income & Expenditure", "Expenditure", l));
    data.balance_sheet.assets.forEach((l) => push("Balance Sheet", "Assets", l));
    data.balance_sheet.funds.forEach((l) => push("Balance Sheet", "Funds & Liabilities", l));

    const pushIncome = (annexure: string, l: IncomeAnnexureRow) => rows.push({
      Statement: annexure, Side: l.name, Particulars: `Zone: ${l.zone ?? "—"} · Bill ${l.bill_no ?? "—"} · Billed ${l.billed_on ?? "—"} · Collected ${l.collected_on ?? "—"} · Mode ${l.mode ?? "—"} · Due ${l.due}`,
      Amount: l.billed,
    });
    data.annexures.membership.forEach((l) => pushIncome("Annexure — Membership", l));
    data.annexures.special.forEach((l) => pushIncome("Annexure — Special Donation", l));
    data.annexures.ads.forEach((l) => pushIncome("Annexure — Advertisements", l));
    data.annexures.other.forEach((l) => pushIncome("Annexure — Other Incomes", l));
    data.annexures.expenses.forEach((e) => rows.push({
      Statement: "Annexure — Expenses", Side: e.category,
      Particulars: `${e.details ?? "—"} · Voucher ${e.bill_no ?? "—"} · ${e.spent_on ?? "—"} · Mode ${e.mode ?? "—"}`,
      Amount: e.amount,
    }));

    exportCsv(`audit-${data.year}`, rows);
  };

  const exportPdf = () => {
    exportPdfSections({
      filename: `audit-${data.year}`,
      title: data.org_name,
      subtitle: `Financial Statements — ${data.year_label}`,
      sections: [
        {
          heading: "Receipts & Payments Account (cash basis)",
          tables: [
            { label: "Receipts", columns: MONEY_COLS, rows: toRows(data.receipts_payments.receipts), total: { label: "Total", value: data.receipts_payments.total_receipts } },
            { label: "Payments", columns: MONEY_COLS, rows: toRows(data.receipts_payments.payments), total: { label: "Total", value: data.receipts_payments.total_payments } },
          ],
        },
        {
          heading: "Income & Expenditure Account (accrual basis)",
          tables: [
            { label: "Income", columns: MONEY_COLS, rows: toRows(data.income_expenditure.income), total: { label: "Total", value: data.income_expenditure.total_income } },
            { label: "Expenditure", columns: MONEY_COLS, rows: toRows(data.income_expenditure.expenditure), total: { label: data.income_expenditure.surplus >= 0 ? "Total + Surplus" : "Total + Deficit", value: data.income_expenditure.total_expenditure + Math.max(0, data.income_expenditure.surplus) } },
          ],
        },
        {
          heading: `Balance Sheet — as at close of ${data.year_label}`,
          tables: [
            { label: "Funds & Liabilities", columns: MONEY_COLS, rows: toRows(data.balance_sheet.funds), total: { label: "Total", value: data.balance_sheet.total_funds } },
            { label: "Assets", columns: MONEY_COLS, rows: toRows(data.balance_sheet.assets), total: { label: "Total", value: data.balance_sheet.total_assets } },
          ],
        },
        {
          heading: "Annexure A — Membership",
          tables: [{ label: "Donor", columns: incomeAnnexureCols(true), rows: incomeAnnexureRows(data.annexures.membership),
            total: { label: "Total", value: sum(data.annexures.membership, (r) => r.billed) } }],
        },
        {
          heading: "Annexure B — Special Donation",
          tables: [{ label: "Donor", columns: incomeAnnexureCols(true), rows: incomeAnnexureRows(data.annexures.special),
            total: { label: "Total", value: sum(data.annexures.special, (r) => r.billed) } }],
        },
        {
          heading: "Annexure C — Advertisements",
          tables: [{ label: "Advertiser", columns: incomeAnnexureCols(false), rows: incomeAnnexureRows(data.annexures.ads),
            total: { label: "Total", value: sum(data.annexures.ads, (r) => r.billed) } }],
        },
        {
          heading: "Annexure D — Other Incomes",
          tables: [{ label: "Name", columns: incomeAnnexureCols(false), rows: incomeAnnexureRows(data.annexures.other),
            total: { label: "Total", value: sum(data.annexures.other, (r) => r.billed) } }],
        },
        {
          heading: "Annexure E — Expenses",
          tables: [{ label: "Expense", columns: expenseAnnexureCols, rows: expenseAnnexureRowsFor(data.annexures.expenses),
            total: { label: "Total", value: sum(data.annexures.expenses, (r) => r.amount) } }],
        },
      ],
      notes: data.notes,
    });
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold">{data.org_name}</h2>
          <p className="text-sm text-muted-foreground">Audited statements — {data.year_label}</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={exportPdf}><FileDown className="h-4 w-4" /> Download PDF</Button>
          <Button variant="outline" size="sm" onClick={exportAll}><Download className="h-4 w-4" /> Export CSV</Button>
        </div>
      </div>

      <Account
        title="Receipts & Payments Account"
        subtitle="Cash basis — money actually received and paid during the year"
        leftLabel="Receipts" leftLines={data.receipts_payments.receipts} leftTotal={data.receipts_payments.total_receipts}
        rightLabel="Payments" rightLines={data.receipts_payments.payments} rightTotal={data.receipts_payments.total_payments}
      />

      <Account
        title="Income & Expenditure Account"
        subtitle="Accrual basis — income earned (billed) and expenditure incurred"
        leftLabel="Expenditure" leftLines={data.income_expenditure.expenditure} leftTotal={data.income_expenditure.total_expenditure}
        rightLabel="Income" rightLines={data.income_expenditure.income} rightTotal={data.income_expenditure.total_income}
        footer={<SurplusRow surplus={data.income_expenditure.surplus} />}
      />

      <Account
        title="Balance Sheet"
        subtitle={`As at close of ${data.year_label}`}
        leftLabel="Funds & Liabilities" leftLines={data.balance_sheet.funds} leftTotal={data.balance_sheet.total_funds}
        rightLabel="Assets" rightLines={data.balance_sheet.assets} rightTotal={data.balance_sheet.total_assets}
      />

      {data.notes.length > 0 && (
        <Card className="space-y-2 p-4">
          <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            <AlertCircle className="h-3.5 w-3.5" /> Notes to the accounts
          </p>
          <ol className="list-decimal space-y-1 pl-5 text-sm text-muted-foreground">
            {data.notes.map((n, i) => <li key={i}>{n}</li>)}
          </ol>
        </Card>
      )}

      <div className="space-y-4 border-t border-border pt-2">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">Annexures — supporting detail</h3>
        <IncomeAnnexure title="Annexure A — Membership" rowLabel="Donor" withZone rows={data.annexures.membership} />
        <IncomeAnnexure title="Annexure B — Special Donation" rowLabel="Donor" withZone rows={data.annexures.special} />
        <IncomeAnnexure title="Annexure C — Advertisements" rowLabel="Advertiser" rows={data.annexures.ads} />
        <IncomeAnnexure title="Annexure D — Other Incomes" rowLabel="Name" rows={data.annexures.other} />
        <ExpenseAnnexure title="Annexure E — Expenses" rows={data.annexures.expenses} />
      </div>
    </div>
  );
}

function IncomeAnnexure({ title, rowLabel, withZone, rows }: { title: string; rowLabel: string; withZone?: boolean; rows: IncomeAnnexureRow[] }) {
  return (
    <Card className="overflow-hidden">
      <div className="border-b border-border px-4 py-3">
        <h4 className="font-semibold">{title}</h4>
        <p className="text-xs text-muted-foreground">{rows.length} {rows.length === 1 ? "entry" : "entries"} · Billed total {money(sum(rows, (r) => r.billed))}</p>
      </div>
      {rows.length === 0 ? (
        <p className="p-4 text-sm text-muted-foreground">Nothing recorded for this year.</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{rowLabel}</TableHead>
                {withZone && <TableHead>Zone</TableHead>}
                <TableHead>Bill No</TableHead>
                <TableHead>Billed Date</TableHead>
                <TableHead className="text-right">Billed</TableHead>
                <TableHead>Collected Date</TableHead>
                <TableHead className="text-right">Collected</TableHead>
                <TableHead className="text-right">Due</TableHead>
                <TableHead>Mode</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r, i) => (
                <TableRow key={i}>
                  <TableCell className="font-medium">{r.name}</TableCell>
                  {withZone && <TableCell>{r.zone ?? "—"}</TableCell>}
                  <TableCell>{r.bill_no ?? "—"}</TableCell>
                  <TableCell>{r.billed_on ? fmtDate(r.billed_on) : "—"}</TableCell>
                  <TableCell className="text-right tabular">{money(r.billed)}</TableCell>
                  <TableCell>{r.collected_on ? fmtDate(r.collected_on) : "—"}</TableCell>
                  <TableCell className="text-right tabular">{money(r.collected)}</TableCell>
                  <TableCell className={`text-right tabular ${r.due > 0 ? "text-destructive" : ""}`}>{money(r.due)}</TableCell>
                  <TableCell>{r.mode ?? "—"}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </Card>
  );
}

function ExpenseAnnexure({ title, rows }: { title: string; rows: ExpenseAnnexureRow[] }) {
  return (
    <Card className="overflow-hidden">
      <div className="border-b border-border px-4 py-3">
        <h4 className="font-semibold">{title}</h4>
        <p className="text-xs text-muted-foreground">{rows.length} {rows.length === 1 ? "entry" : "entries"} · Total {money(sum(rows, (r) => r.amount))}</p>
      </div>
      {rows.length === 0 ? (
        <p className="p-4 text-sm text-muted-foreground">Nothing recorded for this year.</p>
      ) : (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Category</TableHead>
                <TableHead>Details</TableHead>
                <TableHead>Voucher</TableHead>
                <TableHead>Date</TableHead>
                <TableHead>Mode</TableHead>
                <TableHead className="text-right">Amount</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r, i) => (
                <TableRow key={i}>
                  <TableCell className="font-medium">{r.category}</TableCell>
                  <TableCell>{r.details ?? "—"}</TableCell>
                  <TableCell>{r.bill_no ?? "—"}</TableCell>
                  <TableCell>{r.spent_on ? fmtDate(r.spent_on) : "—"}</TableCell>
                  <TableCell>{r.mode ?? "—"}</TableCell>
                  <TableCell className="text-right tabular">{money(r.amount)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </Card>
  );
}

function Account({
  title, subtitle, leftLabel, leftLines, leftTotal, rightLabel, rightLines, rightTotal, footer,
}: {
  title: string; subtitle: string;
  leftLabel: string; leftLines: AuditLine[]; leftTotal: number;
  rightLabel: string; rightLines: AuditLine[]; rightTotal: number;
  footer?: React.ReactNode;
}) {
  // Pad the shorter column so both sides bottom-align, the way a hand-kept
  // ledger reads. The grand totals still sit on the same final row.
  const rows = Math.max(leftLines.length, rightLines.length);
  return (
    <Card className="overflow-hidden">
      <div className="border-b border-border px-4 py-3">
        <h3 className="font-semibold">{title}</h3>
        <p className="text-xs text-muted-foreground">{subtitle}</p>
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 md:divide-x divide-border">
        <Side label={leftLabel} lines={leftLines} total={leftTotal} pad={rows - leftLines.length} />
        <Side label={rightLabel} lines={rightLines} total={rightTotal} pad={rows - rightLines.length} />
      </div>
      {footer}
    </Card>
  );
}

function Side({ label, lines, total, pad }: { label: string; lines: AuditLine[]; total: number; pad: number }) {
  return (
    <div className="flex flex-col">
      <p className="border-b border-border/60 bg-muted/40 px-4 py-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{label}</p>
      <div className="flex-1 divide-y divide-border/40">
        {lines.map((l, i) => (
          <div key={i} className="flex items-baseline justify-between gap-3 px-4 py-1.5 text-sm">
            <span className="text-muted-foreground">{l.label}</span>
            <span className="tabular font-medium">{money(l.amount)}</span>
          </div>
        ))}
        {Array.from({ length: Math.max(0, pad) }).map((_, i) => (
          <div key={`pad-${i}`} className="px-4 py-1.5 text-sm">&nbsp;</div>
        ))}
      </div>
      <div className="flex items-baseline justify-between gap-3 border-t-2 border-border px-4 py-2 text-sm font-semibold">
        <span>Total</span>
        <span className="tabular">{money(total)}</span>
      </div>
    </div>
  );
}

function SurplusRow({ surplus }: { surplus: number }) {
  const deficit = surplus < 0;
  return (
    <div className={`flex items-baseline justify-between gap-3 border-t border-border px-4 py-2 text-sm font-semibold ${deficit ? "text-destructive" : "text-success"}`}>
      <span>{deficit ? "Deficit for the year" : "Surplus for the year"}</span>
      <span className="tabular">{money(Math.abs(surplus))}</span>
    </div>
  );
}
