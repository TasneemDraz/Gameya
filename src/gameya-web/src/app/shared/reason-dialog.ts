import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslocoPipe } from '@jsverse/transloco';

export interface ReasonDialogData {
  /** Transloco key of the dialog title. */
  titleKey: string;
  required: boolean;
  minLength: number;
  maxLength: number;
}

/** Asks for a free-text reason. Closes with the trimmed reason, or `undefined` when cancelled. */
@Component({
  selector: 'gy-reason-dialog',
  imports: [MatDialogModule, MatButtonModule, ReactiveFormsModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | transloco }}</h2>
    <mat-dialog-content>
      <label class="field">
        <span>{{ 'reason.label' | transloco }}</span>
        <textarea
          rows="3"
          data-testid="reason-input"
          [formControl]="reason"
          [attr.maxlength]="data.maxLength"
        ></textarea>
      </label>
      @if (tooShort()) {
        <p class="error" role="alert">
          {{ 'reason.tooShort' | transloco: { min: data.minLength } }}
        </p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="undefined">
        {{ 'common.cancel' | transloco }}
      </button>
      <button
        mat-flat-button
        type="button"
        data-testid="reason-confirm"
        [disabled]="invalid()"
        [mat-dialog-close]="reason.value.trim()"
      >
        {{ 'common.confirm' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
  styleUrl: './reason-dialog.scss',
})
export class ReasonDialog {
  protected readonly data = inject<ReasonDialogData>(MAT_DIALOG_DATA);
  protected readonly reason = new FormControl('', { nonNullable: true });

  private length(): number {
    return this.reason.value.trim().length;
  }

  protected invalid(): boolean {
    const len = this.length();
    if (len > this.data.maxLength) return true;
    return this.data.required ? len < Math.max(1, this.data.minLength) : false;
  }

  protected tooShort(): boolean {
    return this.reason.dirty && this.invalid();
  }
}
