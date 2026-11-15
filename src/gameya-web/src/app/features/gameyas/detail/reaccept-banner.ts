import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { Gameya } from '../gameya.models';

/** Shown to a member whose accepted terms are older than the current ones (AC-G4). */
@Component({
  selector: 'gy-reaccept-banner',
  imports: [MatButtonModule, MatIconModule, TranslocoPipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let g = gameya();
    <section class="banner" role="status" data-testid="reaccept-banner">
      <mat-icon aria-hidden="true">warning</mat-icon>
      <div class="body">
        @if (amountChanged()) {
          <p>
            {{
              'gameyas.page.reacceptBanner'
                | transloco
                  : {
                      oldAmount: (g.termsDiff?.oldContributionAmount | money),
                      newAmount: (g.contributionAmount | money),
                    }
            }}
          </p>
        } @else {
          <p>{{ 'gameyas.page.reacceptBannerGeneric' | transloco }}</p>
        }
        @if (sharesChanged()) {
          <p>
            {{
              'gameyas.page.reacceptShares'
                | transloco: { old: g.termsDiff?.oldShareCount, new: g.shareCount }
            }}
          </p>
        }
        @if (frequencyChanged()) {
          <p>
            {{
              'gameyas.page.reacceptFrequency'
                | transloco
                  : {
                      old: ('gameyas.frequency.' + g.termsDiff?.oldFrequency | transloco),
                      new: ('gameyas.frequency.' + g.frequency | transloco),
                    }
            }}
          </p>
        }
        <div class="actions">
          <button
            mat-flat-button
            type="button"
            data-testid="reaccept"
            [disabled]="busy()"
            (click)="reaccept.emit()"
          >
            {{ 'gameyas.page.reaccept' | transloco }}
          </button>
          @if (canLeave()) {
            <button
              mat-stroked-button
              type="button"
              data-testid="banner-leave"
              [disabled]="busy()"
              (click)="leave.emit()"
            >
              {{ 'gameyas.page.leave' | transloco }}
            </button>
          }
        </div>
      </div>
    </section>
  `,
  styleUrl: './reaccept-banner.scss',
})
export class ReacceptBanner {
  readonly gameya = input.required<Gameya>();
  readonly busy = input(false);
  readonly canLeave = input(false);
  readonly reaccept = output<void>();
  readonly leave = output<void>();

  private readonly diff = computed(() => this.gameya().termsDiff);
  protected readonly amountChanged = computed(() => {
    const old = this.diff()?.oldContributionAmount;
    return old !== null && old !== undefined && old !== this.gameya().contributionAmount;
  });
  protected readonly sharesChanged = computed(() => {
    const old = this.diff()?.oldShareCount;
    return old !== null && old !== undefined && old !== this.gameya().shareCount;
  });
  protected readonly frequencyChanged = computed(() => {
    const old = this.diff()?.oldFrequency;
    return !!old && old !== this.gameya().frequency;
  });
}
