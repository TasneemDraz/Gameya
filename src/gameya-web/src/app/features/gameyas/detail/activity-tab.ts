import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { CairoDatePipe } from '../../../core/format/cairo-date.pipe';
import { GameyaApi } from '../gameya.api';
import { ActivityEntry } from '../gameya.models';

/**
 * Paged activity log; action codes are rendered through `activity.actions.*`. The server leaves out
 * entries and notes a viewer may not see, and never names who declined an invite, so a missing
 * actor is either that invitee or a deleted user.
 */
@Component({
  selector: 'gy-activity-tab',
  imports: [MatButtonModule, TranslocoPipe, CairoDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loaded() && items().length === 0) {
      <p class="empty">{{ 'activity.empty' | transloco }}</p>
    }
    <ol class="list">
      @for (e of items(); track $index) {
        <li class="row activity" data-testid="activity-row">
          <div class="who">
            <span class="name">{{ e.actorName ?? (fallbackActorKey(e) | transloco) }}</span>
            <span>{{ 'activity.actions.' + e.action | transloco }}</span>
            @if (e.note) {
              <span class="note">{{ e.note }}</span>
            }
            <time class="meta" [attr.datetime]="e.occurredAt">{{
              e.occurredAt | cairoDate: true
            }}</time>
          </div>
        </li>
      }
    </ol>
    @if (hasMore()) {
      <button
        mat-stroked-button
        type="button"
        data-testid="activity-more"
        [disabled]="loading()"
        (click)="more()"
      >
        {{ 'activity.more' | transloco }}
      </button>
    }
  `,
  styleUrl: './gameya-tabs.scss',
})
export class ActivityTab {
  private readonly api = inject(GameyaApi);

  readonly gameyaId = input.required<string>();
  /** Bumped by the page to reload after a conflict. */
  readonly refresh = input(0);
  readonly failed = output<unknown>();

  protected readonly items = signal<ActivityEntry[]>([]);
  protected readonly hasMore = signal(false);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  private page = 1;

  constructor() {
    effect(() => {
      const id = this.gameyaId();
      this.refresh();
      untracked(() => this.fetch(id, 1));
    });
  }

  protected fallbackActorKey(e: ActivityEntry): string {
    return e.action === 'INVITE_DECLINED' ? 'activity.invitee' : 'common.deletedUser';
  }

  protected more(): void {
    this.fetch(this.gameyaId(), this.page + 1);
  }

  private fetch(id: string, page: number): void {
    this.loading.set(true);
    this.api.activity(id, page).subscribe({
      next: (res) => {
        this.items.update((list) => (page === 1 ? res.items : [...list, ...res.items]));
        this.page = page;
        this.hasMore.set(res.hasMore);
        this.loading.set(false);
        this.loaded.set(true);
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.failed.emit(err);
      },
    });
  }
}
