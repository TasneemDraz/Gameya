import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { ActivityEntry } from '../gameya.models';
import { ActivityTab } from './activity-tab';

const texts = {
  common: { deletedUser: 'DELETED' },
  activity: {
    invitee: 'INVITEE',
    actions: { INVITE_DECLINED: 'ACT-DECLINED', MEMBER_REMOVED: 'ACT-REMOVED' },
  },
};

const entry = (over: Partial<ActivityEntry>): ActivityEntry => ({
  occurredAt: '2030-01-05T10:00:00Z',
  actorName: 'Sara',
  action: 'MEMBER_REMOVED',
  details: null,
  note: null,
  ...over,
});

describe('ActivityTab', () => {
  let ctrl: HttpTestingController;

  function setup(items: ActivityEntry[]): ComponentFixture<ActivityTab> {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: texts, en: texts },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    ctrl = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(ActivityTab);
    f.componentRef.setInput('gameyaId', 'g1');
    f.detectChanges();
    ctrl
      .expectOne((r) => r.url === '/api/gameyas/g1/activity')
      .flush({ items, page: 1, hasMore: false });
    f.detectChanges();
    return f;
  }
  afterEach(() => ctrl.verify());

  const rows = (f: ComponentFixture<ActivityTab>) =>
    Array.from<HTMLElement>(f.nativeElement.querySelectorAll('[data-testid="activity-row"]'));

  it('a decline without an actor reads as the invitee, not a deleted user', () => {
    const f = setup([
      entry({ action: 'INVITE_DECLINED', actorName: null }),
      entry({ action: 'MEMBER_REMOVED', actorName: null }),
    ]);
    const [declined, removed] = rows(f);
    expect(declined.querySelector('.name')!.textContent).toContain('INVITEE');
    expect(removed.querySelector('.name')!.textContent).toContain('DELETED');
  });

  it('shows a note only when the entry has one', () => {
    const f = setup([entry({ note: 'Did not pay' }), entry({ note: null })]);
    const [withNote, withoutNote] = rows(f);
    expect(withNote.querySelector('.note')!.textContent).toContain('Did not pay');
    expect(withoutNote.querySelector('.note')).toBeNull();
  });
});
