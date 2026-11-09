import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LanguageService } from '../i18n/language.service';
import { CairoDatePipe } from './cairo-date.pipe';

describe('CairoDatePipe', () => {
  let lang: LanguageService;
  let pipe: CairoDatePipe;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: {}, en: {} },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [CairoDatePipe],
    });
    lang = TestBed.inject(LanguageService);
    pipe = TestBed.inject(CairoDatePipe);
  });

  it('renders the Cairo calendar day, not the UTC day', () => {
    lang.use('en');
    expect(pipe.transform('2026-07-01T21:30:00Z')).toBe('Jul 2, 2026');
  });

  it('returns an empty string for null', () => {
    expect(pipe.transform(null)).toBe('');
  });

  it('uses Arabic-Indic digits in Arabic', () => {
    lang.use('ar');
    expect(pipe.transform('2026-07-01T21:30:00Z')).toContain('٢٠٢٦');
  });
});
