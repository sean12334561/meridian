import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'imports', pathMatch: 'full' },
  { path: 'imports', loadChildren: () => import('./features/imports/imports.routes').then((m) => m.importRoutes) },
];
