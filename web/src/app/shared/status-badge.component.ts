import { Component, computed, input } from '@angular/core';

const tones: Record<string, string> = {
  Completed: 'good',
  Accepted: 'good',
  New: 'good',
  Unchanged: 'quiet',
  Duplicate: 'quiet',
  Superseded: 'quiet',
  'No position': 'bad',
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
      display: inline-block;
      border-radius: 999px;
      padding: 0.12rem 0.6rem;
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.02em;
      white-space: nowrap;
      border: 1px solid transparent;
    }
    .good { background: #E3E9D6; color: #3F4E2B; border-color: #C9D3B5; }
    .quiet { background: #F1E4D3; color: #5E4B40; border-color: #E0CDB6; }
    .warn { background: #FBEBCF; color: #6A4510; border-color: #F2D29A; }
    .bad { background: #F8DCD6; color: #8A2216; border-color: #EDB9AE; }
  `,
})
export class StatusBadgeComponent {
  readonly value = input.required<string>();
  protected readonly tone = computed(() => tones[this.value()] ?? 'quiet');
}
