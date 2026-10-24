import { jsPDF } from "jspdf";
import autoTable from "jspdf-autotable";
import { NotoSansBengaliRegular } from "./fonts/NotoSansBengaliRegular";
import { NotoSansBengaliBold } from "./fonts/NotoSansBengaliBold";
import { accentRgb, getLetterhead, pdfImageFormat } from "./letterhead";

const BENGALI_FONT = "NotoSansBengali";

/**
 * jsPDF's built-in core fonts (Helvetica etc.) only cover WinAnsi encoding —
 * Bengali donor names / expense details typed into the app rendered as blank
 * boxes with them. Noto Sans Bengali also covers basic Latin, digits and
 * punctuation, so one embedded font serves an entire document with Bengali
 * and English mixed freely — no per-run font switching needed. jsPDF fonts
 * are registered per document instance, so this runs once per exported PDF.
 */
export function embedBengaliFont(doc: jsPDF) {
  doc.addFileToVFS("NotoSansBengali-Regular.ttf", NotoSansBengaliRegular);
  doc.addFont("NotoSansBengali-Regular.ttf", BENGALI_FONT, "normal");
  doc.addFileToVFS("NotoSansBengali-Bold.ttf", NotoSansBengaliBold);
  doc.addFont("NotoSansBengali-Bold.ttf", BENGALI_FONT, "bold");
  doc.setFont(BENGALI_FONT, "normal");
}

/**
 * The committee's own heading, drawn first on every document: logo, name,
 * city and a rule in its brand colour. These PDFs go to auditors and donors,
 * so they read as the committee's, not the product's. With no committee
 * context (or no logo) it degrades quietly; a document is never refused over
 * its letterhead. Returns the y at which the title should start.
 */
export function drawLetterhead(doc: jsPDF, marginX: number, defaultY: number): number {
  const head = getLetterhead();
  if (!head) return defaultY;

  const top = 34;
  const pageWidth = doc.internal.pageSize.getWidth();
  let textX = marginX;

  const format = head.logoDataUrl ? pdfImageFormat(head.logoDataUrl) : null;
  if (head.logoDataUrl && format) {
    try {
      doc.addImage(head.logoDataUrl, format, marginX, top, 34, 34);
      textX = marginX + 34 + 10;
    } catch {
      // An image jsPDF cannot draw is simply left out.
    }
  }

  doc.setFont(BENGALI_FONT, "bold");
  doc.setFontSize(13);
  doc.setTextColor(0);
  doc.text(head.name, textX, top + 13);
  if (head.city) {
    doc.setFont(BENGALI_FONT, "normal");
    doc.setFontSize(9);
    doc.setTextColor(110);
    doc.text(head.city, textX, top + 26);
  }

  const [r, g, b] = accentRgb(head.accent) ?? [190, 190, 190];
  doc.setDrawColor(r, g, b);
  doc.setLineWidth(1.4);
  doc.line(marginX, top + 42, pageWidth - marginX, top + 42);

  doc.setTextColor(0);
  doc.setDrawColor(0);
  doc.setLineWidth(0.2);
  return top + 42 + 22;
}

export interface PdfColumn {
  key: string;
  label: string;
  /** Right-align + tabular rendering, for money/number columns. */
  align?: "right";
}

/**
 * A simple, print-quality PDF table with a title/subtitle header — used for
 * every "download as PDF" button across Reports, Expenses and the audit
 * statements. Deliberately client-side (jsPDF): no server round trip, no
 * extra endpoint per report, and it works from data already in memory.
 */
export function exportPdfTable(opts: {
  filename: string;
  title: string;
  subtitle?: string;
  columns: PdfColumn[];
  rows: Array<Record<string, unknown>>;
  /** Short lines under the title — e.g. totals, or a note about what's excluded. */
  meta?: string[];
}) {
  const { filename, title, subtitle, columns, rows, meta } = opts;
  const doc = new jsPDF({ unit: "pt", format: "a4" });
  embedBengaliFont(doc);
  const marginX = 40;
  let y = drawLetterhead(doc, marginX, 48);

  doc.setFontSize(15);
  doc.setFont(BENGALI_FONT, "bold");
  doc.text(title, marginX, y);
  y += 18;

  if (subtitle) {
    doc.setFontSize(10);
    doc.setFont(BENGALI_FONT, "normal");
    doc.setTextColor(90);
    doc.text(subtitle, marginX, y);
    y += 14;
  }

  if (meta?.length) {
    doc.setFontSize(9);
    doc.setTextColor(120);
    for (const line of meta) {
      doc.text(line, marginX, y);
      y += 12;
    }
  }

  doc.setTextColor(0);
  y += 6;

  autoTable(doc, {
    startY: y,
    head: [columns.map((c) => c.label)],
    body: rows.map((r) => columns.map((c) => fmtCell(r[c.key]))),
    // font must be set here, not via doc.setFont — jspdf-autotable manages
    // its own font state per cell and would otherwise fall back to helvetica,
    // which has no Bengali glyphs.
    styles: { font: BENGALI_FONT, fontSize: 9, cellPadding: 5 },
    headStyles: { font: BENGALI_FONT, fillColor: [124, 58, 51], textColor: 255, fontStyle: "bold" },
    alternateRowStyles: { fillColor: [248, 246, 243] },
    columnStyles: Object.fromEntries(
      columns.map((c, i) => [i, c.align === "right" ? { halign: "right" as const } : {}])
    ),
    margin: { left: marginX, right: marginX },
  });

  doc.setFontSize(8);
  doc.setTextColor(140);
  doc.text(`Generated ${new Date().toLocaleString()}`, marginX, doc.internal.pageSize.getHeight() - 20);

  doc.save(filename.endsWith(".pdf") ? filename : `${filename}.pdf`);
}

export interface PdfSection {
  heading: string;
  /** Two side-by-side tables (e.g. Receipts | Payments) or one full-width table. */
  tables: Array<{ label: string; columns: PdfColumn[]; rows: Array<Record<string, unknown>>; total?: { label: string; value: number } }>;
}

/**
 * Multiple titled sections in one PDF, each with one or two tables — the
 * shape the audit statements need (Receipts & Payments, Income &
 * Expenditure, Balance Sheet all in a single downloadable file).
 */
export function exportPdfSections(opts: {
  filename: string;
  title: string;
  subtitle?: string;
  sections: PdfSection[];
  notes?: string[];
}) {
  const { filename, title, subtitle, sections, notes } = opts;
  const doc = new jsPDF({ unit: "pt", format: "a4" });
  embedBengaliFont(doc);
  const marginX = 40;
  const pageWidth = doc.internal.pageSize.getWidth();
  let y = drawLetterhead(doc, marginX, 48);

  doc.setFontSize(16);
  doc.setFont(BENGALI_FONT, "bold");
  doc.text(title, marginX, y);
  y += 18;
  if (subtitle) {
    doc.setFontSize(10);
    doc.setFont(BENGALI_FONT, "normal");
    doc.setTextColor(90);
    doc.text(subtitle, marginX, y);
    doc.setTextColor(0);
    y += 20;
  }

  for (const section of sections) {
    if (y > doc.internal.pageSize.getHeight() - 120) {
      doc.addPage();
      y = 48;
    }
    doc.setFontSize(12);
    doc.setFont(BENGALI_FONT, "bold");
    doc.text(section.heading, marginX, y);
    y += 14;

    const colWidth = section.tables.length > 1 ? (pageWidth - marginX * 2 - 16) / 2 : pageWidth - marginX * 2;
    let maxYReached = y;

    section.tables.forEach((t, i) => {
      const startX = marginX + i * (colWidth + 16);
      type Cell = string | { content: string; styles: { fontStyle: "bold"; fillColor: [number, number, number] } };
      const body: Cell[][] = t.rows.map((r) => t.columns.map((c) => fmtCell(r[c.key])));
      // Built as {content, styles} cells (not plain strings) so the bold
      // weight is part of what autoTable measures when it sizes columns —
      // applying bold afterwards via didParseCell happens post-layout, so a
      // wide bolded total silently overflowed the column it was sized for
      // with plain (lighter) text.
      if (t.total) {
        const lastIdx = t.columns.length - 1;
        const totalRow: Cell[] = t.columns.map((_, idx) => ({
          content: idx === 0 ? t.total!.label : idx === lastIdx ? fmtCell(t.total!.value) : "",
          styles: { fontStyle: "bold" as const, fillColor: [248, 246, 243] as [number, number, number] },
        }));
        body.push(totalRow);
      }

      autoTable(doc, {
        startY: y,
        margin: { left: startX, right: pageWidth - startX - colWidth },
        tableWidth: colWidth,
        head: [[t.label, ...t.columns.slice(1).map((c) => c.label)]],
        body,
        styles: { font: BENGALI_FONT, fontSize: 8, cellPadding: 4, overflow: "linebreak" },
        headStyles: { font: BENGALI_FONT, fillColor: [124, 58, 51], textColor: 255, fontStyle: "bold" },
        // Right-align every money/numeric column, not just the last one —
        // a 9-column annexure (Billed/Collected/Due sit mid-table) otherwise
        // left-aligned its actual amounts while right-aligning "Mode".
        columnStyles: Object.fromEntries(
          t.columns.map((c, idx) => [idx, c.align === "right" ? { halign: "right" as const, minCellWidth: 42 } : {}])
        ),
      });

      // @ts-expect-error jspdf-autotable attaches this at runtime
      const finalY = doc.lastAutoTable?.finalY ?? y;
      maxYReached = Math.max(maxYReached, finalY);
    });

    y = maxYReached + 24;
  }

  if (notes?.length) {
    if (y > doc.internal.pageSize.getHeight() - 100) {
      doc.addPage();
      y = 48;
    }
    doc.setFontSize(10);
    doc.setFont(BENGALI_FONT, "bold");
    doc.text("Notes", marginX, y);
    y += 14;
    doc.setFontSize(8.5);
    doc.setFont(BENGALI_FONT, "normal");
    doc.setTextColor(90);
    for (const note of notes) {
      const lines = doc.splitTextToSize(`• ${note}`, pageWidth - marginX * 2);
      doc.text(lines, marginX, y);
      y += lines.length * 11 + 4;
    }
    doc.setTextColor(0);
  }

  doc.setFontSize(8);
  doc.setTextColor(140);
  doc.text(`Generated ${new Date().toLocaleString()}`, marginX, doc.internal.pageSize.getHeight() - 20);

  doc.save(filename.endsWith(".pdf") ? filename : `${filename}.pdf`);
}

function fmtCell(v: unknown): string {
  if (v === null || v === undefined) return "";
  if (typeof v === "number") return v.toLocaleString("en-IN");
  return String(v);
}
