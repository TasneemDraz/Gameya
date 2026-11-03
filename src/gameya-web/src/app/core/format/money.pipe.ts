import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '../i18n/language.service';

@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly lang = inject(LanguageService);

  transform(amount: number | string | null | undefined): string {
    if (amount === null || amount === undefined || amount === '') return '';
    const locale = this.lang.current() === 'ar' ? 'ar-EG' : 'en-EG';
    return new Intl.NumberFormat(locale, { style: 'currency', currency: 'EGP' }).format(
      Number(amount),
    );
  }
}
