import { useState } from "react";
import { SlidersHorizontal, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Popover, PopoverTrigger, PopoverContent } from "@/components/ui/popover";

/** The one <select>/<input> style every filter panel uses, so a page doesn't restate it per field. */
export const FILTER_INPUT_CLS = "h-9 w-full rounded-lg border border-input bg-background px-2.5 text-sm";

/**
 * The one filters affordance every list page reaches for: a single button
 * that opens a panel holding every filter beyond the always-visible search
 * box, instead of a row of <select>s that wraps unpredictably on narrow
 * screens and eats vertical space on every page whether or not anyone is
 * using it. The badge is the only thing that has to be visible for a filter
 * to be "on" — the panel itself is closed by default.
 */
export function FilterMenu({
  activeCount, onClear, children, label = "Filters",
}: {
  /** How many filters inside this panel are currently non-default. Drives the badge and whether "Clear" shows. */
  activeCount: number;
  /** Resets every filter in this panel back to its default. Omit if the panel has nothing worth a bulk reset (e.g. a single field). */
  onClear?: () => void;
  children: React.ReactNode;
  label?: string;
}) {
  // Controlled rather than left to Radix's own uncontrolled open state, so
  // the "Done" button below has something to call — without this there was
  // no way to explicitly close the panel short of clicking outside it.
  const [open, setOpen] = useState(false);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button variant="outline" className="relative gap-1.5">
          <SlidersHorizontal className="h-4 w-4" />
          {label}
          {activeCount > 0 && (
            <span className="ml-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold text-primary-foreground">
              {activeCount}
            </span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent>
        <div className="flex items-center justify-between pb-3">
          <p className="text-sm font-semibold">{label}</p>
          {onClear && activeCount > 0 && (
            <button
              type="button"
              onClick={onClear}
              className="flex items-center gap-1 text-xs font-medium text-muted-foreground hover:text-foreground"
            >
              <X className="h-3 w-3" /> Clear
            </button>
          )}
        </div>
        <div className="space-y-3">{children}</div>
        <Button type="button" size="sm" className="mt-4 w-full" onClick={() => setOpen(false)}>
          Done
        </Button>
      </PopoverContent>
    </Popover>
  );
}

/** One labelled control inside a FilterMenu panel — consistent label sizing/spacing so every page's panel reads the same. */
export function FilterField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <label className="text-xs font-medium text-muted-foreground">{label}</label>
      {children}
    </div>
  );
}
