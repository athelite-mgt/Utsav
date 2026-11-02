import { useState } from "react";
import { HandCoins, ReceiptText, Wallet, TrendingDown, Gift, Megaphone, WalletCards, UtensilsCrossed } from "lucide-react";
import { cn } from "@/lib/utils";
import { useYear } from "@/context/YearContext";
import {
  useSubscriptions, useAdBookings, useSpecialDonations, useFeasts, useFeastExpenses,
  useExpenses, useBudget, useOfferings, useMyAdvances, sum,
} from "@/hooks/useFinance";
import { money, moneyCompact, pct } from "@/lib/format";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { StatCard } from "@/components/common/StatCard";
import { CardsSkeleton } from "@/components/common/states";
import { Progress } from "@/components/ui/progress";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { FundPositionCard } from "@/components/finance/FundPositionCard";
import { IncomeDonut } from "@/components/charts/IncomeDonut";
import { ExpenseBars } from "@/components/charts/ExpenseBars";
import { BudgetBars } from "@/components/charts/BudgetBars";
import { ProjectedBilledActual } from "@/components/charts/ProjectedBilledActual";
import { TargetVsBilled } from "@/components/charts/TargetVsBilled";

export function Dashboard() {
  const [showCollectedBreakdown, setShowCollectedBreakdown] = useState(false);
  const { activeYearId, activeYear } = useYear();
  const subs = useSubscriptions(activeYearId);
  const ads = useAdBookings(activeYearId);
  const otherIncomes = useSpecialDonations(activeYearId);
  const expenses = useExpenses(activeYearId);
  const budget = useBudget(activeYearId);
  const offerings = useOfferings(activeYearId);
  const myAdvances = useMyAdvances(activeYearId);
  const feasts = useFeasts(activeYearId);
  const feastExpenses = useFeastExpenses(activeYearId);

  const loading = subs.isLoading || expenses.isLoading || budget.isLoading;

  // One request covers both sections; splitting here keeps the season totals
  // whole while still breaking the charts down the way the nav does.
  const allSubs = subs.data ?? [];
  const memberSubs = allSubs.filter((r) => r.category !== "special");
  const specialSubs = allSubs.filter((r) => r.category === "special");

  const memberColl = sum(memberSubs, (r) => r.collected);
  const specialColl = sum(specialSubs, (r) => r.collected);
  const adColl = sum(ads.data ?? [], (r) => r.collected);
  const otherColl = sum(otherIncomes.data ?? [], (r) => r.collected);
  const totalCollected = memberColl + specialColl + adColl + otherColl;

  const totalBilled =
    sum(subs.data ?? [], (r) => r.billed) +
    sum(ads.data ?? [], (r) => r.billed) +
    sum(otherIncomes.data ?? [], (r) => r.billed);
  const totalDue = totalBilled - totalCollected;

  const totalSpent = sum(expenses.data ?? [], (r) => r.amount);
  const balance = totalCollected - totalSpent;
  const target = activeYear?.target ?? 0;
  const progress = pct(totalCollected, target || totalBilled || 1);

  const totalForecast = sum(budget.data ?? [], (r) => r.forecast);
  const totalActual = sum(budget.data ?? [], (r) => r.actual);

  // Feast is its own pool of money — deliberately kept out of totalCollected/
  // totalBilled/balance above and out of the Fund Position card's cash-in-
  // hand/bank figures (see FundPositionEndpoint), so it needs its own section
  // rather than folding into any of those season-wide numbers.
  const feastBilled = sum(feasts.data ?? [], (r) => r.billed);
  const feastCollected = sum(feasts.data ?? [], (r) => r.collected);
  const feastDue = feastBilled - feastCollected;
  const feastSpent = sum(feastExpenses.data ?? [], (r) => r.amount);
  const feastNet = feastCollected - feastSpent;

  const incomeSlices = [
    { name: "Membership", value: memberColl },
    { name: "Special Donation", value: specialColl },
    { name: "Advertisements", value: adColl },
    { name: "Other Incomes", value: otherColl },
  ];

  const expenseByCat = Object.values(
    (expenses.data ?? []).reduce<Record<string, { name: string; value: number }>>((acc, e) => {
      const k = e.category ?? "Others";
      acc[k] = acc[k] || { name: k, value: 0 };
      acc[k].value += Number(e.amount) || 0;
      return acc;
    }, {})
  );

  const budgetData = (budget.data ?? []).map((b) => ({
    name: b.category,
    forecast: Number(b.forecast) || 0,
    actual: Number(b.actual) || 0,
    prior: Number(b.prior_actual) || 0,
  }));

  // Comparing what we projected against what actually came in is only fair
  // for people we've actually billed — someone not billed yet would show as
  // "100% behind" on their full projected figure for no real reason.
  const billedOnly = <T extends { billed: number }>(rows: T[]) => rows.filter((r) => (Number(r.billed) || 0) > 0);
  const streamTotals = (rows: Array<{ projected: number; billed: number; collected: number }>) => ({
    projected: sum(rows, (r) => r.projected),
    billed: sum(rows, (r) => r.billed),
    actual: sum(rows, (r) => r.collected),
  });

  const projectedVsBilledVsActual = [
    { name: "Membership", ...streamTotals(billedOnly(memberSubs)) },
    { name: "Special Donation", ...streamTotals(billedOnly(specialSubs)) },
    { name: "Advertisements", ...streamTotals(billedOnly(ads.data ?? [])) },
    { name: "Other Incomes", ...streamTotals(billedOnly(otherIncomes.data ?? [])) },
  ];
  const projectedTotal = sum(projectedVsBilledVsActual, (r) => r.projected);
  const actualOfBilled = sum(projectedVsBilledVsActual, (r) => r.actual);
  const projectionDelta = actualOfBilled - projectedTotal;

  return (
    <div className="space-y-6">
      {/* Season summary — quiet by design. This is a tool you scan, not a banner. */}
      <div className="rounded-lg border border-border bg-card p-5 sm:p-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <p className="text-sm text-muted-foreground">{activeYear?.name ?? "This season"}</p>
            <p className="mt-1 text-3xl font-semibold tabular tracking-tight sm:text-4xl">
              {money(totalCollected)}
            </p>
            <p className="mt-1 text-sm text-muted-foreground">
              collected of {money(target)} target
            </p>
          </div>
          <div className="text-left sm:text-right">
            <p className="text-2xl font-semibold tabular">{progress}%</p>
            <p className="text-sm text-muted-foreground">of target</p>
          </div>
        </div>
        <div className="mt-4 h-1.5 w-full overflow-hidden rounded-full bg-muted">
          <div
            className={cn("h-full rounded-full transition-all", progress >= 100 ? "bg-success" : "bg-primary")}
            style={{ width: `${Math.min(100, progress)}%` }}
          />
        </div>
      </div>

      {/* Only shows up for a signed-in member whose login is linked to a
          collector (Settings > Collectors) with cash still outstanding —
          everyone else never sees this, admin or not. */}
      {myAdvances.data && myAdvances.data.outstanding > 0.01 && (
        <div className="flex items-center gap-3 rounded-lg border border-gold/30 bg-gold/10 p-4">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-gold/15 text-gold">
            <WalletCards className="h-5 w-5" />
          </div>
          <div>
            <p className="font-medium">You have {money(myAdvances.data.outstanding)} outstanding</p>
            <p className="text-sm text-muted-foreground">
              From {myAdvances.data.count} cash advance{myAdvances.data.count === 1 ? "" : "s"} issued to you this year — settle with a bill or return it.
            </p>
          </div>
        </div>
      )}

      {loading ? (
        <CardsSkeleton />
      ) : (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <div className="animate-rise">
            <StatCard label="Total Collected" value={money(totalCollected)} icon={<HandCoins />} tone="success"
              sub={`${money(totalBilled)} billed · tap for breakdown`} onClick={() => setShowCollectedBreakdown(true)} />
          </div>
          <div className="animate-rise" style={{ animationDelay: "60ms" }}>
            <StatCard label="Total Spent" value={money(totalSpent)} icon={<ReceiptText />} tone="primary"
              sub={`${pct(totalSpent, totalForecast || 1)}% of budget`} />
          </div>
          <div className="animate-rise" style={{ animationDelay: "120ms" }}>
            <StatCard label="Balance in Hand" value={money(balance)} icon={<Wallet />}
              tone={balance >= 0 ? "gold" : "destructive"} sub="collected less spent" />
          </div>
          <div className="animate-rise" style={{ animationDelay: "180ms" }}>
            <StatCard label="Outstanding Dues" value={money(totalDue)} icon={<TrendingDown />}
              tone={totalDue > 0 ? "destructive" : "success"} sub={totalDue > 0 ? "to collect" : "all cleared"} />
          </div>
        </div>
      )}

      <FundPositionCard yearId={activeYearId} />

      {/* Feast is a separate pool of money from the committee's general fund
          — income AND expense, both kept entirely out of the season-wide
          totals and the Fund Position card above, so this is the one place
          that shows the feast's own complete picture: what it billed,
          collected, spent, and netted. */}
      <div className="space-y-2.5">
        <div className="flex items-center gap-2">
          <UtensilsCrossed className="h-4 w-4 text-muted-foreground" />
          <h3 className="font-display text-base font-semibold">Feast collection</h3>
          <Badge variant="outline" className="text-xs font-normal text-muted-foreground">
            Separate from the general fund
          </Badge>
        </div>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
          <StatCard label="Billed" value={money(feastBilled)} />
          <StatCard label="Collected" value={money(feastCollected)} tone="success" />
          <StatCard label="Due" value={money(feastDue)} tone={feastDue > 0 ? "destructive" : "success"} />
          <StatCard label="Expenses" value={money(feastSpent)} tone="destructive" />
          <StatCard label="Net" value={money(feastNet)} tone={feastNet >= 0 ? "success" : "destructive"} />
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader className="flex-row items-center justify-between">
            <CardTitle>Income by stream</CardTitle>
            <Badge variant="secondary">{incomeSlices.length} sources</Badge>
          </CardHeader>
          <CardContent>
            <IncomeDonut data={incomeSlices} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex-row items-center justify-between">
            <CardTitle>Top spending</CardTitle>
            <Badge variant="secondary">{expenseByCat.length} categories</Badge>
          </CardHeader>
          <CardContent>
            <ExpenseBars data={expenseByCat} />
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader className="flex-row items-center justify-between">
          <div>
            <CardTitle>Target vs actual billed</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              How the season's ask is tracking against the {money(target)} goal
            </p>
          </div>
          {target > 0 && (
            <Badge variant={totalBilled >= target ? "success" : "secondary"}>
              {pct(totalBilled, target)}% billed
            </Badge>
          )}
        </CardHeader>
        <CardContent>
          <TargetVsBilled target={target} billed={totalBilled} collected={totalCollected} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex-row items-center justify-between">
          <div>
            <CardTitle>Budget vs actual</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {money(totalActual)} spent against {money(totalForecast)} planned
            </p>
          </div>
          <Badge variant={totalActual > totalForecast ? "destructive" : "success"}>
            {totalActual > totalForecast ? "Over" : "Under"} by {moneyCompact(Math.abs(totalForecast - totalActual))}
          </Badge>
        </CardHeader>
        <CardContent>
          <BudgetBars data={budgetData} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="flex-row items-center justify-between">
          <div>
            <CardTitle>Projected vs billed vs actual</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              Of the people already billed: what was asked for vs what came in — by income stream
            </p>
          </div>
          {projectedTotal > 0 && (
            <Badge variant={projectionDelta >= 0 ? "success" : "destructive"}>
              {projectionDelta >= 0 ? "Ahead" : "Behind"} by {moneyCompact(Math.abs(projectionDelta))}
            </Badge>
          )}
        </CardHeader>
        <CardContent>
          <ProjectedBilledActual data={projectedVsBilledVsActual} />
        </CardContent>
      </Card>

      {/* Secondary tiles */}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StatCard label="Advertisers" value={ads.data?.length ?? 0} icon={<Megaphone />}
          sub={`${moneyCompact(adColl)} raised`} />
        <StatCard label="Special Donation" value={money(specialColl)} icon={<Gift />} />
        <StatCard label="Offerings" value={offerings.data?.length ?? 0} icon={<Gift />} tone="gold"
          sub={`${(offerings.data ?? []).filter((o) => o.status === "received").length} received`} />
        <StatCard label="Donor Bills" value={subs.data?.length ?? 0} icon={<HandCoins />}
          sub={`${(subs.data ?? []).filter((s) => s.due > 0).length} with dues`} />
      </div>

      <Dialog open={showCollectedBreakdown} onOpenChange={setShowCollectedBreakdown}>
        <DialogContent className="max-w-sm">
          <DialogHeader><DialogTitle>Where {money(totalCollected)} came from</DialogTitle></DialogHeader>
          <div className="space-y-2">
            {incomeSlices.map((s) => (
              <div key={s.name} className="flex items-center justify-between rounded-lg bg-muted/40 px-3 py-2 text-sm">
                <span className="text-muted-foreground">{s.name}</span>
                <span className="font-semibold tabular">{money(s.value)}</span>
              </div>
            ))}
            <div className="flex items-center justify-between border-t border-border px-3 pt-3 text-sm font-semibold">
              <span>Total</span>
              <span className="tabular">{money(totalCollected)}</span>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
