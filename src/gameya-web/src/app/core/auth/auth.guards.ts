import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth.store';

/**
 * Both guards keep the attempted URL as `returnUrl`; the login and profile pages pass it through
 * `safeReturnUrl` before navigating there.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  return store.isAuthenticated()
    ? true
    : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const profileCompleteGuard: CanActivateFn = (_route, state) => {
  const store = inject(AuthStore);
  return store.profileComplete()
    ? true
    : inject(Router).createUrlTree(['/welcome/name'], { queryParams: { returnUrl: state.url } });
};
