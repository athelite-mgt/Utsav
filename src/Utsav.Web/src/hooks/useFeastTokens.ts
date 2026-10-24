import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { api, getToken, qs } from "@/lib/api";
import type { FeastToken, FeastTokenSummary } from "@/types/db";

const KEY = "feast_tokens";

export const useFeastTokens = (yearId?: string | null, status?: string | null) =>
  useQuery({
    queryKey: [KEY, yearId, status ?? null],
    enabled: Boolean(yearId),
    queryFn: () => api.get<FeastToken[]>(`/feast_tokens${qs({ year_id: yearId, status })}`),
  });

export const useFeastTokenSummary = (yearId?: string | null) =>
  useQuery({
    queryKey: [KEY, "summary", yearId],
    enabled: Boolean(yearId),
    queryFn: () => api.get<FeastTokenSummary>(`/feast_tokens/summary${qs({ year_id: yearId })}`),
  });

/**
 * Downloads the printed sheet directly — same blob-download pattern as
 * asuraStream.exportAsura — and returns the tokens the server actually
 * created, so the caller can refresh its lists without waiting on a second
 * round trip. Not a useMutation: the response here is a PDF file, not JSON,
 * so there's no single shared return shape to cache against.
 */
export async function printFeastTokens(yearId: string, count: number): Promise<void> {
  const token = getToken();
  const response = await fetch("/api/feast_tokens/print", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: JSON.stringify({ year_id: yearId, count }),
  });

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { error?: string };
    throw new Error(body.error ?? `Print failed (${response.status}).`);
  }

  const disposition = response.headers.get("Content-Disposition") ?? "";
  const match = /filename="?([^";]+)"?/.exec(disposition);
  const filename = match?.[1] ?? `feast-tokens-${count}.pdf`;

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

function useFeastTokenAction<TBody extends object>(path: string, successMessage: (r: FeastToken) => string) {
  const qc = useQueryClient();
  return useMutation<FeastToken, Error, TBody>({
    mutationFn: (body) => api.post<FeastToken>(`/feast_tokens/${path}`, body),
    onSuccess: (token) => {
      qc.invalidateQueries({ queryKey: [KEY] });
      toast.success(successMessage(token));
    },
    onError: (e) => toast.error(e.message),
  });
}

export const useAssignFeastToken = () =>
  useFeastTokenAction<{ code: string; feast_id: string }>("assign", (t) => `${t.code} assigned`);

export const useVoidFeastToken = () =>
  useFeastTokenAction<{ code: string }>("void", (t) => `${t.code} voided`);

/**
 * Redeem is called from the gate-scanning page, which wants to handle its
 * own success/error feedback (big donor-name banner, not a toast) — so this
 * one skips the default toast rather than fighting it.
 */
export const useRedeemFeastToken = () => {
  const qc = useQueryClient();
  return useMutation<FeastToken, Error, { code: string }>({
    mutationFn: (body) => api.post<FeastToken>("/feast_tokens/redeem", body),
    onSuccess: () => qc.invalidateQueries({ queryKey: [KEY] }),
  });
};

/**
 * Read-only — a scan or a typed code just "gets" the token's current state
 * so the gate volunteer can see who it belongs to. Nothing is consumed until
 * {@link useRedeemFeastToken} is called from an explicit confirm tap.
 */
export const lookupFeastToken = (code: string) =>
  api.get<FeastToken>(`/feast_tokens/lookup/${encodeURIComponent(code)}`);
