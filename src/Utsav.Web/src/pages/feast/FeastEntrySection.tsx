import { useEffect, useMemo, useRef, useState } from "react";
import { CheckCircle2, XCircle, KeyRound, ScanLine, UserCheck, Loader2 } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { useFeastTokens, useRedeemFeastToken, lookupFeastToken } from "@/hooks/useFeastTokens";
import { QrScanner } from "@/components/feast/QrScanner";
import { PageHeader } from "@/components/common/PageHeader";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent } from "@/components/ui/dialog";
import type { FeastToken } from "@/types/db";

type Result =
  | { kind: "success"; code: string; donorName: string }
  | { kind: "error"; code: string; message: string };

const RESULT_VISIBLE_MS = 3500;

function donorLabel(t: FeastToken) {
  return `${t.donor_name ?? ""} ${t.donor_surname ?? ""}`.trim() || t.code;
}

/**
 * The gate-facing screen: continuous camera scan or manual entry only ever
 * *looks up* a code — it shows who the token belongs to and waits for an
 * explicit "Confirm entry" tap before anything is actually redeemed. This is
 * deliberate: a misread QR, the camera catching the same coupon twice, or a
 * volunteer brushing past a token in someone's hand must never silently
 * admit the wrong person. Assignment and voiding live on the Feast Tokens
 * management page; this screen only ever does the one thing, redeem.
 *
 * The preview/result surfaces as a modal rather than inline — it sits on
 * top of the scanner regardless of scroll position, so the full-size camera
 * view doesn't push the confirm button off screen on a phone.
 */
export function FeastEntrySection() {
  const { activeYearId } = useYear();
  const redeem = useRedeemFeastToken();
  const [preview, setPreview] = useState<FeastToken | null>(null);
  const [lookingUp, setLookingUp] = useState(false);
  const [result, setResult] = useState<Result | null>(null);
  const [manualCode, setManualCode] = useState("");
  const timeoutRef = useRef<ReturnType<typeof setTimeout>>();

  const { data: redeemedToday = [] } = useFeastTokens(activeYearId, "redeemed");
  const todayCount = useMemo(() => {
    const today = new Date().toISOString().slice(0, 10);
    return redeemedToday.filter((t) => t.redeemed_at?.slice(0, 10) === today).length;
  }, [redeemedToday]);

  useEffect(() => () => clearTimeout(timeoutRef.current), []);

  function showResult(next: Result) {
    setResult(next);
    timeoutRef.current = setTimeout(() => setResult(null), RESULT_VISIBLE_MS);
  }

  /** Scan or manual submit both land here — a read-only "get", never a redeem. */
  async function handleCode(raw: string) {
    const code = raw.trim().toUpperCase();
    if (!code || lookingUp || redeem.isPending) return;
    clearTimeout(timeoutRef.current);
    setResult(null);
    setLookingUp(true);
    try {
      const token = await lookupFeastToken(code);
      if (token.status === "assigned") {
        setPreview(token);
      } else {
        const reason =
          token.status === "redeemed" ? "Already redeemed"
          : token.status === "void" ? `Token ${code} has been voided`
          : `Token ${code} has not been assigned to a donor yet`;
        showResult({ kind: "error", code, message: reason });
      }
    } catch (e) {
      showResult({ kind: "error", code, message: (e as Error).message });
    } finally {
      setLookingUp(false);
    }
  }

  async function confirmEntry() {
    if (!preview) return;
    const code = preview.code;
    try {
      const token = await redeem.mutateAsync({ code });
      showResult({ kind: "success", code, donorName: donorLabel(token) });
    } catch (e) {
      showResult({ kind: "error", code, message: (e as Error).message });
    } finally {
      setPreview(null);
    }
  }

  function handleManualSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!manualCode.trim()) return;
    void handleCode(manualCode);
    setManualCode("");
  }

  const busy = lookingUp || redeem.isPending;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Feast Entry"
        description="Scan or type a token code, then confirm to admit"
        icon={<ScanLine />}
        actions={
          <div className="rounded-full bg-success/10 px-4 py-1.5 text-sm font-semibold text-success">
            {todayCount} redeemed today
          </div>
        }
      />

      <div className="space-y-4">
        <QrScanner
          onDecode={handleCode}
          paused={busy || Boolean(preview) || Boolean(result)}
          className="aspect-square w-full lg:aspect-video"
        />

        <Card className="p-4">
          <form onSubmit={handleManualSubmit} className="flex items-center gap-2">
            <KeyRound className="h-4 w-4 shrink-0 text-muted-foreground" />
            <Input
              value={manualCode}
              onChange={(e) => setManualCode(e.target.value)}
              placeholder="Type code manually, e.g. F2026-0001"
              className="flex-1"
              disabled={busy}
            />
            <Button type="submit" disabled={!manualCode.trim() || busy}>
              {lookingUp ? "Checking…" : "Look up"}
            </Button>
          </form>
        </Card>
      </div>

      {/* Confirm step — a modal so it's always visible, centred on screen,
          no matter how tall the scanner above it is or how far scrolled. */}
      <Dialog open={Boolean(preview)} onOpenChange={(v) => !v && setPreview(null)}>
        <DialogContent className="max-w-sm">
          <div className="flex flex-col items-center gap-3 py-2 text-center">
            <UserCheck className="h-14 w-14 text-gold" />
            <p className="text-2xl font-bold">{preview && donorLabel(preview)}</p>
            <p className="text-sm text-muted-foreground">
              {preview?.code} <Badge variant="gold">Assigned, not yet redeemed</Badge>
            </p>
            <div className="flex gap-2 pt-2">
              <Button variant="outline" onClick={() => setPreview(null)} disabled={redeem.isPending}>
                Cancel
              </Button>
              <Button onClick={confirmEntry} disabled={redeem.isPending}>
                {redeem.isPending ? <><Loader2 className="h-4 w-4 animate-spin" /> Admitting…</> : "Confirm entry"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={Boolean(result)} onOpenChange={(v) => !v && setResult(null)}>
        <DialogContent className="max-w-sm">
          <div className="flex flex-col items-center gap-3 py-2 text-center">
            {result?.kind === "success" ? (
              <>
                <CheckCircle2 className="h-14 w-14 text-success" />
                <p className="text-2xl font-bold">{result.donorName}</p>
                <p className="text-sm text-muted-foreground">{result.code} admitted</p>
              </>
            ) : (
              <>
                <XCircle className="h-14 w-14 text-destructive" />
                <p className="text-xl font-bold text-destructive">{result?.message}</p>
                <p className="text-sm text-muted-foreground">{result?.code}</p>
              </>
            )}
            <Button variant="outline" size="sm" onClick={() => setResult(null)}>
              Scan next
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
