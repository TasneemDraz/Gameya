import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { errorInterceptor } from '../../../core/auth/error.interceptor';
import { ActivityEntry, Gameya, GameyaMember } from '../gameya.models';
import { GameyaPage } from './gameya.page';

const texts = {
  gameyaStatus: { Recruiting: 'STATUS-RECRUITING', Active: 'STATUS-ACTIVE' },
  errors: { CONCURRENCY_CONFLICT: 'CONFLICT' },
  activity: {
    more: 'MORE',
    actions: { GAMEYA_CREATED: 'ACT-CREATED', MEMBER_LEFT: 'ACT-LEFT' },
  },
  gameyas: {
    page: {
      overview: 'TAB-OVERVIEW',
      members: 'TAB-MEMBERS',
      rules: 'TAB-RULES',
      activity: 'TAB-ACTIVITY',
      manage: 'TAB-MANAGE',
      pot: 'POT',
      leave: 'LEAVE',
      reaccept: 'REACCEPT',
      reacceptBanner: 'BANNER {{oldAmount}} -> {{newAmount}}',
      noMoneyNotice: 'NO-MONEY',
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
  rulesText: 'Pay on time',
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
  myRole: 'Member',
  myShareCount: 1,
  needsReaccept: false,
  inviteCode: null,
  pendingJoinRequestCount: null,
  termsDiff: null,
  ...over,
});

const member = (over: Partial<GameyaMember> = {}): GameyaMember => ({
  userId: 'u1',
  fullName: 'Mona',
  photoUrl: null,
  phone: '010*****678',
  shareCount: 1,
  isOrganizer: false,
  needsReaccept: false,
  ...over,
});

const entry = (action: string): ActivityEntry => ({
  occurredAt: '2030-01-01T10:00:00Z',
  actorName: 'Sara',
  action,
  details: null,
  note: null,
});

/** Money is formatted with Arabic digits in `ar`; compare plain ASCII digits. */
const digits = (s: string | null | undefined) =>
  (s ?? '')
    .replace(/[٠-٩]/g, (d) => String('٠١٢٣٤٥٦٧٨٩'.indexOf(d)))
    .replace(/[.٫]\d+/g, '')
    .replace(/[^0-9]/g, '');

describe('GameyaPage', () => {
  let ctrl: HttpTestingController;

  function setup(g: Gameya): ComponentFixture<GameyaPage> {
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
          useValue: { paramMap: of(convertToParamMap({ id: 'g1' })) },
        },
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(GameyaPage);
    f.detectChanges();
    ctrl.expectOne('/api/gameyas/g1').flush(g);
    f.detectChanges();
    return f;
  }
  afterEach(() => ctrl.verify());

  const q = (f: ComponentFixture<GameyaPage>, id: string) =>
    (f.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const all = (f: ComponentFixture<GameyaPage>, id: string) =>
    Array.from(
      (f.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(`[data-testid="${id}"]`),
    );
  const tabs = (f: ComponentFixture<GameyaPage>) =>
    Array.from((f.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[role="tab"]'));

  async function selectTab(f: ComponentFixture<GameyaPage>, label: string) {
    const tab = tabs(f).find((t) => t.textContent?.includes(label));
    expect(tab).toBeDefined();
    tab!.click();
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
  }

  it('header shows name, status chip and key numbers', () => {
    const f = setup(gameya());
    expect(q(f, 'gameya-name')!.textContent).toContain('Family circle');
    expect(q(f, 'gameya-status')!.textContent).toContain('STATUS-RECRUITING');
    expect(digits(q(f, 'num-pot')!.textContent)).toBe('5000');
    expect(digits(q(f, 'num-per-share')!.textContent)).toBe('500');
    expect(digits(q(f, 'num-rounds')!.textContent)).toBe('10');
  });

  it('Manage tab exists only for the organizer', () => {
    const member = setup(gameya({ myRole: 'Member' }));
    expect(tabs(member).map((t) => t.textContent?.trim())).not.toContain('TAB-MANAGE');
    expect(tabs(member)).toHaveLength(4);
    TestBed.resetTestingModule();

    const org = setup(
      gameya({ myRole: 'Organizer', inviteCode: 'ABC234', pendingJoinRequestCount: 2 }),
    );
    expect(tabs(org)).toHaveLength(5);
    expect(tabs(org)[4].textContent).toContain('TAB-MANAGE');
    expect(q(org, 'manage-badge')!.textContent).toContain('2');
  });

  it('re-accept banner shows old and new amount and re-accept clears it', () => {
    const f = setup(
      gameya({
        needsReaccept: true,
        contributionAmount: 600,
        termsDiff: { oldContributionAmount: 500, oldShareCount: null, oldFrequency: null },
      }),
    );
    const banner = q(f, 'reaccept-banner')!;
    expect(banner).not.toBeNull();
    expect(banner.textContent).toContain('BANNER');
    expect(digits(banner.textContent)).toBe('500600');
    expect(q(f, 'banner-leave')).not.toBeNull();

    q(f, 'reaccept')!.click();
    f.detectChanges();
    expect(q(f, 'reaccept')!.hasAttribute('disabled')).toBe(true);
    const req = ctrl.expectOne('/api/gameyas/g1/reaccept');
    expect(req.request.method).toBe('POST');
    req.flush(null);
    ctrl.expectOne('/api/gameyas/g1').flush(gameya({ contributionAmount: 600 }));
    f.detectChanges();
    expect(q(f, 'reaccept-banner')).toBeNull();
  });

  it('Leave is shown to members only while Recruiting', () => {
    const recruiting = setup(gameya({ myRole: 'Member', status: 'Recruiting' }));
    expect(q(recruiting, 'leave')).not.toBeNull();
    TestBed.resetTestingModule();

    const active = setup(gameya({ myRole: 'Member', status: 'Active' }));
    expect(q(active, 'leave')).toBeNull();
    TestBed.resetTestingModule();

    const organizer = setup(gameya({ myRole: 'Organizer', status: 'Recruiting' }));
    expect(q(organizer, 'leave')).toBeNull();
  });

  it('leaving asks to confirm and returns to my gameyas', () => {
    const f = setup(gameya());
    const router = TestBed.inject(Router);
    const nav = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    q(f, 'leave')!.click();
    ctrl.expectOne('/api/gameyas/g1/leave').flush(null);
    expect(nav).toHaveBeenCalledWith(['/gameyas']);
  });

  it('activity renders action codes through i18n and pages', async () => {
    const f = setup(gameya());
    await selectTab(f, 'TAB-ACTIVITY');
    const first = ctrl.expectOne(
      (r) => r.url === '/api/gameyas/g1/activity' && r.params.get('page') === '1',
    );
    first.flush({ items: [entry('GAMEYA_CREATED')], page: 1, hasMore: true });
    f.detectChanges();
    expect(all(f, 'activity-row')).toHaveLength(1);
    expect(all(f, 'activity-row')[0].textContent).toContain('ACT-CREATED');
    expect(all(f, 'activity-row')[0].textContent).toContain('Sara');

    q(f, 'activity-more')!.click();
    f.detectChanges();
    expect(q(f, 'activity-more')!.hasAttribute('disabled')).toBe(true);
    ctrl
      .expectOne((r) => r.url === '/api/gameyas/g1/activity' && r.params.get('page') === '2')
      .flush({ items: [entry('MEMBER_LEFT')], page: 2, hasMore: false });
    f.detectChanges();
    const rows = all(f, 'activity-row').map((r) => r.textContent);
    expect(rows).toHaveLength(2);
    expect(rows[1]).toContain('ACT-LEFT');
    expect(q(f, 'activity-more')).toBeNull();
  });

  it('a 409 reloads data and shows a toast', async () => {
    const f = setup(gameya());
    const snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    await selectTab(f, 'TAB-MEMBERS');
    ctrl.expectOne('/api/gameyas/g1/members').flush([member()]);
    f.detectChanges();

    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
      afterClosed: () => of(true),
    } as never);
    q(f, 'leave')!.click();
    ctrl
      .expectOne('/api/gameyas/g1/leave')
      .flush({ code: 'CONCURRENCY_CONFLICT' }, { status: 409, statusText: 'Conflict' });
    f.detectChanges();

    expect(snack).toHaveBeenCalledWith('CONFLICT', undefined, expect.anything());
    ctrl.expectOne('/api/gameyas/g1').flush(gameya());
    ctrl.expectOne('/api/gameyas/g1/members').flush([member(), member({ userId: 'u2' })]);
    f.detectChanges();
    expect(all(f, 'member-row')).toHaveLength(2);
  });

  it('members see phones exactly as the server sends them', async () => {
    const f = setup(gameya());
    await selectTab(f, 'TAB-MEMBERS');
    ctrl.expectOne('/api/gameyas/g1/members').flush([member()]);
    f.detectChanges();
    expect(q(f, 'member-phone')!.textContent?.trim()).toBe('010*****678');
    expect(q(f, 'remove-member')).toBeNull();
  });

  it('the Rules tab shows the rules and the no-money notice', async () => {
    const f = setup(gameya());
    await selectTab(f, 'TAB-RULES');
    expect(q(f, 'rules-text')!.textContent).toContain('Pay on time');
    expect(q(f, 'no-money-notice')!.textContent).toContain('NO-MONEY');
  });
});
