import {
  LayoutDashboard, HandCoins, Megaphone, Sparkles, Gift,
  ReceiptText, PiggyBank, Truck, CalendarClock, ListChecks, Settings, Coins, History, BarChart3, FileArchive, Bell,
  BrainCircuit, UtensilsCrossed, QrCode, ScanLine,
} from "lucide-react";
import type { ComponentType } from "react";
import type { Permission } from "@/types/db";

export interface NavItem {
  to: string;
  label: string;
  short: string; // mobile bottom-nav label
  icon: ComponentType<{ className?: string }>;
  /** Omitted means every active member can see it — read access is universal. */
  permission?: Permission;
  group: "overview" | "income" | "feast" | "spending" | "planning" | "system";
}

// Every role through Viewer can at least read every module — "view
// everything" from the brief — so only Settings (committee configuration
// and user management) is hidden from anyone without manage_settings or
// manage_users. Write access within a visible page is gated separately, on
// the Add/Edit/Delete controls themselves.
export const navItems: NavItem[] = [
  { to: "/", label: "Dashboard", short: "Home", icon: LayoutDashboard, group: "overview" },
  { to: "/reports", label: "Reports", short: "Reports", icon: BarChart3, group: "overview" },
  // Asura is the AI assistant. Treasurer-level only: it reads broadly
  // across every module at once, so it is gated separately from the
  // per-page read access every role already has.
  { to: "/asura", label: "Ask Asura", short: "Asura", icon: BrainCircuit, permission: "use_asura", group: "overview" },
  { to: "/membership", label: "Membership", short: "Members", icon: HandCoins, group: "income" },
  { to: "/ads", label: "Advertisements", short: "Ads", icon: Megaphone, group: "income" },
  { to: "/special-donations", label: "Special Donation", short: "Special", icon: Sparkles, group: "income" },
  { to: "/other-incomes", label: "Other Incomes", short: "Other", icon: Coins, group: "income" },
  { to: "/offerings", label: "Offerings", short: "Offerings", icon: Gift, group: "income" },
  // Feast is its own self-contained pool — income AND expense both kept
  // separate from the general fund (see Dashboard's Feast card and
  // FeastExpense.cs) — so it gets its own sidebar group rather than being
  // folded into Collections/Spending.
  { to: "/feast", label: "Feast Collection", short: "Feast", icon: UtensilsCrossed, group: "feast" },
  { to: "/feast-expenses", label: "Feast Expenses", short: "Spend", icon: ReceiptText, group: "feast" },
  { to: "/feast-tokens", label: "Feast Tokens", short: "Tokens", icon: QrCode, group: "feast" },
  { to: "/feast-entry", label: "Feast Entry", short: "Entry", icon: ScanLine, group: "feast" },
  { to: "/expenses", label: "Expenses", short: "Spend", icon: ReceiptText, group: "spending" },
  { to: "/advances", label: "Cash Advances", short: "Advances", icon: HandCoins, group: "spending" },
  { to: "/vendors", label: "Vendors & Contracts", short: "Vendors", icon: Truck, group: "spending" },
  { to: "/budget", label: "Budget", short: "Budget", icon: PiggyBank, group: "spending" },
  { to: "/documents", label: "Documents", short: "Docs", icon: FileArchive, group: "spending" },
  // Cash Ledger is deliberately absent: it duplicated what the Dashboard's
  // Cash & Bank position now answers better. The page and its rows are still
  // in the database, so re-adding a line here brings it straight back.
  { to: "/schedule", label: "Puja Schedule", short: "Schedule", icon: CalendarClock, group: "planning" },
  { to: "/workflow", label: "Task Tracker", short: "Tasks", icon: ListChecks, group: "planning" },
  { to: "/notifications", label: "Notifications", short: "Alerts", icon: Bell, group: "system" },
  { to: "/history", label: "History", short: "History", icon: History, permission: "read_audit", group: "system" },
  { to: "/settings", label: "Settings", short: "Setup", icon: Settings, permission: "manage_users", group: "system" },
];

// Items surfaced in the mobile bottom bar, flanking the Asura button that
// AppShell renders in the centre slot. Four here plus that centre control is
// five targets, the practical ceiling for thumb reach.
//
// Dashboard is deliberately absent: Asura takes the middle, and the brand mark
// in the top bar already goes home from every page, so a fifth tab spent on it
// bought nothing. Asura itself is NOT listed here -- it is not a peer of these
// tabs and is positioned separately.
export const mobileNav: NavItem[] = navItems.filter((n) =>
  ["/membership", "/expenses", "/workflow", "/settings"].includes(n.to)
);
