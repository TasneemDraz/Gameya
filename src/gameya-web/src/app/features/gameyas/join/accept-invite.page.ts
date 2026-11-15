import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { GameyaApi } from '../gameya.api';
import { MyInvite } from '../gameya.models';
import { JoinForm, JoinSubmission } from './join-form';

type LoadState = 'loading' | 'ready' | 'missing';

/** `/gameyas/invites/:inviteId/accept`: the join form with the share count fixed by the organizer. */
@Component({
  selector: 'gy-accept-invite-page',
  imports: [JoinForm, MatButtonModule, MoneyPipe, RouterLink, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (state()) {
      @case ('loading') {
        <p data-testid="loading" role="status">{{ 'common.loading' | transloco }}</p>
      }
      @case ('ready') {
        @let inv = invite()!;
        <h1 class="title">{{ 'invite.title' | transloco }}</h1>
        <p class="name" data-testid="invite-name">{{ inv.name }}</p>
        @if (inv.organizerName) {
          <p>{{ 'invite.organizer' | transloco: { name: inv.organizerName } }}</p>
        }
        <p>
          {{
            'gameyas.amountTimesShares'
              | transloco: { amount: (inv.contributionAmount | money), shares: inv.shareCount }
          }}
        </p>
        <gy-join-form
          [fixedShares]="inv.invitedShares"
          [roundCount]="inv.roundCount"
          [orderMethod]="inv.orderMethod"
          [busy]="busy()"
          (submitted)="accept($event)"
        />
      }
      @case ('missing') {
        <h1 class="title" data-testid="invite-missing">{{ 'invite.invalid' | transloco }}</h1>
        <a mat-stroked-button routerLink="/gameyas">{{ 'gameyas.title' | transloco }}</a>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .title {
      font-size: 1.5rem;
    }
    .name {
      font-weight: 600;
      overflow-wrap: anywhere;
    }
  `,
})
export class AcceptInvitePage {
  private readonly api = inject(GameyaApi);
  private readonly router = inject(Router);
  private readonly inviteId = inject(ActivatedRoute).snapshot.paramMap.get('inviteId') ?? '';

  protected readonly state = signal<LoadState>('loading');
  protected readonly invite = signal<MyInvite | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    this.api.listMyInvites().subscribe({
      next: (list) => {
        const found = list.find((i) => i.inviteId === this.inviteId) ?? null;
        this.invite.set(found);
        this.state.set(found ? 'ready' : 'missing');
      },
      error: () => this.state.set('missing'),
    });
  }

  protected accept(body: JoinSubmission): void {
    const inv = this.invite();
    if (!inv || this.busy()) return;
    this.busy.set(true);
    this.api.acceptInvite(inv.inviteId, body).subscribe({
      next: () => {
        this.busy.set(false);
        void this.router.navigate(['/gameyas', inv.gameyaId]);
      },
      error: () => this.busy.set(false),
    });
  }
}
