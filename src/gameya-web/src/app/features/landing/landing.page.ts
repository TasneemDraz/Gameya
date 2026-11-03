import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { MoneyPipe } from '../../core/format/money.pipe';

@Component({
  selector: 'gy-landing-page',
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslocoPipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <h1>{{ 'landing.title' | transloco }}</h1>
      <p>{{ 'landing.pitch' | transloco }}</p>
      <ul class="points">
        @for (key of points; track key) {
          <li>
            <mat-icon aria-hidden="true">check_circle</mat-icon>
            <span>{{ key | transloco }}</span>
          </li>
        }
      </ul>
      <p class="sample">
        {{ 'landing.sample' | transloco }}:
        <strong data-testid="money-sample">{{ 1000 | money }}</strong>
      </p>
      <p class="notice" role="note" data-testid="no-money-notice">
        <mat-icon aria-hidden="true">info</mat-icon>
        <span>{{ 'landing.noMoneyNotice' | transloco }}</span>
      </p>
      <a mat-flat-button routerLink="/login" data-testid="start">{{
        'landing.start' | transloco
      }}</a>
    </section>
  `,
  styleUrl: './landing.page.scss',
})
export class LandingPage {
  protected readonly points = ['landing.point1', 'landing.point2', 'landing.point3'];
}
