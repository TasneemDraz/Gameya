import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { authGuard, profileCompleteGuard } from './auth.guards';
import { AuthStore } from './auth.store';

function run(guard: typeof authGuard, url = '/'): unknown {
  return TestBed.runInInjectionContext(() => guard({} as never, { url } as never));
}

const serialize = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

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
    expect(serialize(run(authGuard, '/gameyas'))).toBe('/login?returnUrl=%2Fgameyas');
  });

  it('authGuard keeps the attempted URL as returnUrl', () => {
    const result = run(authGuard, '/gameyas/g1?tab=members');
    const tree = result as UrlTree;
    expect(tree.root.children['primary'].segments.map((s) => s.path)).toEqual(['login']);
    expect(tree.queryParams['returnUrl']).toBe('/gameyas/g1?tab=members');
    expect(serialize(result)).toBe('/login?returnUrl=%2Fgameyas%2Fg1%3Ftab%3Dmembers');
  });

  it('authGuard allows signed-in users', () => {
    signIn(false);
    expect(run(authGuard)).toBe(true);
  });

  it('profileCompleteGuard redirects an incomplete profile to /welcome/name', () => {
    signIn(false);
    expect(serialize(run(profileCompleteGuard, '/gameyas'))).toBe(
      '/welcome/name?returnUrl=%2Fgameyas',
    );
  });

  it('profileCompleteGuard keeps the attempted URL as returnUrl', () => {
    signIn(false);
    const tree = run(profileCompleteGuard, '/gameyas/invites/i1/accept') as UrlTree;
    expect(tree.queryParams['returnUrl']).toBe('/gameyas/invites/i1/accept');
    expect(serialize(tree).startsWith('/welcome/name?')).toBe(true);
  });

  it('profileCompleteGuard allows a complete profile', () => {
    signIn(true);
    expect(run(profileCompleteGuard)).toBe(true);
  });
});
