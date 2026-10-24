import { type ReactNode } from "react";
import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { YearProvider } from "@/context/YearContext";
import { AppShell } from "@/components/layout/AppShell";
import { AppLogoTile } from "@/components/brand/Athelite";
import { Login } from "@/pages/Login";
import { ServerOffline } from "@/pages/ServerOffline";
import { PendingApproval } from "@/pages/PendingApproval";
import { ChooseCommittee } from "@/pages/ChooseCommittee";
import { PlatformConsole } from "@/pages/PlatformConsole";
import { TenantTheme } from "@/components/brand/TenantTheme";
import { useSyncPushRegistration } from "@/hooks/useSyncPushRegistration";
import { Dashboard } from "@/pages/Dashboard";
import { ReportsPage } from "@/pages/Reports";
import { MembershipPage } from "@/pages/Membership";
import { AdsPage } from "@/pages/Ads";
import { SpecialDonationPage } from "@/pages/SpecialDonation";
import { FeastPage } from "@/pages/Feast";
import { FeastTokensPage } from "@/pages/FeastTokens";
import { FeastExpensesPage } from "@/pages/FeastExpenses";
import { FeastEntryPage } from "@/pages/FeastEntry";
import { OtherIncomesPage } from "@/pages/OtherIncomes";
import { OfferingsPage } from "@/pages/Offerings";
import { HistoryPage } from "@/pages/History";
import { ExpensesPage } from "@/pages/Expenses";
import { BudgetPage } from "@/pages/Budget";
// CashLedger is intentionally not routed — see the note in config/nav.tsx.
// pages/CashLedger.tsx is left in place so restoring it is a two-line change.
import { VendorsPage } from "@/pages/Vendors";
import { DocumentsPage } from "@/pages/Documents";
import { NotificationsPage } from "@/pages/Notifications";
import { AdvancesPage } from "@/pages/Advances";
import { SchedulePage } from "@/pages/Schedule";
import { WorkflowPage } from "@/pages/Workflow";
import { SettingsPage } from "@/pages/Settings";
import { AsuraPage } from "@/pages/Asura";

function FullScreenLoader() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-5 bg-background">
      <AppLogoTile className="h-14 w-14" />
      <span className="flex items-center gap-2 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin text-primary" />
        Loading Utsav…
      </span>
    </div>
  );
}

/**
 * Every other route is open to any active member — read access is
 * universal (see config/nav.tsx) and each page gates its own write controls
 * by permission. Settings is the one page hidden entirely, since it's
 * committee configuration and member management with nothing for a role
 * without manage_settings/manage_users to do there.
 */
function SettingsRoute({ children }: { children: ReactNode }) {
  const { can } = useAuth();
  return can("manage_settings") || can("manage_users") ? <>{children}</> : <Navigate to="/" replace />;
}

/**
 * Asura is treasurer-level only. The nav entry is hidden without the
 * permission, but a hand-typed /asura has to be refused too -- the server
 * would 403 anyway, this just avoids rendering a page that cannot work.
 */
function AsuraRoute({ children }: { children: ReactNode }) {
  const { can } = useAuth();
  return can("use_asura") ? <>{children}</> : <Navigate to="/" replace />;
}

/** The audit trail is Admin/Secretariat only — see PermissionMatrix.cs. */
function HistoryRoute({ children }: { children: ReactNode }) {
  const { can } = useAuth();
  return can("read_audit") ? <>{children}</> : <Navigate to="/" replace />;
}

export default function App() {
  return (
    <>
      <TenantTheme />
      <AppContent />
    </>
  );
}

function AppContent() {
  const { loading, user, org, memberships, isPlatformAdmin, status, serverDown } = useAuth();
  const { pathname } = useLocation();

  // Keeps this device's push registration in step with the committee the
  // user is working in: a device is registered per committee, so it must be
  // re-registered after switching.
  useSyncPushRegistration(org?.id ?? null);

  if (loading) return <FullScreenLoader />;
  if (serverDown) return <ServerOffline />;
  if (!user) return <Login />;
  // A platform operator gets the console: on request, and as their home when
  // they belong to no committee (otherwise they would be stranded on the
  // "join a committee" screen with nowhere to work).
  if (isPlatformAdmin && (pathname.startsWith("/platform") || memberships.length === 0)) return <PlatformConsole />;
  if (status !== "active") return <PendingApproval />;
  // Active somewhere but no committee chosen yet: a user with several. Every
  // data endpoint refuses them until they pick one.
  if (!org && memberships.some((m) => m.status === "active")) return <ChooseCommittee />;

  return (
    // Keyed by committee so switching remounts everything below: the selected
    // puja year, page-local state and form drafts all belong to the committee
    // just left, and carrying a year id across would ask the new committee for
    // a year it does not have.
    <YearProvider key={org?.id}>
      <Routes>
        <Route element={<AppShell />}>
          <Route path="/" element={<Dashboard />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/membership" element={<MembershipPage />} />
          <Route path="/ads" element={<AdsPage />} />
          <Route path="/special-donations" element={<SpecialDonationPage />} />
          <Route path="/feast" element={<FeastPage />} />
          <Route path="/feast-expenses" element={<FeastExpensesPage />} />
          <Route path="/feast-tokens" element={<FeastTokensPage />} />
          <Route path="/feast-entry" element={<FeastEntryPage />} />
          <Route path="/other-incomes" element={<OtherIncomesPage />} />
          <Route path="/offerings" element={<OfferingsPage />} />
          {/* Both sections were renamed; installed PWAs and bookmarks still
              point at the old paths. */}
          <Route path="/chanda" element={<Navigate to="/membership" replace />} />
          <Route path="/special" element={<Navigate to="/other-incomes" replace />} />
          <Route path="/expenses" element={<ExpensesPage />} />
          <Route path="/budget" element={<BudgetPage />} />
          <Route path="/vendors" element={<VendorsPage />} />
          <Route path="/documents" element={<DocumentsPage />} />
          <Route path="/notifications" element={<NotificationsPage />} />
          <Route path="/advances" element={<AdvancesPage />} />
          <Route path="/schedule" element={<SchedulePage />} />
          <Route path="/workflow" element={<WorkflowPage />} />
          <Route path="/history" element={<HistoryRoute><HistoryPage /></HistoryRoute>} />
          <Route path="/settings" element={<SettingsRoute><SettingsPage /></SettingsRoute>} />
        </Route>
        {/* Deliberately OUTSIDE AppShell. Asura is a different mode of using
            the app rather than another page in it: it takes the whole
            viewport, with no sidebar, header or marigold chrome, and provides
            its own Close control to come back. */}
        <Route path="/asura" element={<AsuraRoute><AsuraPage /></AsuraRoute>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </YearProvider>
  );
}
