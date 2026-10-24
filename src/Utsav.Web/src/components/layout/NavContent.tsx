import { NavLink } from "react-router-dom";
import { navItems, type NavItem } from "@/config/nav";
import { useAuth } from "@/context/AuthContext";
import { AppLogoTile } from "@/components/brand/Athelite";
import { cn } from "@/lib/utils";

const groupLabels: Record<NavItem["group"], string> = {
  overview: "Overview",
  income: "Collections",
  feast: "Feast",
  spending: "Spending",
  planning: "Planning",
  system: "System",
};

export function NavContent({ onNavigate }: { onNavigate?: () => void }) {
  const { can } = useAuth();
  const items = navItems.filter((n) => !n.permission || can(n.permission));
  const groups = ["overview", "income", "feast", "spending", "planning", "system"] as const;

  return (
    <nav className="flex flex-1 flex-col gap-5 overflow-y-auto px-3 py-4 no-scrollbar">
      {groups.map((g) => {
        const groupItems = items.filter((n) => n.group === g);
        if (!groupItems.length) return null;
        return (
          <div key={g}>
            <p className="mb-1.5 px-3 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
              {groupLabels[g]}
            </p>
            <div className="space-y-0.5">
              {groupItems.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === "/"}
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    cn(
                      "group flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                      isActive
                        ? "bg-primary text-primary-foreground shadow-soft"
                        : "text-muted-foreground hover:bg-accent hover:text-accent-foreground"
                    )
                  }
                >
                  <item.icon className="size-[18px] shrink-0" />
                  <span className="truncate">{item.label}</span>
                </NavLink>
              ))}
            </div>
          </div>
        );
      })}
    </nav>
  );
}

export function BrandMark({ compact }: { compact?: boolean }) {
  return (
    <div className="flex items-center gap-2.5">
      <AppLogoTile className="h-9 w-9" />
      {!compact && (
        <div className="leading-tight">
          <p className="font-display text-sm font-bold tracking-tight">Puja Finance</p>
          <p className="text-[11px] text-muted-foreground">Durga Puja Manager · by Athelite</p>
        </div>
      )}
    </div>
  );
}
