/**
 * The active committee's heading for the documents the browser itself builds
 * (the PDF downloads across Reports, Expenses and the audit statements).
 *
 * Held here, and kept current by TenantTheme, so the existing synchronous
 * export functions pick it up without every page having to pass it in. The
 * logo is fetched ahead of time as a data URL for the same reason: an export
 * must not stop to wait on the network.
 */

export interface Letterhead {
  name: string;
  city: string | null;
  /** "#rrggbb" or null for the default rule colour. */
  accent: string | null;
  /** A PNG or JPEG data URL, or null. */
  logoDataUrl: string | null;
}

let current: Letterhead | null = null;

export const getLetterhead = (): Letterhead | null => current;
export const setLetterhead = (value: Letterhead | null) => {
  current = value;
};

/** jsPDF can embed PNG and JPEG; anything else is left out rather than failing the export. */
export function pdfImageFormat(dataUrl: string): "PNG" | "JPEG" | null {
  const type = /^data:image\/(png|jpeg);base64,/i.exec(dataUrl)?.[1]?.toLowerCase();
  return type === "png" ? "PNG" : type === "jpeg" ? "JPEG" : null;
}

/** "#b45309" (or "#fa0") as 0-255 channels, or null if it is not a plain hex colour. */
export function accentRgb(hex: string | null | undefined): [number, number, number] | null {
  if (!hex || !/^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/.test(hex)) return null;
  const digits = hex.slice(1);
  const full = digits.length === 3 ? [...digits].map((c) => c + c).join("") : digits;
  return [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16)) as [number, number, number];
}

/** Fetches a (public) logo as a data URL, or null on any failure: a missing logo must never break anything. */
export async function loadLogoDataUrl(url: string | null | undefined): Promise<string | null> {
  if (!url) return null;
  try {
    const response = await fetch(url);
    if (!response.ok) return null;
    const blob = await response.blob();
    if (blob.type !== "image/png" && blob.type !== "image/jpeg") return null;
    return await new Promise<string | null>((resolve) => {
      const reader = new FileReader();
      reader.onload = () => resolve(typeof reader.result === "string" ? reader.result : null);
      reader.onerror = () => resolve(null);
      reader.readAsDataURL(blob);
    });
  } catch {
    return null;
  }
}
