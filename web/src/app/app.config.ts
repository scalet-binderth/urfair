import { ApplicationConfig, ErrorHandler, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { ReportingErrorHandler } from './error-report';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: ReportingErrorHandler },
    provideHttpClient(withFetch()),
    // Route parameters such as :slotId are passed to component inputs of the same name.
    provideRouter(routes, withComponentInputBinding()),
  ],
};
