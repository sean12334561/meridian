import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api/api';
import { getImports, uploadImport } from '../../core/api/functions';
import { ImportDto } from '../../core/api/models';
import { errorMessage } from '../../shared/problem-details';
import { StatusBadgeComponent } from '../../shared/status-badge.component';

const maxFileBytes = 200_000_000;

@Component({
  selector: 'app-import-list',
  imports: [DatePipe, RouterLink, MatButtonModule, StatusBadgeComponent],
  template: `
    <h1>Imports</h1>

    @if (error(); as message) {
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
export class ImportListComponent implements OnInit, OnDestroy {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private timer?: ReturnType<typeof setInterval>;

  protected readonly imports = signal<ImportDto[]>([]);
  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.refresh();
    this.timer = setInterval(() => this.refresh(), 2000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
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

  private async refresh(): Promise<void> {
    try {
      this.imports.set(await this.api.invoke(getImports));
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }
}
