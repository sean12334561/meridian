import { describe, expect, it } from 'vitest';
import { ImportDto, ImportPhase, ImportStatus } from '../../core/api/models';
import { importProgress } from './import-progress';

function item(status: ImportStatus, phase: ImportPhase, processedRows: number, totalRows: number | null): ImportDto {
  return { status, phase, processedRows, totalRows } as ImportDto;
}

describe('importProgress', () => {
  it('names the phase while reading the file', () => {
    expect(importProgress(item('Running', 'Parsing', 250_000, 1_000_000))).toEqual({
      label: 'Reading 250,000 of 1,000,000 rows',
      percent: 25,
    });
  });

  it('shows no count while checking, because every row has been read but none is finished', () => {
    expect(importProgress(item('Running', 'Checking', 962_171, 962_171))).toEqual({
      label: 'Checking for duplicates and contradictions',
      percent: null,
    });
  });

  it('shows the lower count at the start of applying as applying, not as the same count falling', () => {
    expect(importProgress(item('Running', 'Applying', 62, 962_171)).label).toBe('Applying 62 of 962,171 rows');
  });

  it('shows a queued import without a count', () => {
    expect(importProgress(item('Pending', 'Queued', 0, null))).toEqual({ label: 'Queued', percent: null });
  });

  it('copes with a running import whose total is not known yet', () => {
    expect(importProgress(item('Running', 'Parsing', 0, null))).toEqual({ label: 'Reading rows', percent: null });
  });

  it('shows plain counts once finished', () => {
    expect(importProgress(item('Completed', 'Done', 5_896, 5_896)).label).toBe('5,896 / 5,896');
  });

  it('shows where a failed import stopped, not a phase that is no longer running', () => {
    expect(importProgress(item('Failed', 'Applying', 74_061, 962_171)).label).toBe('74,061 / 962,171');
    expect(importProgress(item('Failed', 'Parsing', 0, null)).label).toBe('0 / …');
  });
});
