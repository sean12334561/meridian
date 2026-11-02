import { Routes } from '@angular/router';
import { ImportListComponent } from './import-list.component';
import { ImportReportComponent } from './import-report.component';

export const importRoutes: Routes = [
  { path: '', component: ImportListComponent },
  { path: ':id', component: ImportReportComponent },
];
