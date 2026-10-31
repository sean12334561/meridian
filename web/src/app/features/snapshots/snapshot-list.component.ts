import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Api } from '../../core/api/api';
import { createSnapshot, getSnapshots } from '../../core/api/functions';
import { SnapshotSummaryDto } from '../../core/api/models';
import { Problem, applyServerErrors, errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

interface MissingPosition {
  id: number;
  voteRef: string;
}

@Component({
  selector: 'app-snapshot-list',
  imports: [
    DatePipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatCheckboxModule,
    MatFormFieldModule, MatInputModule, StatusBadgeComponent,
  ],
  template: `
    <h1>Snapshots</h1>

    @if (error(); as message) {
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
        <mat-label>Vote IDs, separated by commas</mat-label>
        <input matInput formControlName="voteIds" />
        @if (form.controls.voteIds.hasError('server')) {
          <mat-error>{{ form.controls.voteIds.getError('server') }}</mat-error>
        }
      </mat-form-field>

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
  `,
})
export class SnapshotListComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    from: new FormControl('', { nonNullable: true }),
    to: new FormControl('', { nonNullable: true }),
    session: new FormControl<number | null>(null),
    voteIds: new FormControl('', { nonNullable: true }),
    excludeMissingPositions: new FormControl(false, { nonNullable: true }),
    minCountedVotes: new FormControl<number | null>(1),
  });

  protected readonly snapshots = signal<SnapshotSummaryDto[]>([]);
  protected readonly missing = signal<MissingPosition[]>([]);
  protected readonly creating = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.snapshots.set(await this.api.invoke(getSnapshots));
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }

  protected async create(): Promise<void> {
    const value = this.form.getRawValue();
    const voteIds = value.voteIds.split(',').map((id) => Number(id.trim())).filter((id) => id > 0);

    this.error.set(null);
    this.creating.set(true);
    try {
      const created = await this.api.invoke(createSnapshot, {
        body: {
          name: value.name,
          from: value.from || null,
          to: value.to || null,
          session: value.session,
          voteIds: voteIds.length > 0 ? voteIds : null,
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
