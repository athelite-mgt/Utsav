import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { api } from "@/lib/api";

/**
 * CRUD mutations for an API resource, invalidating the given query keys on
 * success. Keeps every module page from re-implementing create/update/delete.
 */
export function useEntityMutations(resource: string, invalidate: string[], label = "Record") {
  const qc = useQueryClient();
  const done = () => invalidate.forEach((k) => qc.invalidateQueries({ queryKey: [k] }));
  const fail = (e: unknown) => toast.error((e as Error).message);

  const create = useMutation<void, Error, Record<string, unknown>>({
    mutationFn: async (values) => { await api.post(`/${resource}`, values); },
    onSuccess: () => { done(); toast.success(`${label} added`); },
    onError: fail,
  });

  const update = useMutation<void, Error, { id: string; values: Record<string, unknown> }>({
    mutationFn: async ({ id, values }) => { await api.patch(`/${resource}/${id}`, values); },
    onSuccess: () => { done(); toast.success(`${label} updated`); },
    onError: fail,
  });

  const remove = useMutation<void, Error, string>({
    mutationFn: async (id) => { await api.del(`/${resource}/${id}`); },
    onSuccess: () => { done(); toast.success(`${label} deleted`); },
    onError: fail,
  });

  return { create, update, remove };
}
