import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AuthApi } from '../../core/auth/auth.api';
import { problemOf } from '../../core/auth/error.interceptor';

type State = 'working' | 'done' | 'failed';

@Component({
  selector: 'gy-verify-email-page',
  imports: [RouterLink, MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      @switch (state()) {
        @case ('working') {
          <p>{{ 'verifyEmail.verifying' | transloco }}</p>
        }
        @case ('done') {
          <p data-testid="verify-success">{{ 'verifyEmail.success' | transloco }}</p>
          <a mat-flat-button routerLink="/profile">{{ 'verifyEmail.goProfile' | transloco }}</a>
        }
        @case ('failed') {
          <p class="error" role="alert" data-testid="verify-error">{{ errorText() }}</p>
        }
      }
    </section>
  `,
  styleUrl: './verify-email.page.scss',
})
export class VerifyEmailPage {
  private readonly transloco = inject(TranslocoService);
  protected readonly state = signal<State>('working');
  protected readonly errorText = signal('');

  constructor() {
    const token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';
    inject(AuthApi)
      .verifyEmail(token)
      .subscribe({
        next: () => this.state.set('done'),
        error: (err: unknown) => {
          const code = problemOf(err)?.code ?? 'EMAIL_TOKEN_INVALID';
          this.errorText.set(this.transloco.translate(`errors.${code}`));
          this.state.set('failed');
        },
      });
  }
}
