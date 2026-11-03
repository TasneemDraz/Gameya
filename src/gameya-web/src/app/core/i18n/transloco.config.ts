import { HttpClient } from '@angular/common/http';
import { Injectable, inject, isDevMode } from '@angular/core';
import { Translation, TranslocoLoader, provideTransloco } from '@jsverse/transloco';

export const AVAILABLE_LANGS = ['ar', 'en'] as const;
export type Lang = (typeof AVAILABLE_LANGS)[number];
export const DEFAULT_LANG: Lang = 'ar';

@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string) {
    return this.http.get<Translation>(`/i18n/${lang}.json`);
  }
}

export function provideAppTransloco() {
  return provideTransloco({
    config: {
      availableLangs: [...AVAILABLE_LANGS],
      defaultLang: DEFAULT_LANG,
      fallbackLang: DEFAULT_LANG,
      reRenderOnLangChange: true,
      prodMode: !isDevMode(),
    },
    loader: TranslocoHttpLoader,
  });
}
