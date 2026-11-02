import { ImportDto } from '../../core/api/models';

export interface ImportProgress {
  label: string;
  /** Null when there's no meaningful share of the work to show. */
  percent: number | null;
}

function count(value: number): string {
  return value.toLocaleString('en-US');
}

function counted(verb: string, item: ImportDto): ImportProgress {
  if (!item.totalRows) {
    return { label: `${verb} rows`, percent: null };
  }

  return {
    label: `${verb} ${count(item.processedRows)} of ${count(item.totalRows)} rows`,
    percent: (100 * item.processedRows) / item.totalRows,
  };
}

/**
 * processedRows means something different in each phase: rows read while Parsing, all of them while
 * Checking, then rows with their final outcome while Applying. So the count falls when Applying starts,
 * and showing it without its phase looks like an import going backwards.
 */
export function importProgress(item: ImportDto): ImportProgress {
  const running = item.status === 'Pending' || item.status === 'Running';
  if (running) {
    switch (item.phase) {
      case 'Queued':
        return { label: 'Queued', percent: null };
      case 'Parsing':
        return counted('Reading', item);
      case 'Checking':
        return { label: 'Checking for duplicates and contradictions', percent: null };
      case 'Applying':
        return counted('Applying', item);
    }
  }

  return { label: `${count(item.processedRows)} / ${item.totalRows === null || item.totalRows === undefined ? '…' : count(item.totalRows)}`, percent: null };
}
