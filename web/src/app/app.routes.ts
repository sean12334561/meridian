import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'imports', pathMatch: 'full' },
  { path: 'imports', loadChildren: () => import('./features/imports/imports.routes').then((m) => m.importRoutes) },
  { path: 'review', loadChildren: () => import('./features/review/review.routes').then((m) => m.reviewRoutes) },
  { path: 'votes', loadChildren: () => import('./features/votes/votes.routes').then((m) => m.voteRoutes) },
  { path: 'snapshots', loadChildren: () => import('./features/snapshots/snapshots.routes').then((m) => m.snapshotRoutes) },
];
