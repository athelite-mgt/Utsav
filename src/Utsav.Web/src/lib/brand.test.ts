import { describe, expect, it } from "vitest";
import { brandVariables, contrastRatio, hslToRgb, parseHex } from "./brand";

/** Reads the "h s% l%" triplet back into RGB so contrast can be measured on what is really applied. */
function rgbOf(triplet: string): [number, number, number] {
  const [h, s, l] = triplet.split(" ").map((p) => parseFloat(p));
  return hslToRgb(h, s, l);
}

// A spread that includes the awkward ones: pale, saturated yellow, pure
// black/white, and committee-ish ambers and reds.
const colours = [
  "#b45309", "#7f1d1d", "#0f766e", "#2563eb", "#ffeb3b", "#ffffff", "#000000",
  "#fde68a", "#e11d48", "#14532d", "#6b7280", "#f59e0b", "#a3e635", "#1e1b4b",
];

describe("parseHex", () => {
  it("reads six-digit and three-digit hex", () => {
    expect(parseHex("#b45309")).toEqual([0xb4, 0x53, 0x09]);
    expect(parseHex("#fa0")).toEqual([0xff, 0xaa, 0x00]);
  });

  it.each(["red", "#12", "#12345g", "", "rgb(1,2,3)", "#fff;}"])("rejects %j", (value) => {
    expect(parseHex(value)).toBeNull();
  });
});

describe("brandVariables, light mode", () => {
  it.each(colours)("keeps white text readable on %s", (hex) => {
    const vars = brandVariables(hex, false)!;
    const ratio = contrastRatio(rgbOf(vars.primary), rgbOf(vars.primaryForeground));
    // WCAG AA for normal text.
    expect(ratio).toBeGreaterThanOrEqual(4.5);
  });

  it("darkens a pale colour instead of putting white text on it", () => {
    const original = parseHex("#fde68a")!;
    const applied = rgbOf(brandVariables("#fde68a", false)!.primary);
    expect(applied[0] + applied[1] + applied[2]).toBeLessThan(original[0] + original[1] + original[2]);
  });

  it("leaves a colour that is already dark enough alone", () => {
    const vars = brandVariables("#7f1d1d", false)!;
    const [r, g, b] = rgbOf(vars.primary);
    expect(Math.abs(r - 0x7f)).toBeLessThanOrEqual(3);
    expect(Math.abs(g - 0x1d)).toBeLessThanOrEqual(3);
    expect(Math.abs(b - 0x1d)).toBeLessThanOrEqual(3);
  });

  it("keeps the hue of a chromatic colour", () => {
    const hueOf = (triplet: string) => parseFloat(triplet.split(" ")[0]);
    const original = brandVariables("#0f766e", false)!;
    expect(Math.abs(hueOf(original.primary) - 175)).toBeLessThanOrEqual(2);
  });
});

describe("brandVariables, dark mode", () => {
  it.each(colours)("keeps dark text readable on %s and lifts it off the dark page", (hex) => {
    const vars = brandVariables(hex, true)!;
    const primary = rgbOf(vars.primary);
    expect(contrastRatio(primary, rgbOf(vars.primaryForeground))).toBeGreaterThanOrEqual(4.5);
    // Against the dark page background (222 22% 8%) it must still be visible.
    expect(contrastRatio(primary, rgbOf("222 22% 8%"))).toBeGreaterThanOrEqual(3);
  });

  it("lifts a dark colour that would vanish into a dark background", () => {
    const vars = brandVariables("#1e1b4b", true)!;
    const lightness = parseFloat(vars.primary.split(" ")[2]);
    expect(lightness).toBeGreaterThan(40);
  });
});

describe("brandVariables, bad input", () => {
  it("returns nothing for a value that is not a hex colour, so the default theme stays", () => {
    expect(brandVariables("red", false)).toBeNull();
    expect(brandVariables("#fff;} body{display:none", true)).toBeNull();
    expect(brandVariables("", false)).toBeNull();
  });

  it("only ever produces plain 'h s% l%' triplets that cannot carry CSS", () => {
    for (const hex of colours) {
      for (const dark of [false, true]) {
        const vars = brandVariables(hex, dark)!;
        for (const value of [vars.primary, vars.primaryForeground, vars.ring]) {
          expect(value).toMatch(/^\d{1,3} \d{1,3}% \d{1,3}%$/);
        }
      }
    }
  });
});
