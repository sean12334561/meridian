import { describe, expect, it } from 'vitest';
import { parseNormalizations } from './normalizations';

describe('parseNormalizations', () => {
  it('reads each change, with trim and uppercase as separate steps', () => {
    const json = '[{"field":"country_code","original":" deu ","value":"deu"},{"field":"country_code","original":"deu","value":"DEU"}]';

    expect(parseNormalizations(json)).toEqual([
      { field: 'country_code', original: ' deu ', value: 'deu' },
      { field: 'country_code', original: 'deu', value: 'DEU' },
    ]);
  });

  it('gives nothing for a row that was not normalized', () => {
    expect(parseNormalizations(null)).toEqual([]);
    expect(parseNormalizations(undefined)).toEqual([]);
  });
});
