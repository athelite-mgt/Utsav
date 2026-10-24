import * as React from "react";
import { IndianRupee } from "lucide-react";
import { cn } from "@/lib/utils";
import { Input, type InputProps } from "@/components/ui/input";

/**
 * A number input that wears its currency — a ₹ glyph sits inside the field so
 * every money value in the app reads unmistakably as rupees. `forwardRef` and
 * the untouched prop spread keep it a drop-in for react-hook-form's
 * `register(...)`, which is how every form here binds its fields.
 */
export const MoneyInput = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, onFocus, ...props }, ref) => (
    <div className="relative">
      <IndianRupee className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
      <Input
        ref={ref}
        type="number"
        inputMode="decimal"
        className={cn("pl-8", className)}
        // The default value sits at 0 until something is entered — without
        // this, clicking in and typing left the 0 in place and appended
        // after it (typing "500" produced "0500"), since focus alone
        // doesn't select existing text the way tabbing in sometimes does.
        onFocus={(e) => { e.target.select(); onFocus?.(e); }}
        {...props}
      />
    </div>
  )
);
MoneyInput.displayName = "MoneyInput";
