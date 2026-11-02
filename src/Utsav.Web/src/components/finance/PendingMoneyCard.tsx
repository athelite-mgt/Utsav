import { Link } from "react-router-dom";
import { Hourglass } from "lucide-react";
import { useFundPosition } from "@/hooks/useFinance";
import { useAuth } from "@/context/AuthContext";
import { money } from "@/lib/format";
import { Card } from "@/components/ui/card";

/**
 * Money that has been recorded but is NOT in Cash or Bank yet: cash members are
 * holding, online payments waiting to be seen in the account, claims awaiting a
 * decision, and approved own-pocket claims the committee owes back. Hidden when
 * there is none, so a committee that keeps up sees nothing here.
 */
export function PendingMoneyCard({ yearId }: { yearId?: string | null }) {
  const { data } = useFundPosition(yearId);
  const { can } = useAuth();
  const p = data?.pending;
  if (!p) return null;

  const rows = [
    { label: "Held by members", sub: p.heldByMembers.map((h) => `${h.name} ${money(h.amount)}`).join(" · "), amount: p.heldTotal },
    { label: "Online, awaiting confirmation", sub: "Seen in the bank once Finance confirms", amount: p.onlinePending },
    { label: "Expenses awaiting approval", sub: p.pendingExpenses ? `${p.pendingExpenses} claim${p.pendingExpenses === 1 ? "" : "s"}` : "", amount: p.pendingExpensesTotal },
    { label: "Owed to members", sub: p.owedToMembers.map((h) => `${h.name} ${money(h.amount)}`).join(" · "), amount: p.owedTotal },
  ].filter((r) => r.amount > 0);

  if (rows.length === 0) return null;

  return (
    <Card className="space-y-3 border-amber-500/40 p-4 sm:p-5">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Hourglass className="h-4 w-4 text-amber-600" />
          <h3 className="font-display text-base font-semibold">Not in the fund yet</h3>
        </div>
        <Link to="/approvals" className="text-xs font-medium text-primary hover:underline">
          {can("approve_money") ? "Review" : "See yours"}
        </Link>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        {rows.map((r) => (
          <div key={r.label} className="rounded-lg bg-muted/50 px-3 py-2">
            <div className="flex items-baseline justify-between gap-3">
              <p className="text-sm font-medium">{r.label}</p>
              <p className="font-semibold tabular">{money(r.amount)}</p>
            </div>
            {r.sub && <p className="mt-0.5 text-xs text-muted-foreground">{r.sub}</p>}
          </div>
        ))}
      </div>
    </Card>
  );
}
