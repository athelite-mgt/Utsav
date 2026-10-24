import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Cell } from "recharts";
import { moneyCompact } from "@/lib/format";
import { useChartColors, ChartTooltip } from "./chart-kit";

interface Datum {
  name: string;
  value: number;
}

/** Ranked horizontal bars — single-series magnitude (top N categories). */
export function ExpenseBars({ data, max = 8 }: { data: Datum[]; max?: number }) {
  const c = useChartColors();
  const rows = [...data].filter((d) => d.value > 0).sort((a, b) => b.value - a.value).slice(0, max);

  if (!rows.length) {
    return <p className="py-10 text-center text-sm text-muted-foreground">No spending recorded yet.</p>;
  }

  return (
    <ResponsiveContainer width="100%" height={Math.max(180, rows.length * 40)}>
      <BarChart data={rows} layout="vertical" margin={{ left: 8, right: 16, top: 4, bottom: 4 }}>
        <XAxis type="number" hide />
        <YAxis
          type="category"
          dataKey="name"
          width={120}
          tickLine={false}
          axisLine={false}
          tick={{ fontSize: 12, fill: c.axis }}
        />
        <Tooltip cursor={{ fill: c.grid }} content={<ChartTooltip />} />
        <Bar dataKey="value" radius={[4, 4, 4, 4]} barSize={18} name="Spent">
          {rows.map((_, i) => (
            <Cell key={i} fill={c.single} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

export { moneyCompact };
