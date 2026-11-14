import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Gameya } from '../gameya.models';

/** The organizer's rules plus the fixed statement that the app never holds money. */
@Component({
  selector: 'gy-rules-tab',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (gameya().rulesText; as rules) {
      <p class="rules" data-testid="rules-text">{{ rules }}</p>
    } @else {
      <p class="empty" data-testid="rules-text">{{ 'gameyas.page.noRules' | transloco }}</p>
    }
    <p class="notice" role="note" data-testid="no-money-notice">
      {{ 'gameyas.page.noMoneyNotice' | transloco }}
    </p>
  `,
  styleUrl: './gameya-tabs.scss',
})
export class RulesTab {
  readonly gameya = input.required<Gameya>();
}
