import { Component, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { Api } from '../../core/api/api';
import { getVotes, setPositionsBulk } from '../../core/api/functions';
import { GetVotes$Params } from '../../core/api/fn/votes/get-votes';
import { PagedResultOfVoteDto } from '../../core/api/models';
import { CurrentUserService } from '../../core/user/current-user.service';
import { applyServerErrors, errorMessage, Problem } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

@Component({
  selector: 'app-vote-list',
  imports: [
    ReactiveFormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatFormFieldModule,
    MatInputModule, MatPaginatorModule, MatSelectModule, StatusBadgeComponent,
  ],
  template: `
    <h1>Votes</h1>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }
    @if (notice(); as message) {
      <div class="banner ok">{{ message }}</div>
    }

    <form class="filters" [formGroup]="filters" (ngSubmit)="search()">
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>Reference or title</mat-label>
        <input matInput formControlName="q" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic" class="short">
        <mat-label>Session</mat-label>
        <input matInput type="number" formControlName="session" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic" class="date">
        <mat-label>From</mat-label>
        <input matInput type="date" formControlName="from" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic" class="date">
        <mat-label>To</mat-label>
        <input matInput type="date" formControlName="to" />
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic" class="date">
        <mat-label>Position</mat-label>
        <mat-select formControlName="hasPosition">
          <mat-option value="">Any</mat-option>
          <mat-option value="true">Set</mat-option>
          <mat-option value="false">Missing</mat-option>
        </mat-select>
      </mat-form-field>
      <button matButton="outlined" type="submit">Search</button>
    </form>

    <table class="grid">
      <thead>
        <tr>
          <th>Vote</th><th>Date</th><th>Session</th><th>Title</th><th>Position</th>
          <th class="number">Weight</th><th class="number">Open conflicts</th>
        </tr>
      </thead>
      <tbody>
        @for (vote of votes()?.items; track vote.id) {
          <tr>
            <td><a [routerLink]="['/votes', vote.id]">{{ vote.voteRef }}</a></td>
            <td>{{ vote.voteDate }}</td>
            <td>{{ vote.session }}</td>
            <td>{{ vote.title }}</td>
            <td>
              @if (vote.missingPosition) {
                <app-status-badge value="No position" />
              } @else {
                {{ vote.position === 'Y' ? 'For' : 'Against' }}
              }
            </td>
            <td class="number">{{ vote.weight }}</td>
            <td class="number">
              @if (vote.openConflicts > 0) {
                <a routerLink="/review" [queryParams]="{ voteId: vote.id }">{{ vote.openConflicts }}</a>
              } @else {
                0
              }
            </td>
          </tr>
        } @empty {
          <tr><td colspan="7" class="empty">No votes match.</td></tr>
        }
      </tbody>
    </table>
    <mat-paginator [length]="votes()?.total ?? 0" [pageSize]="50" [pageIndex]="page() - 1" hidePageSize (page)="selectPage($event)" />

    @if (current.isAdmin()) {
      <h2>Set position for up to {{ votes()?.total ?? 0 }} votes</h2>
      <p class="muted">Applies to the votes listed above, as last searched. Votes that already have these values are skipped.</p>
      @if (confirming()) {
        <div class="banner info">
          This replaces existing positions on up to {{ votes()?.total ?? 0 }} votes. It can only be undone vote by vote.
          <div class="filters">
            <button matButton="filled" type="button" [disabled]="applying()" (click)="applyBulk()">Replace positions</button>
            <button matButton="outlined" type="button" (click)="confirming.set(false)">Cancel</button>
          </div>
        </div>
      }
      <form class="filters" [formGroup]="bulk" (ngSubmit)="requestBulk()">
        <mat-form-field class="date">
          <mat-label>Position</mat-label>
          <mat-select formControlName="desiredOutcome">
            <mat-option value="Y">For</mat-option>
            <mat-option value="N">Against</mat-option>
          </mat-select>
          @if (bulk.controls.desiredOutcome.hasError('server')) {
            <mat-error>{{ bulk.controls.desiredOutcome.getError('server') }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field class="short">
          <mat-label>Weight</mat-label>
          <input matInput type="number" formControlName="weight" />
          @if (bulk.controls.weight.hasError('server')) {
            <mat-error>{{ bulk.controls.weight.getError('server') }}</mat-error>
          }
        </mat-form-field>
        <mat-checkbox formControlName="onlyMissing">Only votes without a position</mat-checkbox>
        <button matButton="filled" type="submit" [disabled]="applying() || confirming()">Apply</button>
      </form>
    }
  `,
  styles: `
    .short { width: 7rem; }
    .date { width: 10rem; }
  `,
})
export class VoteListComponent implements OnInit {
  private readonly api = inject(Api);
  protected readonly current = inject(CurrentUserService);

  protected readonly filters = new FormGroup({
    q: new FormControl('', { nonNullable: true }),
    session: new FormControl<number | null>(null),
    from: new FormControl('', { nonNullable: true }),
    to: new FormControl('', { nonNullable: true }),
    hasPosition: new FormControl('', { nonNullable: true }),
  });

  protected readonly bulk = new FormGroup({
    desiredOutcome: new FormControl<string | null>(null),
    weight: new FormControl<number | null>(null),
    onlyMissing: new FormControl(true, { nonNullable: true }),
  });

  protected readonly votes = signal<PagedResultOfVoteDto | null>(null);
  protected readonly page = signal(1);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly applying = signal(false);
  protected readonly confirming = signal(false);

  /** What the table shows. Paging and bulk use this, never the form, which may hold unsearched edits. */
  private searched = this.readFilters();

  constructor() {
    this.bulk.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.confirming.set(false));
  }

  ngOnInit(): Promise<void> {
    return this.load();
  }

  protected search(): void {
    this.searched = this.readFilters();
    this.confirming.set(false);
    this.page.set(1);
    void this.load();
  }

  protected selectPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    void this.load();
  }

  // Filling gaps can't lose anything, so only replacing existing positions asks first
  protected requestBulk(): void {
    if (this.bulk.controls.onlyMissing.value) {
      void this.applyBulk();
    } else {
      this.confirming.set(true);
    }
  }

  protected async applyBulk(): Promise<void> {
    // A second click can land before the button shows as disabled
    if (this.applying()) {
      return;
    }

    const { desiredOutcome, weight, onlyMissing } = this.bulk.getRawValue();
    const filter = this.searched;
    this.notice.set(null);
    this.error.set(null);
    this.applying.set(true);
    try {
      const result = await this.api.invoke(setPositionsBulk, {
        body: {
          q: filter.Q,
          session: filter.Session,
          from: filter.From,
          to: filter.To,
          hasPosition: filter.HasPosition,
          desiredOutcome,
          weight: weight ?? 0,
          onlyMissing,
        },
      });
      this.notice.set(`Updated ${result.updated} votes.`);
      await this.load();
    } catch (error) {
      if (error instanceof Problem && applyServerErrors(this.bulk, error).length === 0 && error.status === 400) {
        return;
      }
      this.error.set(errorMessage(error));
    } finally {
      this.applying.set(false);
      this.confirming.set(false);
    }
  }

  private readFilters(): GetVotes$Params {
    const { q, session, from, to, hasPosition } = this.filters.getRawValue();

    return {
      Q: q || undefined,
      Session: session ?? undefined,
      From: from || undefined,
      To: to || undefined,
      HasPosition: hasPosition === '' ? undefined : hasPosition === 'true',
    };
  }

  private async load(): Promise<void> {
    try {
      this.votes.set(await this.api.invoke(getVotes, { ...this.searched, Page: this.page(), PageSize: 50 }));
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
