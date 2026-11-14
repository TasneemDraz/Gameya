import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import { Gameya, GameyaMember } from '../gameya.models';
import { GameyaFormPage } from './gameya-form.page';

const texts = {
  errors: { INVALID_STATUS_TRANSITION: 'BAD-STATUS' },
  gameyas: {
    form: {
      errors: { required: 'REQUIRED', server: 'SERVER-INVALID', general: 'GENERAL' },
      notEditable: 'NOT-EDITABLE',
      reacceptMessageUnknown: 'REACCEPT-UNKNOWN',
      reacceptTitle: 'REACCEPT-TITLE',
      reacceptMessage: 'REACCEPT {{count}}',
    },
  },
};

const existing = (over: Partial<Gameya> = {}): Gameya => ({
  id: 'g1',
  name: 'Family',
  description: null,
  contributionAmount: 500,
  shareCount: 10,
  frequency: 'Monthly',
  startDate: '2020-01-05', // in the past: allowed while unchanged
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
  endDate: '2020-10-05',
  totalPerShare: 5000,
  assignedShares: 1,
  reservedShares: 0,
  availableShares: 9,
  myRole: 'Organizer',
  myShareCount: 1,
  needsReaccept: false,
  inviteCode: 'ABC234',
  pendingJoinRequestCount: 0,
  termsDiff: null,
  ...over,
});

const member = (over: Partial<GameyaMember>): GameyaMember => ({
  userId: 'u',
  fullName: 'X',
  photoUrl: null,
  phone: null,
  shareCount: 1,
  isOrganizer: false,
  needsReaccept: false,
  ...over,
});

const digits = (s: string | null | undefined) =>
  (s ?? '')
    .replace(/[٠-٩]/g, (d) => String('٠١٢٣٤٥٦٧٨٩'.indexOf(d)))
    .replace(/[.٫]\d+/g, '')
    .replace(/[^0-9]/g, '');

describe('GameyaFormPage', () => {
  let ctrl: HttpTestingController;
  let router: Router;

  function setup(id: string | null = null): ComponentFixture<GameyaFormPage> {
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
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
      ],
    });
    ctrl = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const f = TestBed.createComponent(GameyaFormPage);
    f.detectChanges();
    return f;
  }
  afterEach(() => ctrl.verify());

  const q = (f: ComponentFixture<GameyaFormPage>, id: string) =>
    f.nativeElement.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

  function set(f: ComponentFixture<GameyaFormPage>, id: string, value: string): void {
    const el = q(f, id) as HTMLInputElement | HTMLSelectElement;
    el.value = value;
    el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? 'change' : 'input'));
    f.detectChanges();
  }
  function check(f: ComponentFixture<GameyaFormPage>, id: string, on: boolean): void {
    const el = q(f, id) as HTMLInputElement;
    el.checked = on;
    el.dispatchEvent(new Event('change'));
    f.detectChanges();
  }
  function fillValid(f: ComponentFixture<GameyaFormPage>): void {
    set(f, 'f-name', 'Office');
    set(f, 'f-amount', '1000');
    set(f, 'f-shareCount', '10');
    set(f, 'f-startDate', '2099-01-31');
  }
  const submit = (f: ComponentFixture<GameyaFormPage>) => {
    (q(f, 'f-save') as HTMLButtonElement).click();
    f.detectChanges();
  };

  it('calc card updates live: pot, rounds, end date, total per share', () => {
    const f = setup();
    fillValid(f);
    expect(digits(q(f, 'calc-pot')?.textContent)).toBe('10000');
    expect(digits(q(f, 'calc-rounds')?.textContent)).toBe('10');
    expect(digits(q(f, 'calc-total')?.textContent)).toBe('10000');
    expect(q(f, 'calc-end')?.textContent?.trim()).not.toBe('');
    set(f, 'f-shareCount', '12');
    expect(digits(q(f, 'calc-pot')?.textContent)).toBe('12000');
    expect(digits(q(f, 'calc-rounds')?.textContent)).toBe('12');
  });

  it('sends every field, with defaults, when creating', () => {
    const f = setup();
    fillValid(f);
    submit(f);
    const req = ctrl.expectOne('/api/gameyas');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      name: 'Office',
      description: null,
      contributionAmount: 1000,
      shareCount: 10,
      frequency: 'Monthly',
      startDate: '2099-01-31',
      graceDays: 3,
      payoutTiming: 'AfterFullCollection',
      payoutOffsetDays: null,
      orderMethod: 'Manual',
      maxSharesPerMember: 2,
      organizerParticipates: true,
      rulesText: null,
      organizerShares: 1,
    });
    req.flush({ id: 'new1' });
    expect(router.navigate).toHaveBeenCalledWith(['/gameyas', 'new1']);
  });

  it('does not send an invalid form', () => {
    const f = setup();
    set(f, 'f-name', 'ab');
    submit(f);
    ctrl.expectNone('/api/gameyas');
  });

  it('payout offset is shown and required only for FixedDate', () => {
    const f = setup();
    fillValid(f);
    expect(q(f, 'f-payoutOffsetDays')).toBeNull();
    set(f, 'f-payoutTiming', 'FixedDate');
    expect(q(f, 'f-payoutOffsetDays')).not.toBeNull();
    submit(f);
    ctrl.expectNone('/api/gameyas');
    set(f, 'f-payoutOffsetDays', '4');
    submit(f);
    const req = ctrl.expectOne('/api/gameyas');
    expect(req.request.body.payoutOffsetDays).toBe(4);
    expect(req.request.body.payoutTiming).toBe('FixedDate');
    req.flush({ id: 'n' });
  });

  it('my-shares stepper appears only when participating and is capped by max per member', () => {
    const f = setup();
    fillValid(f);
    expect(q(f, 'shares-value')?.textContent?.trim()).toBe('1');
    (q(f, 'shares-inc') as HTMLButtonElement).click();
    f.detectChanges();
    expect(q(f, 'shares-value')?.textContent?.trim()).toBe('2');
    expect((q(f, 'shares-inc') as HTMLButtonElement).disabled).toBe(true);
    set(f, 'f-maxSharesPerMember', '3');
    expect((q(f, 'shares-inc') as HTMLButtonElement).disabled).toBe(false);
    check(f, 'f-organizerParticipates', false);
    expect(q(f, 'shares-value')).toBeNull();
    submit(f);
    const req = ctrl.expectOne('/api/gameyas');
    expect(req.request.body.organizerShares).toBe(0);
    expect(req.request.body.organizerParticipates).toBe(false);
    req.flush({ id: 'n' });
  });

  function setupEdit(status: Gameya['status'], members: GameyaMember[] = []) {
    const f = setup('g1');
    ctrl.expectOne('/api/gameyas/g1').flush(existing({ status }));
    if (status === 'Recruiting') ctrl.expectOne('/api/gameyas/g1/members').flush(members);
    f.detectChanges();
    return f;
  }

  it('editing a money field while Recruiting asks to confirm N members re-accept', () => {
    const f = setupEdit('Recruiting', [
      member({ userId: 'o', isOrganizer: true }),
      member({ userId: 'a' }),
      member({ userId: 'b' }),
    ]);
    const open = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of(false) } as never);
    set(f, 'f-amount', '600');
    submit(f);
    expect(open).toHaveBeenCalledTimes(1);
    const data = (open.mock.calls[0][1] as { data: { message: string } }).data;
    expect(data.message).toContain('2');
    ctrl.expectNone('/api/gameyas/g1'); // cancelled: nothing sent

    open.mockReturnValue({ afterClosed: () => of(true) } as never);
    submit(f);
    const req = ctrl.expectOne('/api/gameyas/g1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.contributionAmount).toBe(600);
    expect(req.request.body.startDate).toBe('2020-01-05');
    req.flush(null);
    expect(router.navigate).toHaveBeenCalledWith(['/gameyas', 'g1']);
  });

  it('an unrelated edit while Recruiting saves without confirmation and allows an unchanged past start date', () => {
    const f = setupEdit('Recruiting', [member({ userId: 'o', isOrganizer: true })]);
    const open = vi.spyOn(TestBed.inject(MatDialog), 'open');
    set(f, 'f-name', 'Family 2');
    submit(f);
    expect(open).not.toHaveBeenCalled();
    ctrl.expectOne('/api/gameyas/g1').flush(null);
  });

  it('a changed start date in the past is rejected when editing', () => {
    const f = setupEdit('Draft');
    set(f, 'f-startDate', '2020-02-05');
    submit(f);
    ctrl.expectNone('/api/gameyas/g1');
  });

  it('server field errors are shown inline', () => {
    const f = setup();
    fillValid(f);
    submit(f);
    ctrl
      .expectOne('/api/gameyas')
      .flush(
        { code: 'VALIDATION_FAILED', errors: { contributionAmount: ['bad'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    f.detectChanges();
    const err = f.nativeElement.querySelector('mat-error') as HTMLElement | null;
    expect(err?.textContent).toContain('SERVER-INVALID');
  });

  it.each(['Active', 'Cancelled'] as const)(
    'editing a %s gameya shows a notice and no form',
    (status) => {
      const f = setupEdit(status);
      expect(q(f, 'not-editable')?.textContent).toContain('NOT-EDITABLE');
      expect(f.nativeElement.querySelector('form')).toBeNull();
    },
  );

  it('a coded error without an errors map shows a form-level message', () => {
    const f = setup();
    fillValid(f);
    submit(f);
    ctrl
      .expectOne('/api/gameyas')
      .flush({ code: 'INVALID_STATUS_TRANSITION' }, { status: 409, statusText: 'Conflict' });
    f.detectChanges();
    expect(q(f, 'form-error')?.textContent).toContain('BAD-STATUS');

    submit(f);
    ctrl.expectOne('/api/gameyas').flush({ code: 'UNKNOWN_X' }, { status: 400, statusText: 'Bad' });
    f.detectChanges();
    expect(q(f, 'form-error')?.textContent).toContain('GENERAL');
  });

  it('a failed members fetch still lets the user save, asking to confirm without a count', () => {
    const f = setup('g1');
    ctrl.expectOne('/api/gameyas/g1').flush(existing({ status: 'Recruiting' }));
    ctrl
      .expectOne('/api/gameyas/g1/members')
      .flush({ code: 'INTERNAL' }, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    expect((q(f, 'f-save') as HTMLButtonElement).disabled).toBe(false);
    const open = vi
      .spyOn(TestBed.inject(MatDialog), 'open')
      .mockReturnValue({ afterClosed: () => of(true) } as never);
    set(f, 'f-amount', '600');
    submit(f);
    const data = (open.mock.calls[0][1] as { data: { message: string } }).data;
    expect(data.message).toBe('REACCEPT-UNKNOWN');
    ctrl.expectOne('/api/gameyas/g1').flush(null);
  });

  it('rejects a contribution amount that is not a whole number', () => {
    const f = setup();
    fillValid(f);
    set(f, 'f-amount', '100.5');
    submit(f);
    ctrl.expectNone('/api/gameyas');
  });
});
