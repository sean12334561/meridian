export interface Normalization {
  field: string;
  original: string;
  value: string;
}

/** The API sends a row's normalizations as a JSON array, or null if nothing was changed (ING-13). */
export function parseNormalizations(json: string | null | undefined): Normalization[] {
  return json ? (JSON.parse(json) as Normalization[]) : [];
}
