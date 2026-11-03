import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthApi } from './auth.api';
import { authInterceptor } from './auth.interceptor';
import { AuthStore } from './auth.store';
import { sharedRefresh } from './shared-refresh';

const session = (token: string) => ({
  accessToken: token,
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  profileComplete: true,
});
const unauthorized = { status: 401, statusText: 'Unauthorized' };

describe('sharedRefresh', () => {
  let ctrl: HttpTestingController;
  let api: AuthApi;
  let store: AuthStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    api = TestBed.inject(AuthApi);
    store = TestBed.inject(AuthStore);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    ctrl.verify();
  });

  it('retries a 401 once inside the lock and keeps the session when the successor cookie works', async () => {
    const request = vi.fn((_name: string, cb: () => Promise<unknown>) => cb());
    vi.stubGlobal('navigator', { locks: { request } });

    const p = sharedRefresh(api, store);
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush(
      { code: 'REFRESH_TOKEN_INVALID' },
      unauthorized,
    );
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush(session('successor'));

    await expect(p).resolves.toMatchObject({ accessToken: 'successor' });
    expect(request).toHaveBeenCalledTimes(1);
    expect(request.mock.calls[0][0]).toBe('gameya-refresh');
    expect(store.accessToken()).toBe('successor');
  });

  it('rejects after the retry also fails with 401', async () => {
    const failed = sharedRefresh(api, store).then(
      () => 'resolved',
      (e: { status: number }) => e.status,
    );
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush({}, unauthorized);
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).flush({}, unauthorized);
    await expect(failed).resolves.toBe(401);
  });

  it('does not retry a network error', async () => {
    const failed = sharedRefresh(api, store).then(
      () => 'resolved',
      () => 'rejected',
    );
    (await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'))).error(new ProgressEvent('error'));
    await expect(failed).resolves.toBe('rejected');
    ctrl.expectNone('/api/auth/refresh');
  });
});
