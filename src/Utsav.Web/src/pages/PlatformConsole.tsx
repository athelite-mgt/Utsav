import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, ChevronDown, Copy, KeyRound, LogOut, Plus, UserCheck, Users } from "lucide-react";
import { toast } from "sonner";
import { api } from "@/lib/api";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { BrandMark } from "@/components/layout/NavContent";
import type { AppRole, CreatedCommittee, PlatformCommittee } from "@/types/db";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { inviteLink, inviteMailto } from "@/lib/invite";
import { InviteLinkCard } from "@/components/auth/InviteLinkCard";

interface Festival { id: string; name: string }

const selectClass =
  "flex h-10 w-full rounded-lg border border-input bg-card px-3 py-2 text-sm shadow-soft focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring";

/**
 * The platform operator's directory of committees, and the place to create one.
 *
 * Deliberately a directory and nothing more: it lists committees and how many
 * people are in each, never any committee's figures. The capability this page
 * rests on grants no access to anyone's books.
 */
const ROLES: { value: AppRole; label: string }[] = [
  { value: "admin", label: "Admin" },
  { value: "secretariat", label: "Secretariat" },
  { value: "finance", label: "Finance" },
  { value: "executive", label: "Executive" },
  { value: "viewer", label: "Viewer" },
];

function RoleSelect({ value, onChange }: { value: AppRole; onChange: (r: AppRole) => void }) {
  return (
    <Select value={value} onValueChange={(v) => onChange(v as AppRole)}>
      <SelectTrigger className="w-36"><SelectValue /></SelectTrigger>
      <SelectContent>{ROLES.map((r) => <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>)}</SelectContent>
    </Select>
  );
}

/**
 * What the operator can do for one committee: see who is in it (names, emails and roles —
 * never its money), approve people who asked to join, and send invitation links. Each action
 * is recorded in that committee's own History as done by the platform operator.
 */
function CommitteeDetail({ committee: c }: { committee: PlatformCommittee }) {
  const queryClient = useQueryClient();
  const empty = c.members === 0;
  const members = useQuery({
    queryKey: ["platform", "members", c.id],
    queryFn: () => api.get<{ member_id: string; email: string; full_name: string | null; role: AppRole }[]>(`/platform/committees/${c.id}/members`),
  });
  const requests = useQuery({
    queryKey: ["platform", "requests", c.id],
    queryFn: () => api.get<{ member_id: string; email: string; full_name: string | null }[]>(`/platform/committees/${c.id}/requests`),
  });
  const [roleFor, setRoleFor] = useState<Record<string, AppRole>>({});
  const [email, setEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<AppRole>(empty ? "admin" : "viewer");
  const [invite, setInvite] = useState<{ invite_code: string; invite_expires_at: string; email: string } | null>(null);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["platform"] });

  const approve = useMutation({
    mutationFn: ({ id, role }: { id: string; role: AppRole }) => api.post(`/platform/committees/${c.id}/requests/${id}/approve`, { role }),
    onSuccess: () => { toast.success("Access granted"); refresh(); },
    onError: (e) => toast.error((e as Error).message),
  });
  const send = useMutation({
    mutationFn: () => api.post<{ invite_code: string; invite_expires_at: string }>(
      `/platform/committees/${c.id}/invites`, { email: email.trim(), role: inviteRole }),
    onSuccess: (r) => { setInvite({ ...r, email: email.trim() }); setEmail(""); refresh(); },
    onError: (e) => toast.error((e as Error).message),
  });

  const people = members.data ?? [];
  const waiting = requests.data ?? [];
  return (
    <div className="space-y-5 border-t border-border bg-muted/30 px-5 py-4">
      <section className="space-y-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold"><Users className="h-4 w-4" /> People in {c.name}</h3>
        {members.isLoading ? <p className="text-sm text-muted-foreground">Loading…</p>
          : people.length === 0 ? <p className="text-sm text-muted-foreground">Nobody yet — approve a request or send an invitation below.</p>
          : (
            <ul className="divide-y divide-border rounded-lg border border-border bg-card">
              {people.map((m) => (
                <li key={m.member_id} className="flex items-center justify-between gap-3 px-3 py-2">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">{m.full_name ?? m.email}</p>
                    <p className="truncate text-xs text-muted-foreground">{m.email}</p>
                  </div>
                  <span className="shrink-0 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium capitalize text-primary">{m.role}</span>
                </li>
              ))}
            </ul>
          )}
      </section>

      <section className="space-y-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold"><UserCheck className="h-4 w-4" /> Requests to join</h3>
        {waiting.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            None waiting. Someone can ask by signing in and entering the short name <code className="font-mono">{c.slug}</code>.
          </p>
        ) : (
          <ul className="divide-y divide-border rounded-lg border border-border bg-card">
            {waiting.map((r) => {
              const role = roleFor[r.member_id] ?? (empty ? "admin" : "viewer");
              return (
                <li key={r.member_id} className="flex flex-wrap items-center justify-between gap-3 px-3 py-2">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">{r.full_name ?? r.email}</p>
                    <p className="truncate text-xs text-muted-foreground">{r.email}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <RoleSelect value={role} onChange={(v) => setRoleFor((s) => ({ ...s, [r.member_id]: v }))} />
                    <Button size="sm" disabled={approve.isPending} onClick={() => approve.mutate({ id: r.member_id, role })}>
                      <Check /> Approve
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section className="space-y-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold"><KeyRound className="h-4 w-4" /> Send an invitation link</h3>
        <form className="flex flex-col gap-2 sm:flex-row" onSubmit={(e) => { e.preventDefault(); if (email.trim()) send.mutate(); }}>
          <Input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} placeholder="person@example.org" aria-label="Email to invite" />
          <RoleSelect value={inviteRole} onChange={setInviteRole} />
          <Button type="submit" disabled={send.isPending || !email.trim()}>Create invitation link</Button>
        </form>
        {invite && <InviteLinkCard code={invite.invite_code} email={invite.email} committee={c.name} expiresAt={invite.invite_expires_at} />}
      </section>
    </div>
  );
}

export function PlatformConsole() {
  const { user, org, signOut } = useAuth();
  const queryClient = useQueryClient();

  const committees = useQuery({
    queryKey: ["platform", "committees"],
    queryFn: () => api.get<PlatformCommittee[]>("/platform/committees"),
  });
  const festivals = useQuery({
    queryKey: ["platform", "festivals"],
    queryFn: () => api.get<Festival[]>("/platform/festivals"),
  });

  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [city, setCity] = useState("");
  const [festival, setFestival] = useState("");
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [email, setEmail] = useState("");
  // Nothing is assumed: the operator chooses from what the server offers, and the
  // first is only pre-selected so the form is usable straight away.
  useEffect(() => {
    if (!festival && festivals.data?.length) setFestival(festivals.data[0].id);
  }, [festival, festivals.data]);
  const [created, setCreated] = useState<{ committee: CreatedCommittee; email: string } | null>(null);
  const [openId, setOpenId] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const create = useMutation({
    mutationFn: () =>
      api.post<CreatedCommittee>("/platform/committees", {
        name,
        slug: slug.trim() || undefined,
        city: city.trim() || undefined,
        festival,
        first_year: Number(year),
        first_admin_email: email,
      }),
    onSuccess: (committee) => {
      setCreated({ committee, email });
      setCopied(false);
      setName(""); setSlug(""); setCity(""); setEmail("");
      void queryClient.invalidateQueries({ queryKey: ["platform", "committees"] });
    },
    onError: (e) => toast.error((e as Error).message),
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  async function copy(code: string) {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
    } catch {
      toast.error("Couldn't copy — select the code and copy it by hand.");
    }
  }

  const expires = created ? new Date(created.committee.invite_expires_at).toLocaleDateString() : "";

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between gap-3 border-b border-border bg-card px-4 py-3 sm:px-6">
        <BrandMark />
        <div className="flex items-center gap-2">
          {org && (
            <Button asChild variant="ghost" size="sm">
              <Link to="/"><ArrowLeft /> Back to {org.name}</Link>
            </Button>
          )}
          <Button variant="ghost" size="sm" onClick={() => signOut()}>
            <LogOut /> Sign out
          </Button>
        </div>
      </header>

      <main className="mx-auto max-w-4xl space-y-6 px-4 py-6 sm:px-6">
        <div>
          <h1 className="font-display text-xl font-bold">Platform console</h1>
          <p className="text-sm text-muted-foreground">
            Signed in as {user?.email}. Create committees and see who is on the platform. Nothing here
            shows any committee's money.
          </p>
        </div>

        {created && (
          <section className="rounded-2xl border border-gold/40 bg-gold/5 p-5" aria-live="polite">
            <div className="flex items-start gap-3">
              <KeyRound className="mt-0.5 h-5 w-5 shrink-0 text-gold" />
              <div className="min-w-0 flex-1">
                <h2 className="font-semibold">Committee created — give them this code</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  Send it to <span className="font-medium text-foreground">{created.email}</span> by a
                  channel you trust. It works once, only for an account registered with that address, and
                  expires on {expires}. <strong>It is shown only now</strong>; if it's lost, use "Re-issue
                  invite" on the committee below.
                </p>
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <code className="rounded-lg border border-border bg-card px-3 py-2 font-mono text-sm tracking-wider">
                    {created.committee.invite_code}
                  </code>
                  <Button variant="outline" size="sm" onClick={() => copy(created.committee.invite_code)}>
                    {copied ? <Check /> : <Copy />} {copied ? "Copied" : "Copy"}
                  </Button>
                  <Button variant="outline" size="sm"
                    onClick={() => copy(inviteLink(window.location.origin, created.committee.invite_code))}>
                    <Copy /> Copy invite link
                  </Button>
                  <Button asChild variant="outline" size="sm">
                    <a href={inviteMailto(created.email, created.committee.slug || "your committee",
                      inviteLink(window.location.origin, created.committee.invite_code))}>
                      Email it
                    </a>
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => setCreated(null)}>Done</Button>
                </div>
              </div>
            </div>
          </section>
        )}

        <section className="rounded-2xl border border-border bg-card p-5 shadow-card">
          <h2 className="font-semibold">Create a committee</h2>
          <form onSubmit={submit} className="mt-4 grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5 sm:col-span-2">
              <Label htmlFor="pc-name">Committee name</Label>
              <Input id="pc-name" value={name} onChange={(e) => setName(e.target.value)} required maxLength={120}
                placeholder="Barrackpore Kali Puja" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pc-slug">Short name <span className="text-muted-foreground">(optional)</span></Label>
              <Input id="pc-slug" value={slug} onChange={(e) => setSlug(e.target.value)} autoCapitalize="none"
                placeholder="barrackpore-kali" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pc-city">City</Label>
              <Input id="pc-city" value={city} onChange={(e) => setCity(e.target.value)} placeholder="Barrackpore" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pc-festival">Festival</Label>
              <select id="pc-festival" className={selectClass} value={festival} onChange={(e) => setFestival(e.target.value)}>
                {(festivals.data ?? []).map((f) => (
                  <option key={f.id} value={f.id}>{f.name}</option>
                ))}
              </select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pc-year">First year</Label>
              <Input id="pc-year" type="number" min={2001} max={2099} value={year} onChange={(e) => setYear(e.target.value)} required />
            </div>
            <div className="space-y-1.5 sm:col-span-2">
              <Label htmlFor="pc-email">First administrator's email</Label>
              <Input id="pc-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required
                placeholder="secretary@example.org" />
            </div>
            <div className="sm:col-span-2">
              <Button type="submit" disabled={create.isPending || !name.trim() || !email.trim() || !festival}>
                <Plus /> {create.isPending ? "Creating…" : "Create committee"}
              </Button>
            </div>
          </form>
        </section>

        <section className="rounded-2xl border border-border bg-card shadow-card">
          <h2 className="border-b border-border px-5 py-3 font-semibold">Committees</h2>
          {committees.isLoading ? (
            <p className="px-5 py-4 text-sm text-muted-foreground">Loading…</p>
          ) : (
            <ul className="divide-y divide-border">
              {(committees.data ?? []).map((c) => (
                <li key={c.id}>
                  <button
                    type="button"
                    className="flex w-full flex-wrap items-center justify-between gap-2 px-5 py-3 text-left hover:bg-muted/40"
                    aria-expanded={openId === c.id}
                    onClick={() => setOpenId(openId === c.id ? null : c.id)}
                  >
                    <div className="min-w-0">
                      <p className="truncate text-sm font-semibold">{c.name}</p>
                      <p className="text-xs text-muted-foreground">
                        {c.slug}{c.city ? ` · ${c.city}` : ""} · created {new Date(c.created_at).toLocaleDateString()}
                      </p>
                    </div>
                    <div className="flex items-center gap-3 text-xs text-muted-foreground">
                      {(c.pending_requests ?? 0) > 0 && (
                        <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-amber-700 dark:text-amber-400">
                          {c.pending_requests} waiting
                        </span>
                      )}
                      {c.pending_invite && <span className="rounded-full bg-gold/10 px-2 py-0.5 text-gold">Invite waiting</span>}
                      <span>{c.members} {c.members === 1 ? "member" : "members"}</span>
                      <ChevronDown className={openId === c.id ? "h-4 w-4 rotate-180 transition-transform" : "h-4 w-4 transition-transform"} />
                    </div>
                  </button>
                  {openId === c.id && <CommitteeDetail committee={c} />}
                </li>
              ))}
              {committees.data?.length === 0 && <li className="px-5 py-4 text-sm text-muted-foreground">None yet.</li>}
            </ul>
          )}
        </section>
      </main>
    </div>
  );
}
