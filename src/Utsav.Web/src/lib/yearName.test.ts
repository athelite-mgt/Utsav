import { describe, expect, it } from "vitest";
import { nextYearName } from "./yearName";

describe("nextYearName", () => {
  it.each([
    ["Durga Puja 2026", 2027, "Durga Puja 2027"],
    ["Kali Puja 2026", 2027, "Kali Puja 2027"],
    ["2026 Saraswati Puja", 2027, "2027 Saraswati Puja"],
    ["Barrackpore Kali Puja 2026 (Centenary)", 2027, "Barrackpore Kali Puja 2027 (Centenary)"],
    ["Kali Puja", 2027, "Kali Puja 2027"],
    ["  Kali Puja  ", 2027, "Kali Puja 2027"],
    ["Cup 2025 Final 2026", 2027, "Cup 2025 Final 2027"],
    ["Club 47 Puja 2026", 2027, "Club 47 Puja 2027"],
    ["Block 12345 Puja", 2027, "Block 12345 Puja 2027"],
  ])("%j -> %j for %i", (previous, year, expected) => {
    expect(nextYearName(previous, year)).toBe(expected);
  });

  it.each([null, undefined, "", "   "])("is just the year for %j, never a guessed festival", (previous) => {
    expect(nextYearName(previous, 2027)).toBe("2027");
  });
});
