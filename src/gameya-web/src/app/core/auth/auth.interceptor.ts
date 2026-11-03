import {
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth.api';
import { AuthStore, Session } from './auth.store';

const AUTH_PREFIX = '/api/auth/';
let inFlight: Promise<Session> | null = null;

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

/** One refresh per tab (shared promise) and one per browser (Web Lock across tabs). */
function sharedRefresh(api: AuthApi, store: AuthStore): Promise<Session> {
  if (inFlight) return inFlight;
  const run = async () => {
    const session = await firstValueFrom(api.refresh());
    store.setSession(session);
    return session;
  };
  const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined;
  const p: Promise<Session> = (async () =>
    locks ? await locks.request('gameya-refresh', run) : await run())();
  inFlight = p;
  const reset = () => {
    inFlight = null;
  };
  p.then(reset, reset);
  return p;
}

export const authInterceptor: HttpInterceptorFn = (req, next: HttpHandlerFn) => {
  const store = inject(AuthStore);
  const api = inject(AuthApi);
  const router = inject(Router);

  return next(withToken(req, store.accessToken())).pipe(
    catchError((err: unknown) => {
      if (
        !(err instanceof HttpErrorResponse) ||
        err.status !== 401 ||
        req.url.includes(AUTH_PREFIX)
      ) {
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
