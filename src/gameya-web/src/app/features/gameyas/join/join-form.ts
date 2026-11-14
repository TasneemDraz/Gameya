import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { JoinBody, OrderMethod } from '../gameya.models';

/** Preferences are limited to this many distinct rounds per share. */
export const MAX_PREFERENCES = 3;

export type JoinSubmission = JoinBody & { acceptRules: true; acceptNoMoney: true };

const SLOTS = Array.from({ length: MAX_PREFERENCES }, (_, i) => i);
const blankShare = (): number[] => SLOTS.map(() => 0);

/**
 * Shares, optional turn preferences (only under ByRequest) and the two terms checkboxes. Used for joining by
 * code and for accepting a direct invite (`fixedShares` set, so the share count cannot change).
 */
@Component({
  selector: 'gy-join-form',
  imports: [MatButtonModule, MatCheckboxModule, MatIconModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="form" (submit)="onSubmit($event)" novalidate>
      <div class="shares" role="group" [attr.aria-label]="'invite.join.shares' | transloco">
        <span class="label">{{ 'invite.join.shares' | transloco }}</span>
        @if (fixedShares() !== null) {
          <strong data-testid="shares-fixed">{{ shares() }}</strong>
        } @else {
          <button
            mat-icon-button
            type="button"
            data-testid="shares-dec"
            [attr.aria-label]="'invite.join.fewer' | transloco"
            [disabled]="shares() <= 1"
            (click)="setShares(shares() - 1)"
          >
            <mat-icon aria-hidden="true">remove</mat-icon>
          </button>
          <strong class="count" data-testid="shares-value" aria-live="polite">{{
            shares()
          }}</strong>
          <button
            mat-icon-button
            type="button"
            data-testid="shares-inc"
            [attr.aria-label]="'invite.join.more' | transloco"
            [disabled]="shares() >= cap()"
            (click)="setShares(shares() + 1)"
          >
            <mat-icon aria-hidden="true">add</mat-icon>
          </button>
        }
      </div>

      @if (orderMethod() === 'ByRequest') {
        @for (share of shareIndexes(); track share) {
          <fieldset class="prefs" data-testid="preferences">
            <legend>
              {{ ('invite.join.preferences' | transloco) + ' (' + (share + 1) + ')' }}
            </legend>
            @for (slot of slots; track slot) {
              <select
                class="pref"
                [attr.data-testid]="'pref-' + share + '-' + slot"
                [attr.aria-label]="
                  ('invite.join.preferences' | transloco) + ' ' + (share + 1) + '.' + (slot + 1)
                "
                (change)="setPreference(share, slot, $any($event.target).value)"
              >
                <option value="0" [selected]="pref(share, slot) === 0">
                  {{ 'invite.join.noPreference' | transloco }}
                </option>
                @for (round of rounds(); track round) {
                  <option
                    [value]="round"
                    [selected]="pref(share, slot) === round"
                    [disabled]="takenElsewhere(share, slot, round)"
                  >
                    {{ 'invite.join.round' | transloco: { n: round } }}
                  </option>
                }
              </select>
            }
          </fieldset>
        }
      }

      <mat-checkbox
        data-testid="accept-rules"
        [checked]="acceptRules()"
        (change)="acceptRules.set($event.checked)"
      >
        {{ 'invite.join.acceptRules' | transloco }}
      </mat-checkbox>
      <mat-checkbox
        data-testid="accept-no-money"
        [checked]="acceptNoMoney()"
        (change)="acceptNoMoney.set($event.checked)"
      >
        {{ 'invite.join.acceptNoMoney' | transloco }}
      </mat-checkbox>

      <button mat-flat-button type="submit" data-testid="join-submit" [disabled]="!canSubmit()">
        {{ (fixedShares() !== null ? 'invite.join.accept' : 'invite.join.submit') | transloco }}
      </button>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }
    .form {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .shares {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
    }
    .label {
      margin-inline-end: auto;
    }
    .count {
      min-inline-size: 2ch;
      text-align: center;
    }
    .prefs {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin: 0;
      padding: 8px;
      border: 1px solid var(--gy-outline, #c4c7c5);
      border-radius: 12px;
    }
    .pref {
      flex: 1 1 8rem;
      min-block-size: 44px;
      padding-inline: 8px;
    }
  `,
})
export class JoinForm {
  /** min(max shares per member, available shares); ignored when `fixedShares` is set. */
  readonly maxShares = input(1);
  readonly fixedShares = input<number | null>(null);
  readonly roundCount = input(0);
  readonly orderMethod = input<OrderMethod>('Manual');
  readonly busy = input(false);
  readonly submitted = output<JoinSubmission>();

  protected readonly slots = SLOTS;
  private readonly chosenShares = signal(1);
  /** Round number per slot per share; 0 means "no preference". */
  private readonly prefs = signal<number[][]>([]);
  protected readonly acceptRules = signal(false);
  protected readonly acceptNoMoney = signal(false);

  protected readonly cap = computed(() => Math.max(1, this.maxShares()));
  protected readonly shares = computed(
    () => this.fixedShares() ?? Math.min(Math.max(1, this.chosenShares()), this.cap()),
  );
  protected readonly shareIndexes = computed(() =>
    Array.from({ length: this.shares() }, (_, i) => i),
  );
  protected readonly rounds = computed(() =>
    Array.from({ length: this.roundCount() }, (_, i) => i + 1),
  );
  protected readonly canSubmit = computed(
    () => this.acceptRules() && this.acceptNoMoney() && !this.busy(),
  );

  protected setShares(n: number): void {
    this.chosenShares.set(Math.min(Math.max(1, n), this.cap()));
  }

  protected pref(share: number, slot: number): number {
    return this.prefs()[share]?.[slot] ?? 0;
  }

  protected setPreference(share: number, slot: number, raw: string): void {
    const value = Number(raw) || 0;
    this.prefs.update((all) => {
      const next = all.map((s) => [...s]);
      while (next.length <= share) next.push(blankShare());
      next[share][slot] = value;
      return next;
    });
  }

  /** A round already picked in another slot of the same share is not selectable again. */
  protected takenElsewhere(share: number, slot: number, round: number): boolean {
    return (this.prefs()[share] ?? []).some((r, i) => i !== slot && r === round);
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    if (!this.canSubmit()) return;
    const byRequest = this.orderMethod() === 'ByRequest';
    const shares = this.shareIndexes().map((i) => ({
      preferences: byRequest ? (this.prefs()[i] ?? []).filter((r) => r > 0) : [],
    }));
    this.submitted.emit({ shares, acceptRules: true, acceptNoMoney: true });
  }
}
