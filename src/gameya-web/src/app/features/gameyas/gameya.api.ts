import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SILENT_ERRORS } from '../../core/auth/error.interceptor';
import {
  ActivityPage,
  Gameya,
  GameyaMember,
  GameyaSettingsBody,
  InvitePreview,
  JoinBody,
  JoinRequest,
  MyGameya,
  MyInvite,
  PendingInvite,
} from './gameya.models';

const opts = { withCredentials: true } as const;
const silent = {
  withCredentials: true,
  context: new HttpContext().set(SILENT_ERRORS, true),
} as const;

@Injectable({ providedIn: 'root' })
export class GameyaApi {
  private readonly http = inject(HttpClient);

  // Gam'eyas
  listMine(): Observable<MyGameya[]> {
    return this.http.get<MyGameya[]>('/api/gameyas', opts);
  }
  get(id: string): Observable<Gameya> {
    return this.http.get<Gameya>(`/api/gameyas/${id}`, opts);
  }
  create(body: GameyaSettingsBody): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/gameyas', body, opts);
  }
  update(id: string, body: GameyaSettingsBody): Observable<void> {
    return this.http.put<void>(`/api/gameyas/${id}`, body, opts);
  }
  open(id: string): Observable<void> {
    return this.http.post<void>(`/api/gameyas/${id}/open`, {}, opts);
  }
  regenerateCode(id: string): Observable<void> {
    return this.http.post<void>(`/api/gameyas/${id}/invite-code`, {}, opts);
  }
  cancel(id: string, reason?: string): Observable<void> {
    return this.http.post<void>(`/api/gameyas/${id}/cancel`, { reason: reason ?? null }, opts);
  }
  activity(id: string, page = 1): Observable<ActivityPage> {
    return this.http.get<ActivityPage>(`/api/gameyas/${id}/activity`, {
      ...opts,
      params: new HttpParams().set('page', page),
    });
  }

  // Members
  members(id: string): Observable<GameyaMember[]> {
    return this.http.get<GameyaMember[]>(`/api/gameyas/${id}/members`, opts);
  }
  leave(id: string): Observable<void> {
    return this.http.post<void>(`/api/gameyas/${id}/leave`, {}, opts);
  }
  reaccept(id: string): Observable<void> {
    return this.http.post<void>(`/api/gameyas/${id}/reaccept`, {}, opts);
  }
  removeMember(id: string, userId: string, reason: string): Observable<void> {
    return this.http.delete<void>(`/api/gameyas/${id}/members/${userId}`, {
      ...opts,
      body: { reason },
    });
  }

  // Join requests
  joinRequests(id: string): Observable<JoinRequest[]> {
    return this.http.get<JoinRequest[]>(`/api/gameyas/${id}/join-requests`, opts);
  }
  approve(id: string, requestId: string): Observable<void> {
    return this.http.post<void>(`/api/gameyas/${id}/join-requests/${requestId}/approve`, {}, opts);
  }
  reject(id: string, requestId: string, reason?: string): Observable<void> {
    return this.http.post<void>(
      `/api/gameyas/${id}/join-requests/${requestId}/reject`,
      { reason: reason ?? null },
      opts,
    );
  }
  withdrawMyRequest(id: string): Observable<void> {
    return this.http.delete<void>(`/api/gameyas/${id}/join-requests/mine`, opts);
  }

  // Direct invites (organizer side)
  invites(id: string): Observable<PendingInvite[]> {
    return this.http.get<PendingInvite[]>(`/api/gameyas/${id}/invites`, opts);
  }
  inviteByPhone(id: string, phone: string, shareCount: number): Observable<{ id: string }> {
    return this.http.post<{ id: string }>(
      `/api/gameyas/${id}/invites`,
      { phone, shareCount },
      opts,
    );
  }
  revokeInvite(id: string, inviteId: string): Observable<void> {
    return this.http.delete<void>(`/api/gameyas/${id}/invites/${inviteId}`, opts);
  }

  // Public invite link and my invites. The preview is silent: the page shows its own 404 state.
  preview(code: string): Observable<InvitePreview> {
    return this.http.get<InvitePreview>(`/api/invite/${encodeURIComponent(code)}`, silent);
  }
  join(code: string, body: JoinBody): Observable<void> {
    return this.http.post<void>(`/api/invite/${encodeURIComponent(code)}/join`, body, opts);
  }
  listMyInvites(): Observable<MyInvite[]> {
    return this.http.get<MyInvite[]>('/api/me/invites', opts);
  }
  acceptInvite(inviteId: string, body: JoinBody): Observable<void> {
    return this.http.post<void>(`/api/me/invites/${inviteId}/accept`, body, opts);
  }
  declineInvite(inviteId: string): Observable<void> {
    return this.http.post<void>(`/api/me/invites/${inviteId}/decline`, {}, opts);
  }
}
