import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { OtpPage } from './otp.page';

const session = (profileComplete: boolean) => ({
  accessToken: 't',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  profileComplete,
});

describe('OtpPage returnUrl', () => {
  let ctrl: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  function setup(returnUrl: string | null) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { ar: {}, en: {} },
          translocoConfig: { availableLangs: ['ar', 'en'], defaultLang: 'ar' },
        }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) },
          },
        },
      ],
    });
    history.replaceState({ phone: '01012345678', resendAvailableAt: '2030-01-01T00:00:00Z' }, '');
    ctrl = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const f = TestBed.createComponent(OtpPage);
    f.detectChanges();
    return f.componentInstance as unknown as { verify(code: string): void };
  }

  async function verify(returnUrl: string | null, profileComplete: boolean) {
    const page = setup(returnUrl);
    page.verify('123456');
    ctrl.expectOne('/api/auth/otp/verify').flush(session(profileComplete));
    if (profileComplete) {
      await Promise.resolve();
      ctrl.expectOne('/api/me').flush({ language: 'ar' });
    }
    await vi.waitFor(() => expect(navigate).toHaveBeenCalled());
  }

  afterEach(() => {
    ctrl.verify();
    history.replaceState(null, '');
  });

  it('a returning user lands on returnUrl after the code', async () => {
    await verify('/j/ABC234', true);
    expect(navigate).toHaveBeenCalledWith('/j/ABC234');
  });

  it('a new user goes to name setup carrying returnUrl, then to it', async () => {
    await verify('/j/ABC234', false);
    expect(navigate).toHaveBeenCalledWith('/welcome/name?returnUrl=%2Fj%2FABC234');
  });

  it('an unsafe returnUrl falls back to /gameyas', async () => {
    await verify('//evil.com', true);
    expect(navigate).toHaveBeenCalledWith('/gameyas');
  });

  it('an unsafe returnUrl is not carried to name setup', async () => {
    await verify('//evil.com', false);
    expect(navigate).toHaveBeenCalledWith('/welcome/name');
  });
});
