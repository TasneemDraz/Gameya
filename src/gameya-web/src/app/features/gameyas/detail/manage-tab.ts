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
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Observable, filter, startWith } from 'rxjs';
import { problemOf } from '../../../core/auth/error.interceptor';
import { ConfirmDialog, ConfirmDialogData } from '../../../shared/confirm-dialog';
import { PhoneInput } from '../../../shared/phone-input';
import { ReasonDialog, ReasonDialogData } from '../../../shared/reason-dialog';
import { GameyaApi, INVITE_CODES_FOR_ORGANIZER } from '../gameya.api';
import { Gameya, JoinRequest, PendingInvite } from '../gameya.models';
import { releaseKey } from './busy';

/** Organizer tools: invite link, join requests, direct invites and status actions. */
@Component({
  selector: 'gy-manage-tab',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatIconModule,
    RouterLink,
    TranslocoPipe,
    PhoneInput,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let g = gameya();
    @if (recruiting() && g.inviteCode) {
      <section class="card" aria-labelledby="invite-link-title">
        <h2 id="invite-link-title">{{ 'gameyas.manage.inviteLink' | transloco }}</h2>
        <p class="meta">
          {{ 'gameyas.manage.code' | transloco }}:
          <strong dir="ltr" data-testid="invite-code">{{ g.inviteCode }}</strong>
        </p>
        <p class="link" dir="ltr" data-testid="invite-link">{{ inviteLink() }}</p>
        <div class="actions">
          <button mat-stroked-button type="button" data-testid="copy-link" (click)="copy()">
            <mat-icon aria-hidden="true">content_copy</mat-icon>
            {{ 'gameyas.manage.copy' | transloco }}
          </button>
          <a
            mat-flat-button
            data-testid="whatsapp"
            target="_blank"
            rel="noopener noreferrer"
            [href]="whatsappUrl()"
          >
            <mat-icon aria-hidden="true">share</mat-icon>
            {{ 'gameyas.manage.whatsapp' | transloco }}
          </a>
          <button
            mat-stroked-button
            type="button"
            data-testid="regenerate"
            [disabled]="busy().has('regenerate')"
            (click)="regenerate()"
          >
            <mat-icon aria-hidden="true">autorenew</mat-icon>
            {{ 'gameyas.manage.regenerate' | transloco }}
          </button>
        </div>
      </section>
    }

    @if (recruiting()) {
      <section class="card" aria-labelledby="requests-title">
        <h2 id="requests-title">
          {{ 'gameyas.manage.requests' | transloco }}
          <span class="count" data-testid="requests-badge">{{ requests().length }}</span>
        </h2>
        @if (requests().length === 0) {
          <p class="empty">{{ 'gameyas.manage.none' | transloco }}</p>
        }
        <ul class="list">
          @for (r of requests(); track r.id) {
            <li class="row" data-testid="request-row">
              <div class="who">
                <span class="name">{{ nameOf(r.fullName) }}</span>
                @if (r.phone) {
                  <span class="phone" dir="ltr">{{ r.phone }}</span>
                }
                <span class="meta">
                  {{
                    'gameyas.manage.requestShares' | transloco: { count: r.sharePreferences.length }
                  }}
                </span>
                @if (preferencesOf(r); as prefs) {
                  <span class="meta">
                    {{ 'gameyas.manage.preferences' | transloco: { rounds: prefs } }}
                  </span>
                }
                @if (!r.termsCurrent) {
                  <span class="warn" data-testid="terms-stale">
                    <mat-icon aria-hidden="true">warning</mat-icon>
                    {{ 'gameyas.manage.termsStale' | transloco }}
                  </span>
                }
              </div>
              <div class="actions">
                <button
                  mat-flat-button
                  type="button"
                  data-testid="approve"
                  [attr.aria-label]="
                    'gameyas.manage.approveNamed' | transloco: { name: nameOf(r.fullName) }
                  "
                  [disabled]="busy().has(r.id) || !r.termsCurrent"
                  (click)="approve(r)"
                >
                  {{ 'gameyas.manage.approve' | transloco }}
                </button>
                <button
                  mat-stroked-button
                  type="button"
                  data-testid="reject"
                  [attr.aria-label]="
                    'gameyas.manage.rejectNamed' | transloco: { name: nameOf(r.fullName) }
                  "
                  [disabled]="busy().has(r.id)"
                  (click)="reject(r)"
                >
                  {{ 'gameyas.manage.reject' | transloco }}
                </button>
              </div>
            </li>
          }
        </ul>
      </section>

      <section class="card" aria-labelledby="invites-title">
        <h2 id="invites-title">{{ 'gameyas.manage.invites' | transloco }}</h2>
        <form class="invite-form" [formGroup]="inviteForm" (ngSubmit)="invite()" novalidate>
          <gy-phone-input formControlName="phone" />
          <label class="field">
            <span class="label">{{ 'gameyas.manage.inviteShares' | transloco }}</span>
            <input
              type="number"
              inputmode="numeric"
              min="1"
              [attr.max]="maxInviteShares()"
              formControlName="shares"
              data-testid="invite-shares"
            />
          </label>
          <button
            mat-flat-button
            type="submit"
            data-testid="invite-submit"
            [disabled]="!inviteValid() || busy().has('invite')"
          >
            {{ 'gameyas.manage.invite' | transloco }}
          </button>
          @if (inviteError(); as e) {
            <p class="error" role="alert" data-testid="invite-error">{{ e | transloco }}</p>
          }
        </form>
        @if (invites().length === 0) {
          <p class="empty">{{ 'gameyas.manage.none' | transloco }}</p>
        }
        <ul class="list">
          @for (i of invites(); track i.id) {
            <li class="row" data-testid="invite-row">
              <div class="who">
                <span class="phone" dir="ltr">{{ i.phone }}</span>
                <span class="meta">{{
                  'gameyas.page.shares' | transloco: { count: i.shareCount }
                }}</span>
              </div>
              <button
                mat-stroked-button
                type="button"
                data-testid="revoke"
                [attr.aria-label]="'gameyas.manage.revokeNamed' | transloco: { name: i.phone }"
                [disabled]="busy().has(i.id)"
                (click)="revoke(i)"
              >
                {{ 'gameyas.manage.revoke' | transloco }}
              </button>
            </li>
          }
        </ul>
      </section>
    }

    @if (editable()) {
      <section class="card actions">
        <a mat-stroked-button [routerLink]="['/gameyas', g.id, 'edit']" data-testid="edit-settings">
          <mat-icon aria-hidden="true">edit</mat-icon>
          {{ 'gameyas.manage.editSettings' | transloco }}
        </a>
        @if (g.status === 'Draft') {
          <button
            mat-flat-button
            type="button"
            data-testid="open"
            [disabled]="busy().has('open')"
            (click)="open()"
          >
            {{ 'gameyas.manage.open' | transloco }}
          </button>
        }
        <button
          mat-stroked-button
          type="button"
          class="destructive"
          data-testid="cancel"
          [disabled]="busy().has('cancel')"
          (click)="cancel()"
        >
          {{ 'gameyas.manage.cancel' | transloco }}
        </button>
      </section>
    }
  `,
  styleUrl: './gameya-tabs.scss',
})
export class ManageTab {
  private readonly api = inject(GameyaApi);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  readonly gameya = input.required<Gameya>();
  /** Bumped by the page to reload after a conflict. */
  readonly refresh = input(0);
  /** The gam'eya itself changed: the page reloads it. */
  readonly changed = output<void>();
  readonly failed = output<unknown>();

  protected readonly requests = signal<JoinRequest[]>([]);
  protected readonly invites = signal<PendingInvite[]>([]);
  protected readonly busy = signal<ReadonlySet<string>>(new Set());
  protected readonly inviteError = signal<string | null>(null);

  private readonly id = computed(() => this.gameya().id);
  protected readonly recruiting = computed(() => this.gameya().status === 'Recruiting');
  protected readonly editable = computed(
    () => this.gameya().status === 'Draft' || this.gameya().status === 'Recruiting',
  );
  protected readonly inviteLink = computed(
    () => `${location.origin}/j/${this.gameya().inviteCode}`,
  );
  private readonly activeLang = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });
  /** Pre-filled in the organizer's current language. */
  protected readonly whatsappUrl = computed(() => {
    this.activeLang();
    const text = this.transloco.translate('gameyas.manage.whatsappMessage', {
      name: this.gameya().name,
      link: this.inviteLink(),
    });
    return 'https://wa.me/?text=' + encodeURIComponent(text);
  });
  protected readonly maxInviteShares = computed(() =>
    Math.max(1, Math.min(this.gameya().maxSharesPerMember, this.gameya().availableShares)),
  );

  protected readonly inviteForm = new FormGroup({
    phone: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    shares: new FormControl<number | null>(1, [Validators.required, Validators.min(1)]),
  });
  private readonly inviteValue = toSignal(
    this.inviteForm.valueChanges.pipe(startWith(this.inviteForm.value)),
    { initialValue: this.inviteForm.value },
  );
  protected readonly inviteValid = computed(() => {
    this.inviteValue();
    const shares = this.inviteForm.controls.shares.value;
    return (
      this.inviteForm.valid && Number.isInteger(shares) && (shares ?? 0) <= this.maxInviteShares()
    );
  });

  constructor() {
    effect(() => {
      const id = this.id();
      const recruiting = this.recruiting();
      this.refresh();
      if (recruiting) untracked(() => this.load(id));
    });
  }

  protected nameOf(name: string | null): string {
    return name ?? this.transloco.translate('common.deletedUser');
  }

  protected preferencesOf(r: JoinRequest): string | null {
    const rounds = [...new Set(r.sharePreferences.flat())].sort((a, b) => a - b);
    const separator = this.transloco.getActiveLang() === 'ar' ? '، ' : ', ';
    return rounds.length > 0 ? rounds.join(separator) : null;
  }

  protected copy(): void {
    const link = this.inviteLink();
    void navigator.clipboard
      ?.writeText(link)
      .then(() =>
        this.snack.open(this.transloco.translate('gameyas.manage.copied'), undefined, {
          duration: 3000,
        }),
      )
      .catch(() => undefined);
  }

  protected regenerate(): void {
    this.confirm(
      'gameyas.manage.regenerateTitle',
      'gameyas.manage.regenerateMessage',
      true,
    ).subscribe(() =>
      this.run('regenerate', this.api.regenerateCode(this.id()), () => this.changed.emit()),
    );
  }

  protected open(): void {
    this.confirm('gameyas.manage.openTitle', 'gameyas.manage.openMessage', false).subscribe(() =>
      this.run('open', this.api.open(this.id()), () => this.changed.emit()),
    );
  }

  protected cancel(): void {
    this.askReason('gameyas.manage.cancelTitle').subscribe((reason) =>
      this.run('cancel', this.api.cancel(this.id(), reason || undefined), () =>
        this.changed.emit(),
      ),
    );
  }

  protected approve(r: JoinRequest): void {
    this.run(r.id, this.api.approve(this.id(), r.id), () => {
      this.requests.update((list) => list.filter((x) => x.id !== r.id));
      this.changed.emit();
    });
  }

  protected reject(r: JoinRequest): void {
    this.askReason('gameyas.manage.rejectTitle').subscribe((reason) =>
      this.run(r.id, this.api.reject(this.id(), r.id, reason || undefined), () => {
        this.requests.update((list) => list.filter((x) => x.id !== r.id));
        this.changed.emit();
      }),
    );
  }

  protected revoke(i: PendingInvite): void {
    this.run(i.id, this.api.revokeInvite(this.id(), i.id), () => {
      this.invites.update((list) => list.filter((x) => x.id !== i.id));
      this.changed.emit();
    });
  }

  protected invite(): void {
    if (!this.inviteValid() || this.busy().has('invite')) return;
    const { phone, shares } = this.inviteForm.getRawValue();
    this.inviteError.set(null);
    this.run(
      'invite',
      this.api.inviteByPhone(this.id(), phone, shares ?? 1),
      () => {
        this.inviteForm.reset({ phone: '', shares: 1 });
        this.api.invites(this.id()).subscribe({
          next: (rows) => this.invites.set(rows),
          error: (err: unknown) => this.failed.emit(err),
        });
        this.changed.emit();
      },
      (err) => this.inviteFailed(err),
    );
  }

  /**
   * INVALID_PHONE and VALIDATION_FAILED are not toasted by the error interceptor, so they are shown
   * here once. Nor are the conflicts in INVITE_CODES_FOR_ORGANIZER, whose shared text speaks as the
   * joiner ("You already…"): they get the organizer's wording here. Every other error goes to the
   * page (which reloads on a 409).
   */
  private inviteFailed(err: unknown): boolean {
    const problem = problemOf(err);
    if (problem?.code && INVITE_CODES_FOR_ORGANIZER.has(problem.code)) {
      this.inviteError.set(`gameyas.manage.inviteErrors.${problem.code}`);
      return true;
    }
    if (problem?.code === 'INVALID_PHONE' || problem?.errors?.['phone']) {
      this.inviteError.set('errors.INVALID_PHONE');
      return true;
    }
    if (problem?.code === 'VALIDATION_FAILED') {
      this.inviteError.set('gameyas.form.errors.general');
      return true;
    }
    return false;
  }

  private load(id: string): void {
    this.api.joinRequests(id).subscribe({
      next: (rows) => this.requests.set(rows),
      error: (err: unknown) => this.failed.emit(err),
    });
    this.api.invites(id).subscribe({
      next: (rows) => this.invites.set(rows),
      error: (err: unknown) => this.failed.emit(err),
    });
  }

  /** Runs one request with its button disabled; `handled` returns true when it showed the error itself. */
  private run(
    key: string,
    request: Observable<unknown>,
    done: () => void,
    handled: (err: unknown) => boolean = () => false,
  ): void {
    if (this.busy().has(key)) return;
    this.busy.update((s) => new Set(s).add(key));
    request.subscribe({
      next: () => {
        this.busy.update((s) => releaseKey(s, key));
        done();
      },
      error: (err: unknown) => {
        this.busy.update((s) => releaseKey(s, key));
        if (!handled(err)) this.failed.emit(err);
      },
    });
  }

  private confirm(titleKey: string, messageKey: string, destructive: boolean): Observable<boolean> {
    const data: ConfirmDialogData = {
      title: this.transloco.translate(titleKey),
      message: this.transloco.translate(messageKey),
      confirmLabel: this.transloco.translate('common.confirm'),
      cancelLabel: this.transloco.translate('common.cancel'),
      destructive,
    };
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .pipe(filter((ok): ok is boolean => ok === true));
  }

  /** Optional reason: emits '' when confirmed empty, nothing when dismissed. */
  private askReason(titleKey: string): Observable<string> {
    const data: ReasonDialogData = { titleKey, required: false, minLength: 0, maxLength: 500 };
    return this.dialog
      .open<ReasonDialog, ReasonDialogData, string | undefined>(ReasonDialog, { data })
      .afterClosed()
      .pipe(filter((r): r is string => typeof r === 'string'));
  }
}
