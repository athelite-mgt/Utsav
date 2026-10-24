/**
 * Publishes the *visual* viewport — what the user can actually see right now —
 * as CSS custom properties, so fixed-position UI can size and place itself
 * against that instead of the layout viewport.
 *
 * Why this is needed: when the on-screen keyboard opens, browsers shrink the
 * visual viewport but leave the layout viewport at full height unless the
 * page opts in with `interactive-widget=resizes-content` (see index.html).
 * Anything measured in `vh`/`dvh`, or centred with `top: 50%`, therefore keeps
 * sizing itself against the whole screen and ends up half-hidden behind the
 * keyboard — which is exactly what made the dialogs unscrollable with their
 * Save button out of reach.
 *
 * Chrome/Android has honoured the meta tag since 108. WebKit has implemented
 * it but no public Safari release ships it yet, so iOS still needs this
 * fallback. Where the meta tag does work these values simply agree with the
 * layout viewport, and the CSS that reads them becomes a no-op.
 */
export function trackVisualViewport() {
  const vv = window.visualViewport;
  if (!vv) return;

  const sync = () => {
    // A backgrounded or mid-transition tab can momentarily report 0. Writing
    // that through would collapse anything sized against it to nothing, so
    // hold the last good value (or the CSS fallback, if we never had one)
    // until a real measurement arrives.
    if (!vv.height) return;

    const root = document.documentElement;
    root.style.setProperty("--vv-height", `${vv.height}px`);
    // The visual viewport can also be scrolled *within* the layout viewport
    // (iOS does this to reveal a focused field), so position needs the offset
    // as well as the height — height alone would leave the dialog adrift.
    root.style.setProperty("--vv-top", `${vv.offsetTop}px`);
  };

  sync();
  vv.addEventListener("resize", sync);
  vv.addEventListener("scroll", sync);
}
