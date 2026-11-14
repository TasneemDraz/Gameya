import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { CairoDatePipe } from '../../core/format/cairo-date.pipe';
import { MoneyPipe } from '../../core/format/money.pipe';
import { GameyaApi } from '../gameyas/gameya.api';
import { InvitePreview } from '../gameyas/gameya.models';
import { JoinForm, JoinSubmission } from '../gameyas/join/join-form';

type View = 'loading' | 'failed' | 'invalid' | 'cancelled' | 'member' | 'pending' | 'full' | 'open';

function isInvalidCode(err: unknown): boolean {
  return (
    err instanceof HttpErrorResponse &&
    (err.status === 404 || err.error?.code === 'INVITE_CODE_INVALID')
  );
}

/** Public `/j/:code`. Shows the terms and the organizer's name only, never member data. */
@Component({
  selector: 'gy-invite-preview-page',
  imports: [JoinForm, MatButtonModule, RouterLink, TranslocoPipe, CairoDatePipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './invite-preview.page.html',
  styleUrl: './invite-preview.page.scss',
})
export class InvitePreviewPage {
  private readonly api = inject(GameyaApi);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  protected readonly code = inject(ActivatedRoute).snapshot.paramMap.get('code') ?? '';

  protected readonly preview = signal<InvitePreview | null>(null);
  protected readonly invalid = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);

  protected readonly loginQuery = { returnUrl: `/j/${this.code}` };

  protected readonly view = computed<View>(() => {
    if (this.invalid()) return 'invalid';
    if (this.loadFailed()) return 'failed';
    const p = this.preview();
    if (!p) return 'loading';
    if (p.state === 'Cancelled') return 'cancelled';
    if (p.relationship === 'Member' || p.relationship === 'Organizer') return 'member';
    if (p.relationship === 'RequestPending') return 'pending';
    if (p.state === 'Full') return 'full';
    return 'open';
  });

  protected readonly maxShares = computed(() => {
    const p = this.preview();
    return p ? Math.max(1, Math.min(p.maxSharesPerMember, p.availableShares)) : 1;
  });

  protected readonly roundCount = computed(() => this.preview()?.shareCount ?? 0);

  constructor() {
    this.load();
  }

  private load(): void {
    this.api.preview(this.code).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.invalid.set(false);
        this.loadFailed.set(false);
      },
      error: (err: unknown) => {
        // Only a 404 / INVITE_CODE_INVALID means the link is bad (unknown, replaced and malformed codes look
        // the same). A rate limit, a server error or a network failure says nothing about the link: offer a
        // retry, and when a reload after join/withdraw/re-accept fails keep the preview already on screen.
        if (isInvalidCode(err)) {
          this.invalid.set(true);
        } else if (!this.preview()) {
          this.loadFailed.set(true);
        }
      },
    });
  }

  protected retry(): void {
    this.loadFailed.set(false);
    this.load();
  }

  protected join(body: JoinSubmission): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api.join(this.code, body).subscribe({
      next: () => {
        this.busy.set(false);
        this.snack.open(this.transloco.translate('invite.join.submitted'), undefined, {
          duration: 4000,
        });
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }

  protected withdraw(): void {
    const p = this.preview();
    if (!p || this.busy()) return;
    this.busy.set(true);
    this.api.withdrawMyRequest(p.gameyaId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }

  protected reaccept(): void {
    const p = this.preview();
    if (!p || this.busy()) return;
    this.busy.set(true);
    this.api.reaccept(p.gameyaId).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }
}
