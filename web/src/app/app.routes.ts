import { Routes } from '@angular/router';
import { HomePage } from './home';
import { VotePage } from './vote';
import { AdminListPage } from './admin-list';
import { AdminSlotPage } from './admin-slot';

export const routes: Routes = [
  { path: '', component: HomePage },
  { path: 's/:slotId', component: VotePage },
  { path: 'admin', component: AdminListPage },
  { path: 'admin/s/:slotId', component: AdminSlotPage },
  { path: '**', redirectTo: '' },
];
