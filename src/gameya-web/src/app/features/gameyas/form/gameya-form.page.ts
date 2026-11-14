import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Observable, filter, map, startWith } from 'rxjs';
import { problemOf } from '../../../core/auth/error.interceptor';
import { CairoDatePipe, CAIRO_TZ } from '../../../core/format/cairo-date.pipe';
import { MoneyPipe } from '../../../core/format/money.pipe';
import { ConfirmDialog, ConfirmDialogData } from '../../../shared/confirm-dialog';
import { GameyaApi } from '../gameya.api';
import {
  Gameya,
  GameyaFrequency,
  GameyaSettingsBody,
  OrderMethod,
  PayoutTiming,
} from '../gameya.models';
import { dueDayInWords, endDate, pot } from '../gameya-calc';

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

function cairoToday(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: CAIRO_TZ }).format(new Date());
}

function integer(c: AbstractControl): ValidationErrors | null {
  const v = c.value;
  return v === null || v === '' || Number.isInteger(v) ? null : { integer: true };
}

/** Name length is measured after trimming, like the server does. */
function trimmedLength(min: number, max: number) {
  return (c: AbstractControl): ValidationErrors | null => {
    const len = String(c.value ?? '').trim().length;
    if (len === 0) return { required: true };
    if (len < min) return { minlength: true };
    return len > max ? { maxlength: true } : null;
  };
}

const num = (v: unknown): number | null => (typeof v === 'number' && Number.isFinite(v) ? v : null);

@Component({
  selector: 'gy-gameya-form-page',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    RouterLink,
    TranslocoPipe,
    MoneyPipe,
    CairoDatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="title">
      {{ (editId ? 'gameyas.form.editTitle' : 'gameyas.form.createTitle') | transloco }}
    </h1>

    @if (notEditable()) {
      <section class="notice" role="note" data-testid="not-editable">
        <p>{{ 'gameyas.form.notEditable' | transloco }}</p>
        <a mat-stroked-button [routerLink]="['/gameyas', editId]">
          {{ 'gameyas.form.backToGameya' | transloco }}
        </a>
      </section>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <section class="section" aria-labelledby="sec-basics">
          <h2 id="sec-basics">{{ 'gameyas.form.basics' | transloco }}</h2>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.name' | transloco }}</mat-label>
            <input matInput formControlName="name" data-testid="f-name" />
            @if (err('name'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.description' | transloco }}</mat-label>
            <textarea
              matInput
              rows="3"
              formControlName="description"
              data-testid="f-description"
            ></textarea>
            @if (err('description'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
        </section>

        <section class="section" aria-labelledby="sec-money">
          <h2 id="sec-money">{{ 'gameyas.form.money' | transloco }}</h2>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.amount' | transloco }}</mat-label>
            <input
              matInput
              type="number"
              inputmode="numeric"
              formControlName="contributionAmount"
              data-testid="f-amount"
            />
            @if (err('contributionAmount'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.shareCount' | transloco }}</mat-label>
            <input
              matInput
              type="number"
              inputmode="numeric"
              formControlName="shareCount"
              data-testid="f-shareCount"
            />
            @if (err('shareCount'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.frequency' | transloco }}</mat-label>
            <select matNativeControl formControlName="frequency" data-testid="f-frequency">
              @for (v of frequencies; track v) {
                <option [value]="v">{{ 'gameyas.frequency.' + v | transloco }}</option>
              }
            </select>
          </mat-form-field>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.startDate' | transloco }}</mat-label>
            <input matInput type="date" formControlName="startDate" data-testid="f-startDate" />
            @if (dueDay(); as d) {
              <mat-hint data-testid="due-day">
                {{
                  d.key
                    | transloco
                      : { day: d.params['day'], weekday: (d.params['weekday'] + '' | transloco) }
                }}
              </mat-hint>
            }
            @if (err('startDate'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.graceDays' | transloco }}</mat-label>
            <input
              matInput
              type="number"
              inputmode="numeric"
              formControlName="graceDays"
              data-testid="f-graceDays"
            />
            @if (err('graceDays'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
        </section>

        <section class="section" aria-labelledby="sec-payout">
          <h2 id="sec-payout">{{ 'gameyas.form.payout' | transloco }}</h2>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.payoutTiming' | transloco }}</mat-label>
            <select matNativeControl formControlName="payoutTiming" data-testid="f-payoutTiming">
              @for (v of timings; track v) {
                <option [value]="v">{{ 'gameyas.payoutTiming.' + v | transloco }}</option>
              }
            </select>
          </mat-form-field>
          @if (value().payoutTiming === 'FixedDate') {
            <mat-form-field class="field">
              <mat-label>{{ 'gameyas.form.payoutOffsetDays' | transloco }}</mat-label>
              <input
                matInput
                type="number"
                inputmode="numeric"
                formControlName="payoutOffsetDays"
                data-testid="f-payoutOffsetDays"
              />
              @if (err('payoutOffsetDays'); as e) {
                <mat-error>{{ e | transloco }}</mat-error>
              }
            </mat-form-field>
          }
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.orderMethod' | transloco }}</mat-label>
            <select matNativeControl formControlName="orderMethod" data-testid="f-orderMethod">
              @for (v of orders; track v) {
                <option [value]="v">{{ 'gameyas.orderMethod.' + v | transloco }}</option>
              }
            </select>
          </mat-form-field>
        </section>

        <section class="section" aria-labelledby="sec-members">
          <h2 id="sec-members">{{ 'gameyas.form.members' | transloco }}</h2>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.maxSharesPerMember' | transloco }}</mat-label>
            <input
              matInput
              type="number"
              inputmode="numeric"
              formControlName="maxSharesPerMember"
              data-testid="f-maxSharesPerMember"
            />
            @if (err('maxSharesPerMember'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
          <label class="check">
            <input
              type="checkbox"
              formControlName="organizerParticipates"
              data-testid="f-organizerParticipates"
            />
            {{ 'gameyas.form.organizerParticipates' | transloco }}
          </label>
          @if (value().organizerParticipates) {
            <div
              class="stepper"
              role="group"
              [attr.aria-label]="'gameyas.form.organizerShares' | transloco"
            >
              <span class="stepper-label">{{ 'gameyas.form.organizerShares' | transloco }}</span>
              <button
                mat-icon-button
                type="button"
                data-testid="shares-dec"
                [attr.aria-label]="'gameyas.form.sharesDec' | transloco"
                [disabled]="myShares() <= 1"
                (click)="step(-1)"
              >
                <mat-icon aria-hidden="true">remove</mat-icon>
              </button>
              <output class="stepper-value" data-testid="shares-value">{{ myShares() }}</output>
              <button
                mat-icon-button
                type="button"
                data-testid="shares-inc"
                [attr.aria-label]="'gameyas.form.sharesInc' | transloco"
                [disabled]="myShares() >= maxShares()"
                (click)="step(1)"
              >
                <mat-icon aria-hidden="true">add</mat-icon>
              </button>
            </div>
            @if (err('organizerShares'); as e) {
              <p class="error" role="alert">{{ e | transloco }}</p>
            }
          }
        </section>

        <section class="section" aria-labelledby="sec-rules">
          <h2 id="sec-rules">{{ 'gameyas.form.rules' | transloco }}</h2>
          <mat-form-field class="field">
            <mat-label>{{ 'gameyas.form.rulesText' | transloco }}</mat-label>
            <textarea
              matInput
              rows="5"
              formControlName="rulesText"
              data-testid="f-rulesText"
            ></textarea>
            @if (err('rulesText'); as e) {
              <mat-error>{{ e | transloco }}</mat-error>
            }
          </mat-form-field>
        </section>

        <aside class="calc" aria-live="polite" data-testid="calc-card">
          @if (calc(); as c) {
            <dl>
              <div>
                <dt>{{ 'gameyas.form.calc.pot' | transloco }}</dt>
                <dd data-testid="calc-pot">{{ c.pot | money }}</dd>
              </div>
              <div>
                <dt>{{ 'gameyas.form.calc.rounds' | transloco }}</dt>
                <dd data-testid="calc-rounds">{{ c.rounds }}</dd>
              </div>
              <div>
                <dt>{{ 'gameyas.form.calc.endDate' | transloco }}</dt>
                <dd data-testid="calc-end">{{ c.end | cairoDate }}</dd>
              </div>
              <div>
                <dt>{{ 'gameyas.form.calc.totalPerShare' | transloco }}</dt>
                <dd data-testid="calc-total">{{ c.totalPerShare | money }}</dd>
              </div>
            </dl>
          }
          <button
            mat-flat-button
            type="submit"
            data-testid="f-save"
            [disabled]="busy() || loading()"
          >
            {{ 'gameyas.form.save' | transloco }}
          </button>
        </aside>
        @if (formError(); as fe) {
          <p class="error" role="alert" data-testid="form-error">{{ fe }}</p>
        }
      </form>
    }
  `,
  styleUrl: './gameya-form.page.scss',
})
export class GameyaFormPage {
  private readonly api = inject(GameyaApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);

  protected readonly editId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly frequencies: GameyaFrequency[] = ['Monthly', 'Weekly'];
  protected readonly timings: PayoutTiming[] = ['AfterFullCollection', 'FixedDate'];
  protected readonly orders: OrderMethod[] = ['Manual', 'RandomDraw', 'ByRequest'];

  protected readonly busy = signal(false);
  protected readonly loading = signal(this.editId !== null);
  protected readonly notEditable = signal(false);
  protected readonly formError = signal<string | null>(null);

  private original: Gameya | null = null;
  /** Null when the member list could not be loaded: ask for confirmation without a count. */
  private otherMembers: number | null = 0;

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [trimmedLength(3, 60)] }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(500)],
    }),
    contributionAmount: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(100),
      Validators.max(1_000_000),
      integer,
    ]),
    shareCount: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(2),
      Validators.max(50),
      integer,
    ]),
    frequency: new FormControl<GameyaFrequency>('Monthly', { nonNullable: true }),
    startDate: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, (c) => this.startDateRule(c)],
    }),
    graceDays: new FormControl<number | null>(3, [
      Validators.required,
      Validators.min(0),
      Validators.max(10),
      integer,
    ]),
    payoutTiming: new FormControl<PayoutTiming>('AfterFullCollection', { nonNullable: true }),
    payoutOffsetDays: new FormControl<number | null>({ value: null, disabled: true }),
    orderMethod: new FormControl<OrderMethod>('Manual', { nonNullable: true }),
    maxSharesPerMember: new FormControl<number | null>(2, [
      Validators.required,
      Validators.min(1),
      Validators.max(5),
      integer,
    ]),
    organizerParticipates: new FormControl(true, { nonNullable: true }),
    organizerShares: new FormControl<number | null>(1),
    rulesText: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(2000)],
    }),
  });

  protected readonly value = toSignal(
    this.form.valueChanges.pipe(
      startWith(null),
      map(() => this.form.getRawValue()),
    ),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly maxShares = computed(() => {
    const m = num(this.value().maxSharesPerMember);
    return m !== null && m >= 1 && m <= 5 ? m : 5;
  });
  protected readonly myShares = computed(() => num(this.value().organizerShares) ?? 1);

  protected readonly dueDay = computed(() => {
    const { startDate, frequency } = this.value();
    return ISO_DATE.test(startDate) ? dueDayInWords(startDate, frequency) : null;
  });

  protected readonly calc = computed(() => {
    const v = this.value();
    const amount = num(v.contributionAmount);
    const shares = num(v.shareCount);
    if (amount === null || shares === null || shares < 1 || amount < 0) return null;
    const total = pot(amount, shares);
    return {
      pot: total,
      rounds: shares,
      totalPerShare: total,
      end: ISO_DATE.test(v.startDate) ? endDate(v.startDate, v.frequency, shares) : '',
    };
  });

  constructor() {
    this.form.valueChanges.subscribe(() => this.sync());
    this.sync();
    if (this.editId) this.load(this.editId);
  }

  protected step(delta: number): void {
    const next = Math.min(this.maxShares(), Math.max(1, this.myShares() + delta));
    this.form.controls.organizerShares.setValue(next);
  }

  /** The first error of a control as a translation key, or null. */
  protected err(name: keyof typeof this.form.controls): string | null {
    const c = this.form.controls[name];
    if (!c.errors) return null;
    const e = c.errors;
    const code =
      ['required', 'integer', 'pastDate', 'min', 'max', 'minlength', 'maxlength', 'server'].find(
        (k) => k in e,
      ) ?? 'server';
    return `gameyas.form.errors.${code}`;
  }

  protected submit(): void {
    this.formError.set(null);
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    const body = this.body();
    if (!this.editId) {
      this.send(this.api.create(body).pipe(map((r) => r.id)));
      return;
    }
    const id = this.editId;
    const needsConfirm =
      this.original?.status === 'Recruiting' && this.otherMembers !== 0 && this.termsChanged(body);
    if (!needsConfirm) {
      this.send(this.api.update(id, body).pipe(map(() => id)));
      return;
    }
    const data: ConfirmDialogData = {
      title: this.transloco.translate('gameyas.form.reacceptTitle'),
      message:
        this.otherMembers === null
          ? this.transloco.translate('gameyas.form.reacceptMessageUnknown')
          : this.transloco.translate('gameyas.form.reacceptMessage', {
              count: this.otherMembers,
            }),
      confirmLabel: this.transloco.translate('common.confirm'),
      cancelLabel: this.transloco.translate('common.cancel'),
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .pipe(filter((ok) => ok === true))
      .subscribe(() => this.send(this.api.update(id, body).pipe(map(() => id))));
  }

  private send(request: Observable<string>): void {
    this.busy.set(true);
    request.subscribe({
      next: (id) => {
        this.busy.set(false);
        void this.router.navigate(['/gameyas', id]);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.showServerErrors(err);
      },
    });
  }

  /**
   * Server errors arrive as flat camelCase keys that match the control names. Coded problems are
   * already reported by the error interceptor, except VALIDATION_FAILED: when none of its keys
   * match a control, a general message is shown here.
   */
  private showServerErrors(err: unknown): void {
    const problem = problemOf(err);
    let matched = 0;
    for (const [key, messages] of Object.entries(problem?.errors ?? {})) {
      const control = (this.form.controls as Record<string, AbstractControl>)[key];
      if (!control || messages.length === 0) continue;
      control.setErrors({ ...control.errors, server: messages[0] });
      control.markAsTouched();
      matched++;
    }
    if (problem?.code === 'VALIDATION_FAILED' && matched === 0) {
      this.formError.set(this.transloco.translate('gameyas.form.errors.general'));
    }
  }

  private termsChanged(body: GameyaSettingsBody): boolean {
    const o = this.original;
    return (
      !!o &&
      (o.contributionAmount !== body.contributionAmount ||
        o.shareCount !== body.shareCount ||
        o.frequency !== body.frequency)
    );
  }

  /** Always every field: the server fills missing ones with zero values, not the defaults. */
  private body(): GameyaSettingsBody {
    const v = this.form.getRawValue();
    const text = (s: string): string | null => (s.trim() === '' ? null : s.trim());
    return {
      name: v.name.trim(),
      description: text(v.description),
      contributionAmount: v.contributionAmount ?? 0,
      shareCount: v.shareCount ?? 0,
      frequency: v.frequency,
      startDate: v.startDate,
      graceDays: v.graceDays ?? 0,
      payoutTiming: v.payoutTiming,
      payoutOffsetDays: v.payoutTiming === 'FixedDate' ? v.payoutOffsetDays : null,
      orderMethod: v.orderMethod,
      maxSharesPerMember: v.maxSharesPerMember ?? 0,
      organizerParticipates: v.organizerParticipates,
      rulesText: v.rulesText.trim() === '' ? null : v.rulesText,
      organizerShares: v.organizerParticipates ? (v.organizerShares ?? 0) : 0,
    };
  }

  /** Only a start date that changed has to be today or later when editing. */
  private startDateRule(c: AbstractControl): ValidationErrors | null {
    const v = c.value as string;
    if (!v || v === this.original?.startDate) return null;
    return v < cairoToday() ? { pastDate: true } : null;
  }

  /** Turns the conditional controls on or off and keeps their limits in step. */
  private sync(): void {
    const f = this.form.controls;
    const fixed = f.payoutTiming.value === 'FixedDate';
    if (fixed && f.payoutOffsetDays.disabled) {
      f.payoutOffsetDays.setValidators([
        Validators.required,
        Validators.min(0),
        Validators.max(10),
        integer,
      ]);
      f.payoutOffsetDays.enable({ emitEvent: false });
    } else if (!fixed && f.payoutOffsetDays.enabled) {
      f.payoutOffsetDays.setValue(null, { emitEvent: false });
      f.payoutOffsetDays.disable({ emitEvent: false });
    }

    const takesPart = f.organizerParticipates.value;
    if (takesPart) {
      const max = untracked(() => this.maxShares());
      const current = num(f.organizerShares.value);
      f.organizerShares.setValidators([
        Validators.required,
        Validators.min(1),
        Validators.max(this.limit(f.maxSharesPerMember.value ?? max)),
        integer,
      ]);
      if (f.organizerShares.disabled) f.organizerShares.enable({ emitEvent: false });
      if (current === null || current === 0) {
        f.organizerShares.setValue(1, { emitEvent: false });
      }
    } else if (f.organizerShares.enabled) {
      f.organizerShares.setValue(0, { emitEvent: false });
      f.organizerShares.disable({ emitEvent: false });
    }
    f.organizerShares.updateValueAndValidity({ emitEvent: false });
    f.payoutOffsetDays.updateValueAndValidity({ emitEvent: false });
  }

  private limit(m: number | null): number {
    return m !== null && m >= 1 && m <= 5 ? m : 5;
  }

  private load(id: string): void {
    this.api.get(id).subscribe((g) => {
      this.original = g;
      if (g.status !== 'Draft' && g.status !== 'Recruiting') {
        this.notEditable.set(true);
        this.loading.set(false);
        return;
      }
      this.form.reset({
        name: g.name,
        description: g.description ?? '',
        contributionAmount: g.contributionAmount,
        shareCount: g.shareCount,
        frequency: g.frequency,
        startDate: g.startDate,
        graceDays: g.graceDays,
        payoutTiming: g.payoutTiming,
        payoutOffsetDays: g.payoutOffsetDays,
        orderMethod: g.orderMethod,
        maxSharesPerMember: g.maxSharesPerMember,
        organizerParticipates: g.organizerParticipates,
        organizerShares: g.organizerParticipates ? g.myShareCount : 0,
        rulesText: g.rulesText ?? '',
      });
      this.sync();
      this.form.updateValueAndValidity();
      if (g.status === 'Recruiting') {
        this.api.members(id).subscribe({
          next: (members) => {
            this.otherMembers = members.filter((m) => !m.isOrganizer).length;
            this.loading.set(false);
          },
          error: () => {
            this.otherMembers = null;
            this.loading.set(false);
          },
        });
      } else {
        this.loading.set(false);
      }
    });
  }
}
