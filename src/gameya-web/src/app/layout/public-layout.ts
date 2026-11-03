import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { LanguageService } from '../core/i18n/language.service';

/** Logged-out pages: header with language toggle, no navigation. */
@Component({
  selector: 'gy-public-layout',
  imports: [RouterOutlet, MatToolbarModule, MatButtonModule, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-toolbar class="bar">
      <span class="brand">{{ 'app.name' | transloco }}</span>
      <button mat-button type="button" class="lang-toggle" (click)="toggle()">
        {{ 'common.otherLanguage' | transloco }}
      </button>
    </mat-toolbar>
    <main class="content"><router-outlet /></main>
  `,
  styles: `
    :host {
      display: block;
      min-block-size: 100dvh;
    }

    .bar {
      display: flex;
      justify-content: space-between;
    }

    .brand {
      font-weight: 600;
    }

    .content {
      max-inline-size: 480px;
      margin-inline: auto;
      padding: 16px;
    }
  `,
})
export class PublicLayout {
  private readonly lang = inject(LanguageService);

  toggle(): void {
    this.lang.use(this.lang.current() === 'ar' ? 'en' : 'ar');
  }
}
