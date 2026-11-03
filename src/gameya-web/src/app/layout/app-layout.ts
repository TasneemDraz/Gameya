import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';
import { LanguageService } from '../core/i18n/language.service';

const WIDE_QUERY = '(min-width: 960px)';

/** Signed-in shell: sidenav at 960px and wider, bottom nav below. */
@Component({
  selector: 'gy-app-layout',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatButtonModule,
    MatIconModule,
    TranslocoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-toolbar class="bar">
      <span class="brand">{{ 'app.name' | transloco }}</span>
      <button mat-button type="button" class="lang-toggle" (click)="toggle()">
        {{ 'common.otherLanguage' | transloco }}
      </button>
    </mat-toolbar>

    @if (wide()) {
      <mat-sidenav-container class="shell">
        <mat-sidenav mode="side" opened class="side">
          <nav class="side-nav" [attr.aria-label]="'app.navigation' | transloco">
            <a mat-button routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
              <mat-icon>home</mat-icon>
              {{ 'nav.home' | transloco }}
            </a>
          </nav>
        </mat-sidenav>
        <mat-sidenav-content class="content"><router-outlet /></mat-sidenav-content>
      </mat-sidenav-container>
    } @else {
      <main class="content"><router-outlet /></main>
      <nav class="bottom-nav" [attr.aria-label]="'app.navigation' | transloco">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
          <mat-icon>home</mat-icon>
          <span>{{ 'nav.home' | transloco }}</span>
        </a>
      </nav>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-block-size: 100dvh;
    }

    .bar {
      display: flex;
      justify-content: space-between;
    }

    .brand {
      font-weight: 600;
    }

    .shell {
      flex: 1;
    }

    .side {
      inline-size: 240px;
    }

    .side-nav {
      display: flex;
      flex-direction: column;
      padding: 8px;
    }

    .side-nav a {
      justify-content: flex-start;
    }

    .content {
      flex: 1;
      padding: 16px;
      padding-block-end: 80px;
    }

    .bottom-nav {
      position: fixed;
      inset-inline: 0;
      inset-block-end: 0;
      display: flex;
      justify-content: space-around;
      padding-block: 4px;
      background: var(--gy-surface-nav);
      border-block-start: 1px solid var(--gy-outline);
    }

    .bottom-nav a {
      display: flex;
      flex-direction: column;
      align-items: center;
      min-inline-size: 64px;
      min-block-size: 48px;
      font-size: 0.75rem;
      color: inherit;
      text-decoration: none;
    }

    .bottom-nav a.active {
      font-weight: 600;
      color: var(--gy-primary);
    }
  `,
})
export class AppLayout {
  private readonly lang = inject(LanguageService);
  private readonly bp = inject(BreakpointObserver);

  readonly wide = toSignal(this.bp.observe(WIDE_QUERY).pipe(map((s) => s.matches)), {
    initialValue: this.bp.isMatched(WIDE_QUERY),
  });

  toggle(): void {
    this.lang.use(this.lang.current() === 'ar' ? 'en' : 'ar');
  }
}
