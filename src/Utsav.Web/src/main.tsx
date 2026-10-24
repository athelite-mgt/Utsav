import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClientProvider } from "@tanstack/react-query";
import { TooltipProvider } from "@/components/ui/tooltip";
import { AppToaster } from "@/components/common/AppToaster";
import { queryClient } from "@/lib/queryClient";
import { ThemeProvider } from "@/context/ThemeProvider";
import { AuthProvider } from "@/context/AuthContext";
import { trackVisualViewport } from "@/lib/viewport";
import App from "./App";
import "./index.css";

// Must run before first paint so dialogs opened immediately still measure
// themselves correctly — it only writes CSS variables, no render involved.
trackVisualViewport();

// Sessions live in an httpOnly cookie now; drop the bearer token an earlier
// build kept in localStorage so a credential does not linger there.
try { localStorage.removeItem("puja-token"); } catch { /* private mode */ }

// Registers the service worker that makes the app installable on mobile
// (Add to Home Screen / Install app). Production-only and best-effort —
// a failed registration should never block the app from rendering.
if ("serviceWorker" in navigator && import.meta.env.PROD) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch(() => { /* installability is a bonus, not a requirement */ });
  });
}

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <TooltipProvider delayDuration={200}>
            <BrowserRouter>
              <App />
            </BrowserRouter>
            <AppToaster />
          </TooltipProvider>
        </AuthProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </React.StrictMode>
);
