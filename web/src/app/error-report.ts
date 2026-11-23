import { ErrorHandler, Injectable } from '@angular/core';

/**
 * Unexpected errors in the browser are also sent to the server log (message and page only, without
 * personal links), so they can be found in operation. At most a few per page load.
 */
@Injectable()
export class ReportingErrorHandler implements ErrorHandler {
  private sent = 0;

  handleError(error: unknown): void {
    console.error(error);
    if (this.sent++ >= 5) return;
    const message = error instanceof Error ? `${error.name}: ${error.message}\n${error.stack ?? ''}` : String(error);
    const path = location.pathname.replace(/\/p\/[^/]+/, '/p/…');
    fetch('/api/client-error', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message: message.slice(0, 1000), path }),
      keepalive: true,
    }).catch(() => { /* offline: nothing to do */ });
  }
}
