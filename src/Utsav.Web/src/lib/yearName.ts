/**
 * What to call next year's puja, carrying the committee's own wording forward:
 * "Kali Puja 2026" becomes "Kali Puja 2027". Mirrors YearNaming on the server
 * (the rollover dialog pre-fills this; the server applies the same rule if the
 * name is left out). With no previous name it is just the year, never a
 * guessed festival.
 */
export function nextYearName(previous: string | null | undefined, year: number): string {
  const name = (previous ?? "").trim();
  if (!name) return String(year);

  // A four-digit year on word boundaries, so "Club 47" and "12345" are left alone.
  const matches = [...name.matchAll(/\b(?:19|20)\d{2}\b/g)];
  const last = matches.at(-1);
  if (!last || last.index === undefined) return `${name} ${year}`;

  // The LAST year in the name: "Cup 2025 Final 2026" is the 2026 edition.
  return name.slice(0, last.index) + year + name.slice(last.index + last[0].length);
}
