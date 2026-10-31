import { Component, computed, input } from '@angular/core';

const tones: Record<string, string> = {
  Completed: 'good',
  Accepted: 'good',
  New: 'good',
  Unchanged: 'quiet',
  Duplicate: 'quiet',
  Superseded: 'quiet',
  Pending: 'warn',
  Running: 'warn',
  Conflict: 'warn',
  PreviouslyDecided: 'quiet',
  Failed: 'bad',
  Rejected: 'bad',
  Invalid: 'bad',
  Contradiction: 'bad',
};

@Component({
  selector: 'app-status-badge',
  template: `<span class="badge" [class]="tone()">{{ value() }}</span>`,
  styles: `
    .badge {
      border-radius: 4px;
      padding: 0.1rem 0.45rem;
      font-size: 0.8rem;
      white-space: nowrap;
    }
    .good { background: #dcefe0; color: #1c5a2c; }
    .quiet { background: #e8e8ec; color: #44464f; }
    .warn { background: #fdecc8; color: #6d4a00; }
    .bad { background: #fadcdc; color: #8c1d18; }
  `,
})
export class StatusBadgeComponent {
  readonly value = input.required<string>();
  protected readonly tone = computed(() => tones[this.value()] ?? 'quiet');
}
