import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { CairoDatePipe } from '../../../core/format/cairo-date.pipe';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { Gameya } from '../gameya.models';
import { dueDayInWords } from '../gameya-calc';

@Component({
  selector: 'gy-overview-tab',
  imports: [TranslocoPipe, CairoDatePipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let g = gameya();
    @if (g.description) {
      <p class="description" data-testid="description">{{ g.description }}</p>
    }
    <dl class="facts">
      <div>
        <dt>{{ 'gameyas.form.frequency' | transloco }}</dt>
        <dd>{{ 'gameyas.frequency.' + g.frequency | transloco }}</dd>
      </div>
      <div>
        <dt>{{ 'gameyas.page.start' | transloco }}</dt>
        <dd>
          {{ g.startDate | cairoDate }}
          <span class="meta">
            {{
              dueDay().key
                | transloco
                  : {
                      day: dueDay().params['day'],
                      weekday: (dueDay().params['weekday'] + '' | transloco),
                    }
            }}
          </span>
        </dd>
      </div>
      <div>
        <dt>{{ 'gameyas.page.end' | transloco }}</dt>
        <dd>{{ g.endDate | cairoDate }}</dd>
      </div>
      <div>
        <dt>{{ 'gameyas.form.calc.totalPerShare' | transloco }}</dt>
        <dd>{{ g.totalPerShare | money }}</dd>
      </div>
      <div>
        <dt>{{ 'gameyas.form.graceDays' | transloco }}</dt>
        <dd>{{ g.graceDays }}</dd>
      </div>
      <div>
        <dt>{{ 'gameyas.form.payoutTiming' | transloco }}</dt>
        <dd>
          {{ 'gameyas.payoutTiming.' + g.payoutTiming | transloco }}
          @if (g.payoutTiming === 'FixedDate' && g.payoutOffsetDays !== null) {
            <span class="meta">
              ({{ 'gameyas.form.payoutOffsetDays' | transloco }}: {{ g.payoutOffsetDays }})
            </span>
          }
        </dd>
      </div>
      <div>
        <dt>{{ 'gameyas.form.orderMethod' | transloco }}</dt>
        <dd>{{ 'gameyas.orderMethod.' + g.orderMethod | transloco }}</dd>
      </div>
      <div>
        <dt>{{ 'gameyas.form.maxSharesPerMember' | transloco }}</dt>
        <dd>{{ g.maxSharesPerMember }}</dd>
      </div>
    </dl>
    <p class="meta">{{ 'gameyas.myShares' | transloco: { count: g.myShareCount } }}</p>
  `,
  styleUrl: './gameya-tabs.scss',
})
export class OverviewTab {
  readonly gameya = input.required<Gameya>();
  protected readonly dueDay = computed(() =>
    dueDayInWords(this.gameya().startDate, this.gameya().frequency),
  );
}
