import { useTheme } from "@/context/ThemeProvider";
import { money } from "@/lib/format";

/** Validated (CVD-safe) chart palettes — see dataviz validator runs. */
export function useChartColors() {
  const { theme } = useTheme();
  const dark = theme === "dark";
  return {
    // categorical: teal / marigold / slate-blue / plum. Separated by lightness
    // as well as hue, so the set survives deuteranopia and protanopia.
    categorical: dark
      ? ["#2FA396", "#E8A317", "#7A93E8", "#C77BAE"]
      : ["#136F66", "#D18700", "#3A5FCD", "#9B4779"],
    // 2-series budget vs actual: blue (forecast) + marigold (actual).
    // Blue/orange is the most robust two-colour pair across all CVD types.
    forecast: dark ? "#7A93E8" : "#3A5FCD",
    actual: dark ? "#E8A317" : "#D18700",
    // third series: last year's actual — deliberately muted/neutral so it
    // reads as context behind forecast/actual, not a third thing competing
    // for attention.
    prior: dark ? "#7C8A88" : "#A3AEAC",
    // single-series magnitude — the brand teal. Deliberately NOT red:
    // red means "money owed" / "over budget" everywhere in this app.
    single: dark ? "#2FA396" : "#136F66",
    grid: dark ? "rgba(255,255,255,0.07)" : "rgba(30,40,38,0.08)",
    axis: dark ? "#9BA5A3" : "#6B7472",
    surface: dark ? "#1B1F21" : "#ffffff",
  };
}

/** Shared tooltip styled to match the app surface. */
export function ChartTooltip({ active, payload, label }: any) {
  if (!active || !payload?.length) return null;
  return (
    <div className="rounded-lg border border-border bg-popover px-3 py-2 text-xs shadow-card">
      {label && <p className="mb-1 font-semibold text-foreground">{label}</p>}
      {payload.map((p: any, i: number) => (
        <div key={i} className="flex items-center gap-2 py-0.5">
          <span className="h-2.5 w-2.5 rounded-sm" style={{ background: p.color || p.payload?.fill }} />
          <span className="text-muted-foreground">{p.name}</span>
          <span className="ml-auto font-semibold tabular text-foreground">{money(p.value)}</span>
        </div>
      ))}
    </div>
  );
}
