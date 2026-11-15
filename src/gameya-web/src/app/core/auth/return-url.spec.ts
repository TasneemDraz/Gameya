import { describe, expect, it } from 'vitest';
import { safeReturnUrl } from './return-url';

describe('safeReturnUrl', () => {
  it.each(['/j/ABC234', '/gameyas/1'])('accepts %s', (u) => expect(safeReturnUrl(u)).toBe(u));
  it.each([
    '//evil.com',
    '/\\evil.com',
    'https://evil.com',
    'javascript:alert(1)',
    'j/ABC',
    '',
    null,
  ])('rejects %s', (u) => expect(safeReturnUrl(u)).toBeNull());
  it('rejects backslashes and control characters anywhere', () => {
    expect(safeReturnUrl('/a\\b')).toBeNull();
    expect(safeReturnUrl('/\t/evil.com')).toBeNull();
  });
});
