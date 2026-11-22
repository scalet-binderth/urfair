import { Injectable, inject, signal } from '@angular/core';
import { Api } from './api';

/** Server facts loaded once: demo mode and whether the organisation area needs a key. */
@Injectable({ providedIn: 'root' })
export class AppInfo {
  private api = inject(Api);
  /** Demo mode shows demo people, sample data and reset buttons. Assumed on until the server answers. */
  readonly demo = signal(true);
  readonly adminProtected = signal(false);
  /** The app sends e-mails itself (mail server set up, not in demo mode). */
  readonly mailSending = signal(false);
  /** Official names of the Pfarre and its Pfarrgemeinden, for selections. */
  readonly parishes = signal<string[]>([]);

  constructor() {
    this.api.info().then(i => { this.demo.set(i.demo); this.adminProtected.set(i.adminProtected); this.mailSending.set(i.mailSending); this.parishes.set(i.parishes ?? []); }, () => { /* keep defaults */ });
  }
}
