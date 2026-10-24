import { authHeaders } from "@/lib/api";

export interface AsuraTile {
  key: string;
  value: string;
  detail: string | null;
  tone: string | null;
}

export interface AsuraChart {
  kind: "bar" | "line" | "none";
  label_column: string;
  value_columns: string[];
  rows: Record<string, unknown>[];
}

export interface AsuraDrill {
  tool_id: string;
  param_name: string;
  from_column: string;
  label: string;
}

export interface AsuraProvenance {
  tool_id: string;
  title: string;
  tables: string[];
  row_count: number;
  truncated: boolean;
  query_ms: number;
  sql: string;
}

export interface AsuraResult {
  title: string;
  tiles: AsuraTile[];
  chart: AsuraChart | null;
  columns: string[];
  rows: Record<string, unknown>[];
  drill: AsuraDrill | null;
  provenance: AsuraProvenance;
}

export type AsuraEvent =
  | { type: "step"; text: string; done: boolean }
  | { type: "token"; text: string }
  | ({ type: "result" } & AsuraResult)
  | { type: "error"; message: string; recoverable: boolean; detail?: string | null }
  | { type: "done"; model: string; provider: string; tier: number; total_ms: number };

/**
 * Streams an answer from Asura.
 *
 * `EventSource` is GET-only, and the question has to travel in a request body:
 * it can name a donor, and a query string lands in access logs and browser
 * history. So this reads the `text/event-stream` response off a POST with
 * fetch + ReadableStream instead, which is also what lets it carry the bearer
 * token and be aborted cleanly.
 */
export async function askAsura(
  question: string,
  onEvent: (e: AsuraEvent) => void,
  signal: AbortSignal,
): Promise<void> {
  const response = await fetch("/api/asura/ask", {
    method: "POST",
    signal,
    headers: {
      "Content-Type": "application/json",
      ...authHeaders("POST"),
    },
    body: JSON.stringify({ question }),
  });

  if (!response.ok || !response.body) {
    // A non-stream failure (401, 429, feature disabled) arrives as plain JSON.
    let message = `Asura is unavailable (${response.status}).`;
    try {
      const body = (await response.json()) as { error?: string };
      if (body.error) message = body.error;
    } catch {
      /* keep the status-based message */
    }
    onEvent({ type: "error", message, recoverable: response.status !== 404 });
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // Frames are separated by a blank line. Anything after the last separator
    // is an incomplete frame and stays in the buffer for the next chunk —
    // without this, a frame split across two network reads is lost.
    let boundary = buffer.indexOf("\n\n");
    while (boundary !== -1) {
      const frame = buffer.slice(0, boundary);
      buffer = buffer.slice(boundary + 2);
      boundary = buffer.indexOf("\n\n");

      const dataLine = frame.split("\n").find((l) => l.startsWith("data:"));
      if (!dataLine) continue;

      try {
        // The payload carries its own `type`, so the `event:` line is
        // redundant here and deliberately not parsed twice.
        onEvent(JSON.parse(dataLine.slice(5).trim()) as AsuraEvent);
      } catch {
        /* a malformed frame must not kill the rest of the stream */
      }
    }
  }
}

/** Runs one catalogued report directly — the drill-down path, no model involved. */
export async function drillAsura(
  toolId: string,
  params: Record<string, string>,
): Promise<{
  title: string;
  columns: string[];
  rows: Record<string, unknown>[];
  truncated: boolean;
  chart: string;
  label_column: string | null;
  value_columns: string[];
  drill: { tool_id: string; param_name: string; from_column: string } | null;
}> {
  const response = await fetch("/api/asura/drill", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...authHeaders("POST"),
    },
    // tool_id, not toolId: the API binds request bodies with the same
    // SnakeCaseLower policy it serializes responses with (Program.cs), and
    // ASP.NET's JSON binder matches property names literally rather than
    // case-insensitively across naming conventions — "toolId" simply never
    // matches an expected "tool_id", so ToolId silently bound to null and
    // every drill-down 400'd as "Unknown report." The sibling askAsura()
    // call next door was never caught by this: its only field, "question",
    // happens to be identical under snake_case and camelCase, which is
    // exactly why the bug hid here instead of there.
    body: JSON.stringify({ tool_id: toolId, params }),
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `Couldn't open that detail (${response.status}).`);
  }

  return response.json();
}

/**
 * Downloads a report as PDF or Excel. The server re-runs the report and
 * renders it, so the file always matches the database rather than whatever
 * this tab happens to be showing.
 */
export async function exportAsura(
  toolId: string,
  format: "pdf" | "xlsx",
  params: Record<string, string>,
  question: string,
): Promise<void> {
  const response = await fetch("/api/asura/export", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...authHeaders("POST"),
    },
    // tool_id — see the matching note in drillAsura above. This is the exact
    // bug behind "PDF and Excel download is not working": every export POST
    // bound ToolId to null server-side and came back 400.
    body: JSON.stringify({ tool_id: toolId, format, params, question }),
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `Export failed (${response.status}).`);
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `${toolId}.${format}`;
  link.click();
  // Revoked after the click so the browser has taken the blob, but not left
  // dangling -- an un-revoked object URL holds the whole file in memory for
  // the life of the document.
  URL.revokeObjectURL(url);
}
