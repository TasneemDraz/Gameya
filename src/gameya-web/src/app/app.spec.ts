import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { App } from './app';

describe('App', () => {
  it('creates and applies the default Arabic RTL document', async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [
        App,
        TranslocoTestingModule.forRoot({
          langs: { ar: {}, en: {} },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(fixture.componentInstance).toBeTruthy();
    expect(document.documentElement.dir).toBe('rtl');
  });
});
