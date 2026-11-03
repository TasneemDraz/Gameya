import { expect, test } from '@playwright/test';

const NOTICE_AR =
  'التطبيق لا يحتفظ بأي أموال ولا ينقلها. كل الدفع بيتم بينكم مباشرة، والتطبيق بيسجّله ويأكّده بس.';

/** A fresh Egyptian mobile per run, so reruns never collide with an earlier OTP challenge. */
function uniquePhone(): string {
  const tail = String(Date.now() % 100_000_000).padStart(8, '0');
  return `010${tail}`;
}

test('signup_in_arabic_reaches_profile', async ({ page, request }) => {
  const phone = uniquePhone();
  const name = 'منى أحمد';

  await page.goto('/');
  await expect(page.getByTestId('no-money-notice')).toContainText(NOTICE_AR);
  await page.getByTestId('start').click();

  await page.getByTestId('phone-input').fill(phone);
  await page.getByTestId('send-code').click();

  await expect(page.getByTestId('otp-0')).toBeVisible();
  const res = await request.get(`/api/dev/otp/${phone}`);
  expect(res.ok()).toBe(true);
  const { code } = (await res.json()) as { code: string };

  await page.getByTestId('otp-0').focus();
  await page.keyboard.type(code);

  await expect(page.getByTestId('name-input')).toBeVisible();
  await expect(page.getByTestId('no-money-notice')).toContainText(NOTICE_AR);
  await page.getByTestId('name-input').fill(name);
  await page.getByTestId('name-save').click();

  await expect(page.getByTestId('profile-name')).toHaveText(name);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
});

test('language_switch_flips_direction_without_reload', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.evaluate(() => {
    (window as unknown as Record<string, unknown>)['__noReload'] = true;
  });
  const navEntries = () => page.evaluate(() => performance.getEntriesByType('navigation').length);
  const before = await navEntries();

  await page.locator('.lang-toggle').click();

  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByTestId('money-sample')).toContainText('EGP');
  expect(await navEntries()).toBe(before);
  expect(
    await page.evaluate(() => (window as unknown as Record<string, unknown>)['__noReload']),
  ).toBe(true);
});
