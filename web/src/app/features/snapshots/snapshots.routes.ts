import { Routes } from '@angular/router';
import { CountryBreakdownComponent } from './country-breakdown.component';
import { SnapshotDetailComponent } from './snapshot-detail.component';
import { SnapshotListComponent } from './snapshot-list.component';

export const snapshotRoutes: Routes = [
  { path: '', component: SnapshotListComponent },
  { path: ':id', component: SnapshotDetailComponent },
  { path: ':id/countries/:code', component: CountryBreakdownComponent },
];
