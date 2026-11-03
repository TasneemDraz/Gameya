import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { authGuard, profileCompleteGuard } from './auth.guards';
import { AuthStore } from './auth.store';

function run(guard: typeof authGuard): unknown {
  return TestBed.runInInjectionContext(() => guard({} as never, {} as never));
}

function signIn(profileComplete: boolean) {
  TestBed.inject(AuthStore).setSession({
    accessToken: 't',
    accessTokenExpiresAt: '',
    profileComplete,
  });
}

describe('guards', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('authGuard sends anonymous users to /login', () => {
    const result = run(authGuard) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/login');
  });

  it('authGuard allows signed-in users', () => {
    signIn(false);
    expect(run(authGuard)).toBe(true);
  });

  it('profileCompleteGuard redirects an incomplete profile to /welcome/name', () => {
    signIn(false);
    const result = run(profileCompleteGuard) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/welcome/name');
  });

  it('profileCompleteGuard allows a complete profile', () => {
    signIn(true);
    expect(run(profileCompleteGuard)).toBe(true);
  });
});
