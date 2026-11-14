import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { StatusChip } from '../../../shared/status-chip';
import { GameyaApi } from '../gameya.api';
import { GameyaStatus, MyGameya, MyInvite } from '../gameya.models';
import { gameyaStatusChip } from '../gameya-status';

const GROUP_ORDER: GameyaStatus[] = ['Draft', 'Recruiting', 'Active', 'Completed', 'Cancelled'];

@Component({
  selector: 'gy-gameyas-home-page',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoPipe, MoneyPipe, StatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="head">
      <h1 class="title">{{ 'gameyas.title' | transloco }}</h1>
      <a mat-flat-button routerLink="/gameyas/new" data-testid="create-gameya">
        <mat-icon aria-hidden="true">add</mat-icon>
        {{ 'gameyas.create' | transloco }}
      </a>
    </header>

    @if (invites().length > 0) {
      <section class="section" aria-labelledby="invites-title">
        <h2 id="invites-title">{{ 'gameyas.invites.title' | transloco }}</h2>
        @for (inv of invites(); track inv.inviteId) {
          <article class="card" data-testid="invite-card">
            <h3 class="name">{{ inv.name }}</h3>
            @if (inv.organizerName) {
              <p class="meta">
                {{ 'gameyas.invites.from' | transloco: { name: inv.organizerName } }}
              </p>
            }
            <p class="meta">
              {{
                'gameyas.amountTimesShares'
                  | transloco: { amount: (inv.contributionAmount | money), shares: inv.shareCount }
              }}
            </p>
            <p class="meta">
              {{ 'gameyas.invites.shares' | transloco: { count: inv.invitedShares } }}
            </p>
            <div class="actions">
              <a
                mat-flat-button
                [routerLink]="['/gameyas/invites', inv.inviteId, 'accept']"
                data-testid="invite-accept"
              >
                {{ 'gameyas.invites.accept' | transloco }}
              </a>
              <button
                mat-stroked-button
                type="button"
                data-testid="invite-decline"
                [disabled]="busy().has(inv.inviteId)"
                (click)="decline(inv)"
              >
                {{ 'gameyas.invites.decline' | transloco }}
              </button>
            </div>
          </article>
        }
      </section>
    }

    @for (group of groups(); track group.status) {
      <section class="section">
        <h2 data-testid="group-header">{{ 'gameyas.groups.' + group.status | transloco }}</h2>
        @for (g of group.items; track g.id) {
          <a class="card" [routerLink]="['/gameyas', g.id]" data-testid="gameya-card">
            <div class="top">
              <h3 class="name">{{ g.name }}</h3>
              <gy-status-chip
                [icon]="chip(g.status).icon"
                [label]="chip(g.status).labelKey | transloco"
                [tone]="chip(g.status).tone"
              />
            </div>
            <p class="meta">
              {{
                'gameyas.amountTimesShares'
                  | transloco: { amount: (g.contributionAmount | money), shares: g.shareCount }
              }}
            </p>
            <p class="meta">{{ 'gameyas.myShares' | transloco: { count: g.myShareCount } }}</p>
            <div class="badges">
              @if (g.role === 'Organizer') {
                <span class="badge" data-testid="organizer-badge">
                  <mat-icon aria-hidden="true">star</mat-icon>
                  {{ 'gameyas.organizer' | transloco }}
                </span>
              }
              @if (g.needsReaccept) {
                <span class="badge" data-testid="needs-reaccept">
                  <mat-icon aria-hidden="true">warning</mat-icon>
                  {{ 'gameyas.needsReaccept' | transloco }}
                </span>
              }
            </div>
          </a>
        }
      </section>
    }

    @if (loaded() && mine().length === 0) {
      <section class="empty" data-testid="empty-state">
        <h2>{{ 'gameyas.empty.title' | transloco }}</h2>
        <p>{{ 'gameyas.empty.body' | transloco }}</p>
        <p class="notice" data-testid="no-money-notice">
          {{ 'gameyas.page.noMoneyNotice' | transloco }}
        </p>
      </section>
    }
  `,
  styleUrl: './gameyas-home.page.scss',
})
export class GameyasHomePage {
  private readonly api = inject(GameyaApi);

  protected readonly mine = signal<MyGameya[]>([]);
  protected readonly invites = signal<MyInvite[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  protected readonly groups = computed(() =>
    GROUP_ORDER.map((status) => ({
      status,
      items: this.mine().filter((g) => g.status === status),
    })).filter((g) => g.items.length > 0),
  );

  protected readonly chip = gameyaStatusChip;

  constructor() {
    this.api.listMine().subscribe((rows) => {
      this.mine.set(rows);
      this.loaded.set(true);
    });
    this.api.listMyInvites().subscribe((rows) => this.invites.set(rows));
  }

  protected decline(inv: MyInvite): void {
    this.busy.update((s) => new Set(s).add(inv.inviteId));
    this.api.declineInvite(inv.inviteId).subscribe({
      next: () => {
        this.invites.update((list) => list.filter((i) => i.inviteId !== inv.inviteId));
        this.release(inv.inviteId);
      },
      error: () => this.release(inv.inviteId),
    });
  }

  private release(id: string): void {
    this.busy.update((s) => {
      const next = new Set(s);
      next.delete(id);
      return next;
    });
  }
}
