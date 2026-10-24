import { jsPDF } from "jspdf";
import { afterEach, describe, expect, it } from "vitest";
import { drawLetterhead, embedBengaliFont } from "./exportPdf";
import { setLetterhead } from "./letterhead";

// A real 1x1 PNG, so jsPDF can actually decode and embed it.
const PNG_1x1 =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

function render(): { y: number; pdf: string } {
  const doc = new jsPDF({ unit: "pt", format: "a4" });
  embedBengaliFont(doc);
  const y = drawLetterhead(doc, 40, 48);
  return { y, pdf: doc.output() };
}

const imageCount = (pdf: string) => (pdf.match(/\/Subtype\s*\/Image/g) ?? []).length;

afterEach(() => setLetterhead(null));

describe("drawLetterhead", () => {
  it("changes nothing when no committee is active, so documents keep their original layout", () => {
    setLetterhead(null);

    const { y, pdf } = render();

    expect(y).toBe(48);
    expect(imageCount(pdf)).toBe(0);
  });

  it("makes room for the committee's heading when there is one", () => {
    setLetterhead({ name: "Barrackpore Kali Puja", city: "Barrackpore", accent: "#7f1d1d", logoDataUrl: null });

    expect(render().y).toBeGreaterThan(48);
  });

  it("embeds the logo when it is a PNG or JPEG", () => {
    setLetterhead({ name: "Barrackpore Kali Puja", city: null, accent: null, logoDataUrl: PNG_1x1 });

    // jsPDF writes a PNG as an image plus its alpha mask, so "present" is the
    // property that matters, not an exact count.
    expect(imageCount(render().pdf)).toBeGreaterThan(0);
  });

  it.each([
    ["a GIF", "data:image/gif;base64,R0lGODlhAQABAAAAACw="],
    ["an SVG", "data:image/svg+xml;base64,PHN2Zy8+"],
    ["something that is not an image", "hello"],
  ])("leaves %s out rather than failing the export", (_label, dataUrl) => {
    setLetterhead({ name: "Barrackpore Kali Puja", city: null, accent: null, logoDataUrl: dataUrl });

    expect(() => render()).not.toThrow();
    expect(imageCount(render().pdf)).toBe(0);
  });

  it("survives a corrupt logo and a bad brand colour", () => {
    setLetterhead({ name: "X", city: null, accent: "#fff;} body", logoDataUrl: "data:image/png;base64,AAAA" });

    expect(() => render()).not.toThrow();
  });
});
