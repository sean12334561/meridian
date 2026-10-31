import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Api } from '../../core/api/api';
import { getVote, getVoteTimeline, setPosition } from '../../core/api/functions';
import { TimelineEntryDto, VoteDetailDto } from '../../core/api/models';
import { CurrentUserService } from '../../core/user/current-user.service';
import { Problem, applyServerErrors, errorMessage } from '../../shared/problem-details';

@Component({
  selector: 'app-vote-detail',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <p><a routerLink="/votes">← Votes</a></p>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    @if (detail(); as current) {
      <h1>{{ current.vote.voteRef }}</h1>
      <p>{{ current.vote.title }}</p>
      <p class="muted">Session {{ current.vote.session }}, {{ current.vote.voteDate }}</p>

      @if (current.vote.openConflicts > 0) {
        <div class="banner info">
          <a routerLink="/review" [queryParams]="{ voteId: current.vote.id }">{{ current.vote.openConflicts }} open conflicts</a>
        </div>
      }

      <h2>Position</h2>
      @if (user.isAdmin()) {
        <form class="filters" [formGroup]="form" (ngSubmit)="save()">
          <mat-form-field>
            <mat-label>Position</mat-label>
            <mat-select formControlName="desiredOutcome">
              <mat-option value="Y">For</mat-option>
              <mat-option value="N">Against</mat-option>
            </mat-select>
            @if (form.controls.desiredOutcome.hasError('server')) {
              <mat-error>{{ form.controls.desiredOutcome.getError('server') }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field>
            <mat-label>Weight (1 to 5)</mat-label>
            <input matInput type="number" formControlName="weight" />
            @if (form.controls.weight.hasError('server')) {
              <mat-error>{{ form.controls.weight.getError('server') }}</mat-error>
            }
          </mat-form-field>
          <button matButton="filled" type="submit">Save</button>
        </form>
      } @else if (current.vote.missingPosition) {
        <p>No position yet. Votes without a position stay out of rankings.</p>
      } @else {
        <p>{{ current.vote.position === 'Y' ? 'For' : 'Against' }}, weight {{ current.vote.weight }}</p>
      }

      <h2>Timeline</h2>
      <table class="grid">
        <thead><tr><th>When</th><th>What</th><th>Who</th><th>Target</th><th>Change</th><th>Source</th></tr></thead>
        <tbody>
          @for (entry of timeline(); track entry.type + entry.id) {
            <tr>
              <td>{{ entry.at | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td>{{ entry.type }}</td>
              <td>{{ entry.actorName }}</td>
              <td>{{ entry.target }}</td>
              <td class="mono">{{ change(entry) }}</td>
              <td>
                @if (entry.importId) {
                  <a [routerLink]="['/imports', entry.importId]">{{ entry.fileName }}</a>
                }
                @if (entry.conflictId) {
                  <a [routerLink]="['/review', entry.conflictId]">Conflict</a>
                }
                @if (entry.snapshotId) {
                  <a [routerLink]="['/snapshots', entry.snapshotId]">Snapshot</a>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>

      <h2>Country votes</h2>
      <table class="grid">
        <thead><tr><th>Country</th><th>Ballot</th><th>Source</th><th class="number">Line</th><th></th></tr></thead>
        <tbody>
          @for (ballot of current.countryVotes; track ballot.countryCode) {
            <tr>
              <td>{{ ballot.countryName }} ({{ ballot.countryCode }})</td>
              <td>{{ ballot.ballot }}</td>
              <td><a [routerLink]="['/imports', ballot.sourceImportId]">{{ ballot.sourceFileName }}</a></td>
              <td class="number">{{ ballot.sourceLine }}</td>
              <td>
                @if (ballot.changedByDecision) {
                  <span class="muted">Changed by a decision</span>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class VoteDetailComponent implements OnInit {
  private readonly api = inject(Api);
  protected readonly user = inject(CurrentUserService);

  readonly id = input.required<string>();

  protected readonly form = new FormGroup({
    desiredOutcome: new FormControl<string | null>(null),
    weight: new FormControl<number | null>(null),
  });

  protected readonly detail = signal<VoteDetailDto | null>(null);
  protected readonly timeline = signal<TimelineEntryDto[]>([]);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): Promise<void> {
    return this.load();
  }

  protected change(entry: TimelineEntryDto): string {
    if (entry.oldValue && entry.newValue) {
      return `${entry.oldValue} → ${entry.newValue}`;
    }

    return entry.newValue ?? '';
  }

  protected async save(): Promise<void> {
    const { desiredOutcome, weight } = this.form.getRawValue();
    this.error.set(null);
    try {
      await this.api.invoke(setPosition, {
        id: Number(this.id()),
        body: { desiredOutcome, weight: weight ?? 0 },
      });
      await this.load();
    } catch (error) {
      if (error instanceof Problem && error.status === 400 && applyServerErrors(this.form, error).length === 0) {
        return;
      }
      this.error.set(errorMessage(error));
    }
  }

  private async load(): Promise<void> {
    const id = Number(this.id());
    try {
      const [detail, timeline] = await Promise.all([
        this.api.invoke(getVote, { id }),
        this.api.invoke(getVoteTimeline, { id }),
      ]);
      this.detail.set(detail);
      this.timeline.set(timeline);
      this.form.patchValue({ desiredOutcome: detail.vote.position, weight: detail.vote.weight });
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
