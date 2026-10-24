import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";
import type { PujaYear } from "@/types/db";

interface YearCtx {
  years: PujaYear[];
  activeYear: PujaYear | null;
  activeYearId: string | null;
  /** The puja year immediately before the active one, if the committee has one on file — null for a first-ever year. */
  previousYearId: string | null;
  setActiveYearId: (id: string) => void;
  loading: boolean;
}
const Ctx = createContext<YearCtx | undefined>(undefined);

export function YearProvider({ children }: { children: ReactNode }) {
  const [override, setOverride] = useState<string | null>(null);

  const { data: years = [], isLoading } = useQuery({
    queryKey: ["puja_years"],
    queryFn: () => api.get<PujaYear[]>("/puja_years"),
  });

  const activeYear = useMemo(() => {
    if (override) return years.find((y) => y.id === override) ?? null;
    return years.find((y) => y.is_active) ?? years[0] ?? null;
  }, [years, override]);

  const previousYearId = useMemo(() => {
    if (!activeYear) return null;
    const priors = years.filter((y) => y.year < activeYear.year).sort((a, b) => b.year - a.year);
    return priors[0]?.id ?? null;
  }, [years, activeYear]);

  return (
    <Ctx.Provider
      value={{
        years,
        activeYear,
        activeYearId: activeYear?.id ?? null,
        previousYearId,
        setActiveYearId: setOverride,
        loading: isLoading,
      }}
    >
      {children}
    </Ctx.Provider>
  );
}

export function useYear() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useYear must be used within YearProvider");
  return ctx;
}
