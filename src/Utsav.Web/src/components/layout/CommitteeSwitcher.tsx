import { useState } from "react";
import { Check, ChevronsUpDown, Clock, Plus } from "lucide-react";
import { toast } from "sonner";
import { useAuth } from "@/context/AuthContext";
import { CommitteeAvatar } from "@/components/brand/CommitteeAvatar";
import { JoinCommitteeForm } from "@/components/auth/JoinCommitteeForm";
import {
  Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/**
 * Always shown once inside a committee: even with a single committee it is the
 * only place to ask to join another one. Pending requests are listed so a member
 * can see they are waiting, and cannot switch into them.
 */
export function CommitteeSwitcher() {
  const { org, memberships, switchCommittee } = useAuth();
  const [busy, setBusy] = useState(false);
  const [joining, setJoining] = useState(false);
  const active = memberships.filter((m) => m.status === "active");
  const pending = memberships.filter((m) => m.status === "pending");

  if (!org) return null;

  return (
    <>
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
        {pending.map((m) => (
          <DropdownMenuItem key={m.org_id} disabled>
            <CommitteeAvatar name={m.name} logoUrl={m.logo_url} accentColour={m.accent_colour} className="h-6 w-6 text-[10px]" />
            <span className="min-w-0 flex-1 truncate">{m.name}</span>
            <span className="flex items-center gap-1 text-[11px] text-muted-foreground"><Clock className="h-3 w-3" /> Awaiting approval</span>
          </DropdownMenuItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={() => setJoining(true)}>
          <Plus className="h-4 w-4" /> Join another committee…
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
    <Dialog open={joining} onOpenChange={setJoining}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Join another committee</DialogTitle>
          <DialogDescription>
            Enter its short name and an admin there will be asked to approve you. Your current committee is not affected.
          </DialogDescription>
        </DialogHeader>
        <JoinCommitteeForm onJoined={() => setJoining(false)} />
      </DialogContent>
    </Dialog>
    </>
  );
}
