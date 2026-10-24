import { type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { Card } from "@/components/ui/card";

interface Props {
  label: string;
  value: ReactNode;
  sub?: ReactNode;
  icon?: ReactNode;
  tone?: "default" | "primary" | "gold" | "success" | "destructive";
  className?: string;
  /** When set, the whole card becomes a button — e.g. a total that can drill into what it's made of. */
  onClick?: () => void;
}

const toneRing: Record<NonNullable<Props["tone"]>, string> = {
  default: "bg-muted text-muted-foreground",
  primary: "bg-primary/10 text-primary",
  gold: "bg-gold/12 text-gold",
  success: "bg-success/12 text-success",
  destructive: "bg-destructive/12 text-destructive",
};

export function StatCard({ label, value, sub, icon, tone = "default", className, onClick }: Props) {
  return (
    <Card
      className={cn(
        "p-4 sm:p-5",
        onClick && "cursor-pointer transition-colors hover:border-primary/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        className
      )}
      onClick={onClick}
      role={onClick ? "button" : undefined}
      tabIndex={onClick ? 0 : undefined}
      onKeyDown={onClick ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onClick(); } } : undefined}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="truncate text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {label}
          </p>
          <p className="mt-1.5 truncate text-xl font-bold tabular tracking-tight sm:text-2xl">
            {value}
          </p>
          {sub && <p className="mt-1 truncate text-xs text-muted-foreground">{sub}</p>}
        </div>
        {icon && (
          <div
            className={cn(
              "flex h-10 w-10 shrink-0 items-center justify-center rounded-xl [&_svg]:size-5",
              toneRing[tone]
            )}
          >
            {icon}
          </div>
        )}
      </div>
    </Card>
  );
}
