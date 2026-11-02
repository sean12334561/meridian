import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { Subject, debounceTime, merge, switchMap } from 'rxjs';
import { Api } from '../../core/api/api';
import { createSnapshot, getSnapshots, previewSnapshot } from '../../core/api/functions';
import { SnapshotPreviewDto, SnapshotSummaryDto } from '../../core/api/models';
import { PollResult, isPermanentFailure, poll } from '../../shared/poll';
import { Problem, applyServerErrors, errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';
import { parseVoteRefs } from './vote-refs';

const previewPageSize = 10;

interface MissingPosition {
  id: number;
  voteRef: string;
}

@Component({
  selector: 'app-snapshot-list',
  imports: [
    DatePipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatCheckboxModule,
    MatFormFieldModule, MatInputModule, MatPaginatorModule, StatusBadgeComponent,
  ],
  template: `
    <h1>Snapshots</h1>

    @if (error() ?? refreshError(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    <h2>New snapshot</h2>
    <form [formGroup]="form" (ngSubmit)="create()">
      <div class="filters">
        <mat-form-field>
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
          @if (form.controls.name.hasError('server')) {
            <mat-error>{{ form.controls.name.getError('server') }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field class="date">
          <mat-label>From</mat-label>
          <input matInput type="date" formControlName="from" />
        </mat-form-field>
        <mat-form-field class="date">
          <mat-label>To</mat-label>
          <input matInput type="date" formControlName="to" />
          @if (form.controls.to.hasError('server')) {
            <mat-error>{{ form.controls.to.getError('server') }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field class="short">
          <mat-label>Session</mat-label>
          <input matInput type="number" formControlName="session" />
        </mat-form-field>
        <mat-form-field class="short">
          <mat-label>Minimum votes</mat-label>
          <input matInput type="number" formControlName="minCountedVotes" />
          @if (form.controls.minCountedVotes.hasError('server')) {
            <mat-error>{{ form.controls.minCountedVotes.getError('server') }}</mat-error>
          }
        </mat-form-field>
      </div>
      <mat-form-field class="wide">
        <mat-label>Vote references, separated by commas, e.g. MA/2024/117</mat-label>
        <input matInput formControlName="voteRefs" />
        @if (form.controls.voteRefs.hasError('server')) {
          <mat-error>{{ form.controls.voteRefs.getError('server') }}</mat-error>
        }
      </mat-form-field>

      <h3>Selected votes</h3>
      @if (previewError(); as message) {
        <div class="banner error">{{ message }}</div>
      }
      @if (preview(); as selection) {
        <p class="selection-total">
          {{ selection.votes.total }} {{ selection.votes.total === 1 ? 'vote' : 'votes' }} selected@if (selection.missingPositions > 0) {, {{ selection.missingPositions }} with no position}
        </p>
        @if (selection.unknownVoteRefs.length > 0) {
          <div class="banner error">Not found: {{ selection.unknownVoteRefs.join(', ') }}</div>
        }
        @if (selection.votes.total > 0) {
          <table class="grid">
            <thead>
              <tr>
                <th>Vote</th><th>Date</th><th class="number">Session</th><th>Title</th>
                <th>Position</th><th class="number">Weight</th>
              </tr>
            </thead>
            <tbody>
              @for (vote of selection.votes.items; track vote.id) {
                <tr>
                  <td><a [routerLink]="['/votes', vote.id]">{{ vote.voteRef }}</a></td>
                  <td>{{ vote.voteDate }}</td>
                  <td class="number">{{ vote.session }}</td>
                  <td>{{ vote.title }}</td>
                  <td>
                    @if (vote.missingPosition) {
                      <span class="no-position">No position</span>
                    } @else {
                      {{ vote.position }}
                    }
                  </td>
                  <td class="number">{{ vote.weight }}</td>
                </tr>
              }
            </tbody>
          </table>
          <mat-paginator
            [length]="selection.votes.total"
            [pageSize]="previewPageSize"
            [pageIndex]="previewPage() - 1"
            hidePageSize
            (page)="selectPreviewPage($event)"
          />
        }
      } @else if (!previewError()) {
        <p class="muted">Choose dates, a session or vote references to see the votes here.</p>
      }

      @if (missing().length > 0) {
        <div class="banner info">
          {{ missing().length }} selected votes have no position:
          @for (vote of missing().slice(0, 20); track vote.id) {
            <a [routerLink]="['/votes', vote.id]">{{ vote.voteRef }}</a>{{ ' ' }}
          }
          @if (missing().length > 20) {
            and {{ missing().length - 20 }} more.
          }
          <div><mat-checkbox formControlName="excludeMissingPositions">Leave them out of this snapshot</mat-checkbox></div>
        </div>
      }

      <button matButton="filled" type="submit" [disabled]="creating()">Create</button>
    </form>

    <h2>All snapshots</h2>
    <table class="grid">
      <thead><tr><th>Name</th><th>Created by</th><th>Created</th><th>Status</th><th>Published</th></tr></thead>
      <tbody>
        @for (snapshot of snapshots(); track snapshot.id) {
          <tr>
            <td><a [routerLink]="['/snapshots', snapshot.id]">{{ snapshot.name }}</a></td>
            <td>{{ snapshot.createdByName }}</td>
            <td>{{ snapshot.createdAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td><app-status-badge [value]="snapshot.status" /></td>
            <td>{{ snapshot.publishedQuarter }}</td>
          </tr>
        } @empty {
          <tr><td colspan="5" class="empty">No snapshots yet.</td></tr>
        }
      </tbody>
    </table>
  `,
  styles: `
    .short { width: 9rem; }
    .date { width: 10rem; }
    .wide { width: 100%; }
    .no-position { color: #8c1d18; }
  `,
})
export class SnapshotListComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    from: new FormControl('', { nonNullable: true }),
    to: new FormControl('', { nonNullable: true }),
    session: new FormControl<number | null>(null),
    voteRefs: new FormControl('', { nonNullable: true }),
    excludeMissingPositions: new FormControl(false, { nonNullable: true }),
    minCountedVotes: new FormControl<number | null>(1),
  });

  protected readonly previewPageSize = previewPageSize;
  protected readonly preview = signal<SnapshotPreviewDto | null>(null);
  protected readonly previewPage = signal(1);
  protected readonly previewError = signal<string | null>(null);
  private readonly previewPageChanges = new Subject<void>();

  protected readonly snapshots = signal<SnapshotSummaryDto[]>([]);
  protected readonly missing = signal<MissingPosition[]>([]);
  protected readonly creating = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly refreshError = signal<string | null>(null);

  constructor() {
    const { from, to, session, voteRefs } = this.form.controls;

    // An exclusion only covers the votes it listed, so a new selection has to ask again (POS-04)
    const selectionChanges = merge(from.valueChanges, to.valueChanges, session.valueChanges, voteRefs.valueChanges);
    selectionChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.missing.set([]);
      this.form.controls.excludeMissingPositions.setValue(false);
      this.previewPage.set(1);
    });

    // switchMap drops an older answer, so a slow response can't replace a newer selection
    merge(selectionChanges.pipe(debounceTime(400)), this.previewPageChanges)
      .pipe(switchMap(() => this.loadPreview()), takeUntilDestroyed())
      .subscribe((result) => this.showPreview(result));
  }

  // Keeps the status column current while a calculation runs (SNP-02)
  ngOnInit(): void {
    poll(() => this.api.invoke(getSnapshots), (result) => this.isFinal(result), 2000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (result.value) {
          this.snapshots.set(result.value);
          this.refreshError.set(null);
        } else {
          this.refreshError.set(errorMessage(result.error));
        }
      });
  }

  protected selectPreviewPage(event: PageEvent): void {
    this.previewPage.set(event.pageIndex + 1);
    this.previewPageChanges.next();
  }

  protected async create(): Promise<void> {
    const value = this.form.getRawValue();

    this.error.set(null);
    this.creating.set(true);
    try {
      const created = await this.api.invoke(createSnapshot, {
        body: {
          name: value.name,
          ...this.selection(),
          excludeMissingPositions: value.excludeMissingPositions,
          minCountedVotes: value.minCountedVotes ?? 0,
        },
      });
      await this.router.navigate(['/snapshots', created.snapshotId]);
    } catch (error) {
      this.showFailure(error);
    } finally {
      this.creating.set(false);
    }
  }

  private selection() {
    const { from, to, session, voteRefs } = this.form.getRawValue();
    const refs = parseVoteRefs(voteRefs);

    return { from: from || null, to: to || null, session, voteRefs: refs.length > 0 ? refs : null };
  }

  private async loadPreview(): Promise<PollResult<SnapshotPreviewDto | null>> {
    const selection = this.selection();
    if (!selection.from && !selection.to && selection.session === null && !selection.voteRefs) {
      return { value: null };
    }

    try {
      const body = { ...selection, page: this.previewPage(), pageSize: previewPageSize };
      return { value: await this.api.invoke(previewSnapshot, { body }) };
    } catch (error) {
      return { error };
    }
  }

  private showPreview(result: PollResult<SnapshotPreviewDto | null>): void {
    this.previewError.set(null);
    if (result.error === undefined) {
      this.preview.set(result.value ?? null);
      return;
    }

    this.preview.set(null);
    const shownOnField = result.error instanceof Problem && result.error.status === 400
      && applyServerErrors(this.form, result.error).length === 0;
    if (!shownOnField) {
      this.previewError.set(errorMessage(result.error));
    }
  }

  private isFinal(result: PollResult<SnapshotSummaryDto[]>): boolean {
    return result.value
      ? !result.value.some((snapshot) => snapshot.status === 'Pending' || snapshot.status === 'Running')
      : isPermanentFailure(result.error);
  }

  private showFailure(error: unknown): void {
    if (error instanceof Problem && error.body['missingPositions']) {
      this.missing.set(error.body['missingPositions'] as MissingPosition[]);
    } else if (error instanceof Problem && error.status === 400 && applyServerErrors(this.form, error).length === 0) {
      return;
    } else {
      this.error.set(errorMessage(error));
    }
  }
}
