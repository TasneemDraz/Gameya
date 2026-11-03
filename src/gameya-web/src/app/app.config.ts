import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { authInterceptor } from './core/auth/auth.interceptor';
import { errorInterceptor } from './core/auth/error.interceptor';
import { provideSessionBootstrap } from './core/auth/session-bootstrap';
import { provideAppTransloco } from './core/i18n/transloco.config';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // errorInterceptor sits outside authInterceptor so it only sees failures that survive a refresh retry.
    provideHttpClient(withInterceptors([errorInterceptor, authInterceptor])),
    provideRouter(routes),
    provideAppTransloco(),
    provideSessionBootstrap(),
  ],
};
