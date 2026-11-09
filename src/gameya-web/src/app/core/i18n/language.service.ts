import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { AVAILABLE_LANGS, DEFAULT_LANG, Lang } from './transloco.config';

const STORAGE_KEY = 'gameya.lang';

function isLang(value: unknown): value is Lang {
  return AVAILABLE_LANGS.includes(value as Lang);
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly lang = signal<Lang>(this.readStored());

  readonly current = this.lang.asReadonly();

  constructor() {
    this.apply(this.lang());
  }

  use(lang: Lang): void {
    this.lang.set(lang);
    this.apply(lang);
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      /* storage unavailable: the choice just will not persist */
    }
  }

  private apply(lang: Lang): void {
    const root = document.documentElement;
    root.lang = lang;
    root.dir = lang === 'ar' ? 'rtl' : 'ltr';
    this.transloco.setActiveLang(lang);
  }

  private readStored(): Lang {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (isLang(stored)) return stored;
    } catch {
      /* storage unavailable */
    }
    return DEFAULT_LANG;
  }
}
