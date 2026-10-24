import * as React from "react";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";

const Dialog = DialogPrimitive.Root;
const DialogTrigger = DialogPrimitive.Trigger;
const DialogClose = DialogPrimitive.Close;

const DialogOverlay = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      "fixed inset-0 z-50 bg-black/50 backdrop-blur-sm data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:pointer-events-none",
      className
    )}
    {...props}
  />
));
DialogOverlay.displayName = DialogPrimitive.Overlay.displayName;

const DialogContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content>
>(({ className, children, ...props }, ref) => (
  <DialogPrimitive.Portal>
    <DialogOverlay />
    {/*
      Centring happens here, with flexbox, rather than on the panel with
      `-translate-y-1/2`. tailwindcss-animate's `animate-in` runs a keyframe
      that composes `transform`, and an animated value outranks a normal
      declaration in the cascade — so the translate was silently dropped and
      the panel sat with its top edge at the midpoint, hanging off the bottom
      of the screen. Flex centring can't be clobbered that way.

      The frame is sized to the *visible* viewport (see lib/viewport.ts), so
      when the keyboard opens the frame shrinks, the panel re-centres in what's
      left and stays fully scrollable within it — Save/Cancel are ordinary
      content at the end of the form, reached by scrolling, not a floating
      overlay eating into the last field.
    */}
    <div className="dialog-viewport-frame pointer-events-none fixed inset-x-0 z-50 flex items-center justify-center p-4">
      <DialogPrimitive.Content
        ref={ref}
        // Radix focuses the first focusable field by default, which on
        // mobile pops the on-screen keyboard the instant a row is tapped —
        // before the user has chosen where they actually want to type.
        // Let them tap a field themselves instead.
        onOpenAutoFocus={(e) => e.preventDefault()}
        className={cn(
          // Outer panel owns the rounding + border and clips its own content
          // (overflow-hidden). The actual scrolling happens on the *inner*
          // div below — that's what keeps the native scrollbar's track
          // inside the rounded corners on desktop instead of running flush
          // to a square edge that broke the panel's symmetry.
          //
          // flex flex-col (not grid): a `grid` container's implicit row
          // track is sized `auto`, which means "fit the item's max-content
          // height" — full stop, independent of the container's own
          // max-height. min-h-0 on the item doesn't change that (it only
          // affects the item's *minimum* size contribution, not how an auto
          // track computes its base size), so the inner scroll area was
          // laid out at its full content height every time — never actually
          // capped, so overflow-y-auto never had anything to do; the visible
          // cropping came entirely from this panel's own overflow-hidden,
          // with no scrollbar reaching the cropped part. A `flex` column
          // with the child as `flex-1 min-h-0` genuinely participates in
          // the flex sizing algorithm, so it shrinks to whatever height is
          // actually left — which is what makes the child's own
          // overflow-y-auto do anything. Matters most exactly when the
          // mobile keyboard halves the available height.
          "dialog-panel relative pointer-events-auto flex min-h-0 max-h-full w-full max-w-lg flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-card duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:pointer-events-none",
          className
        )}
        {...props}
      >
        <div className="dialog-scroll-area grid min-h-0 flex-1 gap-4 overflow-y-auto overscroll-contain p-6">
          {children}
        </div>
        <DialogPrimitive.Close
          className="absolute right-3 top-3 flex h-9 w-9 items-center justify-center rounded-lg bg-destructive text-destructive-foreground shadow-sm transition-colors hover:bg-destructive/90 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
        >
          <X className="h-5 w-5" />
          <span className="sr-only">Close</span>
        </DialogPrimitive.Close>
      </DialogPrimitive.Content>
    </div>
  </DialogPrimitive.Portal>
));
DialogContent.displayName = DialogPrimitive.Content.displayName;

const DialogHeader = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn("flex flex-col space-y-1.5 text-left", className)} {...props} />
);
DialogHeader.displayName = "DialogHeader";

// Ordinary flow content at the end of the form — not pinned/floating. A
// sticky footer used to shrink the usable form area (it sat on top of
// whatever was scrolled underneath it) and, on mobile with the on-screen
// keyboard open, could sit awkwardly mid-panel instead of at the natural
// end. Since the whole panel scrolls (see DialogContent), Save/Cancel are
// always reachable the same way the rest of the form is: scroll to them,
// keyboard open or not.
const DialogFooter = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn(
      "mt-2 flex flex-col-reverse gap-2 border-t border-border pt-4 sm:flex-row sm:justify-end",
      className
    )}
    {...props}
  />
);
DialogFooter.displayName = "DialogFooter";

const DialogTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn("font-display text-lg font-semibold tracking-tight", className)}
    {...props}
  />
));
DialogTitle.displayName = DialogPrimitive.Title.displayName;

const DialogDescription = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description ref={ref} className={cn("text-sm text-muted-foreground", className)} {...props} />
));
DialogDescription.displayName = DialogPrimitive.Description.displayName;

export {
  Dialog, DialogTrigger, DialogClose, DialogContent,
  DialogHeader, DialogFooter, DialogTitle, DialogDescription,
};
