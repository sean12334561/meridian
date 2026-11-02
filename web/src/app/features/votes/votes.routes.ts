import { Routes } from '@angular/router';
import { VoteDetailComponent } from './vote-detail.component';
import { VoteListComponent } from './vote-list.component';

export const voteRoutes: Routes = [
  { path: '', component: VoteListComponent },
  { path: ':id', component: VoteDetailComponent },
];
