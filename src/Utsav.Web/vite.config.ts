import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import fs from "fs";
import path from "path";

/**
 * Optional HTTPS for the dev server.
 *
 * Both single-sign-on buttons need a *secure context*, which browsers grant
 * only to HTTPS and to localhost — never to a plain-HTTP LAN address. Opening
 * the app at http://192.168.0.104:5173 therefore leaves window.crypto.subtle
 * undefined, and MSAL fails with `crypto_nonexistent` before it can reach
 * Microsoft at all.
 *
 * Testing on this machine needs none of this: http://localhost:5173 is already
 * a secure context. Set PUJA_HTTPS=1 only when the app has to be reachable
 * from another device (a phone, say), and generate the certificate first:
 *
 *   openssl req -x509 -nodes -newkey rsa:2048 -days 825 \
 *     -keyout .certs/dev-key.pem -out .certs/dev-cert.pem \
 *     -subj "/CN=puja-dev" \
 *     -addext "subjectAltName=DNS:localhost,IP:127.0.0.1,IP:<your-lan-ip>"
 *
 * It is self-signed, so browsers will warn once per device. The origin also
 * has to be registered with Google and Entra before their buttons work from
 * it — a secure context is necessary, not sufficient.
 */
function devHttps() {
  if (process.env.PUJA_HTTPS !== "1") return undefined;

  const key = path.resolve(__dirname, ".certs/dev-key.pem");
  const cert = path.resolve(__dirname, ".certs/dev-cert.pem");
  if (!fs.existsSync(key) || !fs.existsSync(cert)) {
    throw new Error(
      "PUJA_HTTPS=1 but .certs/dev-key.pem or .certs/dev-cert.pem is missing — see vite.config.ts for the openssl command."
    );
  }

  return { key: fs.readFileSync(key), cert: fs.readFileSync(cert) };
}

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5173,
    host: true,
    https: devHttps(),
    proxy: {
      // Proxied server-side, so the API stays plain HTTP on 5174 even when
      // this server is serving HTTPS — no mixed-content problem.
      "/api": {
        target: "http://localhost:5174",
        changeOrigin: true,
      },
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          react: ["react", "react-dom", "react-router-dom"],
          charts: ["recharts"],
          query: ["@tanstack/react-query"],
          // The embedded Bengali font is ~370 KB of base64 and is only ever
          // needed when someone downloads a PDF — kept in the pdf chunk with
          // jsPDF itself rather than bloating the entry bundle.
          pdf: [
            "jspdf",
            "jspdf-autotable",
            "./src/lib/fonts/NotoSansBengaliRegular",
            "./src/lib/fonts/NotoSansBengaliBold",
          ],
        },
      },
    },
  },
});
