import { useQuery } from "@tanstack/react-query";
import { api } from "@/lib/api";

/** What the sign-in page needs to know before anyone is signed in. All values are public by nature. */
export interface AuthConfig {
  googleClientId: string | null;
  entraClientId: string | null;
  entraTenantId: string | null;
}

interface ServerAuthConfig {
  google_client_id: string | null;
  entra_client_id: string | null;
  entra_tenant_id: string | null;
}

interface BuildTimeValues {
  google?: string;
  entraClient?: string;
  entraTenant?: string;
}

const clean = (value: string | null | undefined) => (value?.trim() ? value.trim() : null);

/**
 * The running server is the source of truth, so a deployment configures sign-in once, in one place,
 * with nothing baked into the build. VITE_* values remain only as a fallback for local development.
 */
export function mergeAuthConfig(server: ServerAuthConfig | null, build: BuildTimeValues): AuthConfig {
  return {
    googleClientId: clean(server?.google_client_id) ?? clean(build.google),
    entraClientId: clean(server?.entra_client_id) ?? clean(build.entraClient),
    entraTenantId: clean(server?.entra_tenant_id) ?? clean(build.entraTenant),
  };
}

/** `undefined` while loading; a config object (possibly all null) once known or if the server is unreachable. */
export function useAuthConfig(): AuthConfig | undefined {
  const { data, isPending } = useQuery({
    queryKey: ["auth-config"],
    queryFn: () => api.get<ServerAuthConfig>("/auth/config"),
    staleTime: Infinity,
    retry: 1,
  });
  if (isPending) return undefined;
  return mergeAuthConfig(data ?? null, {
    google: import.meta.env.VITE_GOOGLE_CLIENT_ID,
    entraClient: import.meta.env.VITE_ENTRA_CLIENT_ID,
    entraTenant: import.meta.env.VITE_ENTRA_TENANT_ID,
  });
}
