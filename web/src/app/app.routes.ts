import { Routes } from '@angular/router';
import { StartPage } from './start';
import { HomePage } from './home';
import { VotePage } from './vote';
import { MyResultPage } from './my-result';
import { AdminListPage } from './admin-list';
import { AdminSlotPage } from './admin-slot';
import { adminGuard, participantGuard } from './session';

export const routes: Routes = [
  { path: '', component: StartPage },
  { path: 'slots', component: HomePage, canActivate: [participantGuard] },
  { path: 's/:slotId', component: VotePage, canActivate: [participantGuard] },
  { path: 's/:slotId/result', component: MyResultPage, canActivate: [participantGuard] },
  { path: 'admin', component: AdminListPage, canActivate: [adminGuard] },
  { path: 'admin/s/:slotId', component: AdminSlotPage, canActivate: [adminGuard] },
  { path: '**', redirectTo: '' },
];
