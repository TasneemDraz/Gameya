import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule, TranslocoService } from '@jsverse/transloco';
import { LanguageService } from './language.service';

function setup(): LanguageService {
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { ar: {}, en: {} },
        translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
      }),
    ],
  });
  return TestBed.inject(LanguageService);
}

describe('LanguageService', () => {
  beforeEach(() => {
    try {
      localStorage.clear();
    } catch {
      /* ignore */
    }
    document.documentElement.removeAttribute('dir');
    document.documentElement.removeAttribute('lang');
  });
  afterEach(() => vi.restoreAllMocks());

  it('defaults to Arabic and rtl', () => {
    const svc = setup();
    expect(svc.current()).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
  });

  it('use("en") sets ltr/en, switches Transloco and stores the choice', () => {
    const svc = setup();
    svc.use('en');
    expect(svc.current()).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
    expect(document.documentElement.lang).toBe('en');
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('en');
    expect(localStorage.getItem('gameya.lang')).toBe('en');
  });

  it('restores the stored language', () => {
    localStorage.setItem('gameya.lang', 'en');
    const svc = setup();
    expect(svc.current()).toBe('en');
    expect(document.documentElement.dir).toBe('ltr');
  });

  it('ignores an invalid stored value', () => {
    localStorage.setItem('gameya.lang', 'fr');
    expect(setup().current()).toBe('ar');
  });

  it('works when localStorage throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const svc = setup();
    expect(svc.current()).toBe('ar');
    expect(() => svc.use('en')).not.toThrow();
    expect(document.documentElement.dir).toBe('ltr');
  });
});
