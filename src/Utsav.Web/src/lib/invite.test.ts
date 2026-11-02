import { describe, expect, it } from "vitest";
import { codeFromHash, inviteLink, inviteMailto } from "./invite";

describe("invitation links", () => {
  it("puts the code in the fragment so servers never see it", () => {
    const link = inviteLink("https://utsav.example.org/", "AB12-CD34");
    expect(link).toBe("https://utsav.example.org/#invite=AB12-CD34");
    expect(new URL(link).search).toBe("");
  });

  it("reads the code back, including encoded characters", () => {
    expect(codeFromHash("#invite=AB12-CD34")).toBe("AB12-CD34");
    expect(codeFromHash("#a=1&invite=x%20y")).toBe("x y");
  });

  it("ignores fragments without a code, or a broken one", () => {
    expect(codeFromHash("")).toBeNull();
    expect(codeFromHash("#section")).toBeNull();
    expect(codeFromHash("#invite=")).toBeNull();
    expect(codeFromHash("#invite=%E0%A4%A")).toBeNull();
  });

  it("builds a mail draft that carries the link and the address", () => {
    const m = inviteMailto("a@b.org", "Test Puja", "https://u/#invite=K");
    expect(m.startsWith("mailto:a%40b.org?subject=")).toBe(true);
    expect(decodeURIComponent(m)).toContain("https://u/#invite=K");
  });
});
