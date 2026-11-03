import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

/** Texts arrive already translated; open with `MatDialog.open(ConfirmDialog, { data })`. */
export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  destructive?: boolean;
}

@Component({
  selector: 'gy-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">{{ data.cancelLabel }}</button>
      <button mat-flat-button [mat-dialog-close]="true" [class.destructive]="data.destructive" cdkFocusInitial>
        {{ data.confirmLabel }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .destructive {
      --mat-button-filled-container-color: var(--gy-danger-fg);
      --mat-button-filled-label-text-color: #fff;
    }
  `,
})
export class ConfirmDialog {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}
