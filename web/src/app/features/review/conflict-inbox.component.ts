import { DatePipe } from '@angular/common';
import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { Api } from '../../core/api/api';
import { getConflicts } from '../../core/api/functions';
import { ConflictStatus, PagedResultOfConflictDto } from '../../core/api/models';
import { errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

@Component({
  selector: 'app-conflict-inbox',
  imports: [DatePipe, FormsModule, RouterLink, MatPaginatorModule, MatSelectModule, StatusBadgeComponent],
  template: `
    <h1>Review</h1>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    <div class="filters">
      <mat-select class="status" [ngModel]="status()" (ngModelChange)="selectStatus($event)" aria-label="Status">
        @for (option of statuses; track option) {
          <mat-option [value]="option">{{ option }}</mat-option>
        }
      </mat-select>
      @if (voteId(); as vote) {
        <span class="muted">Only vote {{ vote }}. <a routerLink="/review">Show all</a></span>
      }
    </div>

    <table class="grid">
      <thead>
        <tr>
          <th>Vote</th><th>Target</th><th>Approved</th><th>Proposed</th><th>Source file</th>
          <th>Uploaded by</th><th>Uploaded</th><th>Status</th>
        </tr>
      </thead>
      <tbody>
        @for (conflict of conflicts()?.items; track conflict.id) {
          <tr>
            <td><a [routerLink]="['/review', conflict.id]">{{ conflict.voteRef }}</a></td>
            <td>{{ conflict.target }}</td>
            <td>{{ conflict.approvedValue }}</td>
            <td>{{ conflict.proposedValue }}</td>
            <td>{{ conflict.proposedSource.fileName }}</td>
            <td>{{ conflict.proposedSource.uploadedByName }}</td>
            <td>{{ conflict.proposedSource.uploadedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td><app-status-badge [value]="conflict.status" /></td>
          </tr>
        } @empty {
          <tr><td colspan="8" class="empty">Nothing here.</td></tr>
        }
      </tbody>
    </table>
    <mat-paginator [length]="conflicts()?.total ?? 0" [pageSize]="50" [pageIndex]="page() - 1" hidePageSize (page)="selectPage($event)" />
  `,
  styles: `.status { width: 10rem; }`,
})
export class ConflictInboxComponent {
  private readonly api = inject(Api);

  readonly voteId = input<string>();

  protected readonly statuses: ConflictStatus[] = ['Pending', 'Accepted', 'Rejected', 'Superseded'];
  protected readonly status = signal<ConflictStatus>('Pending');
  protected readonly page = signal(1);
  protected readonly conflicts = signal<PagedResultOfConflictDto | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.voteId();
      untracked(() => void this.load());
    });
  }

  protected selectStatus(status: ConflictStatus): void {
    this.status.set(status);
    this.page.set(1);
    void this.load();
  }

  protected selectPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.load();
  }

  private async load(): Promise<void> {
    const voteId = this.voteId();
    try {
      this.conflicts.set(
        await this.api.invoke(getConflicts, {
          Status: this.status(),
          VoteId: voteId ? Number(voteId) : undefined,
          Page: this.page(),
          PageSize: 50,
        }),
      );
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
