import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { KsApi, errorMessage } from '../api/ks-api';
import { EcbImportResult } from '../api/models';

@Component({
  selector: 'ks-prices-page',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
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

    <mat-card class="manual">
      <mat-card-header><mat-card-title i18n="@@prices.manual">Kurs von Hand eintragen</mat-card-title></mat-card-header>
      <mat-card-content>
        <p class="hint" i18n="@@prices.manualHint">
          Für Assets ohne Kurs in der Berechnung (Befund „Kurs fehlt“). Das Asset muss schon importiert sein. Trage die Quelle ein, damit der Kurs im Bericht nachvollziehbar bleibt.
        </p>
        <form class="manual-form" (ngSubmit)="saveManual()">
          <mat-form-field>
            <mat-label i18n="@@prices.symbol">Symbol (z. B. SOL)</mat-label>
            <input matInput name="symbol" [ngModel]="symbol()" (ngModelChange)="symbol.set($event)" data-testid="symbol" />
          </mat-form-field>
          <mat-form-field>
            <mat-label i18n="@@prices.at">Zeitpunkt (UTC)</mat-label>
            <input matInput type="datetime-local" name="at" [ngModel]="at()" (ngModelChange)="at.set($event)" data-testid="at" />
          </mat-form-field>
          <mat-form-field>
            <mat-label i18n="@@prices.eur">Kurs in EUR</mat-label>
            <input matInput type="number" min="0" step="any" name="eur" [ngModel]="eurPrice()" (ngModelChange)="eurPrice.set($event)" data-testid="eur" />
          </mat-form-field>
          <mat-form-field>
            <mat-label i18n="@@prices.source">Quelle (z. B. CoinGecko)</mat-label>
            <input matInput name="source" [ngModel]="source()" (ngModelChange)="source.set($event)" data-testid="source" />
          </mat-form-field>
          <button matButton="filled" type="submit" [disabled]="busy() || !manualValid()" data-testid="save-manual" i18n="@@prices.save">Kurs speichern</button>
        </form>
        @if (manualSaved()) {
          <p class="ok" data-testid="manual-saved" i18n="@@prices.saved">Kurs gespeichert. Danach das Steuerjahr neu berechnen.</p>
        }
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
    .manual { margin-top: 16px; }
    .manual-form { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
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

  protected readonly symbol = signal('');
  protected readonly at = signal('');
  protected readonly eurPrice = signal<number | null>(null);
  protected readonly source = signal('');
  protected readonly manualSaved = signal(false);

  protected manualValid(): boolean {
    return !!this.symbol().trim() && !!this.at() && (this.eurPrice() ?? 0) > 0;
  }

  protected saveManual(): void {
    this.busy.set(true);
    this.error.set(null);
    this.manualSaved.set(false);
    this.api
      .setManualPrice({
        symbol: this.symbol().trim(),
        at: new Date(this.at() + 'Z').toISOString(),
        eurPrice: this.eurPrice() as number,
        source: this.source().trim() || null,
      })
      .subscribe({
        next: () => {
          this.manualSaved.set(true);
          this.busy.set(false);
        },
        error: (e: unknown) => {
          this.error.set(errorMessage(e));
          this.busy.set(false);
        },
      });
  }

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
