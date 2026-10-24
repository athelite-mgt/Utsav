import { type CSSProperties } from "react";
import { cn } from "@/lib/utils";

// The Athelite artwork lives in /public/brand as transparent PNGs generated
// from the master logo. Two lockup variants exist because the wordmark is
// printed in charcoal — unreadable on a dark background — so the dark-mode
// copy repaints that ink near-white. The runner mark is gold in both and
// needs no variant.

/** The runner mark on its own — gold, safe on any background. */
export function AtheliteMark({ className }: { className?: string }) {
  return (
    <img
      src="/brand/athelite-mark.png"
      alt=""
      aria-hidden
      draggable={false}
      className={cn("select-none object-contain", className)}
    />
  );
}

/** Full lockup: runner mark above the arched ATHELITE wordmark. */
export function AtheliteLogo({ className }: { className?: string }) {
  return (
    <>
      <img
        src="/brand/athelite-logo.png"
        alt="Athelite"
        draggable={false}
        className={cn("select-none object-contain dark:hidden", className)}
      />
      <img
        src="/brand/athelite-logo-dark.png"
        alt="Athelite"
        draggable={false}
        className={cn("hidden select-none object-contain dark:block", className)}
      />
    </>
  );
}

/**
 * The attribution block. `compact` is the one-line version used in the
 * sidebar footer; the stacked version is for the login page, where there's
 * room to give the logo some presence.
 */
export function PoweredByAthelite({
  compact = false,
  className,
  style,
}: { compact?: boolean; className?: string; style?: CSSProperties }) {
  if (compact) {
    return (
      <div className={cn("flex items-center gap-2", className)} style={style}>
        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">Powered by</span>
        <AtheliteLogo className="h-5 w-auto opacity-80" />
      </div>
    );
  }

  return (
    <div className={cn("flex flex-col items-center gap-2", className)} style={style}>
      <span className="text-[10px] font-medium uppercase tracking-[0.2em] text-muted-foreground">
        Powered by
      </span>
      <AtheliteLogo className="h-14 w-auto opacity-90" />
    </div>
  );
}

/**
 * The app's own identity tile — the Athelite mark on the brand charcoal,
 * matching the installed PWA icon so the sidebar and the home-screen icon
 * read as the same product.
 */
export function AppLogoTile({ className }: { className?: string }) {
  return (
    <div
      className={cn(
        "flex shrink-0 items-center justify-center rounded-xl bg-[#231F20] p-1.5",
        className
      )}
    >
      <AtheliteMark className="h-full w-full" />
    </div>
  );
}
