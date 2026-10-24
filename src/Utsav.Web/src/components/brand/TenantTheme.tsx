import { useEffect } from "react";
import { useAuth } from "@/context/AuthContext";
import { brandVariables } from "@/lib/brand";

const VARIABLES = ["--primary", "--primary-foreground", "--ring"] as const;

/**
 * Applies the active committee's brand colour by overriding the theme's own
 * CSS variables, so branding is a data change rather than a per-committee
 * build, and names the browser tab after the committee.
 *
 * The colour is nudged for legibility (see brandVariables) and recomputed when
 * the user flips between light and dark, since text on a colour that works on
 * a light page is not the same colour that works on a dark one. The values are
 * written with setProperty and are only ever "h s% l%" triplets, so even a bad
 * value reaching here could not carry anything but a colour.
 */
export function TenantTheme() {
  const { org } = useAuth();
  const accent = org?.accent_colour ?? null;

  useEffect(() => {
    const root = document.documentElement;
    const clear = () => VARIABLES.forEach((v) => root.style.removeProperty(v));

    const apply = () => {
      const vars = accent ? brandVariables(accent, root.classList.contains("dark")) : null;
      if (!vars) {
        clear();
        return;
      }
      root.style.setProperty("--primary", vars.primary);
      root.style.setProperty("--primary-foreground", vars.primaryForeground);
      root.style.setProperty("--ring", vars.ring);
    };

    apply();
    const observer = new MutationObserver(apply);
    observer.observe(root, { attributes: true, attributeFilter: ["class"] });
    return () => {
      observer.disconnect();
      clear();
    };
  }, [accent]);

  useEffect(() => {
    // A member has one committee's app open, not "Utsav".
    document.title = org?.name ? `${org.name} — Utsav` : "Utsav";
  }, [org?.name]);

  return null;
}
