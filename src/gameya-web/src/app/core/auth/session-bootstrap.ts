import { inject, provideAppInitializer } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LanguageService } from '../i18n/language.service';
import { AuthApi } from './auth.api';
import { AuthStore } from './auth.store';
import { sharedRefresh } from './shared-refresh';

/**
 * Tries to resume a session from the refresh cookie once, before the first route renders.
 * It uses the shared refresh (in-tab promise plus cross-tab lock, with one retry on 401) and is always silent:
 * anonymous visitors see no snackbar for a 401 or a network error.
 */
export function provideSessionBootstrap() {
  return provideAppInitializer(async () => {
    const api = inject(AuthApi);
    const store = inject(AuthStore);
    const lang = inject(LanguageService);
    try {
      await sharedRefresh(api, store);
    } catch {
      store.clear();
      return;
    }
    // The session is valid from here on; a failed profile fetch only costs the saved language.
    try {
      lang.use((await firstValueFrom(api.me(true))).language);
    } catch {
      /* keep the session */
    }
  });
}
