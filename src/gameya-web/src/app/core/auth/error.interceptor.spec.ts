import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { HANDLED_CODES, errorInterceptor } from './error.interceptor';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let open: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    open = vi.fn();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { errors: { ALREADY_MEMBER: 'JOINER-TEXT', GAMEYA_FULL: 'FULL' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open } },
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });
  afterEach(() => ctrl.verify());

  function fail(code: string, context?: HttpContext): void {
    http.post('/x', {}, { context }).subscribe({ error: () => undefined });
    ctrl.expectOne('/x').flush({ code }, { status: 409, statusText: 'Conflict' });
  }

  it('toasts a coded error', () => {
    fail('ALREADY_MEMBER');
    expect(open).toHaveBeenCalledWith('JOINER-TEXT', undefined, { duration: 5000 });
  });

  it('leaves the codes a caller handles itself to that caller, and toasts the rest', () => {
    const context = new HttpContext().set(HANDLED_CODES, new Set(['ALREADY_MEMBER']));
    fail('ALREADY_MEMBER', context);
    expect(open).not.toHaveBeenCalled();
    fail('GAMEYA_FULL', context);
    expect(open).toHaveBeenCalledWith('FULL', undefined, { duration: 5000 });
  });
});
