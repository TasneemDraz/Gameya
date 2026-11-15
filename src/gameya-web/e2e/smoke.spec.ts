import { APIRequestContext, Page, expect, test } from '@playwright/test';

const NOTICE_AR =
  'التطبيق لا يحتفظ بأي أموال ولا ينقلها. كل الدفع بيتم بينكم مباشرة، والتطبيق بيسجّله ويأكّده بس.';

/** A fresh Egyptian mobile per run, so reruns never collide with an earlier OTP challenge. */
function uniquePhone(): string {
  const tail = String(Date.now() % 100_000_000).padStart(8, '0');
  return `010${tail}`;
}

/** Signs a fresh user up through the real UI and leaves the page on the profile. */
async function signUp(page: Page, request: APIRequestContext, phone: string, name: string) {
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
}

test('signup_in_arabic_reaches_profile', async ({ page, request }) => {
  const name = 'منى أحمد';
  await signUp(page, request, uniquePhone(), name);
  await expect(page).toHaveURL(/\/gameyas$/);

  // A full load restores the session from the refresh cookie.
  await page.goto('/profile');
  await expect(page.getByTestId('profile-name')).toHaveText(name);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
});

test('create_open_and_preview_in_arabic', async ({ page, request, browser, baseURL }) => {
  const name = 'سارة محمود';
  await signUp(page, request, uniquePhone(), name);
  await expect(page).toHaveURL(/\/gameyas$/);

  await page.getByTestId('create-gameya').click();
  await page.getByTestId('f-name').fill('جمعية العيلة');
  await page.getByTestId('f-amount').fill('500');
  await page.getByTestId('f-shareCount').fill('10');
  const start = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10);
  await page.getByTestId('f-startDate').fill(start);
  // The organizer participates with one share by default.
  await expect(page.getByTestId('shares-value')).toHaveText(/^\s*[1١]\s*$/);
  await expect(page.getByTestId('calc-pot')).toContainText(/٥[٬,]?٠٠٠/);
  await page.getByTestId('f-save').click();

  await expect(page.getByTestId('gameya-name')).toHaveText('جمعية العيلة');
  await expect(page.getByTestId('num-pot')).toContainText(/٥[٬,]?٠٠٠/);
  await page.getByRole('tab', { name: 'الإدارة' }).click();
  await page.getByTestId('open').click();
  await page.getByRole('dialog').getByRole('button', { name: 'تأكيد' }).click();

  const codeEl = page.getByTestId('invite-code');
  await expect(codeEl).toBeVisible();
  const code = (await codeEl.innerText()).trim();
  expect(code).toMatch(/^[A-Z0-9]{6}$/);

  const guest = await browser.newContext({ baseURL });
  try {
    const gp = await guest.newPage();
    await gp.goto(`/j/${code}`);
    await expect(gp.getByTestId('gameya-name')).toHaveText('جمعية العيلة');
    await expect(gp.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(gp.getByTestId('login-to-join')).toBeVisible();
    await expect(gp.getByTestId('member-row')).toHaveCount(0);
    await expect(gp.getByTestId('member-phone')).toHaveCount(0);
  } finally {
    await guest.close();
  }
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
