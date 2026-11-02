/**
 * Invitation links. The one-time code rides in the URL *fragment* — the part
 * after "#" — which browsers never send to a server, so the code does not land
 * in access logs, proxies or referrers. On arrival it is moved into
 * sessionStorage (so it survives sign-in) and removed from the address bar.
 */
const KEY = "utsav-invite";

/** The link to send: <origin>/#invite=<code>. */
export function inviteLink(origin: string, code: string): string {
  return `${origin.replace(/\/+$/, "")}/#invite=${encodeURIComponent(code.trim())}`;
}

/** Pulls a code out of a "#invite=…" fragment; null when there is none. */
export function codeFromHash(hash: string): string | null {
  const m = /(?:^#|&)invite=([^&]+)/.exec(hash);
  if (!m) return null;
  try {
    const code = decodeURIComponent(m[1]).trim();
    return code || null;
  } catch {
    return null;
  }
}

/** A ready-to-open mail draft carrying the link — the code never touches a server on the way. */
export function inviteMailto(to: string, committee: string, link: string): string {
  const subject = `You're invited to ${committee} on Utsav`;
  const body =
    `You've been invited to run ${committee} on Utsav.\n\n` +
    `1. Open this link: ${link}\n` +
    `2. Sign in with this email address (${to}) — create the account first if you don't have one.\n` +
    `3. Press "Join" on the page that opens.\n\n` +
    `The link works once and expires soon.`;
  return `mailto:${encodeURIComponent(to)}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
}

/** Call once at start-up: stash a code from the URL and clear it from the address bar. */
export function captureInviteFromUrl(): void {
  try {
    const code = codeFromHash(window.location.hash);
    if (!code) return;
    sessionStorage.setItem(KEY, code);
    window.history.replaceState(null, "", window.location.pathname + window.location.search);
  } catch { /* storage unavailable: the user can still paste the code */ }
}

export function readPendingInvite(): string | null {
  try { return sessionStorage.getItem(KEY); } catch { return null; }
}

export function clearPendingInvite(): void {
  try { sessionStorage.removeItem(KEY); } catch { /* nothing to clear */ }
}
