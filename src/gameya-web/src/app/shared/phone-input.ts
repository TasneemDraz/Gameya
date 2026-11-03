import { ChangeDetectionStrategy, Component, forwardRef, signal } from '@angular/core';
import {
  AbstractControl,
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { normalizeDigits } from '../core/format/digits';

export const EG_MOBILE = /^01[0125]\d{8}$/;

/** Egyptian mobile field: the form value is always plain ASCII digits (e.g. 01012345678). */
@Component({
  selector: 'gy-phone-input',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PhoneInput), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => PhoneInput), multi: true },
  ],
  template: `
    <label class="field">
      <span class="label">{{ 'auth.phoneLabel' | transloco }}</span>
      <input
        type="tel"
        inputmode="tel"
        autocomplete="tel-national"
        dir="ltr"
        data-testid="phone-input"
        [value]="display()"
        [disabled]="disabled()"
        (input)="onInput($event)"
        (blur)="touch()"
      />
    </label>
    @if (showError()) {
      <p class="error" role="alert" data-testid="phone-error">
        {{ 'errors.INVALID_PHONE' | transloco }}
      </p>
    }
  `,
  styleUrl: './phone-input.scss',
})
export class PhoneInput implements ControlValueAccessor, Validator {
  protected readonly display = signal('');
  protected readonly disabled = signal(false);
  protected readonly showError = signal(false);

  private value = '';
  private onChange: (v: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  writeValue(v: string | null): void {
    this.value = v ?? '';
    this.display.set(this.value);
  }
  registerOnChange(fn: (v: string) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(d: boolean): void {
    this.disabled.set(d);
  }
  validate(control: AbstractControl): ValidationErrors | null {
    const v = (control.value ?? '') as string;
    return v === '' || EG_MOBILE.test(v) ? null : { invalidPhone: true };
  }

  protected onInput(ev: Event): void {
    const raw = (ev.target as HTMLInputElement).value;
    this.value = normalizeDigits(raw).replace(/\D/g, '').slice(0, 11);
    this.display.set(this.value);
    (ev.target as HTMLInputElement).value = this.value;
    this.onChange(this.value);
    this.showError.set(
      this.value.length > 0 && !EG_MOBILE.test(this.value) && this.value.length >= 11,
    );
  }

  protected touch(): void {
    this.onTouched();
    this.showError.set(this.value.length > 0 && !EG_MOBILE.test(this.value));
  }
}
