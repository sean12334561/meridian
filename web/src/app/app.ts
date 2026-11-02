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
    .bar {
      gap: 1.5rem;
      position: sticky;
      top: 0;
      z-index: 10;
      --mat-toolbar-container-background-color: var(--color-text);
      --mat-toolbar-container-text-color: var(--color-bg);
      box-shadow: 0 2px 10px rgba(59, 42, 34, 0.18);
      border-bottom: 3px solid var(--color-primary);
    }
    .brand {
      font-family: Georgia, "Times New Roman", serif;
      font-weight: 600;
      font-size: 1.3rem;
      letter-spacing: 0.02em;
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .brand::before {
      content: "";
      width: 0.7rem;
      height: 0.7rem;
      border-radius: 50%;
      background: linear-gradient(135deg, var(--color-accent), var(--color-primary));
      box-shadow: 0 0 0 3px rgba(233, 162, 59, 0.25);
    }
    .grow { flex: 1; }
    nav { display: flex; gap: 0.25rem; font-size: 0.95rem; }
    nav a {
      color: rgba(251, 246, 238, 0.78);
      text-decoration: none;
      padding: 0.35rem 0.8rem;
      border-radius: 999px;
      transition: background-color 120ms ease, color 120ms ease;
    }
    nav a:hover { color: var(--color-bg); background: rgba(251, 246, 238, 0.1); }
    nav a.active { color: #FFFFFF; background: var(--color-primary); }
    main {
      max-width: 1100px;
      margin: 2rem auto 3rem;
      padding: 2rem 2.25rem;
      background: var(--color-card);
      border: 1px solid var(--color-border);
      border-radius: 14px;
      box-shadow: var(--shadow-card);
    }
    @media (max-width: 1160px) {
      main { margin: 1rem; padding: 1.25rem; }
    }
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
