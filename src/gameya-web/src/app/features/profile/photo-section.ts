import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AuthApi, Me } from '../../core/auth/auth.api';

export const MAX_PHOTO_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'gy-photo-section',
  imports: [MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="card">
      <h3>{{ 'profile.photoTitle' | transloco }}</h3>
      @if (me().photoUrl) {
        <img class="photo" [src]="me().photoUrl" [alt]="'profile.photoAlt' | transloco" />
      }
      <div class="row">
        <button mat-stroked-button type="button" (click)="picker.click()">
          {{ 'profile.photoUpload' | transloco }}
        </button>
        <input
          #picker
          hidden
          type="file"
          accept="image/jpeg,image/png,image/heic"
          data-testid="photo-file"
          (change)="onFile($event)"
        />
        @if (me().photoUrl) {
          <button mat-button type="button" (click)="remove()">
            {{ 'profile.photoRemove' | transloco }}
          </button>
        }
      </div>
    </section>
  `,
  styleUrl: './profile.scss',
})
export class PhotoSection {
  readonly me = input.required<Me>();
  readonly changed = output<void>();

  private readonly api = inject(AuthApi);
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  protected onFile(ev: Event): void {
    const el = ev.target as HTMLInputElement;
    const file = el.files?.[0];
    el.value = '';
    if (!file) return;
    if (file.size > MAX_PHOTO_BYTES) {
      this.snack.open(this.transloco.translate('errors.FILE_TOO_LARGE'), undefined, {
        duration: 5000,
      });
      return;
    }
    this.api.setPhoto(file).subscribe(() => this.changed.emit());
  }

  protected remove(): void {
    this.api.removePhoto().subscribe(() => this.changed.emit());
  }
}
