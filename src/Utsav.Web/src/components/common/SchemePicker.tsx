import { Check } from "lucide-react";
import { SCHEMES, useTheme, type Scheme } from "@/context/ThemeProvider";
import { cn } from "@/lib/utils";

// Static previews of each scheme's light-mode chrome — these can't read the
// live CSS variables (only the *active* scheme's tokens are ever mounted at
// once), so the anchor/accent hues are duplicated here in literal HSL,
// mirrored from index.css. Update both places together if a palette moves.
const PREVIEW: Record<Scheme, { anchor: string; accent: string; ground: string }> = {
  blue: { anchor: "hsl(220 75% 45%)", accent: "hsl(40 90% 45%)", ground: "hsl(210 25% 97%)" },
  marigold: { anchor: "hsl(175 70% 18%)", accent: "hsl(38 85% 45%)", ground: "hsl(40 33% 96%)" },
  indigo: { anchor: "hsl(228 45% 29%)", accent: "hsl(28 60% 50%)", ground: "hsl(220 20% 97%)" },
  terracotta: { anchor: "hsl(15 55% 34%)", accent: "hsl(90 30% 42%)", ground: "hsl(40 38% 96%)" },
};

export function SchemePicker({ compact = false }: { compact?: boolean }) {
  const { scheme, setScheme } = useTheme();

  return (
    <div className={cn("grid gap-2", compact ? "grid-cols-4" : "grid-cols-2 sm:grid-cols-4")}>
      {SCHEMES.map((s) => {
        const p = PREVIEW[s.value];
        const active = scheme === s.value;
        return (
          <button
            key={s.value}
            type="button"
            onClick={() => setScheme(s.value)}
            className={cn(
              "flex items-center gap-2.5 rounded-xl border p-2.5 text-left transition-colors",
              active ? "border-primary ring-1 ring-primary" : "border-border hover:bg-accent"
            )}
          >
            <span
              className="relative flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-black/10"
              style={{ background: p.ground }}
            >
              <span className="absolute left-0.5 top-0.5 h-4 w-4 rounded-full" style={{ background: p.anchor }} />
              <span className="absolute bottom-0.5 right-0.5 h-3 w-3 rounded-full" style={{ background: p.accent }} />
              {active && (
                <span className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full bg-primary text-primary-foreground">
                  <Check className="h-2.5 w-2.5" strokeWidth={3} />
                </span>
              )}
            </span>
            {!compact && (
              <span className="min-w-0">
                <span className="block truncate text-sm font-medium">{s.label}</span>
                <span className="block truncate text-xs text-muted-foreground">{s.description}</span>
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
