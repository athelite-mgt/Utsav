import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Cell, ReferenceLine, LabelList } from "recharts";
import { moneyCompact } from "@/lib/format";
import { useChartColors, ChartTooltip } from "./chart-kit";

interface Datum {
  name: string;
  value: number;
}

/**
 * Target vs Actual Billed (plus Collected for context) — how the season's
 * ask is tracking against the goal. The target sits as a reference line so
 * "are we there yet" is a glance, not arithmetic. Bars are coloured by how
 * they relate to target: billed/collected go green once they reach it.
 */
export function TargetVsBilled({
  target, billed, collected,
}: { target: number; billed: number; collected: number }) {
  const c = useChartColors();

  const rows: Datum[] = [
    { name: "Billed", value: billed },
    { name: "Collected", value: collected },
  ];

  const hasTarget = target > 0;
  const top = Math.max(target, billed, collected, 1);

  return (
    <div>
      <ResponsiveContainer width="100%" height={240}>
        <BarChart data={rows} margin={{ left: 8, right: 16, top: 24, bottom: 4 }}>
          <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: c.axis }} />
          <YAxis hide domain={[0, Math.ceil(top * 1.1)]} />
          <Tooltip cursor={{ fill: c.grid }} content={<ChartTooltip />} />
          {hasTarget && (
            <ReferenceLine
              y={target}
              stroke={c.forecast}
              strokeDasharray="5 4"
              label={{ value: `Target ${moneyCompact(target)}`, position: "top", fill: c.forecast, fontSize: 12 }}
            />
          )}
          <Bar dataKey="value" radius={[6, 6, 0, 0]} barSize={64} name="Amount">
            {rows.map((r, i) => (
              <Cell key={i} fill={hasTarget && r.value >= target ? c.actual : c.single} />
            ))}
            <LabelList dataKey="value" position="insideTop" formatter={(v: number) => moneyCompact(v)}
              fill="#fff" fontSize={12} />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
      {!hasTarget && (
        <p className="mt-1 text-center text-xs text-muted-foreground">
          Set a target in Settings › Puja Year to see progress against goal.
        </p>
      )}
    </div>
  );
}
