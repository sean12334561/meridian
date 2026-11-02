import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api/api';
import { getImports, uploadImport } from '../../core/api/functions';
import { ImportCreatedDto, ImportDto } from '../../core/api/models';
import { ImportListComponent } from './import-list.component';

interface Upload {
  fileName: string;
  finish: (created: ImportCreatedDto) => void;
  fail: (error: Error) => void;
}

function importRow(fileName: string): ImportDto {
  return {
    id: fileName,
    fileName,
    uploadedByName: 'Dana',
    uploadedAt: '2026-11-01T10:00:00Z',
    status: 'Pending',
    processedRows: 0,
    totalRows: null,
  } as ImportDto;
}

describe('ImportListComponent', () => {
  let rows: ImportDto[];
  let uploads: Upload[];
  let invoke: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    rows = [];
    uploads = [];
    invoke = vi.fn((operation: unknown, params?: { body: { file: File } }) => {
      if (operation === getImports) {
        return Promise.resolve(rows);
      }
      if (operation === uploadImport) {
        return new Promise<ImportCreatedDto>((resolve, reject) =>
          uploads.push({ fileName: params!.body.file.name, finish: resolve, fail: reject }),
        );
      }
      return Promise.reject(new Error('Unexpected call'));
    });

    TestBed.configureTestingModule({
      imports: [ImportListComponent],
      providers: [provideRouter([]), { provide: Api, useValue: { invoke } }],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  // An upload's continuations run after whenStable would return, so let them finish first
  async function settle(fixture: ComponentFixture<ImportListComponent>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  async function render() {
    const fixture = TestBed.createComponent(ImportListComponent);
    await settle(fixture);
    return fixture;
  }

  function choose(page: HTMLElement, fileName: string): void {
    const input = page.querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: [new File(['a'], fileName)] });
    input.dispatchEvent(new Event('change'));
  }

  function uploadButton(page: HTMLElement): HTMLButtonElement {
    return page.querySelector<HTMLButtonElement>('button')!;
  }

  it('stays on the list after an upload and shows the new import straight away', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    const fixture = await render();
    const page: HTMLElement = fixture.nativeElement;

    choose(page, 'first.csv');
    await settle(fixture);
    rows = [importRow('first.csv')];
    uploads[0].finish({ importId: 'first' } as ImportCreatedDto);
    await settle(fixture);

    expect(navigate).not.toHaveBeenCalled();
    expect(page.querySelector('tbody')!.textContent).toContain('first.csv');
    expect(uploadButton(page).disabled).toBe(false);
  });

  it('accepts a second file straight away and sends it after the first, in the order chosen', async () => {
    const fixture = await render();
    const page: HTMLElement = fixture.nativeElement;

    choose(page, 'first.csv');
    await settle(fixture);
    expect(uploadButton(page).disabled).toBe(false);

    choose(page, 'second.csv');
    await settle(fixture);

    expect(uploads.map((upload) => upload.fileName)).toEqual(['first.csv']);
    expect(page.textContent).toContain('Uploading 2 files');

    uploads[0].finish({ importId: 'first' } as ImportCreatedDto);
    await settle(fixture);
    expect(uploads.map((upload) => upload.fileName)).toEqual(['first.csv', 'second.csv']);
    expect(page.textContent).toContain('Uploading 1 file');

    uploads[1].finish({ importId: 'second' } as ImportCreatedDto);
    await settle(fixture);
    expect(page.textContent).not.toContain('Uploading');
  });

  it('names the file that failed and still sends the next one', async () => {
    const fixture = await render();
    const page: HTMLElement = fixture.nativeElement;

    choose(page, 'first.csv');
    choose(page, 'second.csv');
    await settle(fixture);
    uploads[0].fail(new Error('Refused'));
    await settle(fixture);

    expect(page.querySelector('.banner.error')!.textContent).toContain('first.csv');
    expect(uploads.map((upload) => upload.fileName)).toEqual(['first.csv', 'second.csv']);
    expect(page.textContent).toContain('Uploading 1 file');
    uploads[1].finish({ importId: 'second' } as ImportCreatedDto);
    await settle(fixture);
  });
});
