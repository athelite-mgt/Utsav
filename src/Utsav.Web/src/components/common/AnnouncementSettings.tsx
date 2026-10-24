import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";
import { Megaphone, Send } from "lucide-react";
import { sendAnnouncement } from "@/lib/push";
import { Field } from "@/components/common/Field";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Button } from "@/components/ui/button";

/** Admin-only — broadcasts a push notification to every active member (Viewer included), unlike a transaction update. */
export function AnnouncementSettings() {
  const [title, setTitle] = useState("");
  const [body, setBody] = useState("");

  const send = useMutation({
    mutationFn: () => sendAnnouncement(title.trim(), body.trim()),
    onSuccess: () => {
      toast.success("Announcement sent to every member's device");
      setTitle("");
      setBody("");
    },
    onError: (e: unknown) => toast.error((e as Error).message),
  });

  return (
    <div className="space-y-4">
      <p className="flex items-center gap-2 text-sm text-muted-foreground">
        <Megaphone className="h-4 w-4" /> Goes to every active member's registered device, including Viewers — not just
        who a transaction update reaches.
      </p>
      <Field label="Title" required>
        <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Pandal opens 6 AM tomorrow" maxLength={100} />
      </Field>
      <Field label="Message" required>
        <Textarea value={body} onChange={(e) => setBody(e.target.value)} rows={3} placeholder="What should everyone know?" />
      </Field>
      <Button disabled={!title.trim() || !body.trim() || send.isPending} onClick={() => send.mutate()}>
        <Send className="h-4 w-4" /> {send.isPending ? "Sending…" : "Send to all members"}
      </Button>
    </div>
  );
}
