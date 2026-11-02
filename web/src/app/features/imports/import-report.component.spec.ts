import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api/api';
import { getImport, getImportReport, getImportRows } from '../../core/api/functions';
import { ImportReportComponent } from './import-report.component';

function report(normalized: number) {
  return {
    totals: {
      rows: 1, new: 1, unchanged: 0, duplicate: 0, invalid: 0, contradiction: 0,
      conflict: 0, alreadyPending: 0, previouslyDecided: 0, normalized,
    },
    conflictsOpened: 0,
    votes: [],
  };
}

describe('ImportReportComponent', () => {
  let normalized: number;
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    normalized = 0;
    invoke = vi.fn((operation: unknown, params?: { Normalized?: boolean }) => {
      if (operation === getImport) {
        return Promise.resolve({ fileName: 'q1.csv', status: 'Completed', processedRows: 1, totalRows: 1 });
      }
      if (operation === getImportReport) {
        return Promise.resolve(report(normalized));
      }
      if (operation === getImportRows) {
        const items = params?.Normalized
          ? [{
              lineNo: 7,
              outcome: 'New',
              rawText: 'raw',
              normalizations: '[{"field":"country_code","original":"deu","value":"DEU"}]',
            }]
          : [];
        return Promise.resolve({ items, total: items.length });
      }
      return Promise.reject(new Error('Unexpected call'));
    });

    TestBed.configureTestingModule({
      imports: [ImportReportComponent],
      providers: [provideRouter([]), { provide: Api, useValue: { invoke } }],
    });
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(ImportReportComponent);
    fixture.componentRef.setInput('id', 'import-1');
    for (let i = 0; i < 5; i++) {
      await new Promise((resolve) => setTimeout(resolve));
      await fixture.whenStable();
    }
    return fixture.nativeElement;
  }

  it('says so when rows were normalized and shows each change on request', async () => {
    normalized = 1;
    const page = await render();

    expect(page.querySelector('.banner.info')!.textContent).toContain('1 row was normalized');
    expect(page.querySelector('tbody')!.textContent).not.toContain('country_code');

    page.querySelector<HTMLButtonElement>('.banner.info button')!.click();
    await new Promise((resolve) => setTimeout(resolve));
    await new Promise((resolve) => setTimeout(resolve));

    expect(page.querySelector('.change')!.textContent).toBe('country_code: "deu" → "DEU"');
  });

  it('shows no notice when nothing was normalized', async () => {
    const page = await render();

    expect(page.querySelector('.banner.info')).toBeNull();
  });
});
