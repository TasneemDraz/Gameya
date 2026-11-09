import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthApi } from '../../core/auth/auth.api';
import { problemOf } from '../../core/auth/error.interceptor';
import { LanguageService } from '../../core/i18n/language.service';
import { PhoneInput } from '../../shared/phone-input';

@Component({
  selector: 'gy-phone-page',
  imports: [FormsModule, ReactiveFormsModule, MatButtonModule, TranslocoPipe, PhoneInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="page" (ngSubmit)="submit()">
      <h1>{{ 'auth.phoneTitle' | transloco }}</h1>
      <gy-phone-input [formControl]="phone" />
      @if (error()) {
        <p class="error" role="alert" data-testid="phone-page-error">
          {{ 'errors.' + error() | transloco }}
        </p>
      }
      <button
        mat-flat-button
        type="submit"
        data-testid="send-code"
        [disabled]="phone.invalid || busy()"
      >
        {{ 'auth.sendCode' | transloco }}
      </button>
    </form>
  `,
  styleUrl: './auth.scss',
})
export class PhonePage {
  private readonly api = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly lang = inject(LanguageService);

  protected readonly phone = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.phone.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    const phone = this.phone.value;
    this.api.requestOtp(phone, this.lang.current()).subscribe({
      next: (r) =>
        void this.router.navigate(['/login/code'], {
          state: { phone, resendAvailableAt: r.resendAvailableAt },
        }),
      error: (err: unknown) => {
        this.busy.set(false);
        const code = problemOf(err)?.code;
        this.error.set(code && (code.startsWith('OTP_') || code === 'INVALID_PHONE') ? code : null);
      },
    });
  }
}
