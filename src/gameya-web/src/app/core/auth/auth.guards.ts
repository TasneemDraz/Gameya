import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth.store';

export const authGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  return store.isAuthenticated() ? true : inject(Router).createUrlTree(['/login']);
};

export const profileCompleteGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  return store.profileComplete() ? true : inject(Router).createUrlTree(['/welcome/name']);
};
