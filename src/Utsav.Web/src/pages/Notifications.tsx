import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Bell, Megaphone, ArrowRight, Receipt } from "lucide-react";
import { useNotifications, useNotificationDetail } from "@/hooks/useFinance";
import { markAllSeen, getLastSeen } from "@/lib/notifications";
import { fieldLabel, fmtStamp, fmtValue } from "@/lib/auditFormat";
import { cn } from "@/lib/utils";
import type { NotificationItem } from "@/types/db";
import { PageHeader } from "@/components/common/PageHeader";
import { TableSkeleton, EmptyState, ErrorState } from "@/components/common/states";
import { Card } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

/** "27 Aug 2026, 2:44 PM" in local time, or "5 min ago" for the recent ones. */
function fmtWhen(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  const mins = Math.round((Date.now() - d.getTime()) / 60000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins} min ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs} hr${hrs === 1 ? "" : "s"} ago`;
  return d.toLocaleString(undefined, { day: "numeric", month: "short", hour: "numeric", minute: "2-digit" });
}

/**
 * What changed, who changed it and what value moved to what, for one
 * notification — fetched on demand (only once the user actually opens it)
 * and correlated server-side to the audit entry that caused it, rather than
 * just repeating the notification's own one-line title/body.
 */
function NotificationDetailDialog({ notification, onClose }: { notification: NotificationItem | null; onClose: () => void }) {
  const navigate = useNavigate();
  const { data, isLoading } = useNotificationDetail(notification?.id, Boolean(notification));
  const entry = data?.found ? data.entry : null;

  return (
    <Dialog open={Boolean(notification)} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-w-md">
        {notification && (
          <>
            <DialogHeader>
              <DialogTitle>{notification.title}</DialogTitle>
            </DialogHeader>
            <div className="space-y-2 text-sm">
              <p className="text-muted-foreground">
                {fmtStamp(notification.created_at)}
                {notification.actor_name ? ` · ${notification.actor_name}` : ""}
              </p>
              <p>{notification.body}</p>

              {isLoading ? (
                <p className="text-xs text-muted-foreground">Looking up what changed…</p>
              ) : entry && entry.changes.length > 0 ? (
                <div className="rounded-lg border border-border">
                  {entry.changes.map((c) => (
                    <div key={c.field} className="flex justify-between gap-3 border-b border-border px-3 py-1.5 last:border-0">
                      <span className="text-muted-foreground">{fieldLabel(c.field)}</span>
                      <span className="text-right">{fmtValue(c.from)} → <span className="font-medium">{fmtValue(c.to)}</span></span>
                    </div>
                  ))}
                </div>
              ) : notification.kind === "transaction" ? (
                <p className="text-xs text-muted-foreground">No further detail found for this update.</p>
              ) : null}

              {notification.url && (
                <Button variant="outline" size="sm" className="w-full" onClick={() => navigate(notification.url)}>
                  Go to {notification.title.replace(/ (added|updated|deleted|changed)$/i, "")} <ArrowRight className="h-4 w-4" />
                </Button>
              )}
            </div>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

export function NotificationsPage() {
  const { data = [], isLoading, isError, error } = useNotifications();
  const [selected, setSelected] = useState<NotificationItem | null>(null);

  // Snapshot "unread" before marking seen, so this visit still highlights what
  // was new when the user arrived rather than clearing instantly.
  const seenAt = getLastSeen();
  useEffect(() => {
    if (data.length) markAllSeen(data);
  }, [data]);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Notifications"
        description="Every transaction update and announcement, newest first"
        icon={<Bell />}
      />

      {isLoading ? (
        <TableSkeleton />
      ) : isError ? (
        <ErrorState message={(error as Error)?.message} />
      ) : data.length === 0 ? (
        <EmptyState
          icon={<Bell />}
          title="Nothing yet"
          description="Transaction updates and admin announcements will appear here as they happen."
        />
      ) : (
        <div className="space-y-2">
          {data.map((n) => {
            const isNew = (Date.parse(n.created_at) || 0) > seenAt;
            const announcement = n.kind === "announcement";
            return (
              <Card
                key={n.id}
                onClick={() => setSelected(n)}
                className={cn(
                  "group flex cursor-pointer items-start gap-3 p-4 transition-colors hover:bg-muted/40",
                  isNew && "border-primary/30 bg-primary/[0.03]"
                )}
              >
                <span className={cn(
                  "mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg [&_svg]:size-4",
                  announcement ? "bg-gold/15 text-gold" : "bg-primary/10 text-primary"
                )}>
                  {announcement ? <Megaphone /> : <Receipt />}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <p className="truncate font-medium">{n.title}</p>
                    {announcement && <Badge variant="gold">Announcement</Badge>}
                    {isNew && <span className="h-2 w-2 shrink-0 rounded-full bg-primary" aria-label="New" />}
                  </div>
                  <p className="mt-0.5 text-sm text-muted-foreground">{n.body}</p>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {fmtWhen(n.created_at)}{n.actor_name ? ` · ${n.actor_name}` : ""}
                  </p>
                </div>
                <ArrowRight className="mt-1 h-4 w-4 shrink-0 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100" />
              </Card>
            );
          })}
        </div>
      )}

      <NotificationDetailDialog notification={selected} onClose={() => setSelected(null)} />
    </div>
  );
}
