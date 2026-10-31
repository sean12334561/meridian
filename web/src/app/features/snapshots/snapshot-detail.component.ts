import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Api } from '../../core/api/api';
import { getSnapshot, publishSnapshot } from '../../core/api/functions';
import { SnapshotDetailDto } from '../../core/api/models';
import { CurrentUserService } from '../../core/user/current-user.service';
import { PollResult, isPermanentFailure, poll } from '../../shared/poll';
import { Problem, applyServerErrors, errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

@Component({
  selector: 'app-snapshot-detail',
  imports: [
    DatePipe, DecimalPipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule,
    MatInputModule, MatProgressBarModule, StatusBadgeComponent,
  ],
  template: `
    <p><a routerLink="/snapshots">← Snapshots</a></p>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    @if (detail(); as current) {
      <h1>{{ current.snapshot.name }} <app-status-badge [value]="current.snapshot.status" /></h1>
      <p class="muted">Created by {{ current.snapshot.createdByName }}, {{ current.snapshot.createdAt | date: 'yyyy-MM-dd HH:mm' }}</p>

      @if (running()) {
        <p>Calculating…</p>
        <mat-progress-bar mode="indeterminate" />
      }

      @if (current.error) {
        <div class="banner error">{{ current.error }}</div>
      }

      @for (publication of current.publications; track publication.id) {
        @if (publication.withdrawnAt) {
          <div class="banner info">
            Previously published for {{ publication.quarter }}, superseded on {{ publication.withdrawnAt | date: 'yyyy-MM-dd HH:mm' }}.
          </div>
        } @else {
          <div class="banner ok">
            Published for {{ publication.quarter }} by {{ publication.publishedByName }},
            {{ publication.publishedAt | date: 'yyyy-MM-dd HH:mm' }}.
            @if (publication.reason) {
              Reason: {{ publication.reason }}
            }
          </div>
        }
      }

      <h2>Rules</h2>
      <p>
        Score = 100 × (matched weights + {{ current.rules.abstentionFactor }} × abstention weights) ÷ (weights of Y, N and A ballots).
        Absent isn't counted. A country needs at least {{ current.rules.minCountedVotes }} counted votes,
        otherwise it shows as insufficient data with no rank. Ballots from countries that weren't members on
        the vote's date are rejected at import, so they never count.
      </p>
      <p class="muted">
        {{ current.voteCount }} votes.
        @if (current.rules.excludedVotes.length > 0) {
          {{ current.rules.excludedVotes.length }} left out for having no position.
        }
        @if (current.pendingConflictsAtCreation) {
          {{ current.pendingConflictsAtCreation }} had pending conflicts at creation.
        }
      </p>

      @if (user.isAdmin() && current.snapshot.status === 'Completed') {
        <h2>Publish</h2>
        <form class="filters" [formGroup]="publish" (ngSubmit)="submitPublish()">
          <mat-form-field>
            <mat-label>Quarter, e.g. 2024-Q1</mat-label>
            <input matInput formControlName="quarter" />
            @if (publish.controls.quarter.hasError('server')) {
              <mat-error>{{ publish.controls.quarter.getError('server') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field class="reason">
            <mat-label>Reason (needed if the quarter has a publication)</mat-label>
            <input matInput formControlName="reason" />
            @if (publish.controls.reason.hasError('server')) {
              <mat-error>{{ publish.controls.reason.getError('server') }}</mat-error>
            }
          </mat-form-field>
          <button matButton="filled" type="submit" [disabled]="publishing()">Publish</button>
        </form>
      }

      @if (current.ranking.length > 0) {
        <h2>Ranking</h2>
        <table class="grid">
          <thead>
            <tr>
              <th class="number">Rank</th><th>Country</th><th class="number">Score</th>
              <th class="number">Counted votes</th>
            </tr>
          </thead>
          <tbody>
            @for (row of current.ranking; track row.countryCode) {
              <tr>
                <td class="number">{{ row.rank ?? '–' }}</td>
                <td>
                  <a [routerLink]="['/snapshots', current.snapshot.id, 'countries', row.countryCode]">{{ row.countryName }}</a>
                </td>
                <td class="number">
                  @if (row.score === null) {
                    <span class="muted">Insufficient data</span>
                  } @else {
                    {{ row.score | number: '1.1-1' }}
                  }
                </td>
                <td class="number">{{ row.countedVotes }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    }
  `,
  styles: `.reason { width: 26rem; }`,
})
export class SnapshotDetailComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly user = inject(CurrentUserService);

  readonly id = input.required<string>();

  protected readonly publish = new FormGroup({
    quarter: new FormControl('', { nonNullable: true }),
    reason: new FormControl('', { nonNullable: true }),
  });

  protected readonly detail = signal<SnapshotDetailDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly publishing = signal(false);
  protected readonly running = computed(() => this.isRunning(this.detail()));

  ngOnInit(): void {
    poll(() => this.loadDetail(), (result) => this.isFinal(result))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (result.value) {
          this.detail.set(result.value);
          this.error.set(null);
        } else {
          this.error.set(errorMessage(result.error));
        }
      });
  }

  protected async submitPublish(): Promise<void> {
    // A second click can land before the button shows as disabled
    if (this.publishing()) {
      return;
    }

    const { quarter, reason } = this.publish.getRawValue();
    this.error.set(null);
    this.publishing.set(true);
    try {
      await this.api.invoke(publishSnapshot, {
        id: Number(this.id()),
        body: { quarter, reason: reason || null },
      });
      this.detail.set(await this.loadDetail());
    } catch (error) {
      if (error instanceof Problem && error.status === 400 && applyServerErrors(this.publish, error).length === 0) {
        return;
      }
      this.error.set(errorMessage(error));
    } finally {
      this.publishing.set(false);
    }
  }

  private loadDetail(): Promise<SnapshotDetailDto> {
    return this.api.invoke(getSnapshot, { id: Number(this.id()) });
  }

  private isRunning(detail: SnapshotDetailDto | null | undefined): boolean {
    const status = detail?.snapshot.status;
    return status === 'Pending' || status === 'Running';
  }

  private isFinal(result: PollResult<SnapshotDetailDto>): boolean {
    return result.value ? !this.isRunning(result.value) : isPermanentFailure(result.error);
  }
}
