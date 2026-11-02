import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api/api';
import { getSnapshots, previewSnapshot } from '../../core/api/functions';
import { SnapshotPreviewDto } from '../../core/api/models';
import { SnapshotListComponent } from './snapshot-list.component';

const debounceMs = 450;

function preview(voteRef: string, missing: boolean, unknown: string[] = []): SnapshotPreviewDto {
  return {
    votes: {
      items: [{
        id: 1, voteRef, session: 79, voteDate: '2024-02-06', title: 'Title',
        position: missing ? null : 'Y', weight: missing ? null : 4, missingPosition: missing, openConflicts: 0,
      }],
      page: 1,
      pageSize: 10,
      total: 1,
    },
    missingPositions: missing ? 1 : 0,
    unknownVoteRefs: unknown,
  };
}

describe('SnapshotListComponent preview', () => {
  let answers: Array<(value: SnapshotPreviewDto) => void>;
  let bodies: Array<Record<string, unknown>>;

  beforeEach(() => {
    answers = [];
    bodies = [];
    const invoke = vi.fn((operation: unknown, params?: { body: Record<string, unknown> }) => {
      if (operation === getSnapshots) {
        return Promise.resolve([]);
      }
      if (operation === previewSnapshot) {
        bodies.push(params!.body);
        return new Promise<SnapshotPreviewDto>((resolve) => answers.push(resolve));
      }
      return Promise.reject(new Error('Unexpected call'));
    });

    TestBed.configureTestingModule({
      imports: [SnapshotListComponent],
      providers: [provideRouter([]), { provide: Api, useValue: { invoke } }],
    });
  });

  async function wait(fixture: ComponentFixture<SnapshotListComponent>, ms = 0): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, ms));
    await fixture.whenStable();
  }

  function type(page: HTMLElement, control: string, value: string): void {
    const input = page.querySelector<HTMLInputElement>(`input[formControlName=${control}]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('asks for a selection until there is one', async () => {
    const fixture = TestBed.createComponent(SnapshotListComponent);
    await wait(fixture);

    expect(fixture.nativeElement.textContent).toContain('Choose dates, a session or vote references');
    expect(bodies).toEqual([]);
  });

  it('shows the selected votes, marking those with no position and references not found', async () => {
    const fixture = TestBed.createComponent(SnapshotListComponent);
    await wait(fixture);
    const page: HTMLElement = fixture.nativeElement;

    type(page, 'voteRefs', 'MA/2024/117, MA/2024/999');
    await wait(fixture, debounceMs);

    expect(bodies[0]['voteRefs']).toEqual(['MA/2024/117', 'MA/2024/999']);
    answers[0](preview('MA/2024/117', true, ['MA/2024/999']));
    await wait(fixture);

    expect(page.querySelector('.selection-total')!.textContent!.trim()).toBe('1 vote selected, 1 with no position');
    expect(page.querySelector('table')!.textContent).toContain('MA/2024/117');
    expect(page.querySelector('.no-position')!.textContent).toBe('No position');
    expect(page.querySelector('.banner.error')!.textContent).toContain('Not found: MA/2024/999');
  });

  it('keeps the newest selection when an older answer arrives late', async () => {
    const fixture = TestBed.createComponent(SnapshotListComponent);
    await wait(fixture);
    const page: HTMLElement = fixture.nativeElement;

    type(page, 'voteRefs', 'MA/2024/001');
    await wait(fixture, debounceMs);
    type(page, 'voteRefs', 'MA/2024/002');
    await wait(fixture, debounceMs);

    answers[1](preview('MA/2024/002', false));
    answers[0](preview('MA/2024/001', false));
    await wait(fixture);

    expect(page.querySelector('table')!.textContent).toContain('MA/2024/002');
    expect(page.querySelector('table')!.textContent).not.toContain('MA/2024/001');
  });

  it('clears the preview when the selection is emptied', async () => {
    const fixture = TestBed.createComponent(SnapshotListComponent);
    await wait(fixture);
    const page: HTMLElement = fixture.nativeElement;

    type(page, 'voteRefs', 'MA/2024/001');
    await wait(fixture, debounceMs);
    answers[0](preview('MA/2024/001', false));
    await wait(fixture);

    type(page, 'voteRefs', '');
    await wait(fixture, debounceMs);

    expect(page.textContent).toContain('Choose dates, a session or vote references');
    expect(page.textContent).not.toContain('MA/2024/001');
    expect(bodies.length).toBe(1);
  });
});
