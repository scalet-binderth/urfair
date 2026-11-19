import { Routes } from '@angular/router';
import { VotePage } from './vote';
import { AdminPage } from './admin';
import { ResultsPage } from './results';

export const routes: Routes = [
  { path: '', redirectTo: 'vote', pathMatch: 'full' },
  { path: 'vote', component: VotePage },
  { path: 'admin', component: AdminPage },
  { path: 'results', component: ResultsPage },
  { path: '**', redirectTo: 'vote' },
];
