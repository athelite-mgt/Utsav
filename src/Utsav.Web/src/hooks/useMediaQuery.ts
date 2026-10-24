import { useEffect, useState } from "react";

/**
 * Render one layout, not two.
 *
 * Tailwind's `hidden lg:block` only hides visually — React still mounts every
 * component and the browser still builds every node. On a 183-row list that
 * doubled the DOM. This lets us branch in JS so only the layout in use exists.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() =>
    typeof window !== "undefined" ? window.matchMedia(query).matches : false
  );

  useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = (e: MediaQueryListEvent) => setMatches(e.matches);
    setMatches(mql.matches);
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [query]);

  return matches;
}

/** Tailwind's `lg` breakpoint. */
export const useIsDesktop = () => useMediaQuery("(min-width: 1024px)");
