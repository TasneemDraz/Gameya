import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { LanguageService } from '../i18n/language.service';
import { authInterceptor } from './auth.interceptor';
import { AuthStore } from './auth.store';
import { errorInterceptor } from './error.interceptor';
import { provideSessionBootstrap } from './session-bootstrap';

const session = (token: string) => ({
  accessToken: token,
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  profileComplete: true,
});
const unauthorized = { status: 401, statusText: 'Unauthorized' };
const me = { id: 'u1', language: 'en' };

describe('provideSessionBootstrap', () => {
  let ctrl: HttpTestingController;
  let store: AuthStore;
  let snack: { open: ReturnType<typeof vi.fn> };
  let use: ReturnType<typeof vi.fn>;
  let ready: Promise<void>;

  beforeEach(() => {
    snack = { open: vi.fn() };
    use = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor, authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatSnackBar, useValue: snack },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        { provide: LanguageService, useValue: { use } },
        provideSessionBootstrap(),
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
    ready = TestBed.inject(ApplicationInitStatus).donePromise;
  });
  afterEach(() => ctrl.verify());

  it('resumes the session and applies the saved language', async () => {
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush(session('t1'));
    (await vi.waitFor(() => ctrl.expectOne('/api/me'))).flush(me);
    await ready;
    expect(store.accessToken()).toBe('t1');
    expect(use).toHaveBeenCalledWith('en');
  });

  it('retries a grace-window 401 once and resumes with the other tab successor', async () => {
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush(
      { code: 'REFRESH_TOKEN_INVALID' },
      unauthorized,
    );
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush(session('successor'));
    (await vi.waitFor(() => ctrl.expectOne('/api/me'))).flush(me);
    await ready;
    expect(store.accessToken()).toBe('successor');
    expect(snack.open).not.toHaveBeenCalled();
  });

  it('stays anonymous and silent when both refresh attempts get 401', async () => {
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush({}, unauthorized);
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush({}, unauthorized);
    await ready;
    expect(store.isAuthenticated()).toBe(false);
    expect(snack.open).not.toHaveBeenCalled();
  });

  it('stays silent on a network error at startup', async () => {
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).error(new ProgressEvent('error'));
    await ready;
    expect(store.isAuthenticated()).toBe(false);
    expect(snack.open).not.toHaveBeenCalled();
  });

  it('keeps a valid session when me() fails after a successful refresh', async () => {
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush(session('t1'));
    (await vi.waitFor(() => ctrl.expectOne('/api/me'))).flush(
      {},
      { status: 500, statusText: 'Server Error' },
    );
    await ready;
    expect(store.accessToken()).toBe('t1');
    expect(snack.open).not.toHaveBeenCalled();
  });
});
