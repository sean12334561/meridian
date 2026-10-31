import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { CurrentUserService } from './core/user/current-user.service';
import { UserSwitcherComponent } from './core/user/user-switcher.component';
import { errorMessage } from './shared/problem-details';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, MatToolbarModule, UserSwitcherComponent],
  template: `
    <mat-toolbar class="bar">
      <span class="brand">Meridian</span>
      <nav>
        <a routerLink="/imports" routerLinkActive="active">Imports</a>
        <a routerLink="/review" routerLinkActive="active">Review</a>
        <a routerLink="/votes" routerLinkActive="active">Votes</a>
        <a routerLink="/snapshots" routerLinkActive="active">Snapshots</a>
      </nav>
      <span class="grow"></span>
      <app-user-switcher />
    </mat-toolbar>
    <main>
      @if (error(); as message) {
        <div class="banner error">{{ message }} Users could not be loaded, so refresh the page once the server is up.</div>
      }
      @if (ready()) {
        <router-outlet />
      }
    </main>
  `,
  styles: `
    .bar { gap: 1.5rem; }
    .brand { font-weight: 600; }
    .grow { flex: 1; }
    nav { display: flex; gap: 1rem; font-size: 0.95rem; }
    nav a { color: inherit; text-decoration: none; padding: 0.25rem 0.5rem; border-radius: 4px; }
    nav a.active { background: rgba(0, 0, 0, 0.08); }
    main { max-width: 1100px; margin: 1.5rem auto; padding: 0 1rem; }
  `,
})
export class App implements OnInit {
  private readonly current = inject(CurrentUserService);

  protected readonly ready = signal(false);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      await this.current.load();
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.ready.set(true);
    }
  }
}
