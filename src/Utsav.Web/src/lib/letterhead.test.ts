import { describe, expect, it } from "vitest";
import { accentRgb, getLetterhead, pdfImageFormat, setLetterhead } from "./letterhead";

describe("accentRgb", () => {
  it("reads six- and three-digit hex", () => {
    expect(accentRgb("#b45309")).toEqual([0xb4, 0x53, 0x09]);
    expect(accentRgb("#fa0")).toEqual([0xff, 0xaa, 0x00]);
  });

  it.each([null, undefined, "", "red", "#12", "#fff;}", "rgb(1,2,3)"])("gives no colour for %j", (value) => {
    expect(accentRgb(value)).toBeNull();
  });
});

describe("pdfImageFormat", () => {
  it("accepts only the formats jsPDF can embed", () => {
    expect(pdfImageFormat("data:image/png;base64,AAAA")).toBe("PNG");
    expect(pdfImageFormat("data:image/jpeg;base64,AAAA")).toBe("JPEG");
    expect(pdfImageFormat("data:image/gif;base64,AAAA")).toBeNull();
    expect(pdfImageFormat("data:image/svg+xml;base64,AAAA")).toBeNull();
    expect(pdfImageFormat("not a data url")).toBeNull();
  });
});

describe("the current letterhead", () => {
  it("is empty until a committee sets it, and can be cleared again", () => {
    setLetterhead(null);
    expect(getLetterhead()).toBeNull();
    setLetterhead({ name: "Barrackpore Kali Puja", city: "Barrackpore", accent: "#7f1d1d", logoDataUrl: null });
    expect(getLetterhead()?.name).toBe("Barrackpore Kali Puja");
    setLetterhead(null);
    expect(getLetterhead()).toBeNull();
  });
});
