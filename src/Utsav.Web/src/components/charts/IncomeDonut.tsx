import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip } from "recharts";
import { money, moneyCompact, pct } from "@/lib/format";
import { useChartColors, ChartTooltip } from "./chart-kit";

interface Slice {
  name: string;
  value: number;
}

export function IncomeDonut({ data }: { data: Slice[] }) {
  const c = useChartColors();
  const total = data.reduce((s, d) => s + d.value, 0);
  // Colour is keyed to a stream's position in `data`, not its position after
  // the zero slices are dropped — otherwise a stream with nothing collected
  // yet shifts every following slice's colour out of step with the legend.
  const colourOf = (index: number) => c.categorical[index % c.categorical.length];
  const nonZero = data
    .map((d, index) => ({ ...d, colour: colourOf(index) }))
    .filter((d) => d.value > 0);
  const placeholder = [{ name: "None", value: 1, colour: c.grid }];
  const slices = nonZero.length ? nonZero : placeholder;

  return (
    <div className="flex flex-col items-center gap-4 sm:flex-row sm:gap-6">
      <div className="relative h-44 w-44 shrink-0">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={slices}
              dataKey="value"
              innerRadius={54}
              outerRadius={80}
              paddingAngle={nonZero.length > 1 ? 2 : 0}
              stroke="none"
            >
              {slices.map((s) => (
                <Cell key={s.name} fill={s.colour} />
              ))}
            </Pie>
            {nonZero.length > 0 && <Tooltip content={<ChartTooltip />} />}
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <span className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">Collected</span>
          <span className="text-lg font-bold tabular">{moneyCompact(total)}</span>
        </div>
      </div>

      <div className="w-full flex-1 space-y-2">
        {data.map((d, i) => (
          <div key={d.name} className="flex items-center gap-2.5">
            <span className="h-3 w-3 shrink-0 rounded-sm" style={{ background: colourOf(i) }} />
            <span className="text-sm text-foreground">{d.name}</span>
            <span className="ml-auto text-sm font-semibold tabular">{money(d.value)}</span>
            <span className="w-10 text-right text-xs text-muted-foreground tabular">{pct(d.value, total)}%</span>
          </div>
        ))}
      </div>
    </div>
  );
}
