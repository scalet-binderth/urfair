import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from './api';
import { I18n } from './i18n';

/** Asks for the organisation access key when the API answers 401. Replaced by a real login later. */
@Component({
  selector: 'app-admin-key',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="card row">
      <div class="field">
        <label for="admin-key">{{ i18n.t('admin.keyNeeded') }}</label>
        <input id="admin-key" type="password" [placeholder]="i18n.t('admin.key')" [(ngModel)]="key" (keyup.enter)="submit()" style="max-width:260px" />
      </div>
      <button class="btn" type="button" (click)="submit()">OK</button>
    </div>
  `,
})
export class AdminKeyPrompt {
  protected i18n = inject(I18n);
  private api = inject(Api);
  readonly entered = output<void>();
  protected key = this.api.adminKey();

  protected submit(): void {
    this.api.setAdminKey(this.key);
    this.entered.emit();
  }
}
