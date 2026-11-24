import { Injectable, inject, signal } from '@angular/core';
import { Api } from './api';

/** Server facts loaded once: demo mode, mail sending, parishes and version. */
@Injectable({ providedIn: 'root' })
export class AppInfo {
  private api = inject(Api);
  /** Demo mode shows demo people, sample data and reset buttons. Assumed on until the server answers. */
  readonly demo = signal(true);
  /** The app sends e-mails itself (mail server set up, not in demo mode). */
  readonly mailSending = signal(false);
  /** Official names of the Pfarre and its Pfarrgemeinden, for selections. */
  readonly parishes = signal<string[]>([]);
  /** Short commit of the running server, shown in the footer so error reports name the version. */
  readonly version = signal('');

  constructor() {
    this.api.info().then(i => {
      this.demo.set(i.demo); this.mailSending.set(i.mailSending); this.parishes.set(i.parishes ?? []);
      this.version.set(i.version ?? '');
    }, () => { /* keep defaults */ });
  }
}
