import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { Gameya, GameyaMember, JoinRequest, PendingInvite } from '../gameya.models';
import { ManageTab } from './manage-tab';
import { MembersTab } from './members-tab';

const texts = {
  common: { confirm: 'CONFIRM', cancel: 'CANCEL' },
  reason: { label: 'REASON', tooShort: 'TOO-SHORT {{min}}' },
  gameyas: {
    manage: {
      whatsappMessage: 'JOIN "{{name}}" {{link}}',
      approveNamed: 'APPROVE {{name}}',
      rejectNamed: 'REJECT {{name}}',
      revokeNamed: 'REVOKE {{name}}',
      removeNamed: 'REMOVE {{name}}',
      removeTitle: 'REMOVE-TITLE',
    },
  },
};

const gameya = (over: Partial<Gameya> = {}): Gameya => ({
  id: 'g1',
  name: 'Family circle',
  description: null,
  contributionAmount: 500,
  shareCount: 10,
  frequency: 'Monthly',
  startDate: '2030-01-05',
  graceDays: 3,
  payoutTiming: 'AfterFullCollection',
  payoutOffsetDays: null,
  orderMethod: 'Manual',
  maxSharesPerMember: 2,
  organizerParticipates: true,
  rulesText: null,
  status: 'Recruiting',
  termsVersion: 1,
  currency: 'EGP',
  pot: 5000,
  roundCount: 10,
  endDate: '2030-10-05',
  totalPerShare: 5000,
  assignedShares: 3,
  reservedShares: 0,
  availableShares: 7,
  myRole: 'Organizer',
  myShareCount: 1,
  needsReaccept: false,
  inviteCode: 'ABC234',
  pendingJoinRequestCount: 2,
  termsDiff: null,
  ...over,
});

const request = (over: Partial<JoinRequest>): JoinRequest => ({
  id: 'r1',
  userId: 'u1',
  fullName: 'Mona',
  phone: '01012345678',
  sharePreferences: [[]],
  termsCurrent: true,
  ...over,
});

const invite = (over: Partial<PendingInvite>): PendingInvite => ({
  id: 'i1',
  phone: '01112345678',
  shareCount: 1,
  registeredName: null,
  ...over,
});

const member = (over: Partial<GameyaMember>): GameyaMember => ({
  userId: 'u1',
  fullName: 'Mona',
  photoUrl: null,
  phone: '01012345678',
  shareCount: 1,
  isOrganizer: false,
  needsReaccept: false,
  ...over,
});

function configure() {
  TestBed.configureTestingModule({
    imports: [
      TranslocoTestingModule.forRoot({
        langs: { ar: texts, en: texts },
        translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
      }),
    ],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
    ],
  });
}

const q = (f: ComponentFixture<unknown>, id: string) =>
  (f.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);
const all = (f: ComponentFixture<unknown>, id: string) =>
  Array.from(
    (f.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(`[data-testid="${id}"]`),
  );

describe('ManageTab', () => {
  let ctrl: HttpTestingController;

  function setup(
    g: Gameya,
    requests: JoinRequest[] = [],
    invites: PendingInvite[] = [],
  ): ComponentFixture<ManageTab> {
    configure();
    ctrl = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(ManageTab);
    f.componentRef.setInput('gameya', g);
    f.detectChanges();
    if (g.status === 'Recruiting') {
      ctrl.expectOne('/api/gameyas/g1/join-requests').flush(requests);
      ctrl.expectOne('/api/gameyas/g1/invites').flush(invites);
      f.detectChanges();
    }
    return f;
  }
  afterEach(() => ctrl.verify());

  it('WhatsApp link is wa.me with the pre-filled message containing the /j/ link', () => {
    const f = setup(gameya());
    const link = `${location.origin}/j/ABC234`;
    expect(q(f, 'invite-code')!.textContent).toContain('ABC234');
    expect(q(f, 'invite-link')!.textContent).toContain(link);
    const a = q(f, 'whatsapp') as HTMLAnchorElement;
    expect(a.getAttribute('href')).toBe(
      'https://wa.me/?text=' + encodeURIComponent(`JOIN "Family circle" ${link}`),
    );
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toContain('noopener');
  });

  it('join requests show a count badge; approve and reject (optional reason)', () => {
    const f = setup(gameya(), [
      request({ id: 'r1', fullName: 'Mona' }),
      request({ id: 'r2', userId: 'u2', fullName: 'Hany' }),
      request({ id: 'r3', userId: 'u3', fullName: 'Old', termsCurrent: false }),
    ]);
    const changed = vi.fn();
    f.componentInstance.changed.subscribe(changed);
    expect(q(f, 'requests-badge')!.textContent).toContain('3');

    const approves = all(f, 'approve');
    expect(approves.map((b) => b.getAttribute('aria-label'))).toEqual([
      'APPROVE Mona',
      'APPROVE Hany',
      'APPROVE Old',
    ]);
    expect(approves[2].hasAttribute('disabled')).toBe(true); // stale terms
    expect(q(f, 'terms-stale')).not.toBeNull();

    approves[0].click();
    f.detectChanges();
    expect(all(f, 'approve')[0].hasAttribute('disabled')).toBe(true); // in flight
    ctrl.expectOne('/api/gameyas/g1/join-requests/r1/approve').flush(null);
    f.detectChanges();
    expect(all(f, 'request-row')).toHaveLength(2);
    expect(q(f, 'requests-badge')!.textContent).toContain('2');
    expect(changed).toHaveBeenCalledTimes(1);

    // Reject without a reason sends null.
    const open = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of('') } as never);
    const rejects = all(f, 'reject');
    expect(rejects[0].getAttribute('aria-label')).toBe('REJECT Hany');
    rejects[0].click();
    expect((open.mock.calls[0][1] as { data: { required: boolean } }).data.required).toBe(false);
    const req = ctrl.expectOne('/api/gameyas/g1/join-requests/r2/reject');
    expect(req.request.body).toEqual({ reason: null });
    req.flush(null);
    f.detectChanges();
    expect(all(f, 'request-row')).toHaveLength(1);

    // Reject with a reason sends it; dismissing the dialog sends nothing.
    open.mockReturnValue({ afterClosed: () => of(undefined) } as never);
    all(f, 'reject')[0].click();
    ctrl.expectNone('/api/gameyas/g1/join-requests/r3/reject');
    open.mockReturnValue({ afterClosed: () => of('Too late') } as never);
    all(f, 'reject')[0].click();
    expect(ctrl.expectOne('/api/gameyas/g1/join-requests/r3/reject').request.body).toEqual({
      reason: 'Too late',
    });
  });

  it('invite by phone posts the phone and share count; revoke removes the row', () => {
    const f = setup(gameya(), [], [invite({ id: 'i1', registeredName: 'Hany' })]);
    const phone = q(f, 'phone-input') as HTMLInputElement;
    phone.value = '01098765432';
    phone.dispatchEvent(new Event('input'));
    const shares = q(f, 'invite-shares') as HTMLInputElement;
    shares.value = '2';
    shares.dispatchEvent(new Event('input'));
    f.detectChanges();

    q(f, 'invite-submit')!.click();
    f.detectChanges();
    expect(q(f, 'invite-submit')!.hasAttribute('disabled')).toBe(true);
    const post = ctrl.expectOne((r) => r.method === 'POST' && r.url === '/api/gameyas/g1/invites');
    expect(post.request.body).toEqual({ phone: '01098765432', shareCount: 2 });
    post.flush({ id: 'i2' }, { status: 201, statusText: 'Created' });
    ctrl
      .expectOne((r) => r.method === 'GET' && r.url === '/api/gameyas/g1/invites')
      .flush([
        invite({ id: 'i1', registeredName: 'Hany' }),
        invite({ id: 'i2', phone: '01098765432', shareCount: 2 }),
      ]);
    f.detectChanges();
    expect(all(f, 'invite-row')).toHaveLength(2);

    const revokes = all(f, 'revoke');
    expect(revokes[0].getAttribute('aria-label')).toBe('REVOKE Hany');
    expect(revokes[1].getAttribute('aria-label')).toBe('REVOKE 01098765432');
    revokes[0].click();
    f.detectChanges();
    expect(all(f, 'revoke')[0].hasAttribute('disabled')).toBe(true);
    const del = ctrl.expectOne('/api/gameyas/g1/invites/i1');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    f.detectChanges();
    expect(all(f, 'invite-row')).toHaveLength(1);
  });

  it('Open for recruiting shown only in Draft; Cancel in Draft and Recruiting', () => {
    const draft = setup(gameya({ status: 'Draft', inviteCode: null }));
    expect(q(draft, 'open')).not.toBeNull();
    expect(q(draft, 'cancel')).not.toBeNull();
    expect(q(draft, 'whatsapp')).toBeNull();

    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    const changed = vi.fn();
    draft.componentInstance.changed.subscribe(changed);
    q(draft, 'open')!.click();
    draft.detectChanges();
    expect(q(draft, 'open')!.hasAttribute('disabled')).toBe(true);
    ctrl.expectOne('/api/gameyas/g1/open').flush(null);
    expect(changed).toHaveBeenCalled();
    TestBed.resetTestingModule();

    const recruiting = setup(gameya({ status: 'Recruiting' }));
    expect(q(recruiting, 'open')).toBeNull();
    expect(q(recruiting, 'cancel')).not.toBeNull();
    TestBed.resetTestingModule();

    const active = setup(gameya({ status: 'Active' }));
    expect(q(active, 'open')).toBeNull();
    expect(q(active, 'cancel')).toBeNull();
  });

  it('cancel sends the optional reason', () => {
    const f = setup(gameya({ status: 'Draft', inviteCode: null }));
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of('No longer needed'),
    } as never);
    q(f, 'cancel')!.click();
    expect(ctrl.expectOne('/api/gameyas/g1/cancel').request.body).toEqual({
      reason: 'No longer needed',
    });
  });
});

describe('MembersTab organizer tools', () => {
  let ctrl: HttpTestingController;

  afterEach(() => ctrl.verify());

  it('remove member requires a reason of at least 5 characters', async () => {
    configure();
    ctrl = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(MembersTab);
    f.componentRef.setInput('gameya', gameya());
    f.detectChanges();
    ctrl
      .expectOne('/api/gameyas/g1/members')
      .flush([
        member({ userId: 'o', fullName: 'Sara', isOrganizer: true }),
        member({ userId: 'u1', fullName: 'Mona' }),
      ]);
    f.detectChanges();

    const removes = all(f, 'remove-member');
    expect(removes).toHaveLength(1); // never on the organizer's own row
    expect(removes[0].getAttribute('aria-label')).toBe('REMOVE Mona');

    removes[0].click();
    f.detectChanges();
    await f.whenStable();
    const input = document.querySelector<HTMLTextAreaElement>('[data-testid="reason-input"]')!;
    const confirm = document.querySelector<HTMLButtonElement>('[data-testid="reason-confirm"]')!;
    expect(input).not.toBeNull();

    input.value = 'abcd';
    input.dispatchEvent(new Event('input'));
    f.detectChanges();
    await f.whenStable();
    expect(confirm.disabled).toBe(true);
    ctrl.expectNone('/api/gameyas/g1/members/u1');

    input.value = 'Never pays';
    input.dispatchEvent(new Event('input'));
    f.detectChanges();
    await f.whenStable();
    expect(confirm.disabled).toBe(false);
    confirm.click();
    f.detectChanges();
    await f.whenStable();

    const del = ctrl.expectOne('/api/gameyas/g1/members/u1');
    expect(del.request.method).toBe('DELETE');
    expect(del.request.body).toEqual({ reason: 'Never pays' });
    del.flush(null);
    f.detectChanges();
    expect(all(f, 'member-row')).toHaveLength(1);
  });
});
