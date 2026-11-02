import { useState } from "react";
import { Check, Copy, Mail } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { inviteLink, inviteMailto } from "@/lib/invite";

/**
 * A freshly issued invitation: the link to send (the code sits after the "#", so
 * no server ever sees it), the bare code, and a ready-written email. The code is
 * shown only here — the server keeps just a hash — so this is the moment to copy it.
 */
export function InviteLinkCard({
  code, email, committee, expiresAt,
}: { code: string; email: string; committee: string; expiresAt: string }) {
  const [copied, setCopied] = useState<"link" | "code" | null>(null);
  const link = inviteLink(window.location.origin, code);

  async function copy(what: "link" | "code") {
    try {
      await navigator.clipboard.writeText(what === "link" ? link : code);
      setCopied(what);
      window.setTimeout(() => setCopied(null), 2000);
    } catch {
      toast.error("Couldn't copy — select it and copy by hand.");
    }
  }

  return (
    <div className="space-y-3 rounded-xl border border-gold/40 bg-gold/5 p-4" aria-live="polite">
      <p className="text-sm">
        Send this to <span className="font-medium">{email}</span> by a channel you trust. It works once, only for an
        account with that email, and expires on {new Date(expiresAt).toLocaleDateString()}.{" "}
        <strong>It is shown only now.</strong>
      </p>
      <div className="flex gap-2">
        <Input readOnly value={link} aria-label="Invitation link" className="font-mono text-xs" onFocus={(e) => e.currentTarget.select()} />
        <Button type="button" variant="outline" onClick={() => copy("link")}>
          {copied === "link" ? <Check /> : <Copy />} {copied === "link" ? "Copied" : "Copy link"}
        </Button>
      </div>
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <span className="text-muted-foreground">or the code:</span>
        <code className="rounded-lg border border-border bg-card px-2.5 py-1 font-mono tracking-wider">{code}</code>
        <Button type="button" variant="ghost" size="sm" onClick={() => copy("code")}>
          {copied === "code" ? <Check /> : <Copy />} {copied === "code" ? "Copied" : "Copy code"}
        </Button>
        <Button asChild variant="outline" size="sm">
          <a href={inviteMailto(email, committee, link)}><Mail /> Email it</a>
        </Button>
      </div>
    </div>
  );
}
