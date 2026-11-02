import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { Api } from '../../core/api/api';
import { getSnapshot, getSnapshotVotes } from '../../core/api/functions';
import { CurrentUserService } from '../../core/user/current-user.service';
import { SnapshotDetailComponent } from './snapshot-detail.component';

function fakeApi(status: string): ReturnType<typeof vi.fn> {
  return vi.fn((operation: unknown, params?: { Page: number }) => {
    if (operation === getSnapshot) {
      return Promise.resolve({
        snapshot: { id: 5, name: 'Q1', status, createdByName: 'Dana', createdAt: '2026-11-01T10:00:00Z' },
        rules: { abstentionFactor: 0.5, minCountedVotes: 1, excludedVotes: [] },
        voteCount: 11,
        ranking: [],
        publications: [],
      });
    }
    if (operation === getSnapshotVotes) {
      return Promise.resolve({
        items: [{ id: 1, voteRef: 'MA/2024/001', session: 79, voteDate: '2024-02-06', title: 'Title', position: 'Y', weight: 4 }],
        page: params!.Page,
        pageSize: 10,
        total: 11,
      });
    }
    return Promise.reject(new Error('Unexpected call'));
  });
}

async function render(invoke: ReturnType<typeof vi.fn>): Promise<HTMLElement> {
  TestBed.configureTestingModule({
    imports: [SnapshotDetailComponent],
    providers: [
      provideRouter([]),
      { provide: Api, useValue: { invoke } },
      { provide: CurrentUserService, useValue: { isAdmin: () => false } },
    ],
  });

  const fixture = TestBed.createComponent(SnapshotDetailComponent);
  fixture.componentRef.setInput('id', '5');
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
  }

  return fixture.nativeElement;
}

describe('SnapshotDetailComponent votes', () => {
  it('lists the votes of a completed snapshot', async () => {
    const invoke = fakeApi('Completed');
    const page = await render(invoke);

    expect(page.querySelector('table')!.textContent).toContain('MA/2024/001');
    expect(invoke).toHaveBeenCalledWith(getSnapshotVotes, { id: 5, Page: 1, PageSize: 10 });
  });

  it('waits for the calculation before listing them', async () => {
    const invoke = fakeApi('Running');
    const page = await render(invoke);

    expect(page.querySelector('table')).toBeNull();
    expect(invoke).not.toHaveBeenCalledWith(getSnapshotVotes, expect.anything());
  });
});
