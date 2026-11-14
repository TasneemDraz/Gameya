import { describe, expect, it } from 'vitest';
import { dueDayInWords, endDate, pot } from './gameya-calc';

describe('gameya-calc', () => {
  it('end date clamps the 31st to month end', () =>
    expect(endDate('2026-01-31', 'Monthly', 3)).toBe('2026-03-31'));
  it('end date for February', () => expect(endDate('2026-01-31', 'Monthly', 2)).toBe('2026-02-28'));
  it('end date for a leap February', () =>
    expect(endDate('2028-01-31', 'Monthly', 2)).toBe('2028-02-29'));
  it('end date rolls over the year', () =>
    expect(endDate('2026-11-15', 'Monthly', 4)).toBe('2027-02-15'));
  it('weekly end date', () => expect(endDate('2026-12-01', 'Weekly', 4)).toBe('2026-12-22'));
  it('a single share ends on the start date', () =>
    expect(endDate('2026-12-01', 'Monthly', 1)).toBe('2026-12-01'));
  it('pot', () => expect(pot(1000, 10)).toBe(10000));
  it('due day in words', () => {
    expect(dueDayInWords('2026-11-15', 'Monthly')).toEqual({
      key: 'gameyas.form.dueDay.monthly',
      params: { day: 15 },
    });
    expect(dueDayInWords('2026-11-14', 'Weekly')).toEqual({
      key: 'gameyas.form.dueDay.weekly',
      params: { weekday: 'gameyas.weekday.6' },
    });
  });
});
