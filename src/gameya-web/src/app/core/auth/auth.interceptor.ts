import {
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthApi } from './auth.api';
import { AuthStore } from './auth.store';
import { sharedRefresh } from './shared-refresh';

/** Public endpoints: a 401 here is a real answer, and refreshing would loop (refresh) or be pointless. */
const PUBLIC_PREFIXES = ['/api/auth/refresh', '/api/auth/otp/', '/api/auth/email/verify'];

function skipsRefresh(url: string): boolean {
  const path = url.split(/[?#]/)[0];
  return path.endsWith('/api/auth/logout') || PUBLIC_PREFIXES.some((p) => path.includes(p));
}

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

export const authInterceptor: HttpInterceptorFn = (req, next: HttpHandlerFn) => {
  const store = inject(AuthStore);
  const api = inject(AuthApi);
  const router = inject(Router);

  return next(withToken(req, store.accessToken())).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401 || skipsRefresh(req.url)) {
        return throwError(() => err);
      }
      return from(
        sharedRefresh(api, store).catch((refreshErr: unknown) => {
          store.clear();
          void router.navigateByUrl('/login');
          throw refreshErr;
        }),
      ).pipe(switchMap((session) => next(withToken(req, session.accessToken))));
    }),
  );
};
