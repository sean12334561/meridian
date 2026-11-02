import { Injectable, computed, inject, signal } from '@angular/core';
import { Api } from '../api/api';
import { getUsers } from '../api/functions';
import { UserDto } from '../api/models';

const storageKey = 'meridian.userId';

/** Who the app acts as. Survives a refresh; the API still decides what each role may do. */
@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly api = inject(Api);

  readonly users = signal<UserDto[]>([]);
  readonly id = signal<number | null>(Number(localStorage.getItem(storageKey)) || null);
  readonly user = computed(() => this.users().find((u) => u.id === this.id()) ?? null);
  readonly isAdmin = computed(() => this.user()?.role === 'Admin');

  async load(): Promise<void> {
    const users = await this.api.invoke(getUsers);
    this.users.set(users);
    if (!users.some((u) => u.id === this.id()) && users.length > 0) {
      this.select(users[0].id);
    }
  }

  select(id: number): void {
    localStorage.setItem(storageKey, String(id));
    this.id.set(id);
  }
}
