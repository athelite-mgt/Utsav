import { Toaster } from "sonner";
import { useTheme } from "@/context/ThemeProvider";
import { useIsDesktop } from "@/hooks/useMediaQuery";

/**
 * Toasts used to land top-centre, which on a phone parks them directly over
 * the header — hiding the menu button for the whole time the toast is up.
 * They now come from the bottom on mobile (clearing the bottom nav) and from
 * the top-right on desktop, where nothing is underneath them.
 */
export function AppToaster() {
  const isDesktop = useIsDesktop();
  const { theme } = useTheme();

  return (
    <Toaster
      position={isDesktop ? "top-right" : "bottom-center"}
      // Two offsets, because sonner switches to `mobileOffset` on its own
      // below 600px and ignores `offset` entirely there. Both need to clear
      // the bottom nav (~64px) plus the home indicator; setting only the
      // first leaves phone toasts sitting on top of the nav bar.
      offset={isDesktop ? 20 : 88}
      mobileOffset={{ bottom: "88px", left: "12px", right: "12px" }}
      theme={theme}
      richColors
      closeButton
      duration={3500}
      toastOptions={{
        style: { fontFamily: "IBM Plex Sans, system-ui, sans-serif" },
        className: "text-sm",
      }}
    />
  );
}
