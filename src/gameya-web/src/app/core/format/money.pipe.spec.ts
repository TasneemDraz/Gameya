import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LanguageService } from '../i18n/language.service';
import { MoneyPipe } from './money.pipe';

describe('MoneyPipe', () => {
  let lang: LanguageService;
  let pipe: MoneyPipe;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: {}, en: {} },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [MoneyPipe],
    });
    lang = TestBed.inject(LanguageService);
    pipe = TestBed.inject(MoneyPipe);
  });

  it('formats Arabic with Arabic-Indic digits and ج.م', () => {
    lang.use('ar');
    const out = pipe.transform(2000);
    expect(out).toContain('٢٬٠٠٠');
    expect(out).toContain('ج.م');
  });

  it('formats English with EGP', () => {
    lang.use('en');
    const out = pipe.transform(2000);
    expect(out).toContain('2,000');
    expect(out).toContain('EGP');
  });
});
