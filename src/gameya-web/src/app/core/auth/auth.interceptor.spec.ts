import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthStore } from './auth.store';

const session = (token: string) => ({
  accessToken: token,
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  profileComplete: true,
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let store: AuthStore;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);
    store.setSession(session('old'));
  });
  afterEach(() => ctrl.verify());

  it('attaches the bearer token', () => {
    http.get('/api/me').subscribe();
    const req = ctrl.expectOne('/api/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer old');
    req.flush({});
  });

  it('shares one refresh between three parallel 401s and retries all with the new token', async () => {
    const results: unknown[] = [];
    const urls = ['/api/x1', '/api/x2', '/api/x3'];
    for (const u of urls) http.get(u).subscribe((r) => results.push(r));
    for (const u of urls) ctrl.expectOne(u).flush({}, { status: 401, statusText: 'Unauthorized' });

    const refresh = await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'));
    refresh.flush(session('new'));

    const retries = await vi.waitFor(() => urls.map((u) => ctrl.expectOne(u)));
    for (const r of retries) {
      expect(r.request.headers.get('Authorization')).toBe('Bearer new');
      r.flush({ ok: true });
    }
    expect(results).toHaveLength(3);
    expect(store.accessToken()).toBe('new');
  });

  it('clears the store and goes to /login when the refresh fails', async () => {
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    let failed = false;
    http.get('/api/me').subscribe({ error: () => (failed = true) });
    ctrl.expectOne('/api/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    // A 401 from refresh is retried once (another tab may have rotated the cookie); the second 401 is final.
    for (let attempt = 0; attempt < 2; attempt++) {
      const refresh = await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'));
      refresh.flush({ code: 'REFRESH_TOKEN_INVALID' }, { status: 401, statusText: 'Unauthorized' });
    }
    await vi.waitFor(() => expect(failed).toBe(true));
    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('does not try to refresh a 401 from an /api/auth/ URL', () => {
    let status = 0;
    http.post('/api/auth/otp/verify', {}).subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/auth/otp/verify').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
  });

  it('refreshes and retries /api/auth/logout-all after an expired access token', async () => {
    let done = false;
    http.post('/api/auth/logout-all', {}).subscribe(() => (done = true));
    const first = ctrl.expectOne('/api/auth/logout-all');
    expect(first.request.headers.get('Authorization')).toBe('Bearer old');
    first.flush({}, { status: 401, statusText: 'Unauthorized' });

    const refresh = await vi.waitFor(() => ctrl.expectOne('/api/auth/refresh'));
    refresh.flush(session('new'));

    const retry = await vi.waitFor(() => ctrl.expectOne('/api/auth/logout-all'));
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => expect(done).toBe(true));
  });

  it('does not start another refresh when a request to the refresh endpoint answers 401', () => {
    let status = 0;
    http.post('/api/auth/refresh', {}).subscribe({ error: (e) => (status = e.status) });
    ctrl
      .expectOne('/api/auth/refresh')
      .flush({ code: 'REFRESH_TOKEN_INVALID' }, { status: 401, statusText: 'Unauthorized' });
    expect(status).toBe(401);
    ctrl.expectNone('/api/auth/refresh');
  });

  it.each(['/api/auth/logout', '/api/auth/email/verify'])(
    'does not try to refresh a 401 from %s',
    (url) => {
      let status = 0;
      http.post(url, {}).subscribe({ error: (e) => (status = e.status) });
      ctrl.expectOne(url).flush({}, { status: 401, statusText: 'Unauthorized' });
      expect(status).toBe(401);
      ctrl.expectNone('/api/auth/refresh');
    },
  );
});
