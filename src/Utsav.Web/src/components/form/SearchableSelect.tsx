import { useMemo, useState } from "react";
import { Check, ChevronsUpDown, Plus, Search } from "lucide-react";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export interface SearchableOption {
  value: string;
  label: string;
  /** Small trailing caption, e.g. "2 tokens already" — informational, never blocks picking it again. */
  hint?: string;
}

interface Props {
  value: string | null;
  onChange: (value: string) => void;
  options: SearchableOption[];
  placeholder?: string;
  searchPlaceholder?: string;
  emptyText?: string;
  /** When provided, a query with no exact label match offers "Add '<query>'" as its own row. */
  onCreateNew?: (name: string) => void | Promise<void>;
  createLabel?: (query: string) => string;
  creating?: boolean;
  className?: string;
}

/**
 * Type-to-filter dropdown — Popover + plain buttons rather than Radix
 * Select, which has no built-in search surface. Used for the feast-token
 * donor picker, where a committee can have hundreds of names and scrolling
 * a plain list isn't workable.
 */
export function SearchableSelect({
  value, onChange, options, placeholder = "Select…", searchPlaceholder = "Search…",
  emptyText = "No matches", onCreateNew, createLabel, creating, className,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");

  const selected = options.find((o) => o.value === value);
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return options;
    return options.filter((o) => o.label.toLowerCase().includes(q));
  }, [options, query]);

  const exactMatch = options.some((o) => o.label.trim().toLowerCase() === query.trim().toLowerCase());

  function pick(v: string) {
    onChange(v);
    setOpen(false);
    setQuery("");
  }

  async function createNew() {
    if (!onCreateNew || !query.trim()) return;
    await onCreateNew(query.trim());
    setOpen(false);
    setQuery("");
  }

  return (
    <Popover open={open} onOpenChange={(v) => { setOpen(v); if (!v) setQuery(""); }}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          className={cn("w-full justify-between font-normal", !selected && "text-muted-foreground", className)}
        >
          <span className="truncate">{selected ? selected.label : placeholder}</span>
          <ChevronsUpDown className="h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-[--radix-popover-trigger-width] p-2" align="start">
        <div className="relative mb-2">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={searchPlaceholder}
            className="h-8 pl-8 text-sm"
            autoFocus
          />
        </div>
        <div className="max-h-56 overflow-y-auto">
          {filtered.length === 0 && !(onCreateNew && query.trim()) && (
            <p className="px-2 py-3 text-center text-xs text-muted-foreground">{emptyText}</p>
          )}
          {filtered.map((o) => (
            <button
              key={o.value}
              type="button"
              onClick={() => pick(o.value)}
              className={cn(
                "flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted",
                o.value === value && "bg-muted font-medium",
              )}
            >
              <span className="truncate">{o.label}</span>
              <span className="flex shrink-0 items-center gap-1.5">
                {o.hint && <span className="text-[11px] text-muted-foreground">{o.hint}</span>}
                {o.value === value && <Check className="h-3.5 w-3.5 text-primary" />}
              </span>
            </button>
          ))}
          {onCreateNew && query.trim() && !exactMatch && (
            <button
              type="button"
              onClick={createNew}
              disabled={creating}
              className="flex w-full items-center gap-1.5 rounded-md px-2 py-1.5 text-left text-sm text-primary hover:bg-primary/10 disabled:opacity-50"
            >
              <Plus className="h-3.5 w-3.5" />
              {creating ? "Adding…" : (createLabel ? createLabel(query.trim()) : `Add "${query.trim()}"`)}
            </button>
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}
