import { cn } from "@/lib/utils";

/**
 * A committee's mark: its logo if it has one, otherwise its initial on its own
 * brand colour. A committee that has not uploaded a logo should still read as
 * itself rather than as a blank square.
 */
export function CommitteeAvatar({
  name,
  logoUrl,
  accentColour,
  className,
}: {
  name: string;
  logoUrl?: string | null;
  accentColour?: string | null;
  className?: string;
}) {
  if (logoUrl) {
    return <img src={logoUrl} alt="" className={cn("h-9 w-9 shrink-0 rounded-lg object-cover", className)} />;
  }

  return (
    <span
      aria-hidden
      className={cn(
        "grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-primary font-display text-sm font-bold text-primary-foreground",
        className
      )}
      style={accentColour ? { backgroundColor: accentColour } : undefined}
    >
      {name.trim().slice(0, 1).toUpperCase()}
    </span>
  );
}
