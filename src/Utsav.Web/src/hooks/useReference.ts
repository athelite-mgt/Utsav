import { useQuery } from "@tanstack/react-query";
import { api, qs } from "@/lib/api";
import type { Collector, Lookup, LookupCategory, Zone } from "@/types/db";

const FIVE_MIN = 5 * 60_000;

export function useLookups(category: LookupCategory) {
  return useQuery({
    queryKey: ["lookups", category],
    queryFn: () => api.get<Lookup[]>(`/lookups${qs({ category })}`),
    staleTime: FIVE_MIN,
  });
}

export function useCollectors() {
  return useQuery({
    queryKey: ["collectors"],
    queryFn: () => api.get<Collector[]>("/collectors"),
    staleTime: FIVE_MIN,
  });
}

export function useZones() {
  return useQuery({
    queryKey: ["zones"],
    queryFn: () => api.get<Zone[]>("/zones"),
    staleTime: FIVE_MIN,
  });
}
