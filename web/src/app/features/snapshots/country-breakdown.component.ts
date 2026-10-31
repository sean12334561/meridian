import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api/api';
import { getCountryBreakdown } from '../../core/api/functions';
import { CountryBreakdownDto } from '../../core/api/models';
import { errorMessage } from '../../shared/problem-details';

@Component({
  selector: 'app-country-breakdown',
  imports: [DecimalPipe, RouterLink],
  template: `
    <p><a [routerLink]="['/snapshots', id()]">← Snapshot</a></p>

    @if (error(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    @if (breakdown(); as current) {
      <h1>{{ current.score.countryName }}</h1>
      <p>
        @if (current.score.score === null) {
          Insufficient data
        } @else {
          Score {{ current.score.score | number: '1.1-1' }}, rank {{ current.score.rank }}
        }
        <span class="muted">
          ({{ current.score.countedVotes }} counted votes, {{ current.score.earnedWeight }} of {{ current.score.countedWeight }} weight)
        </span>
      </p>

      <table class="grid">
        <thead>
          <tr>
            <th>Vote</th><th>Title</th><th>Ballot</th><th>Position</th><th class="number">Weight</th>
            <th class="number">Points</th><th>Why</th><th>Source</th>
          </tr>
        </thead>
        <tbody>
          @for (row of current.contributions; track row.voteId) {
            <tr>
              <td><a [routerLink]="['/votes', row.voteId]">{{ row.voteRef }}</a></td>
              <td>{{ row.title }}</td>
              <td>{{ row.ballot }}</td>
              <td>{{ row.position }}</td>
              <td class="number">{{ row.weight }}</td>
              <td class="number">{{ row.points }}</td>
              <td>{{ row.reason }}</td>
              <td><a [routerLink]="['/imports', row.sourceImportId]">{{ row.sourceFileName }}</a></td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
})
export class CountryBreakdownComponent implements OnInit {
  private readonly api = inject(Api);

  readonly id = input.required<string>();
  readonly code = input.required<string>();

  protected readonly breakdown = signal<CountryBreakdownDto | null>(null);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      this.breakdown.set(await this.api.invoke(getCountryBreakdown, { id: Number(this.id()), code: this.code() }));
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
