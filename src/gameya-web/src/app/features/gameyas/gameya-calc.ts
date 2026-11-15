import { GameyaFrequency } from './gameya.models';

export function pot(amount: number, shares: number): number {
  return amount * shares;
}

function parse(iso: string): { y: number; m: number; d: number } {
  const [y, m, d] = iso.split('-').map(Number);
  return { y, m, d };
}

function format(y: number, m: number, d: number): string {
  return `${String(y).padStart(4, '0')}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

/**
 * The last due date: Monthly is the same day clamped to month end, `shares - 1` months later
 * (mirrors `CairoCalendar.MonthlyDueDate`); Weekly is `7 * (shares - 1)` days later.
 */
export function endDate(start: string, frequency: GameyaFrequency, shares: number): string {
  const { y, m, d } = parse(start);
  const steps = Math.max(0, shares - 1);
  if (frequency === 'Weekly') {
    const t = new Date(Date.UTC(y, m - 1, d + 7 * steps));
    return format(t.getUTCFullYear(), t.getUTCMonth() + 1, t.getUTCDate());
  }
  const index = m - 1 + steps;
  const year = y + Math.floor(index / 12);
  const month = (index % 12) + 1;
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return format(year, month, Math.min(d, lastDay));
}

/** Transloco key and params that describe the due day: day of month (Monthly) or weekday (Weekly). */
export function dueDayInWords(
  start: string,
  frequency: GameyaFrequency,
): { key: string; params: Record<string, string | number> } {
  const { y, m, d } = parse(start);
  if (frequency === 'Weekly') {
    const weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay();
    return { key: 'gameyas.form.dueDay.weekly', params: { weekday: `gameyas.weekday.${weekday}` } };
  }
  return { key: 'gameyas.form.dueDay.monthly', params: { day: d } };
}
