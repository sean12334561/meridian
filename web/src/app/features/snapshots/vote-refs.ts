/** Splits the list box on commas or white space. The API rejects any reference it doesn't know. */
export function parseVoteRefs(text: string): string[] {
  return [...new Set(text.split(/[\s,]+/).filter((ref) => ref.length > 0))];
}
