import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Legend, CartesianGrid } from "recharts";
import { moneyCompact } from "@/lib/format";
import { useChartColors, ChartTooltip } from "./chart-kit";

interface Datum {
  name: string;
  projected: number;
  billed: number;
  actual: number;
}

/**
 * Grouped bars per income stream (Membership / Special Donation /
 * Advertisements / Other Incomes): what
 * was asked for, what was actually billed, and what came in. Projected is
 * the ask before a bill goes out, billed is the bill itself, actual is
 * collected — three different moments in the same collection, not three
 * independent totals.
 */
export function ProjectedBilledActual({ data, max = 8 }: { data: Datum[]; max?: number }) {
  const c = useChartColors();
  const rows = [...data]
    .filter((d) => d.projected > 0 || d.billed > 0 || d.actual > 0)
    .sort((a, b) => Math.max(b.projected, b.billed, b.actual) - Math.max(a.projected, a.billed, a.actual))
    .slice(0, max);

  if (!rows.length) {
    return <p className="py-10 text-center text-sm text-muted-foreground">No collections recorded yet.</p>;
  }

  return (
    <ResponsiveContainer width="100%" height={Math.max(220, rows.length * 46)}>
      <BarChart data={rows} layout="vertical" margin={{ left: 8, right: 16, top: 4, bottom: 4 }} barGap={2}>
        <CartesianGrid horizontal={false} stroke={c.grid} />
        <XAxis type="number" tickFormatter={moneyCompact} tick={{ fontSize: 11, fill: c.axis }} axisLine={false} tickLine={false} />
        <YAxis type="category" dataKey="name" width={120} tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: c.axis }} />
        <Tooltip cursor={{ fill: c.grid }} content={<ChartTooltip />} />
        <Legend wrapperStyle={{ fontSize: 12, paddingTop: 8 }} iconType="circle" iconSize={9} />
        <Bar dataKey="projected" name="Projected" fill={c.prior} radius={[0, 4, 4, 0]} barSize={9} />
        <Bar dataKey="billed" name="Billed" fill={c.forecast} radius={[0, 4, 4, 0]} barSize={9} />
        <Bar dataKey="actual" name="Actual" fill={c.actual} radius={[0, 4, 4, 0]} barSize={9} />
      </BarChart>
    </ResponsiveContainer>
  );
}
