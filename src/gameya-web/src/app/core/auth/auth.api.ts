import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Lang } from '../i18n/transloco.config';
import { SILENT_ERRORS } from './error.interceptor';
import { Session } from './auth.store';

export interface OtpRequested {
  expiresAt: string;
  resendAvailableAt: string;
}

export interface Me {
  id: string;
  fullName: string | null;
  phone: string;
  email: string | null;
  emailVerified: boolean;
  pendingEmail: string | null;
  photoUrl: string | null;
  language: Lang;
  profileComplete: boolean;
}

const opts = { withCredentials: true } as const;
const silent = {
  withCredentials: true,
  context: new HttpContext().set(SILENT_ERRORS, true),
} as const;

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  requestOtp(phone: string, language: Lang): Observable<OtpRequested> {
    return this.http.post<OtpRequested>('/api/auth/otp/request', { phone, language }, opts);
  }
  verifyOtp(phone: string, code: string): Observable<Session> {
    return this.http.post<Session>('/api/auth/otp/verify', { phone, code }, opts);
  }
  refresh(): Observable<Session> {
    return this.http.post<Session>('/api/auth/refresh', {}, silent);
  }
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}, opts);
  }
  logoutAll(): Observable<void> {
    return this.http.post<void>('/api/auth/logout-all', {}, opts);
  }
  verifyEmail(token: string): Observable<void> {
    return this.http.post<void>('/api/auth/email/verify', { token }, opts);
  }
  /** `quiet` suppresses the global error snackbar, for the startup probe of anonymous visitors. */
  me(quiet = false): Observable<Me> {
    return this.http.get<Me>('/api/me', quiet ? silent : opts);
  }
  updateProfile(fullName: string, language: Lang): Observable<void> {
    return this.http.put<void>('/api/me/profile', { fullName, language }, opts);
  }
  setPhoto(file: File): Observable<{ photoUrl: string }> {
    const form = new FormData();
    form.append('file', file);
    return this.http.put<{ photoUrl: string }>('/api/me/photo', form, opts);
  }
  removePhoto(): Observable<void> {
    return this.http.delete<void>('/api/me/photo', opts);
  }
  changeEmail(email: string): Observable<void> {
    return this.http.put<void>('/api/me/email', { email }, opts);
  }
  resendEmail(): Observable<void> {
    return this.http.post<void>('/api/me/email/resend', {}, opts);
  }
  deleteAccount(): Observable<void> {
    return this.http.delete<void>('/api/me', opts);
  }
}
