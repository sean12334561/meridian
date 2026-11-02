import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api/api';
import { getImports, uploadImport } from '../../core/api/functions';
import { ImportDto } from '../../core/api/models';
import { PollResult, isPermanentFailure, poll } from '../../shared/poll';
import { errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';
import { importProgress } from './import-progress';

const maxFileBytes = 200_000_000;

@Component({
  selector: 'app-import-list',
  imports: [DatePipe, RouterLink, MatButtonModule, StatusBadgeComponent],
  template: `
    <h1>Imports</h1>

    @if (error() ?? refreshError(); as message) {
      <div class="banner error">{{ message }}</div>
    }

    <div class="filters">
      <input #file type="file" accept=".csv" hidden (change)="upload(file)" />
      <button matButton="filled" (click)="file.click()">Upload CSV</button>
      <span class="muted">{{ uploadStatus() }}</span>
    </div>

    <table class="grid">
      <thead>
        <tr>
          <th>File</th><th>Uploaded by</th><th>Uploaded</th><th>Status</th><th class="number">Rows</th>
        </tr>
      </thead>
      <tbody>
        @for (item of imports(); track item.id) {
          <tr>
            <td><a [routerLink]="['/imports', item.id]">{{ item.fileName }}</a></td>
            <td>{{ item.uploadedByName }}</td>
            <td>{{ item.uploadedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
            <td><app-status-badge [value]="item.status" /></td>
            <td class="number">{{ progressLabel(item) }}</td>
          </tr>
        } @empty {
          <tr><td colspan="5" class="empty">No imports yet.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class ImportListComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly destroyRef = inject(DestroyRef);
  private sending = Promise.resolve();

  protected readonly imports = signal<ImportDto[]>([]);
  protected readonly uploading = signal(0);
  protected readonly error = signal<string | null>(null);

  /** Kept apart from error: the next good refresh clears this, but mustn't clear an upload's message. */
  protected readonly refreshError = signal<string | null>(null);

  protected readonly uploadStatus = computed(() => {
    const count = this.uploading();
    if (count === 0) {
      return 'Processing continues in the background.';
    }
    return count === 1 ? 'Uploading 1 file…' : `Uploading ${count} files…`;
  });

  protected progressLabel(item: ImportDto): string {
    return importProgress(item).label;
  }

  ngOnInit(): void {
    poll(() => this.api.invoke(getImports), (result) => isPermanentFailure(result.error), 2000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => this.show(result));
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    // Checked here too: on a slow link the server may reset the connection before its 413 arrives
    if (file.size > maxFileBytes) {
      this.error.set('The file is too large. The limit is 200 MB.');
      return;
    }

    this.error.set(null);
    this.uploading.update((count) => count + 1);

    // One at a time, in the order chosen: a revised file must not overtake its original
    this.sending = this.sending.then(() => this.send(file));
  }

  /** Never rejects, or one failed upload would stop the ones queued behind it. */
  private async send(file: File): Promise<void> {
    try {
      await this.api.invoke(uploadImport, { body: { file } });
    } catch (error) {
      // Several uploads can be queued, so the message has to say which one failed
      this.error.set(`${file.name}: ${errorMessage(error)}`);
      return;
    } finally {
      this.uploading.update((count) => count - 1);
    }

    await this.refresh();
  }

  /** Shows a finished upload's row now, rather than at the next poll. */
  private async refresh(): Promise<void> {
    try {
      this.show({ value: await this.api.invoke(getImports) });
    } catch (error) {
      this.show({ error });
    }
  }

  private show(result: PollResult<ImportDto[]>): void {
    if (result.value) {
      this.imports.set(result.value);
      this.refreshError.set(null);
    } else {
      this.refreshError.set(errorMessage(result.error));
    }
  }
}
