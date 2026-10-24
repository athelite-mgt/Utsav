import { LogOut, Moon, Sun, User } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { useTheme } from "@/context/ThemeProvider";
import { initials } from "@/lib/format";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { SchemePicker } from "@/components/common/SchemePicker";
import {
  DropdownMenu, DropdownMenuTrigger, DropdownMenuContent,
  DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";

export function UserMenu() {
  const { user, signOut, role } = useAuth();
  const { theme, toggle } = useTheme();
  const name = user?.full_name ?? user?.email ?? "User";

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring">
        <Avatar className="h-9 w-9 border border-border">
          <AvatarFallback>{initials(name)}</AvatarFallback>
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <DropdownMenuLabel className="normal-case">
          <div className="flex items-center gap-2">
            <User className="h-4 w-4" />
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-foreground">{name}</p>
              <p className="truncate text-xs text-muted-foreground">{user?.email}</p>
            </div>
          </div>
          <Badge variant={role === "admin" ? "gold" : "secondary"} className="mt-2 capitalize">
            {role ?? "—"}
          </Badge>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={toggle}>
          {theme === "dark" ? <Sun /> : <Moon />}
          {theme === "dark" ? "Light mode" : "Dark mode"}
        </DropdownMenuItem>
        <div className="px-2 py-1.5">
          <p className="mb-1.5 px-0.5 text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
            Colour scheme
          </p>
          <SchemePicker compact />
        </div>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={() => signOut()} className="text-destructive focus:text-destructive">
          <LogOut />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
