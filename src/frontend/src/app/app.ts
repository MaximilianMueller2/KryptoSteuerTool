import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'ks-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
  template: `
    <mat-toolbar>
      <span class="title" i18n="@@app.title">Krypto-Steuer</span>
      <nav>
        <a matButton routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" i18n="@@nav.year">Steuerjahr</a>
        <a matButton routerLink="/import" routerLinkActive="active" i18n="@@nav.import">Import</a>
        <a matButton routerLink="/pruefung" routerLinkActive="active" i18n="@@nav.review">Prüfung</a>
        <a matButton routerLink="/kurse" routerLinkActive="active" i18n="@@nav.prices">Kurse</a>
        <a matButton routerLink="/status" routerLinkActive="active" i18n="@@nav.status">Status</a>
      </nav>
    </mat-toolbar>
    <main>
      <router-outlet />
    </main>
    <footer i18n="@@app.disclaimer">
      Berechnungshilfe, keine Steuerberatung. Bitte die Ergebnisse vor der Abgabe prüfen.
    </footer>
  `,
  styles: `
    mat-toolbar {
      flex-wrap: wrap;
      gap: 8px;
    }
    .title {
      font-weight: 500;
      margin-right: 16px;
    }
    nav {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }
    nav a.active {
      background: var(--mat-sys-secondary-container);
    }
    main {
      padding: 16px;
      max-width: 1100px;
      margin: 0 auto;
    }
    footer {
      padding: 16px;
      text-align: center;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class App {}
