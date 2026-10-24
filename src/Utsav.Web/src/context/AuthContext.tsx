import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api, setToken, getToken, ApiError } from "@/lib/api";
import { hasPermission } from "@/lib/permissions";
import type { AccessStatus, AppRole, Organization, Permission, User } from "@/types/db";

interface AuthContextValue {
  user: User | null;
  org: Organization | null;
  role: AppRole | null;
  status: AccessStatus | null;
  can: (permission: Permission) => boolean;
  loading: boolean;
  serverDown: boolean;
  signIn: (email: string, password: string) => Promise<{ error: string | null }>;
  signUp: (email: string, password: string, fullName: string) => Promise<{ error: string | null }>;
  signInWithGoogle: (credential: string) => Promise<{ error: string | null }>;
  signInWithMicrosoft: (idToken: string) => Promise<{ error: string | null }>;
  signOut: () => Promise<void>;
  refreshSession: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

interface SessionPayload { user: User; org: Organization | null; role: AppRole | null; status: AccessStatus }
interface AuthPayload extends SessionPayload { token: string }

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [org, setOrg] = useState<Organization | null>(null);
  const [role, setRole] = useState<AppRole | null>(null);
  const [status, setStatus] = useState<AccessStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [serverDown, setServerDown] = useState(false);

  function applySession(s: SessionPayload) {
    setUser(s.user);
    setOrg(s.org);
    setRole(s.role);
    setStatus(s.status);
  }

  async function loadMe() {
    applySession(await api.get<SessionPayload>("/auth/me"));
  }

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!getToken()) {
        // No session yet — still ping the API so we can detect a stopped server.
        try {
          await api.get("/auth/me");
        } catch (e) {
          if (!cancelled && !(e instanceof ApiError)) setServerDown(true);
        }
        if (!cancelled) setLoading(false);
        return;
      }
      try {
        if (!cancelled) await loadMe();
      } catch (e) {
        if (e instanceof ApiError) setToken(null); // expired/invalid session
        else if (!cancelled) setServerDown(true);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  async function authenticate(path: string, body: Record<string, string>) {
    try {
      const { token, ...session } = await api.post<AuthPayload>(path, body);
      setToken(token);
      applySession(session);
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
    async signOut() {
      try { await api.post("/auth/logout"); } catch { /* token may already be gone */ }
      setToken(null);
      setUser(null);
      setOrg(null);
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
