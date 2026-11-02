import { Routes } from '@angular/router';
import { ConflictDetailComponent } from './conflict-detail.component';
import { ConflictInboxComponent } from './conflict-inbox.component';

export const reviewRoutes: Routes = [
  { path: '', component: ConflictInboxComponent },
  { path: ':id', component: ConflictDetailComponent },
];
