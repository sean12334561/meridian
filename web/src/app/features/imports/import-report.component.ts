import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Api } from '../../core/api/api';
import { getImport, getImportReport, getImportRows } from '../../core/api/functions';
import { ImportDto, ImportReportDto, PagedResultOfImportRowDto, RowOutcome } from '../../core/api/models';
import { PollResult, isPermanentFailure, poll } from '../../shared/poll';
import { errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

const flagged: RowOutcome[] = ['Invalid', 'Duplicate', 'Contradiction', 'Conflict', 'PreviouslyDecided'];

@Component({
  selector: 'app-import-report',
  imports: [DatePipe, FormsModule, RouterLink, MatPaginatorModule, MatProgressBarModule, MatSelectModule, StatusBadgeComponent],
  template: `
    <p><a routerLink="/imports">← Imports</a></p>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    @if (item(); as current) {
      <h1>{{ current.fileName }}</h1>
      <p class="muted">
        Uploaded by {{ current.uploadedByName }}, {{ current.uploadedAt | date: 'yyyy-MM-dd HH:mm' }}
        · <app-status-badge [value]="current.status" />
      </p>

      @if (current.error) {
        <div class="banner error">{{ current.error }}</div>
      }

      @if (running()) {
        <p>{{ current.phase }}: {{ current.processedRows }} of {{ current.totalRows ?? '…' }} rows</p>
        <mat-progress-bar
          [mode]="current.totalRows ? 'determinate' : 'indeterminate'"
          [value]="percent()"
        />
      }
    }

    @if (report(); as result) {
      @if (result.identicalTo; as earlier) {
        <div class="banner info">
          This file is identical to
          <a [routerLink]="['/imports', earlier.importId]">{{ earlier.fileName }}</a>,
          uploaded {{ earlier.uploadedAt | date: 'yyyy-MM-dd HH:mm' }}.
        </div>
      }

      <h2>Totals</h2>
      <div class="totals">
        @for (total of totals(); track total.label) {
          <div><strong>{{ total.value }}</strong><span class="muted">{{ total.label }}</span></div>
        }
      </div>

      <h2>Rows</h2>
      <div class="filters">
        <mat-select class="outcome" [ngModel]="outcome()" (ngModelChange)="selectOutcome($event)" aria-label="Outcome">
          <mat-option value="flagged">Normalized rows</mat-option>
          @for (option of outcomes; track option) {
            <mat-option [value]="option">{{ option }}</mat-option>
          }
        </mat-select>
      </div>
      <table class="grid">
        <thead>
          <tr><th class="number">Line</th><th>Outcome</th><th>Reason</th><th>Original value</th></tr>
        </thead>
        <tbody>
          @for (row of rows()?.items; track row.lineNo) {
            <tr>
              <td class="number">{{ row.lineNo }}</td>
              <td>
                @if (row.outcome) {
                  <app-status-badge [value]="row.outcome" />
                }
              </td>
              <td>
                {{ row.reason }}
                @if (row.conflictId) {
                  <a [routerLink]="['/review', row.conflictId]">Open conflict</a>
                }
                @if (row.normalizations) {
                  <div class="mono">{{ row.normalizations }}</div>
                }
              </td>
              <td class="mono">{{ row.rawText }}</td>
            </tr>
          } @empty {
            <tr><td colspan="4" class="empty">No rows.</td></tr>
          }
        </tbody>
      </table>
      <mat-paginator [length]="rows()?.total ?? 0" [pageSize]="50" [pageIndex]="rowPage() - 1" hidePageSize (page)="selectRowPage($event)" />

      <h2>Votes</h2>
      <table class="grid">
        <thead>
          <tr>
            <th>Vote</th><th class="number">New</th><th class="number">Unchanged</th><th class="number">Duplicate</th>
            <th class="number">Invalid</th><th class="number">Contradiction</th><th class="number">Conflict</th>
            <th class="number">Decided</th><th>Details</th>
          </tr>
        </thead>
        <tbody>
          @for (vote of votesOnPage(); track vote.voteRef) {
            <tr>
              <td>{{ vote.voteRef }}</td>
              <td class="number">{{ vote.new }}</td>
              <td class="number">{{ vote.unchanged }}</td>
              <td class="number">{{ vote.duplicate }}</td>
              <td class="number">{{ vote.invalid }}</td>
              <td class="number">{{ vote.contradiction }}</td>
              <td class="number">{{ vote.conflict }}</td>
              <td class="number">{{ vote.previouslyDecided }}</td>
              <td>{{ vote.details }}</td>
            </tr>
          }
        </tbody>
      </table>
      <mat-paginator [length]="result.votes.length" [pageSize]="pageSize" [pageIndex]="votePage()" hidePageSize (page)="votePage.set($event.pageIndex)" />
    }
  `,
  styles: `
    .totals { display: flex; flex-wrap: wrap; gap: 1.5rem; }
    .totals div { display: flex; flex-direction: column; }
    .totals strong { font-size: 1.3rem; font-weight: 500; }
    .outcome { width: 14rem; }
  `,
})
export class ImportReportComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly outcomes: RowOutcome[] = flagged;
  protected readonly pageSize = 50;
  protected readonly item = signal<ImportDto | null>(null);
  protected readonly report = signal<ImportReportDto | null>(null);
  protected readonly rows = signal<PagedResultOfImportRowDto | null>(null);
  protected readonly outcome = signal<string>('Invalid');
  protected readonly rowPage = signal(1);
  protected readonly votePage = signal(0);
  protected readonly error = signal<string | null>(null);

  protected readonly running = computed(() => {
    const status = this.item()?.status;
    return status === 'Pending' || status === 'Running';
  });

  protected readonly percent = computed(() => {
    const item = this.item();
    return item?.totalRows ? (100 * item.processedRows) / item.totalRows : 0;
  });

  protected readonly totals = computed(() => {
    const report = this.report();
    if (!report) {
      return [];
    }

    return [
      { label: 'Rows', value: report.totals.rows },
      { label: 'New', value: report.totals.new },
      { label: 'Unchanged', value: report.totals.unchanged },
      { label: 'Duplicate', value: report.totals.duplicate },
      { label: 'Invalid', value: report.totals.invalid },
      { label: 'Contradiction', value: report.totals.contradiction },
      { label: 'Conflict', value: report.totals.conflict },
      { label: 'Already pending', value: report.totals.alreadyPending },
      { label: 'Previously decided', value: report.totals.previouslyDecided },
      { label: 'Normalized', value: report.totals.normalized },
      { label: 'Conflicts opened', value: report.conflictsOpened },
    ];
  });

  protected readonly votesOnPage = computed(() => {
    const start = this.votePage() * this.pageSize;
    return this.report()?.votes.slice(start, start + this.pageSize) ?? [];
  });

  // The ID is in the route, so a refresh resumes here (ING-02)
  ngOnInit(): void {
    poll(() => this.api.invoke(getImport, { id: this.id() }), (result) => this.isFinal(result))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => this.show(result));
  }

  protected selectOutcome(outcome: string): void {
    this.outcome.set(outcome);
    this.rowPage.set(1);
    void this.loadRows();
  }

  protected selectRowPage(event: PageEvent): void {
    this.rowPage.set(event.pageIndex + 1);
    void this.loadRows();
  }

  private isFinal(result: PollResult<ImportDto>): boolean {
    const status = result.value?.status;
    return status === 'Completed' || status === 'Failed' || isPermanentFailure(result.error);
  }

  private async show(result: PollResult<ImportDto>): Promise<void> {
    if (!result.value) {
      this.error.set(errorMessage(result.error));
      return;
    }

    this.error.set(null);
    this.item.set(result.value);
    if (result.value.status === 'Completed') {
      await this.loadReport();
    }
  }

  private async loadReport(): Promise<void> {
    try {
      this.report.set(await this.api.invoke(getImportReport, { id: this.id() }));
      await this.loadRows();
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }

  private async loadRows(): Promise<void> {
    const outcome = this.outcome();
    try {
      this.rows.set(
        await this.api.invoke(getImportRows, {
          id: this.id(),
          Page: this.rowPage(),
          PageSize: this.pageSize,
          Outcome: outcome === 'flagged' ? undefined : (outcome as RowOutcome),
          Normalized: outcome === 'flagged' ? true : undefined,
        }),
      );
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
