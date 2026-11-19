import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { I18n } from './i18n';
import { Toast } from './toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="top">
      <div class="wrap bar">
        <div class="brand">
          <svg width="28" height="28" viewBox="0 0 30 30" aria-hidden="true">
            <path
              d="M15 3c4 5 8 8.5 8 14a8 8 0 0 1-16 0c0-3.2 1.6-5.4 3.4-7.2.2 2.6 1.4 4.2 3 4.9C12.6 10.5 13.3 6.6 15 3Z"
              fill="var(--accent)"
            />
          </svg>
          <div>
            <b>{{ i18n.t('app.name') }}</b
            ><small>{{ i18n.t('app.tagline') }}</small>
          </div>
        </div>
        <nav class="steps">
          <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">{{
            i18n.t('nav.vote')
          }}</a>
          <a routerLink="/admin" routerLinkActive="on" ariaCurrentWhenActive="page">{{ i18n.t('nav.admin') }}</a>
          <button type="button" class="lang" (click)="i18n.toggle()">
            {{ i18n.t('lang.switch') }}
          </button>
        </nav>
      </div>
    </header>
    <div class="wrap demo small">{{ i18n.t('app.demo') }}</div>
    <main class="wrap"><router-outlet /></main>
    <footer class="wrap small">{{ i18n.t('app.madeFor') }}</footer>
    @if (toast.message(); as m) {
      <div class="toast" role="status">{{ m }}</div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .top {
      background: var(--surface);
      border-bottom: 1px solid var(--line);
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding-block: 14px;
    }
    .brand {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .brand b {
      font-size: 18px;
    }
    .brand small {
      display: block;
      color: var(--muted);
      font-size: 12px;
      line-height: 1.2;
    }
    .steps {
      display: flex;
      gap: 4px;
      flex-wrap: wrap;
      align-items: center;
    }
    .steps a {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      border-radius: 999px;
      text-decoration: none;
      color: var(--muted);
      font-weight: 500;
      font-size: 14px;
    }
    .steps .n {
      display: inline-grid;
      place-items: center;
      width: 22px;
      height: 22px;
      border-radius: 50%;
      border: 1px solid var(--line);
      font-size: 12px;
    }
    .steps a.on {
      background: var(--accent-soft);
      color: var(--ink);
    }
    .steps a.on .n {
      background: var(--accent);
      border-color: var(--accent);
      color: var(--accent-ink);
    }
    .lang {
      margin-left: 6px;
      padding: 6px 10px;
      border-radius: 8px;
      border: 1px solid var(--line);
      background: transparent;
      cursor: pointer;
      font-size: 13px;
    }
    .demo {
      color: var(--muted);
      padding-block: 10px;
    }
    main {
      padding-block: 8px 40px;
    }
    footer {
      color: var(--muted);
      padding-block: 16px 32px;
      border-top: 1px solid var(--line);
    }
  `,
})
export class App {
  protected i18n = inject(I18n);
  protected toast = inject(Toast);
}
