import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthApi } from '../../core/auth/auth.api';
import { AuthStore } from '../../core/auth/auth.store';
import { problemOf } from '../../core/auth/error.interceptor';
import { LanguageService } from '../../core/i18n/language.service';

@Component({
  selector: 'gy-name-setup-page',
  imports: [FormsModule, ReactiveFormsModule, MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="page" (ngSubmit)="submit()">
      <h1>{{ 'auth.nameTitle' | transloco }}</h1>
      <label class="field">
        <span>{{ 'auth.nameLabel' | transloco }}</span>
        <input type="text" autocomplete="name" data-testid="name-input" [formControl]="name" />
      </label>
      @if (error()) {
        <p class="error" role="alert" data-testid="name-error">
          {{ 'errors.' + error() | transloco }}
        </p>
      }
      <p class="notice" role="note" data-testid="no-money-notice">
        {{ 'landing.noMoneyNotice' | transloco }}
      </p>
      <button
        mat-flat-button
        type="submit"
        data-testid="name-save"
        [disabled]="name.invalid || busy()"
      >
        {{ 'auth.nameSave' | transloco }}
      </button>
    </form>
  `,
  styleUrl: './auth.scss',
})
export class NameSetupPage {
  private readonly api = inject(AuthApi);
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly lang = inject(LanguageService);

  protected readonly name = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.minLength(2), Validators.maxLength(60)],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.name.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.updateProfile(this.name.value.trim(), this.lang.current()).subscribe({
      next: () => {
        this.store.markProfileComplete();
        void this.router.navigateByUrl('/profile');
      },
      error: (err: unknown) => {
        this.busy.set(false);
        const code = problemOf(err)?.code;
        this.error.set(
          code === 'NAME_INVALID' || code === 'VALIDATION_FAILED' ? 'NAME_INVALID' : null,
        );
      },
    });
  }
}
