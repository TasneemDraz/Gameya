import { inject, provideAppInitializer } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LanguageService } from '../i18n/language.service';
import { AuthApi } from './auth.api';
import { AuthStore } from './auth.store';

/** Tries to resume a session from the refresh cookie once, before the first route renders. */
export function provideSessionBootstrap() {
  return provideAppInitializer(async () => {
    const api = inject(AuthApi);
    const store = inject(AuthStore);
    const lang = inject(LanguageService);
    try {
      store.setSession(await firstValueFrom(api.refresh()));
      const me = await firstValueFrom(api.me());
      lang.use(me.language);
    } catch {
      store.clear();
    }
  });
}
