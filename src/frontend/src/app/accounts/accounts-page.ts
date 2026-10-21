import { Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { KsApi, errorMessage } from '../api/ks-api';
import { Account, BitgetStatus, BitgetSyncResult, Holding } from '../api/models';

@Component({
  selector: 'ks-accounts-page',
  imports: [DatePipe, DecimalPipe, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  template: `
    <h1 i18n="@@accounts.title">Konten und Bestand</h1>

    @if (error(); as e) {
      <p class="error" role="alert" data-testid="error">{{ e }}</p>
    }

    @if (accounts().length === 0) {
      <p data-testid="no-accounts" i18n="@@accounts.none">Noch kein Konto. Lege unter „Import“ eines an.</p>
    }

    @for (account of accounts(); track account.id) {
      <mat-card class="account" data-testid="account">
        <mat-card-header>
          <mat-card-title>{{ account.name }}</mat-card-title>
          <mat-card-subtitle>{{ account.platform }} · {{ account.type }}</mat-card-subtitle>
        </mat-card-header>
        @if (isBitget(account)) {
          <mat-card-content>
            @if (status(account.id); as s) {
              @if (s.configured) {
                <p data-testid="configured">
                  <ng-container i18n="@@accounts.connected">Bitget-API verbunden</ng-container>
                  ({{ s.accountType ?? '?' }}).
                  @if (s.lastSyncAtUtc) {
                    <ng-container i18n="@@accounts.lastSync">Letzter Sync:</ng-container> {{ s.lastSyncAtUtc | date: 'medium' }}.
                  }
                </p>
                @for (w of s.warnings; track w) {
                  <p class="warn" data-testid="key-warning">{{ w }}</p>
                }
              } @else {
                <p class="hint" i18n="@@accounts.keyHint">
                  Lege in Bitget einen API-Key nur mit Leserechten an und trage ihn hier ein. Er wird verschlüsselt gespeichert und nie wieder angezeigt.
                </p>
                <form class="creds" (ngSubmit)="saveCredentials(account)" autocomplete="off">
                  <mat-form-field>
                    <mat-label i18n="@@accounts.apiKey">API-Key</mat-label>
                    <input matInput name="apiKey" [ngModel]="creds().apiKey" (ngModelChange)="setCred('apiKey', $event)" autocomplete="off" data-testid="api-key" />
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label i18n="@@accounts.secret">Secret</mat-label>
                    <input matInput type="password" name="secret" [ngModel]="creds().secret" (ngModelChange)="setCred('secret', $event)" autocomplete="off" data-testid="secret" />
                  </mat-form-field>
                  <mat-form-field>
                    <mat-label i18n="@@accounts.passphrase">Passphrase</mat-label>
                    <input matInput type="password" name="passphrase" [ngModel]="creds().passphrase" (ngModelChange)="setCred('passphrase', $event)" autocomplete="off" data-testid="passphrase" />
                  </mat-form-field>
                  <button matButton="filled" type="submit" [disabled]="busy() || !credsComplete()" data-testid="save-key" i18n="@@accounts.saveKey">Key speichern und prüfen</button>
                </form>
              }
            }
          </mat-card-content>
          <mat-card-actions>
            @if (status(account.id)?.configured) {
              <button matButton="filled" type="button" [disabled]="busy()" (click)="sync(account)" data-testid="sync" i18n="@@accounts.sync">Jetzt synchronisieren</button>
              <button matButton type="button" [disabled]="busy()" (click)="removeKey(account)" data-testid="remove-key" i18n="@@accounts.removeKey">Key entfernen</button>
            }
          </mat-card-actions>
          @if (syncResults()[account.id]; as r) {
            <mat-card-content data-testid="sync-result">
              <p class="ok">
                {{ r.import.new }} <ng-container i18n="@@accounts.newTx">neue Buchungen</ng-container>,
                {{ r.import.duplicates }} <ng-container i18n="@@accounts.dups">bereits vorhanden</ng-container>,
                {{ r.import.needsReview }} <ng-container i18n="@@accounts.review">zur Prüfung</ng-container>.
              </p>
              @for (w of r.warnings; track w) { <p class="warn">{{ w }}</p> }
              @if (r.failedSections.length > 0) {
                <p class="warn" data-testid="failed-sections">
                  <ng-container i18n="@@accounts.failed">Nicht abrufbar:</ng-container> {{ r.failedSections.join(', ') }}
                </p>
              }
              @if (r.balanceCheckSkipped) { <p class="hint">{{ r.balanceCheckSkipped }}</p> }
              @if (r.balanceMismatches.length > 0) {
                <p class="warn" i18n="@@accounts.mismatch">Saldo weicht ab (Börse gegenüber Buchungen):</p>
                <ul data-testid="mismatches">
                  @for (m of r.balanceMismatches; track m.symbol) {
                    <li>{{ m.symbol }}: {{ m.exchangeBalance | number: '1.0-8' }} / {{ m.computedHolding | number: '1.0-8' }} (Δ {{ m.difference | number: '1.0-8' }})</li>
                  }
                </ul>
              } @else if (!r.balanceCheckSkipped) {
                <p class="ok" i18n="@@accounts.balanced">Salden stimmen mit den Buchungen überein.</p>
              }
            </mat-card-content>
          }
        }
      </mat-card>
    }

    <h2 i18n="@@holdings.title">Bestand heute</h2>
    @if (holdings().length === 0) {
      <p i18n="@@holdings.empty">Kein Bestand aus den importierten Buchungen.</p>
    } @else {
      <div class="table-wrap">
        <table data-testid="holdings">
          <thead>
            <tr>
              <th i18n="@@t.account">Konto</th><th i18n="@@t.asset">Asset</th><th class="num" i18n="@@t.qty">Menge</th>
              <th class="num" i18n="@@t.costBasis">Kosten €</th><th class="num" i18n="@@t.lots">Lots</th>
              <th i18n="@@t.oldest">Ältester Kauf</th><th i18n="@@t.freeFrom">Frühestens steuerfrei ab</th>
            </tr>
          </thead>
          <tbody>
            @for (h of holdings(); track $index) {
              <tr>
                <td>{{ h.account }}</td><td>{{ h.asset }}</td><td class="num">{{ h.quantity | number: '1.0-8' }}</td>
                <td class="num">{{ h.costEur | number: '1.2-2' }}</td><td class="num">{{ h.lots }}</td>
                <td>{{ h.oldestAcquired | date: 'dd.MM.yyyy' }}</td><td>{{ h.nextTaxFreeFrom | date: 'dd.MM.yyyy' }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    .account { margin-bottom: 16px; }
    .creds { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .hint { color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); }
    .warn { color: var(--mat-sys-tertiary); }
    .ok { color: var(--mat-sys-primary); }
    .table-wrap { overflow-x: auto; }
    table { border-collapse: collapse; width: 100%; }
    th, td { padding: 6px 10px; border-bottom: 1px solid var(--mat-sys-outline-variant); text-align: left; white-space: nowrap; }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
  `,
})
export class AccountsPage {
  private readonly api = inject(KsApi);

  protected readonly accounts = signal<Account[]>([]);
  protected readonly holdings = signal<Holding[]>([]);
  protected readonly statuses = signal<Record<string, BitgetStatus>>({});
  protected readonly syncResults = signal<Record<string, BitgetSyncResult>>({});
  protected readonly creds = signal({ apiKey: '', secret: '', passphrase: '' });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.listAccounts().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        accounts.filter((a) => this.isBitget(a)).forEach((a) => this.loadStatus(a.id));
      },
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
    this.loadHoldings();
  }

  protected isBitget(account: Account): boolean {
    return account.platform.toLowerCase() === 'bitget';
  }

  protected status(id: string): BitgetStatus | undefined {
    return this.statuses()[id];
  }

  protected credsComplete(): boolean {
    const c = this.creds();
    return !!(c.apiKey.trim() && c.secret.trim() && c.passphrase.trim());
  }

  protected setCred(field: 'apiKey' | 'secret' | 'passphrase', value: string): void {
    this.creds.update((c) => ({ ...c, [field]: value }));
  }

  private loadStatus(id: string): void {
    this.api.bitgetStatus(id).subscribe({
      next: (s) => this.statuses.update((all) => ({ ...all, [id]: s })),
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }

  private loadHoldings(): void {
    this.api.holdings().subscribe({
      next: (h) => this.holdings.set(h),
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }

  protected saveCredentials(account: Account): void {
    this.busy.set(true);
    this.error.set(null);
    const c = this.creds();
    this.api.saveBitgetCredentials(account.id, { apiKey: c.apiKey.trim(), secret: c.secret.trim(), passphrase: c.passphrase.trim() }).subscribe({
      next: (s) => {
        // Geheimnisse sofort aus dem Formular entfernen; sie liegen nur noch verschlüsselt auf dem Server.
        this.creds.set({ apiKey: '', secret: '', passphrase: '' });
        this.statuses.update((all) => ({ ...all, [account.id]: s }));
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(errorMessage(e));
        this.busy.set(false);
      },
    });
  }

  protected sync(account: Account): void {
    this.busy.set(true);
    this.error.set(null);
    this.api.syncBitget(account.id).subscribe({
      next: (result) => {
        this.syncResults.update((all) => ({ ...all, [account.id]: result }));
        this.loadStatus(account.id);
        this.loadHoldings();
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(errorMessage(e));
        this.busy.set(false);
      },
    });
  }

  protected removeKey(account: Account): void {
    this.api.deleteBitgetCredentials(account.id).subscribe({
      next: () => this.loadStatus(account.id),
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }
}
