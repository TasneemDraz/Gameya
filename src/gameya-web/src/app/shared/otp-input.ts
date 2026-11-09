import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  output,
  viewChildren,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { normalizeDigits } from '../core/format/digits';

const LENGTH = 6;

/** Six single-digit boxes. Emits `completed` with the ASCII code once all are filled. */
@Component({
  selector: 'gy-otp-input',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="boxes" dir="ltr" role="group" [attr.aria-label]="'auth.codeLabel' | transloco">
      @for (i of indexes; track i) {
        <input
          #box
          type="text"
          inputmode="numeric"
          maxlength="1"
          autocomplete="one-time-code"
          [attr.data-testid]="'otp-' + i"
          [attr.aria-label]="('auth.digit' | transloco) + ' ' + (i + 1)"
          (input)="onInput(i, $event)"
          (keydown)="onKeydown(i, $event)"
          (paste)="onPaste($event)"
          (focus)="select($event)"
        />
      }
    </div>
  `,
  styleUrl: './otp-input.scss',
})
export class OtpInput {
  readonly completed = output<string>();
  protected readonly indexes = Array.from({ length: LENGTH }, (_, i) => i);
  private readonly boxes = viewChildren<ElementRef<HTMLInputElement>>('box');

  clear(): void {
    this.boxes().forEach((b) => (b.nativeElement.value = ''));
    this.boxes()[0]?.nativeElement.focus();
  }

  protected onInput(i: number, ev: Event): void {
    const el = ev.target as HTMLInputElement;
    const digits = normalizeDigits(el.value).replace(/\D/g, '');
    if (digits.length > 1) {
      this.fill(i, digits);
      return;
    }
    el.value = digits;
    if (digits && i < LENGTH - 1) this.focus(i + 1);
    this.emitIfFull();
  }

  protected onKeydown(i: number, ev: KeyboardEvent): void {
    const boxes = this.boxes();
    if (ev.key === 'Backspace' && !boxes[i].nativeElement.value && i > 0) {
      boxes[i - 1].nativeElement.value = '';
      this.focus(i - 1);
      ev.preventDefault();
    }
  }

  protected onPaste(ev: ClipboardEvent): void {
    ev.preventDefault();
    const digits = normalizeDigits(ev.clipboardData?.getData('text') ?? '').replace(/\D/g, '');
    if (digits) this.fill(0, digits);
  }

  protected select(ev: Event): void {
    (ev.target as HTMLInputElement).select();
  }

  private fill(from: number, digits: string): void {
    const boxes = this.boxes();
    [...digits.slice(0, LENGTH - from)].forEach(
      (d, k) => (boxes[from + k].nativeElement.value = d),
    );
    this.focus(Math.min(from + digits.length, LENGTH - 1));
    this.emitIfFull();
  }

  private focus(i: number): void {
    this.boxes()[i]?.nativeElement.focus();
  }

  private emitIfFull(): void {
    const code = this.boxes()
      .map((b) => b.nativeElement.value)
      .join('');
    if (code.length === LENGTH) this.completed.emit(code);
  }
}
