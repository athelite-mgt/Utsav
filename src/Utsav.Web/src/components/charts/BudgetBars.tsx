import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Legend, CartesianGrid } from "recharts";
import { moneyCompact } from "@/lib/format";
import { useChartColors, ChartTooltip } from "./chart-kit";

interface Datum {
  name: string;
  forecast: number;
  actual: number;
  /** Last year's actual for this category — omit to fall back to the plain 2-series chart. */
  prior?: number;
}

/** Grouped bars: last year / this year's budget / this year's actual (legend present). */
export function BudgetBars({ data, max = 8 }: { data: Datum[]; max?: number }) {
  const c = useChartColors();
  const showPrior = data.some((d) => (d.prior ?? 0) > 0);
  const rows = [...data]
    .filter((d) => d.forecast > 0 || d.actual > 0 || (d.prior ?? 0) > 0)
    .sort((a, b) => Math.max(b.forecast, b.actual, b.prior ?? 0) - Math.max(a.forecast, a.actual, a.prior ?? 0))
    .slice(0, max);

  if (!rows.length) {
    return <p className="py-10 text-center text-sm text-muted-foreground">No budget lines yet.</p>;
  }

  return (
    <ResponsiveContainer width="100%" height={Math.max(220, rows.length * 46)}>
      <BarChart data={rows} layout="vertical" margin={{ left: 8, right: 16, top: 4, bottom: 4 }} barGap={2}>
        <CartesianGrid horizontal={false} stroke={c.grid} />
        <XAxis type="number" tickFormatter={moneyCompact} tick={{ fontSize: 11, fill: c.axis }} axisLine={false} tickLine={false} />
        <YAxis type="category" dataKey="name" width={120} tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: c.axis }} />
        <Tooltip cursor={{ fill: c.grid }} content={<ChartTooltip />} />
        <Legend wrapperStyle={{ fontSize: 12, paddingTop: 8 }} iconType="circle" iconSize={9} />
        {showPrior && <Bar dataKey="prior" name="Last year" fill={c.prior} radius={[0, 4, 4, 0]} barSize={9} />}
        <Bar dataKey="forecast" name="Budget" fill={c.forecast} radius={[0, 4, 4, 0]} barSize={9} />
        <Bar dataKey="actual" name="Actual" fill={c.actual} radius={[0, 4, 4, 0]} barSize={9} />
      </BarChart>
    </ResponsiveContainer>
  );
}
