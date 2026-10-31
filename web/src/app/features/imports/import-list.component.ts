import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api/api';
import { getImports, uploadImport } from '../../core/api/functions';
import { ImportDto } from '../../core/api/models';
import { isPermanentFailure, poll } from '../../shared/poll';
import { errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

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
      <button matButton="filled" [disabled]="uploading()" (click)="file.click()">
        {{ uploading() ? 'Uploading…' : 'Upload CSV' }}
      </button>
      <span class="muted">Processing continues in the background.</span>
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
            <td class="number">{{ item.processedRows }} / {{ item.totalRows ?? '…' }}</td>
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
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly imports = signal<ImportDto[]>([]);
  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Kept apart from error: the next good refresh clears this, but mustn't clear an upload's message. */
  protected readonly refreshError = signal<string | null>(null);

  ngOnInit(): void {
    poll(() => this.api.invoke(getImports), (result) => isPermanentFailure(result.error), 2000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (result.value) {
          this.imports.set(result.value);
          this.refreshError.set(null);
        } else {
          this.refreshError.set(errorMessage(result.error));
        }
      });
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
    this.uploading.set(true);
    try {
      const created = await this.api.invoke(uploadImport, { body: { file } });
      await this.router.navigate(['/imports', created.importId]);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.uploading.set(false);
    }
  }
}
