import { CalendarDays } from "lucide-react";
import { useYear } from "@/context/YearContext";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

export function YearSwitcher() {
  const { years, activeYearId, setActiveYearId } = useYear();
  if (!years.length) return null;

  return (
    <Select value={activeYearId ?? undefined} onValueChange={setActiveYearId}>
      <SelectTrigger className="h-9 w-auto gap-2 border-border/70 bg-secondary/60 px-3 text-sm font-medium">
        <CalendarDays className="h-4 w-4 text-primary" />
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {years.map((y) => (
          <SelectItem key={y.id} value={y.id}>
            {y.name ?? `Puja ${y.year}`}
            {y.is_active ? "  ●" : ""}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
