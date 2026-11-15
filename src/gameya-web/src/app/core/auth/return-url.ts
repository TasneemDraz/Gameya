/**
 * Accepts only same-site paths: a single leading slash, never a double slash, a backslash, a scheme or an
 * absolute URL. Control characters (which browsers strip before parsing) are rejected too.
 */
export function safeReturnUrl(raw: string | null | undefined): string | null {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//')) return null;
  // eslint-disable-next-line no-control-regex
  if (raw.includes('\\') || /[\u0000-\u001f\u007f]/.test(raw)) return null;
  return raw;
}
