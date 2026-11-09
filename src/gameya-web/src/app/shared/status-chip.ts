import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

export type StatusTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info';

/** Status is never colour-only: the chip always shows an icon and text. */
@Component({
  selector: 'gy-status-chip',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': '"tone-" + tone()' },
  template: `<mat-icon aria-hidden="true">{{ icon() }}</mat-icon
    ><span>{{ label() }}</span>`,
  styleUrl: './status-chip.scss',
})
export class StatusChip {
  readonly icon = input.required<string>();
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');
}
