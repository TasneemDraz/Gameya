/** Returns a copy of `set` without `key`; used to re-enable a per-item button after its request ends. */
export function releaseKey(set: ReadonlySet<string>, key: string): ReadonlySet<string> {
  const next = new Set(set);
  next.delete(key);
  return next;
}
