import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, Copy, KeyRound, LogOut, Plus } from "lucide-react";
import { toast } from "sonner";
import { api } from "@/lib/api";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { BrandMark } from "@/components/layout/NavContent";
import type { CreatedCommittee, PlatformCommittee } from "@/types/db";

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
  const [festival, setFestival] = useState("durga");
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [email, setEmail] = useState("");
  const [created, setCreated] = useState<{ committee: CreatedCommittee; email: string } | null>(null);
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

  const reissue = useMutation({
    mutationFn: ({ id, to }: { id: string; to: string }) =>
      api.post<{ invite_code: string; invite_expires_at: string }>(`/platform/committees/${id}/reissue-invite`, { email: to }),
    onSuccess: (result, { id, to }) => {
      const committee = committees.data?.find((c) => c.id === id);
      setCreated({ committee: { id, slug: committee?.slug ?? "", ...result }, email: to });
      setCopied(false);
      window.scrollTo({ top: 0, behavior: "smooth" });
      void queryClient.invalidateQueries({ queryKey: ["platform", "committees"] });
    },
    onError: (e) => toast.error((e as Error).message),
  });

  function askAndReissue(committee: PlatformCommittee) {
    const to = window.prompt(`Email address for ${committee.name}'s first administrator:`);
    if (to?.trim()) reissue.mutate({ id: committee.id, to: to.trim() });
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
                {(festivals.data ?? [{ id: "durga", name: "Durga Puja" }]).map((f) => (
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
              <Button type="submit" disabled={create.isPending || !name.trim() || !email.trim()}>
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
                <li key={c.id} className="flex flex-wrap items-center justify-between gap-2 px-5 py-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold">{c.name}</p>
                    <p className="text-xs text-muted-foreground">
                      {c.slug}{c.city ? ` · ${c.city}` : ""} · created {new Date(c.created_at).toLocaleDateString()}
                    </p>
                  </div>
                  <div className="flex items-center gap-3 text-xs text-muted-foreground">
                    {c.pending_invite && <span className="rounded-full bg-gold/10 px-2 py-0.5 text-gold">Invite waiting</span>}
                    <span>{c.members} {c.members === 1 ? "member" : "members"}</span>
                    {c.members === 0 && (
                      <Button variant="outline" size="sm" disabled={reissue.isPending} onClick={() => askAndReissue(c)}>
                        <KeyRound /> Re-issue invite
                      </Button>
                    )}
                  </div>
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
