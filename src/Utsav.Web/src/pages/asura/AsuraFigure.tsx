/**
 * The Asura mask — original artwork, drawn here rather than shipped as an
 * image.
 *
 * Deliberately vector and hand-built for three reasons: it stays crisp at any
 * size from the 38px header mark to the full-screen hero, its glowing parts are
 * real DOM nodes so CSS can animate them (the eyes blink while idle, faster
 * while a question is being answered), and it is our own drawing — no
 * licensing question about an image of a deity or demon pulled off the web.
 *
 * The design is a stylised asura face: a crown of three horns, an angular mask
 * and two eyes, over a halo of rotating dashes. Geometric
 * rather than illustrative, so it reads as "high tech" alongside the aurora and
 * glass rather than as a piece of religious art.
 */
export function AsuraFigure({
  className,
  busy = false,
  compact = false,
}: {
  className?: string;
  busy?: boolean;
  /**
   * Crops to the crown/mask/tusks and drops the two halo rings.
   *
   * The full viewBox (0 0 220 220) reserves a wide margin for the halo, which
   * is right for the 120-240px hero but meant the mask itself rendered at only
   * a fraction of a small icon's pixels — at 34px in the mobile nav button the
   * eyes were sub-pixel and the face read as a blurry smudge rather than a
   * face. Cropping to content and dropping the rings (which a tiny icon has no
   * room to show meaningfully anyway) make the same mask legible at icon size
   * without forking the artwork into a second drawing.
   */
  compact?: boolean;
}) {
  return (
    <svg
      className={`asura-figure${busy ? " busy" : ""}${className ? ` ${className}` : ""}`}
      viewBox={compact ? "52 6 116 168" : "0 0 220 220"}
      role="img"
      aria-label="Asura"
    >
      <defs>
        <linearGradient id="asura-edge" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#a78bfa" />
          <stop offset="50%" stopColor="#22d3ee" />
          <stop offset="100%" stopColor="#db2777" />
        </linearGradient>
        <radialGradient id="asura-eye" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#ffffff" />
          <stop offset="35%" stopColor="#22d3ee" />
          <stop offset="100%" stopColor="rgba(34,211,238,0)" />
        </radialGradient>
        <radialGradient id="asura-core" cx="50%" cy="38%" r="62%">
          <stop offset="0%" stopColor="#2a2440" />
          <stop offset="100%" stopColor="#0a0a12" />
        </radialGradient>
        <filter id="asura-glow" x="-60%" y="-60%" width="220%" height="220%">
          <feGaussianBlur stdDeviation="4" result="b" />
          <feMerge>
            <feMergeNode in="b" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>

      {/* Everything except the lids lives in this group, because the whole
          figure's outer glow (.asura-figure's drop-shadow in CSS) is applied
          to this group specifically rather than the whole <svg>. The lids
          animate via `transform: scaleY()` every blink, and a parent-level
          CSS filter computes its effect region from the element's
          pre-transform geometry in some engines — left on the whole figure,
          that smeared a faint shadow of the lids' full, uncollapsed
          rectangles down from the crown on every frame, blink or not. Scoping
          the filter to a sibling group the lids aren't inside of removes them
          from that compositing step entirely, so there's nothing left to
          ghost regardless of what their transform is doing. */}
      <g className="asura-glow-group">
        {/* halo — rotating dashes. Cropped out of the compact viewBox anyway,
            so skipped entirely there rather than left to render off-canvas. */}
        {!compact && (
          <>
            <circle
              className="asura-halo"
              cx="110"
              cy="110"
              r="98"
              fill="none"
              stroke="url(#asura-edge)"
              strokeWidth="1.5"
              strokeDasharray="3 11"
              opacity="0.75"
            />
            <circle
              className="asura-halo-2"
              cx="110"
              cy="110"
              r="88"
              fill="none"
              stroke="url(#asura-edge)"
              strokeWidth="0.8"
              strokeDasharray="22 9"
              opacity="0.4"
            />
          </>
        )}

        {/* crown of three horns */}
        <g stroke="url(#asura-edge)" strokeWidth="2.4" fill="none" strokeLinecap="round" filter="url(#asura-glow)">
          <path d="M110 18 L110 46" />
          <path d="M74 28 L86 54" />
          <path d="M146 28 L134 54" />
          <path d="M96 30 Q110 14 124 30" opacity="0.55" />
        </g>

        {/* mask */}
        <path
          d="M110 44 L160 72 L152 136 Q110 186 68 136 L60 72 Z"
          fill="url(#asura-core)"
          stroke="url(#asura-edge)"
          strokeWidth="2"
        />

        {/* A brow line where the third eye used to sit — without it the forehead
            reads as an empty gap rather than a deliberate space. */}
        <path
          d="M88 70 Q110 60 132 70"
          fill="none"
          stroke="url(#asura-edge)"
          strokeWidth="1.6"
          strokeLinecap="round"
          opacity="0.6"
        />

        {/* cheek circuitry */}
        <g stroke="#a78bfa" strokeWidth="1.1" fill="none" opacity="0.5" strokeLinecap="round">
          <path d="M74 92 L88 92 L94 100" />
          <path d="M146 92 L132 92 L126 100" />
          <path d="M80 118 L92 126" />
          <path d="M140 118 L128 126" />
          <path d="M110 150 L110 166" />
          <path d="M98 158 L110 166 L122 158" />
        </g>

        {/* Eyes. The two lids are siblings of the eyes rather than part of them,
            so the blink can scale a lid down over a static eye -- scaling the eye
            itself would shrink the glow with it and read as a dimming bulb. */}
        <g className="asura-eyes">
          <ellipse cx="90" cy="100" rx="13" ry="7.5" fill="url(#asura-eye)" />
          <ellipse cx="130" cy="100" rx="13" ry="7.5" fill="url(#asura-eye)" />
          <ellipse cx="90" cy="100" rx="3.2" ry="3.2" fill="#ffffff" />
          <ellipse cx="130" cy="100" rx="3.2" ry="3.2" fill="#ffffff" />
        </g>

        {/* tusks */}
        <g fill="none" stroke="url(#asura-edge)" strokeWidth="2" strokeLinecap="round" opacity="0.85">
          <path d="M96 132 Q92 146 98 152" />
          <path d="M124 132 Q128 146 122 152" />
        </g>
      </g>

      {/* Lids: mask-coloured bars that sweep down over each eye to blink.
          Deliberately outside .asura-glow-group (see comment above) — a sibling of
          it, not a child — so they paint crisp and never contribute to that
          group's filtered composite. Still after it in document order, so
          they still paint on top of the eyes when covering them. */}
      <g className="asura-lids" fill="url(#asura-core)">
        <rect className="asura-lid" x="75" y="84" width="30" height="17" rx="3" />
        <rect className="asura-lid" x="115" y="84" width="30" height="17" rx="3" />
      </g>
    </svg>
  );
}
