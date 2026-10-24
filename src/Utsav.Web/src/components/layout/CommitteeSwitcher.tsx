import { useState } from "react";
import { Check, ChevronsUpDown } from "lucide-react";
import { toast } from "sonner";
import { useAuth } from "@/context/AuthContext";
import { CommitteeAvatar } from "@/components/brand/CommitteeAvatar";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/**
 * Renders nothing for a single-committee user: a switcher with one entry is
 * noise, and most committees will only ever have the one.
 */
export function CommitteeSwitcher() {
  const { org, memberships, switchCommittee } = useAuth();
  const [busy, setBusy] = useState(false);
  const active = memberships.filter((m) => m.status === "active");

  if (!org || active.length < 2) return null;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        disabled={busy}
        className="flex w-full items-center gap-2.5 rounded-xl border border-border bg-card px-2.5 py-2 text-left transition-colors hover:bg-accent disabled:opacity-60"
      >
        <CommitteeAvatar name={org.name} logoUrl={org.logo_url} accentColour={org.accent_colour} className="h-7 w-7 text-xs" />
        <span className="min-w-0 flex-1 truncate text-sm font-semibold">{org.name}</span>
        <ChevronsUpDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="w-60">
        <DropdownMenuLabel>Switch committee</DropdownMenuLabel>
        {active.map((m) => (
          <DropdownMenuItem
            key={m.org_id}
            onSelect={async () => {
              if (m.org_id === org.id) return;
              setBusy(true);
              const { error } = await switchCommittee(m.org_id);
              setBusy(false);
              if (error) toast.error(error);
            }}
          >
            <CommitteeAvatar name={m.name} logoUrl={m.logo_url} accentColour={m.accent_colour} className="h-6 w-6 text-[10px]" />
            <span className="min-w-0 flex-1 truncate">{m.name}</span>
            {m.org_id === org.id && <Check className="h-3.5 w-3.5" />}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
