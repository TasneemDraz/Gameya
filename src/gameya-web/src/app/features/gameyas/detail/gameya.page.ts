import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map } from 'rxjs';
import { problemOf } from '../../../core/auth/error.interceptor';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { ConfirmDialog, ConfirmDialogData } from '../../../shared/confirm-dialog';
import { StatusChip } from '../../../shared/status-chip';
import { GameyaApi } from '../gameya.api';
import { Gameya } from '../gameya.models';
import { gameyaStatusChip } from '../gameya-status';
import { ActivityTab } from './activity-tab';
import { ManageTab } from './manage-tab';
import { MembersTab } from './members-tab';
import { OverviewTab } from './overview-tab';
import { ReacceptBanner } from './reaccept-banner';
import { RulesTab } from './rules-tab';

/**
 * The gam'eya page. Tab content is lazy, so only the active tab holds data; a 409 from any action
 * reloads the gam'eya and bumps `refresh`, which reloads the active tab. The error interceptor has
 * already shown the translated toast.
 */
@Component({
  selector: 'gy-gameya-page',
  imports: [
    MatButtonModule,
    MatIconModule,
    MatTabsModule,
    RouterLink,
    TranslocoPipe,
    MoneyPipe,
    StatusChip,
    OverviewTab,
    MembersTab,
    RulesTab,
    ActivityTab,
    ManageTab,
    ReacceptBanner,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (gameya(); as g) {
      <header class="head">
        <div class="top">
          <h1 class="title" data-testid="gameya-name">{{ g.name }}</h1>
          <gy-status-chip
            data-testid="gameya-status"
            [icon]="chip().icon"
            [label]="chip().labelKey | transloco"
            [tone]="chip().tone"
          />
        </div>
        <dl class="numbers">
          <div>
            <dt>{{ 'gameyas.page.pot' | transloco }}</dt>
            <dd data-testid="num-pot">{{ g.pot | money }}</dd>
          </div>
          <div>
            <dt>{{ 'gameyas.page.perShare' | transloco }}</dt>
            <dd data-testid="num-per-share">{{ g.contributionAmount | money }}</dd>
          </div>
          <div>
            <dt>{{ 'gameyas.page.rounds' | transloco }}</dt>
            <dd data-testid="num-rounds">{{ g.roundCount }}</dd>
          </div>
        </dl>
        @if (g.status === 'Recruiting') {
          <p class="meta">
            {{ 'gameyas.page.availableShares' | transloco: { count: g.availableShares } }}
          </p>
        }
        @if (canLeave() && !g.needsReaccept) {
          <button
            mat-stroked-button
            type="button"
            data-testid="leave"
            [disabled]="busy()"
            (click)="leave()"
          >
            {{ 'gameyas.page.leave' | transloco }}
          </button>
        }
      </header>

      @if (g.needsReaccept) {
        <gy-reaccept-banner
          [gameya]="g"
          [busy]="busy()"
          [canLeave]="canLeave()"
          (reaccept)="reaccept()"
          (leave)="leave()"
        />
      }

      <mat-tab-group
        mat-stretch-tabs="false"
        [selectedIndex]="tab()"
        (selectedIndexChange)="tab.set($event)"
      >
        <mat-tab [label]="'gameyas.page.overview' | transloco">
          <ng-template matTabContent>
            <gy-overview-tab [gameya]="g" />
          </ng-template>
        </mat-tab>
        <mat-tab [label]="'gameyas.page.members' | transloco">
          <ng-template matTabContent>
            <gy-members-tab
              [gameya]="g"
              [refresh]="refresh()"
              (changed)="reload()"
              (failed)="onFailed($event)"
            />
          </ng-template>
        </mat-tab>
        <mat-tab [label]="'gameyas.page.rules' | transloco">
          <ng-template matTabContent>
            <gy-rules-tab [gameya]="g" />
          </ng-template>
        </mat-tab>
        <mat-tab [label]="'gameyas.page.activity' | transloco">
          <ng-template matTabContent>
            <gy-activity-tab [gameyaId]="g.id" [refresh]="refresh()" (failed)="onFailed($event)" />
          </ng-template>
        </mat-tab>
        @if (g.myRole === 'Organizer') {
          <mat-tab>
            <ng-template mat-tab-label>
              {{ 'gameyas.page.manage' | transloco }}
              @if (g.pendingJoinRequestCount) {
                <span class="count" data-testid="manage-badge">
                  {{ g.pendingJoinRequestCount }}
                </span>
              }
            </ng-template>
            <ng-template matTabContent>
              <gy-manage-tab
                [gameya]="g"
                [refresh]="refresh()"
                (changed)="reload()"
                (failed)="onFailed($event)"
              />
            </ng-template>
          </mat-tab>
        }
      </mat-tab-group>
    } @else if (loadFailed()) {
      <section class="empty" data-testid="load-failed">
        <p>{{ 'gameyas.page.loadFailed' | transloco }}</p>
        <div class="actions">
          <button mat-stroked-button type="button" (click)="reload()">
            {{ 'common.retry' | transloco }}
          </button>
          <a mat-button routerLink="/gameyas">{{ 'gameyas.page.backToList' | transloco }}</a>
        </div>
      </section>
    } @else {
      <p class="meta" role="status">{{ 'common.loading' | transloco }}</p>
    }
  `,
  styleUrl: './gameya.page.scss',
})
export class GameyaPage {
  private readonly api = inject(GameyaApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  protected readonly gameya = signal<Gameya | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly tab = signal(0);
  /** Bumped after a conflict so the active tab reloads its own data. */
  protected readonly refresh = signal(0);

  private id = '';

  protected readonly chip = computed(() => gameyaStatusChip(this.gameya()?.status ?? 'Draft'));
  protected readonly canLeave = computed(() => {
    const g = this.gameya();
    return g?.myRole === 'Member' && g.status === 'Recruiting';
  });

  constructor() {
    inject(ActivatedRoute)
      .paramMap.pipe(
        map((p) => p.get('id') ?? ''),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((id) => {
        this.id = id;
        this.gameya.set(null);
        this.tab.set(0);
        this.reload();
      });
  }

  protected reload(): void {
    this.loadFailed.set(false);
    this.api.get(this.id).subscribe({
      next: (g) => this.gameya.set(g),
      error: () => {
        if (!this.gameya()) this.loadFailed.set(true);
      },
    });
  }

  protected reaccept(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.reaccept(this.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.reload();
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.onFailed(err);
      },
    });
  }

  protected leave(): void {
    if (this.busy()) return;
    const data: ConfirmDialogData = {
      title: this.transloco.translate('gameyas.page.leaveTitle'),
      message: this.transloco.translate('gameyas.page.leaveMessage'),
      confirmLabel: this.transloco.translate('gameyas.page.leave'),
      cancelLabel: this.transloco.translate('common.cancel'),
      destructive: true,
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .pipe(filter((ok) => ok === true))
      .subscribe(() => {
        this.busy.set(true);
        this.api.leave(this.id).subscribe({
          next: () => {
            this.busy.set(false);
            void this.router.navigate(['/gameyas']);
          },
          error: (err: unknown) => {
            this.busy.set(false);
            this.onFailed(err);
          },
        });
      });
  }

  /**
   * Coded errors were already toasted by the error interceptor. A 409 means our copy is stale:
   * reload the gam'eya and the active tab. VALIDATION_FAILED is never toasted globally, so it is
   * reported here once.
   */
  protected onFailed(err: unknown): void {
    const status = err instanceof HttpErrorResponse ? err.status : 0;
    if (status === 409) {
      this.reload();
      this.refresh.update((n) => n + 1);
      return;
    }
    if (problemOf(err)?.code === 'VALIDATION_FAILED') {
      this.snack.open(this.transloco.translate('gameyas.form.errors.general'), undefined, {
        duration: 5000,
      });
    }
  }
}
