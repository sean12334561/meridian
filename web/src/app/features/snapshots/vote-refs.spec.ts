import { describe, expect, it } from 'vitest';
import { parseVoteRefs } from './vote-refs';

describe('parseVoteRefs', () => {
  it('splits on commas and white space and drops repeats', () => {
    expect(parseVoteRefs(' MA/2024/117, MA/2024/118\nMA/2024/117,, ')).toEqual(['MA/2024/117', 'MA/2024/118']);
  });

  it('keeps text it does not recognise, so the API can reject it by name', () => {
    expect(parseVoteRefs('MA/2024/117, oops')).toEqual(['MA/2024/117', 'oops']);
  });

  it('gives nothing for an empty box', () => {
    expect(parseVoteRefs('  ')).toEqual([]);
  });
});
