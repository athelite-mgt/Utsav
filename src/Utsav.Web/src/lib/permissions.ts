import type { AppRole, Permission } from "@/types/db";

/**
 * Mirrors Utsav.Domain/Authorization/PermissionMatrix.cs exactly, cell for
 * cell — that file is exhaustively unit tested, this one drives what the UI
 * shows so nobody sees a button the server would reject. The server checks
 * again on every write regardless; this copy only ever affects presentation.
 */
const GRANTS: Record<AppRole, Permission[]> = {
  admin: [
    "read", "manage_settings", "manage_users",
    "write_income", "write_finance", "write_workflow", "write_advances", "read_audit",
    "use_asura",
  ],
  secretariat: [
    "read", "manage_users",
    "write_income", "write_finance", "write_workflow", "write_advances", "read_audit",
    "use_asura",
  ],
  finance: [
    "read", "write_income", "write_finance", "write_workflow", "write_advances",
    "use_asura",
  ],
  executive: ["read", "write_workflow", "write_advances"],
  viewer: ["read"],
};

export function hasPermission(role: AppRole | null, permission: Permission): boolean {
  return role != null && GRANTS[role].includes(permission);
}
