import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';

@Component({
  selector: 'ks-root',
  imports: [RouterOutlet, MatToolbarModule],
  template: `
    <mat-toolbar>
      <span i18n="@@app.title">Krypto-Steuer</span>
    </mat-toolbar>
    <main>
      <router-outlet />
    </main>
  `,
  styles: `
    main {
      padding: 16px;
      max-width: 960px;
      margin: 0 auto;
    }
  `,
})
export class App {}
