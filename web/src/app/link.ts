import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, LinkInfo } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';

/** Opened from the personal link /p/{token}: confirms the stored dates of birth and baptism if needed, then signs the person in. */
@Component({
  selector: 'app-link',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (info(); as i) {
      <form class="card stack narrow" (submit)="$event.preventDefault(); confirm()">
        <p class="muted small">{{ i.slotName }}</p>
        <h1>{{ i18n.t('link.title') }}</h1>
        @if (i.needsCheck || i.needsBaptism) {
          <p>{{ i18n.t(i.needsCheck && i.needsBaptism ? 'link.leadBoth' : i.needsCheck ? 'link.lead' : 'link.leadBaptism') }}</p>
          @if (i.needsCheck) {
            <div class="field"><label for="l-birth">{{ i18n.t('participants.birthDate') }}</label>
              <input id="l-birth" name="birth" type="date" [(ngModel)]="birthDate" autocomplete="bday" /></div>
          }
          @if (i.needsBaptism) {
            <div class="field"><label for="l-baptism">{{ i18n.t('participants.baptismDate') }}</label>
              <input id="l-baptism" name="baptism" type="date" [(ngModel)]="baptismDate" /></div>
          }
        } @else {
          <p>{{ i18n.t('link.leadNoCheck') }}</p>
        }
        @if (error()) { <p class="errors">{{ error() }}</p> }
        <div><button class="btn" type="submit" [disabled]="busy() || (i.needsCheck && !birthDate) || (i.needsBaptism && !baptismDate)">{{ i18n.t('link.go') }}</button></div>
        <p class="muted small">{{ i18n.t('link.note') }}</p>
      </form>
    } @else if (notFound()) {
      <div class="card stack narrow"><h1>{{ i18n.t('link.invalid') }}</h1><p>{{ i18n.t('link.invalidText') }}</p></div>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `.narrow { max-width: 520px }`,
})
export class LinkPage implements OnInit {
  /** Bound from the route parameter :token. */
  readonly token = input.required<string>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  private session = inject(SessionService);
  private router = inject(Router);
  protected info = signal<LinkInfo | null>(null);
  protected notFound = signal(false);
  protected error = signal<string | null>(null);
  protected busy = signal(false);
  protected birthDate = '';
  protected baptismDate = '';

  async ngOnInit(): Promise<void> {
    try { this.info.set(await this.api.linkInfo(this.token())); } catch { this.notFound.set(true); }
  }

  protected async confirm(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const birth = this.birthDate || null;
      const baptism = this.baptismDate || null;
      const res = await this.api.linkCheck(this.token(), birth, baptism);
      this.session.signOut();
      this.api.setParticipantLink({ token: this.token(), check: birth ?? '', baptism: baptism ?? '' });
      this.session.signIn({ kind: 'participant', name: res.name });
      await this.router.navigate(['/s', res.slotId]);
    } catch (e) {
      this.error.set(this.i18n.errors(e)[0]);
    } finally {
      this.busy.set(false);
    }
  }
}
