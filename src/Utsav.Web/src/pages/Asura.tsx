import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  BarChart3, ChevronLeft, Database, Download, FileSpreadsheet, FileText, PiggyBank, Printer,
  Send, ShieldCheck, Sparkles, TrendingUp, Users, X,
} from "lucide-react";
import { askAsura, drillAsura, exportAsura, type AsuraEvent, type AsuraResult } from "@/lib/asuraStream";
import { exportCsv } from "@/lib/exportCsv";
import { AsuraFigure } from "./asura/AsuraFigure";
import "./asura/asura.css";

interface Step {
  text: string;
  done: boolean;
}

interface DrillState {
  title: string;
  columns: string[];
  rows: Record<string, unknown>[];
  from: string;
}

interface Turn {
  question: string;
  steps: Step[];
  answer: string;
  result: AsuraResult | null;
  error: string | null;
  // Per-tier reasons ("groq/...: HTTP 403 ...") behind a disclosure rather
  // than in the headline message -- the person asking wants to know what to
  // do, whoever is diagnosing an outage wants to know exactly what each
  // provider said. Was computed server-side and silently dropped here until
  // now: the TS event type never declared the field, so there was no way to
  // see *why* a failure happened without reading server logs directly.
  errorDetail: string | null;
  drill: DrillState | null;
  streaming: boolean;
}

const SUGGESTIONS = [
  { icon: TrendingUp, text: "Which zones are behind on collections?" },
  { icon: PiggyBank, text: "Compare expenses against the budget by category" },
  { icon: Users, text: "Who has been billed but hasn't paid yet?" },
  { icon: BarChart3, text: "Which vendors were paid the most this year?" },
];

/** SQL returns numbers as numbers; anything money-shaped reads better grouped Indian-style. */
function cell(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "number") return value.toLocaleString("en-IN", { maximumFractionDigits: 2 });
  return String(value);
}

function isNumeric(value: unknown): boolean {
  return typeof value === "number";
}

/**
 * Grows the bars to their real heights.
 *
 * Deliberately driven by useEffect rather than requestAnimationFrame. rAF is
 * suspended entirely while a tab is hidden, so an answer that arrived in a
 * background tab would leave every bar stuck at zero — a *wrong* chart, not
 * merely an unanimated one. This was a real bug in the prototype. useEffect is
 * a normal task and always runs, so the final state is guaranteed either way.
 */
function Bars({
  chart,
  onDrill,
}: {
  chart: NonNullable<AsuraResult["chart"]>;
  onDrill: ((row: Record<string, unknown>) => void) | null;
}) {
  const [grown, setGrown] = useState(false);
  useEffect(() => {
    setGrown(true);
  }, []);

  const series = chart.value_columns.slice(0, 2);
  const max = Math.max(
    1,
    ...chart.rows.flatMap((r) => series.map((c) => Number(r[c]) || 0)),
  );

  return (
    <div className="asura-chart">
      <div className="asura-chart-head">
        <b>{chart.label_column.replace(/_/g, " ")}</b>
        <span>{onDrill ? "tap a bar for detail" : "₹"}</span>
      </div>
      <div className="asura-bars">
        {chart.rows.map((row, i) => {
          const label = String(row[chart.label_column] ?? "—");
          const bars = (
            <>
              <div className="asura-barpair">
                {series.map((c, s) => (
                  <div
                    key={c}
                    className={`asura-bar s${s}`}
                    style={{ height: grown ? `${Math.round(((Number(row[c]) || 0) / max) * 100)}%` : 0 }}
                  />
                ))}
              </div>
              <label>{label}</label>
            </>
          );

          // A drillable group is a real <button>, so it is keyboard-operable.
          // "Clickable" has to include the keyboard, not just the mouse.
          return onDrill ? (
            <button
              key={`${label}-${i}`}
              type="button"
              className="asura-bargrp drillable"
              onClick={() => onDrill(row)}
              aria-label={`Show detail for ${label}`}
            >
              {bars}
            </button>
          ) : (
            <div key={`${label}-${i}`} className="asura-bargrp">
              {bars}
            </div>
          );
        })}
      </div>
      {series.length > 1 && (
        <div className="asura-legend">
          {series.map((c, s) => (
            <span key={c}>
              <i style={{ background: s === 0 ? "rgba(167,139,250,.55)" : "var(--a-cyan)" }} />
              {c.replace(/_/g, " ")}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

function ResultTable({
  columns,
  rows,
  onDrill,
}: {
  columns: string[];
  rows: Record<string, unknown>[];
  onDrill: ((row: Record<string, unknown>) => void) | null;
}) {
  // Identifier columns are what the drill-down travels on, not something a
  // treasurer needs to read.
  const visible = columns.filter((c) => !c.endsWith("_id"));

  return (
    <div className="asura-table-wrap">
      <table className="asura-table">
        <thead>
          <tr>
            {visible.map((c) => (
              <th key={c} className={isNumeric(rows[0]?.[c]) ? "num" : undefined}>
                {c.replace(/_/g, " ")}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.slice(0, 200).map((row, i) => (
            <tr
              key={i}
              className={onDrill ? "drillable" : undefined}
              onClick={onDrill ? () => onDrill(row) : undefined}
            >
              {visible.map((c) => (
                <td key={c} className={isNumeric(row[c]) ? "num" : undefined}>
                  {cell(row[c])}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function AsuraPage() {
  const [turns, setTurns] = useState<Turn[]>([]);
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  const navigate = useNavigate();
  const abort = useRef<AbortController | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: "smooth", block: "end" });
  }, [turns]);

  // A navigation away mid-answer must cancel the stream, or the request keeps
  // burning a shared free-tier quota for an answer nobody will read.
  useEffect(() => () => abort.current?.abort(), []);

  const patchLast = useCallback((patch: (t: Turn) => Turn) => {
    setTurns((prev) => prev.map((t, i) => (i === prev.length - 1 ? patch(t) : t)));
  }, []);

  const ask = useCallback(
    async (text: string) => {
      const trimmed = text.trim();
      if (!trimmed || busy) return;

      setQuestion("");
      setBusy(true);
      setTurns((prev) => [
        ...prev,
        { question: trimmed, steps: [], answer: "", result: null, error: null, errorDetail: null, drill: null, streaming: true },
      ]);

      const controller = new AbortController();
      abort.current = controller;

      const onEvent = (e: AsuraEvent) => {
        switch (e.type) {
          case "step":
            patchLast((t) => {
              // A step arriving with done:true replaces the running line of
              // the same name rather than appending a duplicate.
              const existing = t.steps.findIndex((s) => s.text === e.text);
              const steps = [...t.steps];
              if (existing >= 0) steps[existing] = { text: e.text, done: e.done };
              else steps.push({ text: e.text, done: e.done });
              return { ...t, steps };
            });
            break;
          case "token":
            patchLast((t) => ({ ...t, answer: t.answer + e.text }));
            break;
          case "result": {
            const { type: _ignored, ...result } = e;
            patchLast((t) => ({ ...t, result: result as AsuraResult }));
            break;
          }
          case "error":
            patchLast((t) => ({ ...t, error: e.message, errorDetail: e.detail ?? null }));
            break;
          case "done":
            patchLast((t) => ({ ...t, streaming: false }));
            break;
        }
      };

      try {
        await askAsura(trimmed, onEvent, controller.signal);
      } catch (err) {
        if (!controller.signal.aborted) {
          patchLast((t) => ({
            ...t,
            error: err instanceof Error ? err.message : "Couldn't reach Asura.",
          }));
        }
      } finally {
        patchLast((t) => ({ ...t, streaming: false }));
        setBusy(false);
        abort.current = null;
      }
    },
    [busy, patchLast],
  );

  const drill = useCallback(
    async (turnIndex: number, row: Record<string, unknown>) => {
      const turn = turns[turnIndex];
      const spec = turn?.result?.drill;
      if (!spec) return;

      const value = row[spec.from_column];
      if (value === null || value === undefined) return;

      try {
        // Only a tool id and a parameter cross the wire — never SQL.
        const detail = await drillAsura(spec.tool_id, { [spec.param_name]: String(value) });
        const label = String(row[turn.result!.chart?.label_column ?? ""] ?? "detail");
        setTurns((prev) =>
          prev.map((t, i) =>
            i === turnIndex
              ? { ...t, drill: { title: detail.title, columns: detail.columns, rows: detail.rows, from: label } }
              : t,
          ),
        );
      } catch (err) {
        setTurns((prev) =>
          prev.map((t, i) =>
            i === turnIndex
              ? { ...t, error: err instanceof Error ? err.message : "Couldn't open that detail.", errorDetail: null }
              : t,
          ),
        );
      }
    },
    [turns],
  );

  return (
    <div className="asura-root">
      <div className="asura-aurora" aria-hidden="true">
        <span className="asura-blob asura-blob-1" />
        <span className="asura-blob asura-blob-2" />
        <span className="asura-blob asura-blob-3" />
      </div>

      <div className="asura-lights" aria-hidden="true">
        <span className="asura-beam" />
        <div className="asura-streaks">
          <span className="asura-streak" />
          <span className="asura-streak" />
          <span className="asura-streak" />
          <span className="asura-streak" />
          <span className="asura-streak" />
        </div>
        <div className="asura-embers">
          <span className="asura-ember" />
          <span className="asura-ember" />
          <span className="asura-ember" />
          <span className="asura-ember" />
          <span className="asura-ember" />
          <span className="asura-ember" />
          <span className="asura-ember" />
          <span className="asura-ember" />
        </div>
      </div>

      <div className="asura-stage">
      <header className="asura-head">
        <AsuraFigure busy={busy} />
        <div className="asura-title" style={{ flex: 1, minWidth: 0 }}>
          <b>Asura</b>
          <small>Reporting intelligence · asks questions of the committee's books</small>
        </div>
        <span className="asura-badge">
          <ShieldCheck size={12} />
          View-only
        </span>
        {/* The takeover hides the sidebar, and an installed PWA has no browser
            back button, so an explicit way out is not optional. */}
        <button
          type="button"
          className="asura-close"
          onClick={() => navigate("/")}
          aria-label="Close Asura and return to the committee app"
        >
          <X size={14} />
          <span>Close</span>
        </button>
      </header>

      <div className="asura-thread">
        {turns.length === 0 && (
          <div className="asura-welcome asura-hero">
            <AsuraFigure busy={busy} />
            <h2>What would you like to know?</h2>
            <p>
              Ask in plain English. Asura reads the committee's books — collections, expenses,
              budgets, vendors, advances — and builds the report for you. It can only ever read;
              every answer shows the tables it used.
            </p>
            <span className="asura-label">Try one</span>
            <div className="asura-chips">
              {SUGGESTIONS.map(({ icon: Icon, text }) => (
                <button key={text} type="button" className="asura-chip" onClick={() => ask(text)}>
                  <Icon size={14} />
                  <span>{text}</span>
                </button>
              ))}
            </div>
          </div>
        )}

        {turns.map((turn, index) => (
          <div key={index}>
            <div className="asura-row me">
              <div className="asura-bubble">{turn.question}</div>
              <div className="asura-av me">You</div>
            </div>

            <div className="asura-row" style={{ marginTop: "1.1rem" }}>
              <div className="asura-av ai"><AsuraFigure compact /></div>
              <div className="asura-bubble ai">
                {/* The live ticker. Steps come from the orchestrator's real
                    control flow, so a slow query genuinely sits on
                    "Executing query" for as long as it actually runs. */}
                {turn.steps.length > 0 && !turn.result && !turn.error && (
                  <div className="asura-steps">
                    {turn.steps.map((s) => (
                      <div key={s.text} className={`asura-step ${s.done ? "done" : "running"}`}>
                        <span className="dot" />
                        <span>{s.text}</span>
                      </div>
                    ))}
                  </div>
                )}

                {turn.error && (
                  <div className="asura-error">
                    {turn.error}
                    {turn.errorDetail && (
                      <details className="asura-trace" style={{ marginTop: "0.6rem" }}>
                        <summary>
                          <Database size={13} style={{ color: "var(--a-cyan)" }} />
                          Why — per-provider detail
                        </summary>
                        <pre>{turn.errorDetail}</pre>
                      </details>
                    )}
                  </div>
                )}

                {turn.answer && (
                  <p style={{ margin: 0 }}>
                    {turn.answer}
                    {turn.streaming && <span className="asura-caret" />}
                  </p>
                )}

                {turn.result && (
                  <>
                    <details className="asura-trace" style={{ marginTop: "0.7rem" }}>
                      <summary>
                        <Database size={13} style={{ color: "var(--a-cyan)" }} />
                        How I got this — {turn.result.provenance.tables.length} table
                        {turn.result.provenance.tables.length === 1 ? "" : "s"},{" "}
                        {turn.result.provenance.row_count.toLocaleString("en-IN")} rows,{" "}
                        {turn.result.provenance.query_ms} ms
                      </summary>
                      <pre>
                        {turn.result.provenance.tables.join(", ")}
                        {"\n\n"}
                        {turn.result.provenance.sql}
                        {turn.result.provenance.truncated ? "\n\n(result was capped)" : ""}
                      </pre>
                    </details>

                    {turn.result.tiles.length > 0 && (
                      <div className="asura-tiles">
                        {turn.result.tiles.map((t) => (
                          <div key={t.key} className="asura-tile">
                            <div className="k">{t.key}</div>
                            <div className="v">{t.value}</div>
                            {t.detail && <div className="d">{t.detail}</div>}
                          </div>
                        ))}
                      </div>
                    )}

                    {turn.result.chart && (
                      <Bars
                        chart={turn.result.chart}
                        onDrill={turn.result.drill ? (row) => drill(index, row) : null}
                      />
                    )}

                    {turn.result.rows.length > 0 && (
                      <ResultTable
                        columns={turn.result.columns}
                        rows={turn.result.rows}
                        onDrill={turn.result.drill ? (row) => drill(index, row) : null}
                      />
                    )}

                    {turn.drill && (
                      <div className="asura-drillpanel">
                        <div className="asura-crumb">
                          <button
                            type="button"
                            className="asura-act"
                            style={{ minHeight: 32, padding: "0.25rem 0.5rem" }}
                            onClick={() =>
                              setTurns((prev) =>
                                prev.map((t, i) => (i === index ? { ...t, drill: null } : t)),
                              )
                            }
                          >
                            <ChevronLeft size={13} />
                            Back
                          </button>
                          <span>
                            {turn.drill.title} — {turn.drill.from}
                          </span>
                        </div>
                        {turn.drill.rows.length > 0 ? (
                          <ResultTable columns={turn.drill.columns} rows={turn.drill.rows} onDrill={null} />
                        ) : (
                          <p style={{ color: "var(--a-fg-3)", fontSize: "0.8rem", margin: 0 }}>
                            Nothing recorded for that yet.
                          </p>
                        )}
                      </div>
                    )}

                    <div className="asura-rail">
                      <button
                        type="button"
                        className="asura-act"
                        onClick={() =>
                          exportCsv(
                            `asura-${turn.result!.provenance.tool_id}`,
                            turn.result!.rows as Record<string, unknown>[],
                          )
                        }
                      >
                        <Download size={14} />
                        CSV
                      </button>
                      <button
                        type="button"
                        className="asura-act"
                        onClick={() =>
                          exportAsura(turn.result!.provenance.tool_id, "pdf", {}, turn.question).catch((e) =>
                            setTurns((prev) =>
                              prev.map((t, i) => (i === index ? { ...t, error: String(e.message ?? e) } : t)),
                            ),
                          )
                        }
                      >
                        <FileText size={14} />
                        PDF
                      </button>
                      <button
                        type="button"
                        className="asura-act"
                        onClick={() =>
                          exportAsura(turn.result!.provenance.tool_id, "xlsx", {}, turn.question).catch((e) =>
                            setTurns((prev) =>
                              prev.map((t, i) => (i === index ? { ...t, error: String(e.message ?? e) } : t)),
                            ),
                          )
                        }
                      >
                        <FileSpreadsheet size={14} />
                        Excel
                      </button>
                      <button type="button" className="asura-act" onClick={() => window.print()}>
                        <Printer size={14} />
                        Print
                      </button>
                    </div>
                  </>
                )}
              </div>
            </div>
          </div>
        ))}

        <div ref={bottom} />
      </div>

      <div className="asura-composer">
        <div className="asura-inputwrap">
          <div className="asura-inputbar">
            <label htmlFor="asura-q" className="sr-only" style={{ position: "absolute", width: 1, height: 1, overflow: "hidden", clip: "rect(0 0 0 0)" }}>
              Ask Asura
            </label>
            <textarea
              id="asura-q"
              rows={1}
              value={question}
              placeholder="Ask about collections, expenses, donors, budgets…"
              onChange={(e) => {
                setQuestion(e.target.value);
                e.target.style.height = "auto";
                e.target.style.height = `${Math.min(e.target.scrollHeight, 110)}px`;
              }}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  ask(question);
                }
              }}
            />
            <button
              type="button"
              className="asura-send"
              disabled={busy || question.trim().length === 0}
              onClick={() => ask(question)}
              aria-label="Send question"
            >
              {busy ? <Sparkles size={17} /> : <Send size={17} />}
            </button>
          </div>
        </div>
        <p className="asura-hint">
          <ShieldCheck size={11} />
          Read-only · every answer cites the tables it read · nothing is ever edited
        </p>
      </div>
      </div>
    </div>
  );
}
