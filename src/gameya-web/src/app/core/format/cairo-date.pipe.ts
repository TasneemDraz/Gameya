import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '../i18n/language.service';

export const CAIRO_TZ = 'Africa/Cairo';

@Pipe({ name: 'cairoDate', pure: false })
export class CairoDatePipe implements PipeTransform {
  private readonly lang = inject(LanguageService);

  transform(value: string | Date | null | undefined, withTime = false): string {
    if (!value) return '';
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    const locale = this.lang.current() === 'ar' ? 'ar-EG' : 'en-US';
    const options: Intl.DateTimeFormatOptions = {
      timeZone: CAIRO_TZ,
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      ...(withTime ? { hour: 'numeric', minute: '2-digit' } : {}),
    };
    return new Intl.DateTimeFormat(locale, options).format(date);
  }
}
