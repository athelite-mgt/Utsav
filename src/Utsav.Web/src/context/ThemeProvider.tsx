import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

type Mode = "light" | "dark";
export type Scheme = "blue" | "marigold" | "indigo" | "terracotta";

export const SCHEMES: { value: Scheme; label: string; description: string }[] = [
  { value: "blue", label: "Ocean Blue", description: "Crisp, professional blue" },
  { value: "marigold", label: "Marigold & Teal", description: "Festive, warm bone ground" },
  { value: "indigo", label: "Indigo & Copper", description: "Cool, corporate-ledger feel" },
  { value: "terracotta", label: "Terracotta & Sage", description: "Earthy and handmade" },
];

interface ThemeCtx {
  theme: Mode; // kept as `theme` — every existing caller (UserMenu, etc.) already uses this name
  scheme: Scheme;
  toggle: () => void;
  setScheme: (s: Scheme) => void;
}
const Ctx = createContext<ThemeCtx | undefined>(undefined);
const MODE_KEY = "puja-theme";
const SCHEME_KEY = "puja-scheme";

export function ThemeProvider({ children }: { children: ReactNode }) {
  // Light + Ocean Blue is the fixed default for anyone without a saved
  // preference yet — deliberately not following the OS dark-mode setting,
  // so a first visit always looks the same regardless of device. Once a
  // user picks something (mode or scheme), that choice is remembered here.
  const [theme, setTheme] = useState<Mode>(() => {
    const stored = localStorage.getItem(MODE_KEY) as Mode | null;
    return stored ?? "light";
  });
  const [scheme, setSchemeState] = useState<Scheme>(() => {
    const stored = localStorage.getItem(SCHEME_KEY) as Scheme | null;
    return stored && SCHEMES.some((s) => s.value === stored) ? stored : "blue";
  });

  useEffect(() => {
    const root = document.documentElement;
    root.classList.toggle("dark", theme === "dark");
    localStorage.setItem(MODE_KEY, theme);
  }, [theme]);

  useEffect(() => {
    document.documentElement.dataset.scheme = scheme;
    localStorage.setItem(SCHEME_KEY, scheme);
  }, [scheme]);

  return (
    <Ctx.Provider
      value={{
        theme,
        scheme,
        toggle: () => setTheme((t) => (t === "dark" ? "light" : "dark")),
        setScheme: setSchemeState,
      }}
    >
      {children}
    </Ctx.Provider>
  );
}

export function useTheme() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useTheme must be used within ThemeProvider");
  return ctx;
}
