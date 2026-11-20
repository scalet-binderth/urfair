import { Injectable, inject, signal } from '@angular/core';
import { Api } from './api';

/** Server facts loaded once: demo mode and whether the organisation area needs a key. */
@Injectable({ providedIn: 'root' })
export class AppInfo {
  private api = inject(Api);
  /** Demo mode shows demo people, sample data and reset buttons. Assumed on until the server answers. */
  readonly demo = signal(true);
  readonly adminProtected = signal(false);

  constructor() {
    this.api.info().then(i => { this.demo.set(i.demo); this.adminProtected.set(i.adminProtected); }, () => { /* keep defaults */ });
  }
}
