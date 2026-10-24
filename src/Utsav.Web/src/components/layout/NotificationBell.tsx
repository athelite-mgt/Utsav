import { NavLink } from "react-router-dom";
import { Bell } from "lucide-react";
import { useNotifications } from "@/hooks/useFinance";
import { unreadCount } from "@/lib/notifications";
import { cn } from "@/lib/utils";

/** Top-bar entry point to the Notifications page, with an unread dot/count. */
export function NotificationBell() {
  const { data = [] } = useNotifications();
  const unread = unreadCount(data);

  return (
    <NavLink
      to="/notifications"
      aria-label={unread > 0 ? `Notifications, ${unread} unread` : "Notifications"}
      className={({ isActive }) => cn(
        "relative flex h-9 w-9 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-accent hover:text-foreground",
        isActive && "bg-accent text-foreground"
      )}
    >
      <Bell className="h-5 w-5" />
      {unread > 0 && (
        <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold leading-none text-primary-foreground">
          {unread > 9 ? "9+" : unread}
        </span>
      )}
    </NavLink>
  );
}
