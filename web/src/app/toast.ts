import { Injectable, signal } from '@angular/core';

/** Short status message at the bottom of the screen. */
@Injectable({ providedIn: 'root' })
export class Toast {
  readonly message = signal<string | null>(null);
  private timer?: ReturnType<typeof setTimeout>;

  show(text: string): void {
    this.message.set(text);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.message.set(null), 3500);
  }

  /** Copies text to the clipboard and confirms with okText; if the clipboard is blocked, shows the text itself. */
  async copy(text: string, okText: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.show(okText);
    } catch {
      this.show(text);
    }
  }
}
