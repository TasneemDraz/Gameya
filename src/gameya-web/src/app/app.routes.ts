import { Routes } from '@angular/router';
import { authGuard, profileCompleteGuard } from './core/auth/auth.guards';
import { AppLayout } from './layout/app-layout';
import { PublicLayout } from './layout/public-layout';

// Home is /profile until sub-project 2 adds /gameyas.
export const routes: Routes = [
  {
    path: '',
    component: PublicLayout,
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/landing/landing.page').then((m) => m.LandingPage),
      },
      {
        path: 'login',
        pathMatch: 'full',
        loadComponent: () => import('./features/auth/phone.page').then((m) => m.PhonePage),
      },
      {
        path: 'login/code',
        loadComponent: () => import('./features/auth/otp.page').then((m) => m.OtpPage),
      },
      {
        path: 'verify-email',
        loadComponent: () =>
          import('./features/verify-email/verify-email.page').then((m) => m.VerifyEmailPage),
      },
    ],
  },
  {
    path: '',
    component: AppLayout,
    canActivate: [authGuard],
    children: [
      {
        path: 'welcome/name',
        loadComponent: () => import('./features/auth/name-setup.page').then((m) => m.NameSetupPage),
      },
      {
        path: 'profile',
        canActivate: [profileCompleteGuard],
        loadComponent: () => import('./features/profile/profile.page').then((m) => m.ProfilePage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
