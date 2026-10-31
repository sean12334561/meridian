import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Api } from '../../core/api/api';
import { decideConflict, getConflict } from '../../core/api/functions';
import { ConflictDto, Decision } from '../../core/api/models';
import { Problem, errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

@Component({
  selector: 'app-conflict-detail',
  imports: [DatePipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, StatusBadgeComponent],
  template: `
    <p><a routerLink="/review">← Review</a></p>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    @if (alreadyDecided(); as message) {
      <div class="banner info">{{ message }}</div>
    }

    @if (conflict(); as current) {
      <h1>
        <a [routerLink]="['/votes', current.voteId]">{{ current.voteRef }}</a>: {{ current.target }}
        <app-status-badge [value]="current.status" />
      </h1>
      <p class="muted">{{ current.voteTitle }}</p>

      <div class="sides">
        <section>
          <h2>Approved value</h2>
          <p class="value">{{ current.approvedValue }}</p>
          <p class="muted">
            {{ current.approvedSource.fileName }}
            @if (current.approvedSource.line) {
              , line {{ current.approvedSource.line }}
            }
            <br />Uploaded by {{ current.approvedSource.uploadedByName }},
            {{ current.approvedSource.uploadedAt | date: 'yyyy-MM-dd HH:mm' }}
            @if (current.approvedSource.publishedAt) {
              <br />Published {{ current.approvedSource.publishedAt | date: 'yyyy-MM-dd HH:mm' }}
            }
          </p>
        </section>
        <section>
          <h2>Proposed value</h2>
          <p class="value">{{ current.proposedValue }}</p>
          <p class="muted">
            {{ current.proposedSource.fileName }}, line {{ current.proposedSource.line }}
            <br />Uploaded by {{ current.proposedSource.uploadedByName }},
            {{ current.proposedSource.uploadedAt | date: 'yyyy-MM-dd HH:mm' }}
            <br />Published {{ current.proposedSource.publishedAt | date: 'yyyy-MM-dd HH:mm' }}
          </p>
        </section>
      </div>

      @if (current.status === 'Pending') {
        <p class="muted">A published ranking will not change. New rankings use the decided value.</p>
        <mat-form-field class="note">
          <mat-label>Note (optional)</mat-label>
          <textarea matInput rows="2" [formControl]="note"></textarea>
          @if (note.hasError('maxlength')) {
            <mat-error>At most 1,000 characters.</mat-error>
          }
          @if (note.hasError('server')) {
            <mat-error>{{ note.getError('server') }}</mat-error>
          }
        </mat-form-field>
        <div class="filters">
          <button matButton="filled" [disabled]="busy() || note.invalid" (click)="decide('Accept')">Accept proposed value</button>
          <button matButton="outlined" [disabled]="busy() || note.invalid" (click)="decide('Reject')">Keep approved value</button>
        </div>
      } @else {
        <p>
          {{ current.status }}
          @if (current.decidedByName) {
            by {{ current.decidedByName }}, {{ current.decidedAt | date: 'yyyy-MM-dd HH:mm' }}
          }
        </p>
        @if (current.note) {
          <p class="muted">{{ current.note }}</p>
        }
      }
    }
  `,
  styles: `
    .sides { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
    .sides section { border: 1px solid #e3e3e8; border-radius: 6px; padding: 0 1rem; }
    .value { font-size: 1.4rem; margin: 0.25rem 0; word-break: break-word; }
    .note { width: 100%; }
  `,
})
export class ConflictDetailComponent implements OnInit {
  private readonly api = inject(Api);

  readonly id = input.required<string>();

  protected readonly conflict = signal<ConflictDto | null>(null);
  protected readonly note = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly alreadyDecided = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.conflict.set(await this.api.invoke(getConflict, { id: Number(this.id()) }));
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }

  protected async decide(decision: Decision): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.conflict.set(
        await this.api.invoke(decideConflict, {
          id: Number(this.id()),
          body: { decision, note: this.note.value || null },
        }),
      );
    } catch (error) {
      this.showFailure(error);
    } finally {
      this.busy.set(false);
    }
  }

  // Losing a race is expected: show who decided and the conflict as it now stands (CON-05)
  private showFailure(error: unknown): void {
    if (error instanceof Problem && error.status === 409 && error.body['current']) {
      const current = error.body['current'] as ConflictDto;
      this.conflict.set(current);
      const at = new Date(current.decidedAt!).toLocaleString();
      this.alreadyDecided.set(`${current.decidedByName} already decided this at ${at}. Your decision was not applied.`);
    } else if (error instanceof Problem && error.errors['note']) {
      this.note.setErrors({ server: error.errors['note'].join(' ') });
    } else {
      this.error.set(errorMessage(error));
    }
  }
}
