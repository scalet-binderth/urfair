import { Routes } from '@angular/router';
import { StartPage } from './start';
import { PlanPage } from './plan';
import { HomePage } from './home';
import { VotePage, unsavedVoteGuard } from './vote';
import { MyResultPage } from './my-result';
import { LinkPage } from './link';
import { AccountsPage } from './accounts';
import { SetPasswordPage } from './set-password';
import { AdminListPage } from './admin-list';
import { AdminSlotPage } from './admin-slot';
import { LegalPage } from './legal';
import { adminGuard, participantGuard } from './session';

export const routes: Routes = [
  { path: '', component: StartPage },
  { path: 'plan', component: PlanPage },
  { path: 'p/:token', component: LinkPage },
  { path: 'account/:token', component: SetPasswordPage },
  { path: 'slots', component: HomePage, canActivate: [participantGuard] },
  { path: 's/:slotId', component: VotePage, canActivate: [participantGuard], canDeactivate: [unsavedVoteGuard] },
  { path: 's/:slotId/result', component: MyResultPage, canActivate: [participantGuard] },
  { path: 'admin', component: AdminListPage, canActivate: [adminGuard] },
  { path: 'admin/accounts', component: AccountsPage, canActivate: [adminGuard] },
  { path: 'admin/s/:slotId', component: AdminSlotPage, canActivate: [adminGuard] },
  { path: 'impressum', component: LegalPage, data: { kind: 'imprint' } },
  { path: 'datenschutz', component: LegalPage, data: { kind: 'privacy' } },
  { path: '**', redirectTo: '' },
];
