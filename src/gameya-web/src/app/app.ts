import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LanguageService } from './core/i18n/language.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
})
export class App {
  // Instantiated at startup so dir/lang are applied before first render.
  protected readonly lang = inject(LanguageService);
}
