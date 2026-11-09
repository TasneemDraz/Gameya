import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from './auth.api';
import { AuthStore, Session } from './auth.store';

let inFlight: Promise<Session> | null = null;

/**
 * One refresh per tab (shared promise) and one per browser (Web Lock across tabs).
 * A 401 inside the lock is retried once: another tab may have rotated the token meanwhile, so the cookie
 * can already hold its successor. Only a second failure reaches the caller.
 */
export function sharedRefresh(api: AuthApi, store: AuthStore): Promise<Session> {
  if (inFlight) return inFlight;
  const attempt = async () => {
    const session = await firstValueFrom(api.refresh());
    store.setSession(session);
    return session;
  };
  const run = async () => {
    try {
      return await attempt();
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.status === 401) return attempt();
      throw err;
    }
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
