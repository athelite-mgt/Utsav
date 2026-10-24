import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api, setCsrfToken, ApiError } from "@/lib/api";
import { hasPermission } from "@/lib/permissions";
import { queryClient } from "@/lib/queryClient";
import type { AccessStatus, AppRole, Membership, Organization, Permission, User } from "@/types/db";

interface AuthContextValue {
  user: User | null;
  org: Organization | null;
  /** Every committee this account belongs to, including pending ones. */
  memberships: Membership[];
  /** A platform operator may provision committees. It grants no access to any committee's data. */
  isPlatformAdmin: boolean;
  role: AppRole | null;
  status: AccessStatus | null;
  can: (permission: Permission) => boolean;
  loading: boolean;
  serverDown: boolean;
  signIn: (email: string, password: string) => Promise<{ error: string | null }>;
  signUp: (email: string, password: string, fullName: string) => Promise<{ error: string | null }>;
  signInWithGoogle: (credential: string) => Promise<{ error: string | null }>;
  signInWithMicrosoft: (idToken: string) => Promise<{ error: string | null }>;
  /** Pin this session to one of the user's committees. */
  switchCommittee: (orgId: string) => Promise<{ error: string | null }>;
  /** Ask to join a committee by its slug; an admin there still has to approve. */
  requestAccess: (slug: string) => Promise<{ error: string | null }>;
  /** Redeem the one-time code a platform operator gave a committee's first administrator. */
  redeemInvite: (code: string) => Promise<{ error: string | null }>;
  signOut: () => Promise<void>;
  refreshSession: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

interface SessionPayload {
  user: User;
  org: Organization | null;
  memberships: Membership[];
  is_platform_admin?: boolean;
  role: AppRole | null;
  status: AccessStatus;
  /** Held in memory only; echoed on every state-changing request. See lib/api.ts. */
  csrf_token?: string;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [org, setOrg] = useState<Organization | null>(null);
  const [memberships, setMemberships] = useState<Membership[]>([]);
  const [isPlatformAdmin, setIsPlatformAdmin] = useState(false);
  const [role, setRole] = useState<AppRole | null>(null);
  const [status, setStatus] = useState<AccessStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [serverDown, setServerDown] = useState(false);

  function applySession(s: SessionPayload) {
    setCsrfToken(s.csrf_token ?? null);
    setUser(s.user);
    setOrg(s.org);
    setMemberships(s.memberships ?? []);
    setIsPlatformAdmin(s.is_platform_admin === true);
    setRole(s.role);
    setStatus(s.status);
  }

  async function loadMe() {
    applySession(await api.get<SessionPayload>("/auth/me"));
  }

  useEffect(() => {
    let cancelled = false;
    (async () => {
      // The session is a cookie the page cannot see, so the only way to know
      // whether there is one is to ask. A 401 simply means "not signed in";
      // anything that is not an API answer means the server is not there.
      try {
        if (!cancelled) await loadMe();
      } catch (e) {
        if (!(e instanceof ApiError) && !cancelled) setServerDown(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  async function authenticate(path: string, body: Record<string, string>) {
    try {
      applySession(await api.post<SessionPayload>(path, body));
      setServerDown(false);
      return { error: null };
    } catch (e) {
      if (!(e instanceof ApiError)) {
        setServerDown(true);
        return { error: "Cannot reach the server. Is it running? (npm run dev)" };
      }
      return { error: (e as Error).message };
    }
  }

  const value: AuthContextValue = {
    user,
    org,
    memberships,
    isPlatformAdmin,
    role,
    status,
    can: (permission) => hasPermission(role, permission),
    loading,
    serverDown,
    signIn: (email, password) => authenticate("/auth/login", { email, password }),
    signUp: (email, password, full_name) => authenticate("/auth/signup", { email, password, full_name }),
    signInWithGoogle: (credential) => authenticate("/auth/google", { credential }),
    // snake_case on the wire, like every other field this API takes.
    signInWithMicrosoft: (idToken) => authenticate("/auth/entra", { id_token: idToken }),
    async switchCommittee(orgId) {
      try {
        applySession(await api.post<SessionPayload>("/auth/switch-org", { org_id: orgId }));
        // Every cached query belongs to the committee we just left. Cleared
        // rather than invalidated: an invalidate leaves a frame where the
        // previous committee's figures render under the new committee's name.
        queryClient.clear();
        return { error: null };
      } catch (e) {
        return { error: e instanceof ApiError ? (e as Error).message : "Could not switch committee" };
      }
    },
    async requestAccess(slug) {
      try {
        await api.post("/auth/request-access", { slug });
        await loadMe();
        return { error: null };
      } catch (e) {
        return { error: e instanceof ApiError ? (e as Error).message : "Could not send the request" };
      }
    },
    async redeemInvite(code) {
      try {
        applySession(await api.post<SessionPayload>("/auth/redeem-invite", { code }));
        queryClient.clear();
        return { error: null };
      } catch (e) {
        return { error: e instanceof ApiError ? (e as Error).message : "Could not redeem that code" };
      }
    },
    async signOut() {
      try { await api.post("/auth/logout"); } catch { /* token may already be gone */ }
      setCsrfToken(null);
      setUser(null);
      setOrg(null);
      setMemberships([]);
      setIsPlatformAdmin(false);
      queryClient.clear();
      setRole(null);
      setStatus(null);
    },
    refreshSession: loadMe,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
