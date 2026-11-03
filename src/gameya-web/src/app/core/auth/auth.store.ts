import { Injectable, computed, signal } from '@angular/core';

export interface Session {
  accessToken: string;
  accessTokenExpiresAt: string;
  profileComplete: boolean;
}

/** In-memory session state; the refresh token lives in an HttpOnly cookie, never here. */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly token = signal<string | null>(null);
  private readonly complete = signal(false);

  readonly accessToken = this.token.asReadonly();
  readonly profileComplete = this.complete.asReadonly();
  readonly isAuthenticated = computed(() => this.token() !== null);

  setSession(s: Session): void {
    this.token.set(s.accessToken);
    this.complete.set(s.profileComplete);
  }

  markProfileComplete(): void {
    this.complete.set(true);
  }

  clear(): void {
    this.token.set(null);
    this.complete.set(false);
  }
}
