import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter } from 'rxjs';
import { ReasonDialog, ReasonDialogData } from '../../../shared/reason-dialog';
import { GameyaApi } from '../gameya.api';
import { Gameya, GameyaMember } from '../gameya.models';
import { releaseKey } from './busy';

/** Members list. Phones are shown exactly as the server sends them (masked for members). */
@Component({
  selector: 'gy-members-tab',
  imports: [MatButtonModule, MatIconModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (members().length === 0 && loaded()) {
      <p class="empty">{{ 'gameyas.manage.none' | transloco }}</p>
    }
    <ul class="list">
      @for (m of members(); track m.userId) {
        <li class="row" data-testid="member-row">
          <div class="who">
            <span class="name">{{ nameOf(m) }}</span>
            @if (m.phone) {
              <span class="phone" dir="ltr" data-testid="member-phone">{{ m.phone }}</span>
            }
            <span class="meta">{{
              'gameyas.page.shares' | transloco: { count: m.shareCount }
            }}</span>
            <span class="badges">
              @if (m.isOrganizer) {
                <span class="badge">
                  <mat-icon aria-hidden="true">star</mat-icon>
                  {{ 'gameyas.page.organizerBadge' | transloco }}
                </span>
              }
              @if (m.needsReaccept) {
                <span class="badge">
                  <mat-icon aria-hidden="true">warning</mat-icon>
                  {{ 'gameyas.needsReaccept' | transloco }}
                </span>
              }
            </span>
          </div>
          @if (canRemove() && !m.isOrganizer) {
            <button
              mat-stroked-button
              type="button"
              class="destructive"
              data-testid="remove-member"
              [attr.aria-label]="'gameyas.manage.removeNamed' | transloco: { name: nameOf(m) }"
              [disabled]="busy().has(m.userId)"
              (click)="remove(m)"
            >
              {{ 'gameyas.manage.removeMember' | transloco }}
            </button>
          }
        </li>
      }
    </ul>
  `,
  styleUrl: './gameya-tabs.scss',
})
export class MembersTab {
  private readonly api = inject(GameyaApi);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);

  readonly gameya = input.required<Gameya>();
  /** Bumped by the page to reload after a conflict. */
  readonly refresh = input(0);
  readonly changed = output<void>();
  readonly failed = output<unknown>();

  protected readonly members = signal<GameyaMember[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  private readonly id = computed(() => this.gameya().id);
  protected readonly canRemove = computed(
    () => this.gameya().myRole === 'Organizer' && this.gameya().status === 'Recruiting',
  );

  constructor() {
    effect(() => {
      const id = this.id();
      this.refresh();
      untracked(() => this.load(id));
    });
  }

  protected nameOf(m: GameyaMember): string {
    return m.fullName ?? this.transloco.translate('common.deletedUser');
  }

  protected remove(m: GameyaMember): void {
    const data: ReasonDialogData = {
      titleKey: 'gameyas.manage.removeTitle',
      required: true,
      minLength: 5,
      maxLength: 500,
    };
    this.dialog
      .open<ReasonDialog, ReasonDialogData, string | undefined>(ReasonDialog, { data })
      .afterClosed()
      .pipe(filter((r): r is string => typeof r === 'string' && r.length >= 5))
      .subscribe((reason) => {
        this.busy.update((s) => new Set(s).add(m.userId));
        this.api.removeMember(this.id(), m.userId, reason).subscribe({
          next: () => {
            this.members.update((list) => list.filter((x) => x.userId !== m.userId));
            this.busy.update((s) => releaseKey(s, m.userId));
            this.changed.emit();
          },
          error: (err: unknown) => {
            this.busy.update((s) => releaseKey(s, m.userId));
            this.failed.emit(err);
          },
        });
      });
  }

  private load(id: string): void {
    this.api.members(id).subscribe({
      next: (rows) => {
        this.members.set(rows);
        this.loaded.set(true);
      },
      error: (err: unknown) => this.failed.emit(err),
    });
  }
}
