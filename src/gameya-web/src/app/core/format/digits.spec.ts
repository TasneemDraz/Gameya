import { normalizeDigits } from './digits';

describe('normalizeDigits', () => {
  it('maps Arabic-Indic digits to ASCII', () => {
    expect(normalizeDigits('٠١٠')).toBe('010');
    expect(normalizeDigits('٠١٢٣٤٥٦٧٨٩')).toBe('0123456789');
  });
  it('maps Persian digits to ASCII', () => {
    expect(normalizeDigits('۰۱۲۳۴۵۶۷۸۹')).toBe('0123456789');
  });
  it('leaves other characters alone', () => {
    expect(normalizeDigits('+20 1abc٠')).toBe('+20 1abc0');
  });
});
