import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AuthApi, Me } from '../../core/auth/auth.api';
import { StatusChip } from '../../shared/status-chip';

/** Three states: no email, an unverified pending email (with resend), a verified email. */
@Component({
  selector: 'gy-email-section',
  imports: [FormsModule, ReactiveFormsModule, MatButtonModule, TranslocoPipe, StatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="card">
      <h3>{{ 'profile.emailTitle' | transloco }}</h3>
      @if (me().email) {
        <div class="row" data-testid="email-verified">
          <span dir="ltr">{{ me().email }}</span>
          <gy-status-chip
            icon="check_circle"
            tone="success"
            [label]="'status.verified' | transloco"
          />
        </div>
      }
      @if (me().pendingEmail) {
        <div class="row" data-testid="email-pending">
          <span class="hint">
            {{ 'profile.emailPending' | transloco: { email: me().pendingEmail } }}
          </span>
          <gy-status-chip
            icon="schedule"
            tone="warning"
            [label]="'status.unverified' | transloco"
          />
        </div>
        <button mat-stroked-button type="button" data-testid="email-resend" (click)="resend()">
          {{ 'profile.emailResend' | transloco }}
        </button>
      } @else if (!me().email) {
        <p class="hint">{{ 'profile.emailNone' | transloco }}</p>
      }
      <form (ngSubmit)="save()">
        <label class="field">
          <span>{{ 'profile.emailLabel' | transloco }}</span>
          <input
            type="email"
            dir="ltr"
            autocomplete="email"
            data-testid="email-input"
            [formControl]="email"
          />
        </label>
        <button mat-flat-button type="submit" data-testid="email-save" [disabled]="email.invalid">
          {{ 'profile.emailSave' | transloco }}
        </button>
      </form>
    </section>
  `,
  styleUrl: './profile.scss',
})
export class EmailSection {
  readonly me = input.required<Me>();
  readonly changed = output<void>();

  private readonly api = inject(AuthApi);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  protected readonly email = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  });

  protected save(): void {
    if (this.email.invalid) return;
    this.api.changeEmail(this.email.value.trim()).subscribe(() => {
      this.email.reset('');
      this.notifySent();
    });
  }

  protected resend(): void {
    this.api.resendEmail().subscribe(() => this.notifySent());
  }

  private notifySent(): void {
    this.snack.open(this.transloco.translate('profile.emailSent'), undefined, { duration: 4000 });
    this.changed.emit();
  }
}
