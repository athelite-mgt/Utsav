import { useMemo, useState } from "react";

export type SortDir = "asc" | "desc";

/**
 * Generic click-to-sort for table rows. Nulls/undefined always sort to the
 * bottom regardless of direction — an empty bill no. or unset date isn't
 * meaningfully "less than" a real value.
 */
export function useSort<T>(rows: T[], accessors: Record<string, (row: T) => unknown>, defaultKey: string | null = null) {
  const [sortKey, setSortKey] = useState<string | null>(defaultKey);
  const [sortDir, setSortDir] = useState<SortDir>("asc");

  const toggleSort = (key: string) => {
    if (sortKey === key) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortKey(key);
      setSortDir("asc");
    }
  };

  const sorted = useMemo(() => {
    const get = sortKey ? accessors[sortKey] : null;
    if (!get) return rows;
    const dir = sortDir === "asc" ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = get(a);
      const bv = get(b);
      if ((av === null || av === undefined) && (bv === null || bv === undefined)) return 0;
      if (av === null || av === undefined) return 1;
      if (bv === null || bv === undefined) return -1;
      if (typeof av === "number" && typeof bv === "number") return (av - bv) * dir;
      return String(av).localeCompare(String(bv)) * dir;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, sortKey, sortDir]);

  return { sorted, sortKey, sortDir, toggleSort };
}
