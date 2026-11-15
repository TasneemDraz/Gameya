import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { errorInterceptor } from '../../core/auth/error.interceptor';
import { InvitePreview } from '../gameyas/gameya.models';
import { InvitePreviewPage } from './invite-preview.page';

const texts = {
  invite: {
    title: 'INVITED',
    organizer: 'BY {{name}}',
    available: 'AVAIL {{count}}',
    login: 'LOGIN',
    full: 'FULL',
    fullBody: 'FULLBODY',
    cancelled: 'CANCELLED',
    cancelledBody: 'CANCELBODY',
    invalid: 'INVALID',
    invalidBody: 'INVALIDBODY',
    member: 'MEMBER',
    organizerOwn: 'OWN',
    open: 'OPEN',
    pending: 'PENDING',
    invited: 'INVITED-DIRECTLY',
    openInvite: 'OPEN-INVITE',
    withdraw: 'WITHDRAW',
    reaccept: 'REACCEPT',
    rulesTitle: 'RULES',
    loadFailed: 'LOADFAILED',
    join: {
      shares: 'SHARES',
      fewer: 'FEWER',
      more: 'MORE',
      preferences: 'PREFS',
      acceptRules: 'RULES',
      acceptNoMoney: 'NOMONEY',
      submit: 'SUBMIT',
    },
  },
  common: { retry: 'RETRY' },
  gameyas: { page: { noMoneyNotice: 'NOMONEY' }, amountTimesShares: '{{amount}} x {{shares}}' },
};

const preview = (over: Partial<InvitePreview> = {}): InvitePreview => ({
  gameyaId: 'g1',
  state: 'Open',
  name: 'Family circle',
  description: null,
  contributionAmount: 500,
  currency: 'EGP',
  shareCount: 10,
  frequency: 'Monthly',
  startDate: '2030-01-05',
  endDate: '2030-10-05',
  pot: 5000,
  graceDays: 3,
  payoutTiming: 'AfterFullCollection',
  payoutOffsetDays: null,
  orderMethod: 'Manual',
  maxSharesPerMember: 2,
  availableShares: 7,
  rulesText: 'Pay on time',
  organizerName: 'Sara',
  relationship: null,
  requestNeedsReaccept: false,
  inviteId: null,
  ...over,
});

describe('InvitePreviewPage', () => {
  let ctrl: HttpTestingController;

  function setup(response: InvitePreview | 404 | 429): ComponentFixture<InvitePreviewPage> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: texts, en: texts },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ code: 'ABC234' }) } },
        },
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(InvitePreviewPage);
    f.detectChanges();
    const req = ctrl.expectOne('/api/invite/ABC234');
    if (response === 404) {
      req.flush({ code: 'INVITE_CODE_INVALID' }, { status: 404, statusText: 'Not Found' });
    } else if (response === 429) {
      req.flush(null, { status: 429, statusText: 'Too Many Requests' });
    } else {
      req.flush(response);
    }
    f.detectChanges();
    return f;
  }
  afterEach(() => ctrl.verify());

  const q = (f: ComponentFixture<InvitePreviewPage>, id: string) =>
    (f.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('guest sees preview without member names and a login link with returnUrl', () => {
    const f = setup(preview());
    expect(q(f, 'gameya-name')!.textContent).toContain('Family circle');
    expect(q(f, 'organizer-name')!.textContent).toContain('Sara');
    const link = q(f, 'login-to-join') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toContain('returnUrl=%2Fj%2FABC234');
    expect((f.nativeElement as HTMLElement).querySelector('gy-join-form')).toBeNull();
  });

  it('a signed-in visitor sees the join form', () => {
    const f = setup(preview({ relationship: 'None' }));
    expect((f.nativeElement as HTMLElement).querySelector('gy-join-form')).not.toBeNull();
    expect(q(f, 'login-to-join')).toBeNull();
  });

  it('full, cancelled and invalid codes show distinct pages', () => {
    let f = setup(preview({ state: 'Full', availableShares: 0 }));
    expect(q(f, 'state-full')).not.toBeNull();
    expect(q(f, 'state-cancelled')).toBeNull();
    expect(q(f, 'state-invalid')).toBeNull();

    f = setup(preview({ state: 'Cancelled' }));
    expect(q(f, 'state-cancelled')).not.toBeNull();
    expect(q(f, 'state-full')).toBeNull();
    expect(q(f, 'state-invalid')).toBeNull();

    f = setup(404);
    expect(q(f, 'state-invalid')).not.toBeNull();
    expect(q(f, 'state-full')).toBeNull();
    expect(q(f, 'state-cancelled')).toBeNull();
  });

  it('members get a link to the gam’eya', () => {
    const f = setup(preview({ relationship: 'Member' }));
    const link = q(f, 'open-gameya') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/gameyas/g1');
  });

  it('a directly invited visitor is sent to their invite instead of the join form, even when full', () => {
    for (const state of ['Open', 'Full'] as const) {
      const f = setup(preview({ state, relationship: 'Invited', inviteId: 'i7' }));
      expect(q(f, 'state-invited')!.textContent).toContain('INVITED-DIRECTLY');
      const link = q(f, 'open-invite') as HTMLAnchorElement;
      expect(link.getAttribute('href')).toBe('/gameyas/invites/i7/accept');
      expect(link.textContent).toContain('OPEN-INVITE');
      expect((f.nativeElement as HTMLElement).querySelector('gy-join-form')).toBeNull();
      expect(q(f, 'state-full')).toBeNull();
    }
  });

  it('a 429 shows the retry state, not the invalid state, and retry fetches again', () => {
    const f = setup(429);
    expect(q(f, 'state-failed')).not.toBeNull();
    expect(q(f, 'state-invalid')).toBeNull();
    q(f, 'retry')!.click();
    ctrl.expectOne('/api/invite/ABC234').flush(preview());
    f.detectChanges();
    expect(q(f, 'state-failed')).toBeNull();
    expect(q(f, 'gameya-name')).not.toBeNull();
  });

  it('a 404 still shows invalid', () => {
    const f = setup(404);
    expect(q(f, 'state-invalid')).not.toBeNull();
    expect(q(f, 'state-failed')).toBeNull();
  });

  it('a failed reload after an action keeps the preview on screen', () => {
    const f = setup(preview({ relationship: 'RequestPending' }));
    q(f, 'withdraw')!.click();
    ctrl.expectOne('/api/gameyas/g1/join-requests/mine').flush(null);
    ctrl.expectOne('/api/invite/ABC234').flush(null, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    expect(q(f, 'state-failed')).toBeNull();
    expect(q(f, 'state-invalid')).toBeNull();
    expect(q(f, 'gameya-name')).not.toBeNull();
  });

  it('submitting the join form posts to the join endpoint with the body', () => {
    const f = setup(preview({ relationship: 'None' }));
    q(f, 'accept-rules')!.querySelector('input')!.click();
    q(f, 'accept-no-money')!.querySelector('input')!.click();
    f.detectChanges();
    q(f, 'join-submit')!.click();
    const req = ctrl.expectOne('/api/invite/ABC234/join');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      shares: [{ preferences: [] }],
      acceptRules: true,
      acceptNoMoney: true,
    });
    req.flush(null);
    ctrl.expectOne('/api/invite/ABC234').flush(preview({ relationship: 'RequestPending' }));
  });

  it('re-accept posts to the reaccept endpoint', () => {
    const f = setup(preview({ relationship: 'RequestPending', requestNeedsReaccept: true }));
    q(f, 'reaccept')!.click();
    const req = ctrl.expectOne('/api/gameyas/g1/reaccept');
    expect(req.request.method).toBe('POST');
    req.flush(null);
    ctrl.expectOne('/api/invite/ABC234').flush(preview({ relationship: 'RequestPending' }));
  });

  it('a pending request can be withdrawn', () => {
    const f = setup(preview({ relationship: 'RequestPending' }));
    q(f, 'withdraw')!.click();
    ctrl.expectOne('/api/gameyas/g1/join-requests/mine').flush(null);
    ctrl.expectOne('/api/invite/ABC234').flush(preview({ relationship: 'None' }));
  });
});
