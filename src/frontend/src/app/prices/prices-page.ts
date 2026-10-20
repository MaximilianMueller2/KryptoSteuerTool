import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { KsApi, errorMessage } from '../api/ks-api';
import { EcbImportResult } from '../api/models';

@Component({
  selector: 'ks-prices-page',
  imports: [MatButtonModule, MatCardModule],
  template: `
    <h1 i18n="@@prices.title">Kurse</h1>
    <mat-card>
      <mat-card-header><mat-card-title i18n="@@prices.ecb">EZB-Referenzkurse (USD → EUR)</mat-card-title></mat-card-header>
      <mat-card-content>
        <p i18n="@@prices.hint">
          Für USDT, USDC und andere USD-Stablecoins wird der EZB-Kurs EUR/USD des jeweiligen Tages verwendet (1 Stablecoin = 1 USD).
          Ohne Kurse bleiben die Futures-Summen in EUR unvollständig; es wird nie mit 0 € gerechnet.
        </p>
        <button matButton="filled" type="button" [disabled]="busy()" (click)="fetch()" data-testid="fetch" i18n="@@prices.fetch">Online abrufen</button>
        <p class="hint" i18n="@@prices.upload">Oder die Datei eurofxref-hist.csv (bzw. .zip) von der EZB hochladen:</p>
        <input type="file" data-testid="file" accept=".csv,.zip" (change)="upload($event)" />
      </mat-card-content>
    </mat-card>

    @if (error(); as e) {
      <p class="error" role="alert" data-testid="error">{{ e }}</p>
    }
    @if (result(); as r) {
      <p class="ok" data-testid="result">
        {{ r.rates }} <ng-container i18n="@@prices.rates">Kurse gelesen</ng-container>, {{ r.pricesStored }}
        <ng-container i18n="@@prices.stored">gespeichert für</ng-container> {{ r.assets.join(', ') || '–' }}.
      </p>
      @if (r.warning) { <p class="warn">{{ r.warning }}</p> }
    }
  `,
  styles: `
    .hint { color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); }
    .ok { color: var(--mat-sys-primary); }
    .warn { color: var(--mat-sys-tertiary); }
  `,
})
export class PricesPage {
  private readonly api = inject(KsApi);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<EcbImportResult | null>(null);

  protected fetch(): void {
    this.handle(this.api.fetchEcb());
  }

  protected upload(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.handle(this.api.uploadEcb(file));
  }

  private handle(request: ReturnType<KsApi['fetchEcb']>): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (result) => {
        this.result.set(result);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(errorMessage(e));
        this.busy.set(false);
      },
    });
  }
}
