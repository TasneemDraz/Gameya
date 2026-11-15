import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { AcceptInvitePage } from './accept-invite.page';
import { MyInvite } from '../gameya.models';

const texts = {
  common: { loading: 'LOADING' },
  invite: {
    title: 'INVITED',
    organizer: 'BY {{name}}',
    invalid: 'INVALID',
    join: {
      shares: 'SHARES',
      preferences: 'PREFS',
      acceptRules: 'RULES',
      acceptNoMoney: 'NOMONEY',
      accept: 'ACCEPT',
    },
  },
  gameyas: { title: 'MINE', amountTimesShares: '{{amount}} x {{shares}}' },
};

const invite = (over: Partial<MyInvite> = {}): MyInvite => ({
  inviteId: 'i1',
  gameyaId: 'g1',
  name: 'Family circle',
  contributionAmount: 500,
  shareCount: 10,
  roundCount: 10,
  invitedShares: 3,
  orderMethod: 'Manual',
  organizerName: 'Sara',
  ...over,
});

describe('AcceptInvitePage', () => {
  let ctrl: HttpTestingController;
  let router: Router;

  function setup(): ComponentFixture<AcceptInvitePage> {
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
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ inviteId: 'i1' }) } },
        },
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const f = TestBed.createComponent(AcceptInvitePage);
    f.detectChanges();
    return f;
  }
  afterEach(() => ctrl.verify());

  const q = (f: ComponentFixture<AcceptInvitePage>, id: string) =>
    (f.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const tickBoth = (f: ComponentFixture<AcceptInvitePage>) => {
    q(f, 'accept-rules')!.querySelector('input')!.click();
    q(f, 'accept-no-money')!.querySelector('input')!.click();
    f.detectChanges();
  };

  it('shows a loading state until the list arrives', () => {
    const f = setup();
    expect(q(f, 'loading')).not.toBeNull();
    ctrl.expectOne('/api/me/invites').flush([invite()]);
    f.detectChanges();
    expect(q(f, 'loading')).toBeNull();
  });

  it('renders the form with the fixed share count and posts the accept, then navigates', () => {
    const f = setup();
    ctrl.expectOne('/api/me/invites').flush([invite()]);
    f.detectChanges();
    expect(q(f, 'shares-fixed')!.textContent!.trim()).toBe('3');
    tickBoth(f);
    q(f, 'join-submit')!.click();
    const req = ctrl.expectOne('/api/me/invites/i1/accept');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      shares: [{ preferences: [] }, { preferences: [] }, { preferences: [] }],
      acceptRules: true,
      acceptNoMoney: true,
    });
    req.flush(null);
    expect(router.navigate).toHaveBeenCalledWith(['/gameyas', 'g1']);
  });

  it('shows the missing state when the invite is not in the list', () => {
    const f = setup();
    ctrl.expectOne('/api/me/invites').flush([invite({ inviteId: 'other' })]);
    f.detectChanges();
    expect(q(f, 'invite-missing')).not.toBeNull();
    expect(q(f, 'join-submit')).toBeNull();
  });

  it('resets busy when the accept fails', () => {
    const f = setup();
    ctrl.expectOne('/api/me/invites').flush([invite()]);
    f.detectChanges();
    tickBoth(f);
    q(f, 'join-submit')!.click();
    f.detectChanges();
    expect((q(f, 'join-submit') as HTMLButtonElement).disabled).toBe(true);
    ctrl
      .expectOne('/api/me/invites/i1/accept')
      .flush({ code: 'X' }, { status: 409, statusText: 'Conflict' });
    f.detectChanges();
    expect((q(f, 'join-submit') as HTMLButtonElement).disabled).toBe(false);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
