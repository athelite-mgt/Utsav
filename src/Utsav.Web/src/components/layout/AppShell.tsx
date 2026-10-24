import { Fragment, useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { Menu } from "lucide-react";
import { mobileNav } from "@/config/nav";
import { useAuth } from "@/context/AuthContext";
import { cn } from "@/lib/utils";
import { Sheet, SheetContent, SheetTrigger } from "@/components/ui/sheet";
import { Button } from "@/components/ui/button";
import { PoweredByAthelite } from "@/components/brand/Athelite";
import { InstallPrompt } from "@/components/common/InstallPrompt";
import { NotificationPromptModal } from "@/components/common/NotificationPromptModal";
import { NavContent, BrandMark } from "./NavContent";
import { YearSwitcher } from "./YearSwitcher";
import { UserMenu } from "./UserMenu";
import { NotificationBell } from "./NotificationBell";
import { AsuraFigure } from "@/pages/asura/AsuraFigure";
import "./asura-nav.css";

/**
 * Asura's centre slot in the mobile bottom bar.
 *
 * Not a NavItem in config/nav.tsx on purpose: everything there renders as an
 * equal tab, and the point of this control is that it is not equal — it is
 * bigger, it sits above the bar, and it carries Asura's palette rather than
 * the committee app's.
 */
function AsuraTab() {
  return (
    <NavLink to="/asura" className="asura-navbtn" aria-label="Ask Asura">
      <span className="asura-navbtn-disc">
        <span className="asura-navbtn-halo" aria-hidden="true" />
        <AsuraFigure compact />
      </span>
      <span className="asura-navbtn-label">Asura</span>
    </NavLink>
  );
}

export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const { can } = useAuth();
  const location = useLocation();
  const tabs = mobileNav.filter((n) => !n.permission || can(n.permission));
  const asuraEnabled = can("use_asura");
  const midpoint = Math.ceil(tabs.length / 2);

  return (
    <div className="min-h-screen bg-background">
      {/* Desktop sidebar */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 flex-col border-r border-border bg-card lg:flex">
        <div className="flex h-16 items-center border-b border-border px-5">
          <BrandMark />
        </div>
        <NavContent />
        <div className="border-t border-border px-5 py-3">
          <PoweredByAthelite compact />
        </div>
      </aside>

      {/* Main column */}
      <div className="flex min-h-screen flex-col lg:pl-64">
        {/* Top bar */}
        <header className="sticky top-0 z-20 flex h-16 items-center gap-3 border-b border-border bg-card/80 px-4 backdrop-blur-md sm:px-6 pt-safe">
          {/* Mobile menu */}
          <Sheet open={mobileOpen} onOpenChange={setMobileOpen}>
            <SheetTrigger asChild>
              <Button variant="ghost" size="icon" className="lg:hidden">
                <Menu className="h-5 w-5" />
              </Button>
            </SheetTrigger>
            <SheetContent side="left" className="flex w-72 flex-col p-0">
              <div className="flex h-16 items-center border-b border-border px-5">
                <BrandMark />
              </div>
              <NavContent onNavigate={() => setMobileOpen(false)} />
              <div className="border-t border-border px-5 py-3">
                <p className="text-[10px] text-muted-foreground">Powered by Athelite</p>
              </div>
            </SheetContent>
          </Sheet>

          <div className="lg:hidden">
            <BrandMark compact />
          </div>

          <div className="ml-auto flex items-center gap-2 sm:gap-3">
            {/* Asura's desktop entry point. Hidden below lg because on a phone
                the centre control in the bottom bar is the way in, and two
                entry points on one screen is just clutter. */}
            {can("use_asura") && (
              <NavLink to="/asura" className="asura-topbtn hidden lg:inline-flex">
                <AsuraFigure compact />
                Ask Asura
              </NavLink>
            )}
            <YearSwitcher />
            <NotificationBell />
            <UserMenu />
          </div>
        </header>

        {/* Page content. Keying the wrapper on the path remounts it each
            navigation, so the fade-in replays on every page rather than only
            the first — a soft settle as you move around, nothing showy. */}
        <main className="flex-1 px-4 pb-24 pt-5 sm:px-6 lg:pb-10">
          <div key={location.pathname} className="mx-auto w-full max-w-6xl animate-fade-in">
            <Outlet />
          </div>
        </main>
      </div>

      <InstallPrompt />
      <NotificationPromptModal />

      {/* Mobile bottom nav.

          The tabs are split in half so Asura can hold the centre slot rather
          than queue up as another flex item -- it is larger, lifted above the
          bar and in its own palette, because it opens a different mode of the
          app instead of navigating to another page.

          overflow-visible matters: the centre control has a negative top
          margin to sit above the bar, and the default would clip it. */}
      <nav className="fixed inset-x-0 bottom-0 z-30 overflow-visible border-t border-border bg-card/95 backdrop-blur-md lg:hidden pb-safe">
        <div className="relative mx-auto flex max-w-lg items-end justify-around overflow-visible">
          {tabs.map((item, index) => (
            <Fragment key={item.to}>
              {/* Dropped in at the midpoint. When the member cannot use Asura
                  the slot simply is not there and the tabs close up. */}
              {index === midpoint && asuraEnabled && <span className="asura-navbtn-slot" />}
              <NavLink
                to={item.to}
                className={cn(
                  "flex flex-1 flex-col items-center gap-1 py-2.5 text-[11px] font-medium transition-colors",
                  location.pathname.startsWith(item.to) ? "text-primary" : "text-muted-foreground"
                )}
              >
                <span
                  className={cn(
                    "flex h-8 w-12 items-center justify-center rounded-full transition-colors",
                    location.pathname.startsWith(item.to) && "bg-primary/10"
                  )}
                >
                  <item.icon className="size-[18px]" />
                </span>
                {item.short}
              </NavLink>
            </Fragment>
          ))}
          {/* An odd number of tabs would otherwise leave the slot unplaced. */}
          {midpoint >= tabs.length && asuraEnabled && <span className="asura-navbtn-slot" />}
          {/* Positioned absolutely over the slot above, so it can overhang
              the bar without making the bar taller. */}
          {asuraEnabled && <AsuraTab />}
        </div>
      </nav>
    </div>
  );
}
