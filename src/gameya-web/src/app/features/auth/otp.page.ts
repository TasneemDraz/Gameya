import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { AuthApi } from '../../core/auth/auth.api';
import { AuthStore, Session } from '../../core/auth/auth.store';
import { problemOf } from '../../core/auth/error.interceptor';
import { CairoDatePipe } from '../../core/format/cairo-date.pipe';
import { LanguageService } from '../../core/i18n/language.service';
import { OtpInput } from '../../shared/otp-input';

interface OtpState {
  phone: string;
  resendAvailableAt: string;
}

@Component({
  selector: 'gy-otp-page',
  imports: [MatButtonModule, TranslocoPipe, OtpInput],
  providers: [CairoDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <h1>{{ 'auth.codeTitle' | transloco }}</h1>
      <p class="hint">{{ 'auth.codeSent' | transloco: { phone: phone } }}</p>
      <gy-otp-input (completed)="verify($event)" />
      @if (errorText()) {
        <p class="error" role="alert" data-testid="otp-error">{{ errorText() }}</p>
      }
      @if (secondsLeft() > 0) {
        <p class="hint" data-testid="resend-countdown">
          {{ 'auth.resendIn' | transloco: { seconds: secondsLeft() } }}
        </p>
      }
      <button
        mat-stroked-button
        type="button"
        data-testid="resend"
        [disabled]="secondsLeft() > 0 || busy()"
        (click)="resend()"
      >
        {{ 'auth.resend' | transloco }}
      </button>
      <button mat-button type="button" (click)="changeNumber()">
        {{ 'auth.changeNumber' | transloco }}
      </button>
    </section>
  `,
  styleUrl: './auth.scss',
})
export class OtpPage {
  private readonly api = inject(AuthApi);
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly lang = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);
  private readonly cairo = inject(CairoDatePipe);
  private readonly otp = viewChild.required(OtpInput);

  // The router state is only on the navigation while it is being activated; history.state covers later construction.
  private readonly state = (this.router.getCurrentNavigation()?.extras.state ??
    history.state ??
    {}) as Partial<OtpState>;
  protected readonly phone = this.state.phone ?? '';
  private readonly resendAt = signal(this.state.resendAvailableAt ?? '');
  private readonly now = signal(Date.now());
  protected readonly secondsLeft = computed(
    () => Math.max(0, Math.ceil((new Date(this.resendAt()).getTime() - this.now()) / 1000)) || 0,
  );
  protected readonly busy = signal(false);
  protected readonly errorText = signal<string | null>(null);

  constructor() {
    if (!this.phone) {
      void this.router.navigateByUrl('/login');
      return;
    }
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected verify(code: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.errorText.set(null);
    this.api.verifyOtp(this.phone, code).subscribe({
      next: (session) => void this.signedIn(session),
      error: (err: unknown) => {
        this.busy.set(false);
        this.errorText.set(this.describe(err));
        this.otp().clear();
      },
    });
  }

  protected resend(): void {
    this.busy.set(true);
    this.errorText.set(null);
    this.api.requestOtp(this.phone, this.lang.current()).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.resendAt.set(r.resendAvailableAt);
        this.now.set(Date.now());
        this.otp().clear();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.errorText.set(this.describe(err));
      },
    });
  }

  protected changeNumber(): void {
    void this.router.navigateByUrl('/login');
  }

  private async signedIn(session: Session): Promise<void> {
    this.store.setSession(session);
    if (session.profileComplete) {
      try {
        this.lang.use((await firstValueFrom(this.api.me())).language);
      } catch {
        /* keep the current language */
      }
    }
    await this.router.navigateByUrl(session.profileComplete ? '/profile' : '/welcome/name');
  }

  private describe(err: unknown): string | null {
    const problem = problemOf(err);
    const params = problem?.params ?? {};
    switch (problem?.code) {
      case 'OTP_INVALID':
        return this.transloco.translate('auth.attemptsLeft', {
          attemptsLeft: params['attemptsLeft'] ?? 0,
        });
      case 'OTP_LOCKED':
        return this.transloco.translate('auth.lockedUntil', {
          until: this.cairo.transform(params['lockedUntil'] as string, true),
        });
      case undefined:
        return null;
      default:
        return this.transloco.translate(`errors.${problem?.code}`, params);
    }
  }
}
