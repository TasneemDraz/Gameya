import { Routes } from '@angular/router';
import { authGuard, profileCompleteGuard } from './core/auth/auth.guards';
import { AppLayout } from './layout/app-layout';
import { PublicLayout } from './layout/public-layout';

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
        path: 'j/:code',
        loadComponent: () =>
          import('./features/invite/invite-preview.page').then((m) => m.InvitePreviewPage),
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
        path: 'gameyas',
        pathMatch: 'full',
        canActivate: [profileCompleteGuard],
        loadComponent: () =>
          import('./features/gameyas/home/gameyas-home.page').then((m) => m.GameyasHomePage),
      },
      {
        path: 'gameyas/new',
        canActivate: [profileCompleteGuard],
        loadComponent: () =>
          import('./features/gameyas/form/gameya-form.page').then((m) => m.GameyaFormPage),
      },
      {
        path: 'gameyas/invites/:inviteId/accept',
        canActivate: [profileCompleteGuard],
        loadComponent: () =>
          import('./features/gameyas/join/accept-invite.page').then((m) => m.AcceptInvitePage),
      },
      {
        path: 'gameyas/:id',
        canActivate: [profileCompleteGuard],
        loadComponent: () =>
          import('./features/gameyas/detail/gameya.page').then((m) => m.GameyaPage),
      },
      {
        path: 'gameyas/:id/edit',
        canActivate: [profileCompleteGuard],
        loadComponent: () =>
          import('./features/gameyas/form/gameya-form.page').then((m) => m.GameyaFormPage),
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
