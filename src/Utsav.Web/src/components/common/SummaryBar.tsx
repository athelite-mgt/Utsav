import { money } from "@/lib/format";
import { cn } from "@/lib/utils";

interface Item {
  label: string;
  value: number | string;
  tone?: "default" | "success" | "destructive" | "primary";
  isMoney?: boolean;
  /** Small caption under the value — e.g. flagging a figure that's a fallback rather than what was actually entered this year. */
  hint?: string;
}

const tones: Record<string, string> = {
  default: "text-foreground",
  success: "text-success",
  destructive: "text-destructive",
  primary: "text-primary",
};

export function SummaryBar({ items }: { items: Item[] }) {
  return (
    <div className="grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-border bg-border sm:grid-cols-4">
      {items.map((it) => (
        <div key={it.label} className="bg-card px-4 py-3">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{it.label}</p>
          <p className={cn("mt-1 text-lg font-bold tabular", tones[it.tone ?? "default"])}>
            {it.isMoney === false ? it.value : money(Number(it.value))}
          </p>
          {it.hint && <p className="mt-0.5 truncate text-[11px] text-muted-foreground">{it.hint}</p>}
        </div>
      ))}
    </div>
  );
}
