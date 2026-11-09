import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AuthApi, Me } from '../../core/auth/auth.api';
import { problemOf } from '../../core/auth/error.interceptor';
import { LanguageService } from '../../core/i18n/language.service';
import { Lang } from '../../core/i18n/transloco.config';
import { DangerZone } from './danger-zone';
import { EmailSection } from './email-section';
import { PhotoSection } from './photo-section';

@Component({
  selector: 'gy-profile-page',
  imports: [
    FormsModule,
    ReactiveFormsModule,
    MatButtonModule,
    TranslocoPipe,
    PhotoSection,
    EmailSection,
    DangerZone,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="title">{{ 'profile.title' | transloco }}</h1>
    @if (me(); as m) {
      <p class="phone" dir="ltr">{{ m.phone }}</p>
      <h2 data-testid="profile-name">{{ m.fullName }}</h2>

      <section class="card">
        <form (ngSubmit)="saveName()">
          <label class="field">
            <span>{{ 'profile.nameLabel' | transloco }}</span>
            <input
              type="text"
              autocomplete="name"
              data-testid="profile-name-input"
              [formControl]="name"
            />
          </label>
          @if (nameError()) {
            <p class="error" role="alert">{{ 'errors.NAME_INVALID' | transloco }}</p>
          }
          <button
            mat-flat-button
            type="submit"
            [disabled]="name.invalid || name.value === m.fullName"
          >
            {{ 'profile.save' | transloco }}
          </button>
        </form>
      </section>

      <section class="card">
        <h3>{{ 'profile.language' | transloco }}</h3>
        <div class="row" role="group" [attr.aria-label]="'profile.language' | transloco">
          <button
            mat-stroked-button
            type="button"
            data-testid="lang-ar"
            [attr.aria-pressed]="lang.current() === 'ar'"
            (click)="setLanguage('ar')"
          >
            العربية
          </button>
          <button
            mat-stroked-button
            type="button"
            data-testid="lang-en"
            [attr.aria-pressed]="lang.current() === 'en'"
            (click)="setLanguage('en')"
          >
            English
          </button>
        </div>
      </section>

      <gy-photo-section [me]="m" (changed)="load()" />
      <gy-email-section [me]="m" (changed)="load()" />
      <gy-danger-zone />
    }
  `,
  styleUrl: './profile.scss',
})
export class ProfilePage {
  private readonly api = inject(AuthApi);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  protected readonly lang = inject(LanguageService);

  protected readonly me = signal<Me | null>(null);
  protected readonly nameError = signal(false);
  protected readonly name = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.minLength(2), Validators.maxLength(60)],
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.api.me().subscribe((me) => {
      this.me.set(me);
      this.name.setValue(me.fullName ?? '', { emitEvent: false });
    });
  }

  protected saveName(): void {
    this.persist(this.name.value.trim(), this.lang.current());
  }

  protected setLanguage(l: Lang): void {
    this.lang.use(l);
    this.persist(this.me()?.fullName ?? this.name.value.trim(), l);
  }

  private persist(fullName: string, language: Lang): void {
    this.nameError.set(false);
    this.api.updateProfile(fullName, language).subscribe({
      next: () => {
        this.snack.open(this.transloco.translate('profile.saved'), undefined, { duration: 2500 });
        this.load();
      },
      error: (err: unknown) => {
        const code = problemOf(err)?.code;
        this.nameError.set(code === 'NAME_INVALID' || code === 'VALIDATION_FAILED');
      },
    });
  }
}
