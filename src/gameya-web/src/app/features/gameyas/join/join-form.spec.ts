import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { OrderMethod } from '../gameya.models';
import { JoinForm, JoinSubmission } from './join-form';

const texts = {
  invite: {
    join: {
      shares: 'SHARES',
      fewer: 'FEWER',
      more: 'MORE',
      preferences: 'PREFS',
      noPreference: 'NONE',
      round: 'R{{n}}',
      acceptRules: 'RULES',
      acceptNoMoney: 'NOMONEY',
      submit: 'SUBMIT',
      accept: 'ACCEPT',
    },
  },
};

describe('JoinForm', () => {
  let f: ComponentFixture<JoinForm>;
  const sent: JoinSubmission[] = [];

  function setup(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: texts, en: texts },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }],
    });
    f = TestBed.createComponent(JoinForm);
    for (const [k, v] of Object.entries(inputs)) f.componentRef.setInput(k, v);
    sent.length = 0;
    f.componentInstance.submitted.subscribe((s) => sent.push(s));
    f.detectChanges();
  }
  const q = (id: string) =>
    (f.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const click = (id: string) => {
    q(id)!.click();
    f.detectChanges();
  };
  const tick = (id: string) => {
    q(id)!.querySelector<HTMLInputElement>('input')!.click();
    f.detectChanges();
  };
  const pick = (id: string, value: number) => {
    const sel = q(id) as HTMLSelectElement;
    sel.value = String(value);
    sel.dispatchEvent(new Event('change'));
    f.detectChanges();
  };
  const submit = () => q('join-submit') as HTMLButtonElement;

  it('submit is disabled until both checkboxes are ticked', () => {
    setup({ maxShares: 2, roundCount: 5 });
    expect(submit().disabled).toBe(true);
    tick('accept-rules');
    expect(submit().disabled).toBe(true);
    tick('accept-no-money');
    expect(submit().disabled).toBe(false);
    submit().click();
    expect(sent).toEqual([
      { shares: [{ preferences: [] }], acceptRules: true, acceptNoMoney: true },
    ]);
  });

  it('submit is disabled while busy', () => {
    setup({ maxShares: 2, roundCount: 5, busy: true });
    tick('accept-rules');
    tick('accept-no-money');
    expect(submit().disabled).toBe(true);
  });

  it('share stepper is capped by min(max per member, available)', () => {
    setup({ maxShares: 2, roundCount: 5 });
    click('shares-inc');
    click('shares-inc');
    click('shares-inc');
    expect(q('shares-value')!.textContent!.trim()).toBe('2');
    expect((q('shares-inc') as HTMLButtonElement).disabled).toBe(true);
    click('shares-dec');
    click('shares-dec');
    expect(q('shares-value')!.textContent!.trim()).toBe('1');
    expect((q('shares-dec') as HTMLButtonElement).disabled).toBe(true);
  });

  it('no preference pickers outside ByRequest', () => {
    setup({ maxShares: 1, roundCount: 6, orderMethod: 'Manual' as OrderMethod });
    expect(q('preferences')).toBeNull();
  });

  it('preference pickers appear under ByRequest and allow at most 3 distinct rounds', () => {
    setup({ maxShares: 1, roundCount: 6, orderMethod: 'ByRequest' as OrderMethod });
    expect((f.nativeElement as HTMLElement).querySelectorAll('select').length).toBe(3);
    pick('pref-0-0', 2);
    const second = q('pref-0-1') as HTMLSelectElement;
    const taken = Array.from(second.options).find((o) => o.value === '2')!;
    expect(taken.disabled).toBe(true);
    pick('pref-0-1', 4);
    pick('pref-0-2', 6);
    tick('accept-rules');
    tick('accept-no-money');
    submit().click();
    expect(sent[0].shares).toEqual([{ preferences: [2, 4, 6] }]);
  });

  it('invite mode fixes the share count', () => {
    setup({ fixedShares: 3, maxShares: 1, roundCount: 5 });
    expect(q('shares-fixed')!.textContent!.trim()).toBe('3');
    expect(q('shares-inc')).toBeNull();
    tick('accept-rules');
    tick('accept-no-money');
    expect(submit().textContent).toContain('ACCEPT');
    submit().click();
    expect(sent[0].shares.length).toBe(3);
  });
});
