// Minimal service worker — exists mainly so Chrome/Android treat this app
// as installable. Deliberately does NOT cache /api/* (this is a finance
// app; stale balances are worse than a failed request) and only caches the
// app shell itself, network-first, so an install never shows stale UI
// either.
// Bumped when the shell's assets change materially (v2: Athelite branding
// and app icons) — the activate handler drops every cache but this one, so
// already-installed clients pick the new artwork up on next launch.
const CACHE = "puja-finance-shell-v2";
const SHELL = ["/", "/index.html", "/manifest.webmanifest"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(SHELL)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.pathname.startsWith("/api/")) return; // always network, never cached

  event.respondWith(
    fetch(request)
      .then((response) => {
        const copy = response.clone();
        caches.open(CACHE).then((cache) => cache.put(request, copy));
        return response;
      })
      .catch(() => caches.match(request).then((cached) => cached || caches.match("/index.html")))
  );
});

// The payload is whatever DynamicSql.TryNotifyAsync built server-side —
// {title, body, url} — see PushQueueProcessor. A malformed or missing
// payload still shows *something* rather than silently dropping the push,
// since by the time this fires the browser has already committed to
// waking the user up for it.
self.addEventListener("push", (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { body: event.data ? event.data.text() : "" };
  }

  const title = data.title || "Puja Finance";
  event.waitUntil(
    self.registration.showNotification(title, {
      body: data.body || "",
      icon: "/icons/icon-192.png",
      badge: "/icons/icon-192.png",
      data: { url: data.url || "/" },
    })
  );
});

// Focuses an already-open tab and routes it to the relevant page rather
// than opening a duplicate — most people leave this installed and running.
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const url = event.notification.data?.url || "/";

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
      const existing = clients.find((c) => new URL(c.url).origin === self.location.origin);
      if (existing) {
        return existing.navigate(url).then((c) => c && c.focus());
      }
      return self.clients.openWindow(url);
    })
  );
});
