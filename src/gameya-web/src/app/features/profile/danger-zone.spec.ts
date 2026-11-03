import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import type { MockInstance } from 'vitest';
import { AuthStore } from '../../core/auth/auth.store';
import { DangerZone } from './danger-zone';

const session = {
  accessToken: 'tok',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  profileComplete: true,
};

describe('DangerZone', () => {
  let ctrl: HttpTestingController;
  let store: AuthStore;
  let snack: { open: ReturnType<typeof vi.fn> };
  let navigate: MockInstance<Router['navigateByUrl']>;

  function setup() {
    snack = { open: vi.fn() };
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { profile: { logoutFailed: 'FAILED' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MatSnackBar, useValue: snack },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(true) }) } },
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
    store.setSession(session);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    return TestBed.createComponent(DangerZone);
  }
  afterEach(() => ctrl.verify());

  const click = (f: ReturnType<typeof setup>, id: string) => {
    f.detectChanges();
    (f.nativeElement.querySelector(`[data-testid="${id}"]`) as HTMLButtonElement).click();
  };

  it('a failed logout shows an error and keeps the session', () => {
    const f = setup();
    click(f, 'logout');
    ctrl.expectOne('/api/auth/logout').flush({}, { status: 500, statusText: 'Server Error' });
    expect(snack.open).toHaveBeenCalledWith('FAILED', undefined, expect.anything());
    expect(store.isAuthenticated()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('a failed logout-all shows an error and keeps the session', () => {
    const f = setup();
    click(f, 'logout-all');
    ctrl.expectOne('/api/auth/logout-all').flush({}, { status: 500, statusText: 'Server Error' });
    expect(snack.open).toHaveBeenCalledWith('FAILED', undefined, expect.anything());
    expect(store.isAuthenticated()).toBe(true);
  });

  it('a successful logout-all clears the session and leaves', () => {
    const f = setup();
    click(f, 'logout-all');
    ctrl.expectOne('/api/auth/logout-all').flush(null, { status: 204, statusText: 'No Content' });
    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/');
  });
});
