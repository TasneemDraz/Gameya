import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { MyGameya, MyInvite } from '../gameya.models';
import { GameyasHomePage } from './gameyas-home.page';

const card = (over: Partial<MyGameya>): MyGameya => ({
  id: 'g1',
  name: 'Family',
  status: 'Active',
  role: 'Member',
  contributionAmount: 500,
  shareCount: 10,
  myShareCount: 2,
  needsReaccept: false,
  ...over,
});

const invite: MyInvite = {
  inviteId: 'i1',
  gameyaId: 'g9',
  name: 'Office',
  contributionAmount: 1000,
  shareCount: 12,
  roundCount: 12,
  invitedShares: 1,
  orderMethod: 'Manual',
  organizerName: 'Sara',
};

const texts = {
  gameyas: {
    organizer: 'ORGANIZER',
    needsReaccept: 'REACCEPT',
    groups: { Active: 'GROUP-ACTIVE', Recruiting: 'GROUP-RECRUITING' },
    page: { noMoneyNotice: 'NO-MONEY' },
  },
};

describe('GameyasHomePage', () => {
  let ctrl: HttpTestingController;

  function setup(mine: MyGameya[], invites: MyInvite[]) {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: texts, en: texts },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    ctrl = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(GameyasHomePage);
    f.detectChanges();
    ctrl.expectOne('/api/gameyas').flush(mine);
    ctrl.expectOne('/api/me/invites').flush(invites);
    f.detectChanges();
    return f;
  }
  afterEach(() => ctrl.verify());

  const all = (f: ReturnType<typeof setup>, id: string) =>
    Array.from<HTMLElement>(f.nativeElement.querySelectorAll(`[data-testid="${id}"]`));

  it('shows pending invites above cards grouped by status', () => {
    const f = setup(
      [
        card({ id: 'a', status: 'Active' }),
        card({ id: 'b', status: 'Recruiting' }),
        card({ id: 'c', status: 'Active' }),
      ],
      [invite],
    );
    const el: HTMLElement = f.nativeElement;
    expect(all(f, 'invite-card')).toHaveLength(1);
    expect(all(f, 'gameya-card')).toHaveLength(3);
    const headers = all(f, 'group-header').map((h) => h.textContent?.trim());
    expect(headers).toEqual(['GROUP-RECRUITING', 'GROUP-ACTIVE']);
    const invitePos = el
      .querySelector('[data-testid="invite-card"]')!
      .compareDocumentPosition(el.querySelector('[data-testid="gameya-card"]')!);
    expect(invitePos & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      el.querySelector<HTMLAnchorElement>('[data-testid="invite-accept"]')!.getAttribute('href'),
    ).toBe('/gameyas/invites/i1/accept');
  });

  it('shows the organizer badge and needs-reaccept marker', () => {
    const f = setup(
      [card({ id: 'a', role: 'Organizer' }), card({ id: 'b', needsReaccept: true })],
      [],
    );
    const cards = all(f, 'gameya-card');
    expect(cards[0].querySelector('[data-testid="organizer-badge"]')).not.toBeNull();
    expect(cards[0].querySelector('[data-testid="needs-reaccept"]')).toBeNull();
    expect(cards[1].querySelector('[data-testid="needs-reaccept"]')).not.toBeNull();
    expect(cards[1].querySelector('[data-testid="organizer-badge"]')).toBeNull();
  });

  it('decline removes the invite after the 204', () => {
    const f = setup([card({})], [invite]);
    all(f, 'invite-decline')[0].click();
    expect(all(f, 'invite-card')).toHaveLength(1);
    ctrl
      .expectOne('/api/me/invites/i1/decline')
      .flush(null, { status: 204, statusText: 'No Content' });
    f.detectChanges();
    expect(all(f, 'invite-card')).toHaveLength(0);
  });

  it('empty state explains a gameya and that the app never holds money', () => {
    const f = setup([], []);
    expect(all(f, 'empty-state')).toHaveLength(1);
    expect(all(f, 'no-money-notice')[0].textContent).toContain('NO-MONEY');
    expect(all(f, 'create-gameya').length).toBeGreaterThan(0);
  });
});
