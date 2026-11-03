import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type StatusTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

/** Status is never colour-only: the chip always shows an icon and text. */
@Component({
  selector: 'gy-status-chip',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': '"tone-" + tone()' },
  template: `<mat-icon aria-hidden="true">{{ icon() }}</mat-icon><span>{{ label() }}</span>`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding-block: 2px;
      padding-inline: 8px 12px;
      border-radius: 999px;
      font-size: 0.875rem;
      line-height: 1.5;
      color: var(--gy-tone-fg);
      background: var(--gy-tone-bg);
    }

    mat-icon {
      inline-size: 18px;
      block-size: 18px;
      font-size: 18px;
    }

    :host(.tone-neutral) {
      --gy-tone-fg: var(--gy-neutral-fg);
      --gy-tone-bg: var(--gy-neutral-bg);
    }

    :host(.tone-success) {
      --gy-tone-fg: var(--gy-success-fg);
      --gy-tone-bg: var(--gy-success-bg);
    }

    :host(.tone-warning) {
      --gy-tone-fg: var(--gy-warning-fg);
      --gy-tone-bg: var(--gy-warning-bg);
    }

    :host(.tone-danger) {
      --gy-tone-fg: var(--gy-danger-fg);
      --gy-tone-bg: var(--gy-danger-bg);
    }

    :host(.tone-info) {
      --gy-tone-fg: var(--gy-info-fg);
      --gy-tone-bg: var(--gy-info-bg);
    }
  `,
})
export class StatusChip {
  readonly icon = input.required<string>();
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');
}
