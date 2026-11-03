import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map, startWith, switchMap } from 'rxjs';
import { AuthApi } from '../../core/auth/auth.api';
import { AuthStore } from '../../core/auth/auth.store';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog';

@Component({
  selector: 'gy-danger-zone',
  imports: [ReactiveFormsModule, MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="card">
      <h3>{{ 'profile.dangerTitle' | transloco }}</h3>
      <div class="row">
        <button mat-stroked-button type="button" data-testid="logout" (click)="logout()">
          {{ 'profile.logout' | transloco }}
        </button>
        <button mat-stroked-button type="button" data-testid="logout-all" (click)="logoutAll()">
          {{ 'profile.logoutAll' | transloco }}
        </button>
      </div>
      <label class="field">
        <span class="hint">{{ 'profile.deletePrompt' | transloco: { word: word() } }}</span>
        <input type="text" data-testid="delete-word" [formControl]="typed" />
      </label>
      <button
        mat-stroked-button
        class="destructive"
        type="button"
        data-testid="delete-account"
        [disabled]="!matches()"
        (click)="deleteAccount()"
      >
        {{ 'profile.deleteAccount' | transloco }}
      </button>
    </section>
  `,
  styleUrl: './profile.scss',
})
export class DangerZone {
  private readonly api = inject(AuthApi);
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);

  protected readonly typed = new FormControl('', { nonNullable: true });
  private readonly typedValue = toSignal(this.typed.valueChanges.pipe(startWith('')), {
    initialValue: '',
  });
  private readonly activeLang = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  /** The localized confirmation word: «حذف» in Arabic, DELETE in English. */
  protected readonly word = computed(() => {
    this.activeLang();
    return this.transloco.translate('profile.deleteWord');
  });
  protected readonly matches = computed(
    () => this.typedValue().trim().toLowerCase() === this.word().toLowerCase(),
  );

  protected logout(): void {
    this.api.logout().subscribe({ complete: () => this.leave(), error: () => this.leave() });
  }

  protected logoutAll(): void {
    this.confirm('profile.logoutAllTitle', 'profile.logoutAllMessage', 'profile.logoutAll', false)
      .pipe(switchMap(() => this.api.logoutAll()))
      .subscribe({ complete: () => this.leave() });
  }

  protected deleteAccount(): void {
    if (!this.matches()) return;
    this.confirm('profile.deleteTitle', 'profile.deleteMessage', 'profile.deleteAccount', true)
      .pipe(switchMap(() => this.api.deleteAccount()))
      .subscribe({ complete: () => this.leave() });
  }

  private confirm(title: string, message: string, confirmLabel: string, destructive: boolean) {
    const data: ConfirmDialogData = {
      title: this.transloco.translate(title),
      message: this.transloco.translate(message),
      confirmLabel: this.transloco.translate(confirmLabel),
      cancelLabel: this.transloco.translate('common.cancel'),
      destructive,
    };
    return this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .pipe(
        filter((ok) => ok === true),
        map(() => undefined),
      );
  }

  private leave(): void {
    this.store.clear();
    void this.router.navigateByUrl('/');
  }
}
