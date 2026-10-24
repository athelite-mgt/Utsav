import {
  BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Legend, CartesianGrid, Cell,
  PieChart, Pie,
} from "recharts";
import { moneyCompact, money, pct } from "@/lib/format";
import { useChartColors, ChartTooltip } from "./chart-kit";

/**
 * The reporting charts lean a little more "presentation" than the quiet
 * dashboard ones — vertical gradient fills and a soft drop shadow give bars
 * and slices a sense of depth (the "3D-ish" look), and everything grows in
 * with an ease-out curve rather than snapping. Colours still come from the
 * one CVD-safe palette in chart-kit, so depth never costs legibility.
 */

export interface Series {
  key: string;
  name: string;
  /** Index into the categorical palette; falls back to cycling. */
  colorIndex?: number;
}

/** A vertical linear gradient (lighter top → solid base) plus a reusable soft shadow. */
function ChartDefs({ colors }: { colors: string[] }) {
  return (
    <defs>
      {colors.map((c, i) => (
        <linearGradient key={i} id={`rg-${i}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={c} stopOpacity={0.95} />
          <stop offset="55%" stopColor={c} stopOpacity={1} />
          <stop offset="100%" stopColor={c} stopOpacity={0.72} />
        </linearGradient>
      ))}
      <filter id="rg-shadow" x="-20%" y="-20%" width="140%" height="140%">
        <feDropShadow dx="0" dy="2" stdDeviation="3" floodOpacity="0.18" />
      </filter>
    </defs>
  );
}

/**
 * Grouped vertical bars with one gradient per series. Used for the
 * projected/billed/collected style comparisons where each category (an income
 * stream, a collector, a zone) shows two or three measures side by side.
 */
export function GroupedGradientBars({
  data, series, height = 320,
}: {
  data: Array<Record<string, number | string>>;
  series: Series[];
  height?: number;
}) {
  const c = useChartColors();
  const palette = c.categorical;

  if (!data.length) {
    return <p className="py-12 text-center text-sm text-muted-foreground">Nothing to chart for this selection.</p>;
  }

  return (
    <ResponsiveContainer width="100%" height={height}>
      <BarChart data={data} margin={{ left: 4, right: 8, top: 12, bottom: 4 }} barGap={4} barCategoryGap="22%">
        <ChartDefs colors={palette} />
        <CartesianGrid vertical={false} stroke={c.grid} />
        <XAxis dataKey="name" tick={{ fontSize: 11, fill: c.axis }} axisLine={false} tickLine={false} interval={0}
          angle={data.length > 5 ? -18 : 0} textAnchor={data.length > 5 ? "end" : "middle"} height={data.length > 5 ? 48 : 24} />
        <YAxis tickFormatter={moneyCompact} tick={{ fontSize: 11, fill: c.axis }} axisLine={false} tickLine={false} width={54} />
        <Tooltip cursor={{ fill: c.grid }} content={<ChartTooltip />} />
        <Legend wrapperStyle={{ fontSize: 12, paddingTop: 10 }} iconType="circle" iconSize={9} />
        {series.map((s, i) => {
          const idx = (s.colorIndex ?? i) % palette.length;
          return (
            <Bar
              key={s.key} dataKey={s.key} name={s.name}
              fill={`url(#rg-${idx})`} radius={[5, 5, 0, 0]} maxBarSize={46}
              style={{ filter: "url(#rg-shadow)" }}
              isAnimationActive animationDuration={850} animationEasing="ease-out"
            />
          );
        })}
      </BarChart>
    </ResponsiveContainer>
  );
}

/**
 * Single-measure horizontal bars, one gradient colour per row — for
 * "who/what contributed how much" rankings (collector, zone, category) where
 * the category label needs room to breathe.
 */
export function RankedGradientBars({
  data, height,
}: {
  data: Array<{ name: string; value: number }>;
  height?: number;
}) {
  const c = useChartColors();
  const palette = c.categorical;
  const rows = [...data].filter((d) => d.value > 0).sort((a, b) => b.value - a.value);

  if (!rows.length) {
    return <p className="py-12 text-center text-sm text-muted-foreground">Nothing to chart for this selection.</p>;
  }

  return (
    <ResponsiveContainer width="100%" height={height ?? Math.max(200, rows.length * 44)}>
      <BarChart data={rows} layout="vertical" margin={{ left: 8, right: 20, top: 4, bottom: 4 }}>
        <ChartDefs colors={palette} />
        <CartesianGrid horizontal={false} stroke={c.grid} />
        <XAxis type="number" tickFormatter={moneyCompact} tick={{ fontSize: 11, fill: c.axis }} axisLine={false} tickLine={false} />
        <YAxis type="category" dataKey="name" width={130} tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: c.axis }} />
        <Tooltip cursor={{ fill: c.grid }} content={<ChartTooltip />} />
        <Bar dataKey="value" name="Amount" radius={[0, 6, 6, 0]} maxBarSize={30}
          style={{ filter: "url(#rg-shadow)" }} isAnimationActive animationDuration={850} animationEasing="ease-out">
          {rows.map((_, i) => <Cell key={i} fill={`url(#rg-${i % palette.length})`} />)}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

/** A donut with gradient slices, a soft shadow and a total in the middle. */
export function DimensionalDonut({
  data, centerLabel = "Total", centerValue,
}: {
  data: Array<{ name: string; value: number }>;
  centerLabel?: string;
  /** Overrides the centre figure when the ring's slice-sum isn't the number to show (e.g. an overspent balance). */
  centerValue?: number;
}) {
  const c = useChartColors();
  const palette = c.categorical;
  const total = centerValue ?? data.reduce((s, d) => s + d.value, 0);
  const colourOf = (i: number) => palette[i % palette.length];
  // Carry each slice's palette index from its position in the full data set,
  // so dropping zero-value slices never shifts a slice's colour out of step
  // with the legend beside it.
  const nonZero = data
    .map((d, i) => ({ ...d, gradientId: `dg-${i % palette.length}`, colour: colourOf(i) }))
    .filter((d) => d.value > 0);
  const slices = nonZero.length
    ? nonZero
    : [{ name: "None", value: 1, gradientId: "", colour: c.grid }];

  return (
    <div className="flex flex-col items-center gap-5 sm:flex-row sm:gap-7">
      <div className="relative h-48 w-48 shrink-0">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <defs>
              {palette.map((col, i) => (
                <linearGradient key={i} id={`dg-${i}`} x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stopColor={col} stopOpacity={1} />
                  <stop offset="100%" stopColor={col} stopOpacity={0.68} />
                </linearGradient>
              ))}
              <filter id="dg-shadow" x="-20%" y="-20%" width="140%" height="140%">
                <feDropShadow dx="0" dy="2" stdDeviation="4" floodOpacity="0.2" />
              </filter>
            </defs>
            <Pie
              data={slices} dataKey="value" innerRadius={58} outerRadius={86}
              paddingAngle={nonZero.length > 1 ? 2 : 0} stroke="none"
              style={{ filter: "url(#dg-shadow)" }}
              isAnimationActive animationDuration={800} animationEasing="ease-out"
            >
              {slices.map((s) => (
                <Cell key={s.name} fill={s.gradientId ? `url(#${s.gradientId})` : s.colour} />
              ))}
            </Pie>
            {nonZero.length > 0 && <Tooltip content={<ChartTooltip />} />}
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <span className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">{centerLabel}</span>
          <span className="text-lg font-bold tabular">{moneyCompact(total)}</span>
        </div>
      </div>

      <div className="w-full flex-1 space-y-2">
        {data.map((d, i) => (
          <div key={d.name} className="flex items-center gap-2.5">
            <span className="h-3 w-3 shrink-0 rounded-sm" style={{ background: colourOf(i) }} />
            <span className="truncate text-sm text-foreground">{d.name}</span>
            <span className="ml-auto text-sm font-semibold tabular">{money(d.value)}</span>
            <span className="w-10 text-right text-xs text-muted-foreground tabular">{pct(d.value, total)}%</span>
          </div>
        ))}
      </div>
    </div>
  );
}
