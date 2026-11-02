import type { ApprovalStatus } from "@/types/db";
import { Badge } from "@/components/ui/badge";
import { statusLabel } from "@/lib/approval";
import { cn } from "@/lib/utils";

/** A small status chip for anything awaiting approval; approved rows show nothing. */
export function ApprovalBadge({ status, className }: { status?: ApprovalStatus | null; className?: string }) {
  if (!status || status === "approved") return null;
  return (
    <Badge
      variant="outline"
      className={cn(
        status === "pending" && "border-amber-500/50 bg-amber-500/10 text-amber-700 dark:text-amber-400",
        status === "sent_back" && "border-orange-500/50 bg-orange-500/10 text-orange-700 dark:text-orange-400",
        status === "rejected" && "border-destructive/50 bg-destructive/10 text-destructive",
        className,
      )}
    >
      {statusLabel(status)}
    </Badge>
  );
}
