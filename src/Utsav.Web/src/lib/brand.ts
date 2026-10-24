/**
 * Turns a committee's brand colour into theme values that stay readable.
 *
 * The app's theme is a set of HSL triplets in CSS variables (--primary and
 * friends), so a committee's colour is applied by overriding those, not by a
 * per-committee build. The one thing that cannot be left to the committee is
 * legibility: a pale-yellow button with white text, or a navy button that
 * vanishes into the dark theme, would make the app unusable for that
 * committee's own members. So the colour is nudged, keeping its hue, until
 * text on it meets WCAG AA (4.5:1).
 */

export interface BrandVars {
  /** "h s% l%" for --primary */
  primary: string;
  /** "h s% l%" for --primary-foreground */
  primaryForeground: string;
  /** "h s% l%" for --ring */
  ring: string;
}

const HEX = /^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;

/** The theme's own foreground for text on a primary-coloured surface in dark mode (hsl 222 30% 8%). */
const DARK_FOREGROUND: [number, number, number] = hslToRgb(222, 30, 8);
const WHITE: [number, number, number] = [255, 255, 255];
const AA = 4.5;
/**
 * Aim a little above the line: the values are rounded to whole numbers before
 * they are applied, and rounding alone can drop a borderline colour just under.
 */
const TARGET = AA + 0.25;

export function parseHex(value: string): [number, number, number] | null {
  if (!HEX.test(value)) return null;
  const digits = value.slice(1);
  const full = digits.length === 3 ? [...digits].map((c) => c + c).join("") : digits;
  return [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16)) as [number, number, number];
}

export function rgbToHsl(r: number, g: number, b: number): [number, number, number] {
  const [rn, gn, bn] = [r, g, b].map((v) => v / 255);
  const max = Math.max(rn, gn, bn);
  const min = Math.min(rn, gn, bn);
  const l = (max + min) / 2;
  const d = max - min;
  if (d === 0) return [0, 0, l * 100];
  const s = d / (1 - Math.abs(2 * l - 1));
  let h: number;
  if (max === rn) h = ((gn - bn) / d) % 6;
  else if (max === gn) h = (bn - rn) / d + 2;
  else h = (rn - gn) / d + 4;
  return [(h * 60 + 360) % 360, s * 100, l * 100];
}

export function hslToRgb(h: number, s: number, l: number): [number, number, number] {
  const sn = s / 100;
  const ln = l / 100;
  const c = (1 - Math.abs(2 * ln - 1)) * sn;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = ln - c / 2;
  const [r, g, b] =
    h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
  return [r, g, b].map((v) => Math.round((v + m) * 255)) as [number, number, number];
}

function luminance([r, g, b]: [number, number, number]): number {
  const [rl, gl, bl] = [r, g, b].map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * rl + 0.7152 * gl + 0.0722 * bl;
}

export function contrastRatio(a: [number, number, number], b: [number, number, number]): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const triplet = (h: number, s: number, l: number) => `${Math.round(h)} ${Math.round(s)}% ${Math.round(l)}%`;

/**
 * Theme values for a committee colour, or null if it isn't a plain hex colour,
 * in which case the default theme is left alone. The output is only ever
 * numbers in an "h s% l%" shape, so it cannot carry anything but a colour.
 */
export function brandVariables(hex: string, dark: boolean): BrandVars | null {
  const rgb = parseHex(hex);
  if (!rgb) return null;

  const [h, s, l0] = rgbToHsl(...rgb);
  let l = l0;

  if (dark) {
    // Text on it is dark, and it has to show against a dark page: lift it.
    while (l < 92 && contrastRatio(hslToRgb(h, s, l), DARK_FOREGROUND) < TARGET) l += 1;
    while (l < 92 && contrastRatio(hslToRgb(h, s, l), hslToRgb(222, 22, 8)) < 3.25) l += 1;
    const primary = triplet(h, s, l);
    return { primary, primaryForeground: triplet(222, 30, 8), ring: primary };
  }

  // Text on it is white: darken it until that is readable.
  while (l > 8 && contrastRatio(hslToRgb(h, s, l), WHITE) < TARGET) l -= 1;
  const primary = triplet(h, s, l);
  return { primary, primaryForeground: "0 0% 100%", ring: primary };
}
