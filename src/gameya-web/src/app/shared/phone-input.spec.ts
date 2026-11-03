import { Component } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { PhoneInput } from './phone-input';

@Component({
  imports: [PhoneInput, ReactiveFormsModule],
  template: `<gy-phone-input [formControl]="ctrl" />`,
})
class Host {
  ctrl = new FormControl('');
}

function type(host: HTMLElement, text: string) {
  const input = host.querySelector('input') as HTMLInputElement;
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  return input;
}

describe('PhoneInput', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: {}, en: {} },
          translocoConfig: { defaultLang: 'ar' },
        }),
      ],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('accepts "010 1234 5678" and normalizes the value to 01012345678', () => {
    const { fixture, el } = setup();
    expect(type(el, '010 1234 5678').getAttribute('inputmode')).toBe('tel');
    expect(fixture.componentInstance.ctrl.value).toBe('01012345678');
    expect(fixture.componentInstance.ctrl.valid).toBe(true);
  });

  it('normalizes Arabic-Indic digits', () => {
    const { fixture, el } = setup();
    type(el, '٠١٠١٢٣٤٥٦٧٨');
    expect(fixture.componentInstance.ctrl.value).toBe('01012345678');
  });

  it('rejects numbers that are not Egyptian mobiles', () => {
    const { fixture, el } = setup();
    type(el, '0131234567');
    expect(fixture.componentInstance.ctrl.errors).toEqual({ invalidPhone: true });
  });
});
