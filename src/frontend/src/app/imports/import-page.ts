import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { KsApi, errorMessage } from '../api/ks-api';
import { Account, AccountType, ImportRun } from '../api/models';

const SOURCES = [
  { value: '', label: 'Automatisch erkennen' },
  { value: 'bitget-csv', label: 'Bitget CSV (Futures)' },
  { value: 'bitget-api', label: 'Bitget API-Bündel (JSON)' },
  { value: 'traderepublic-csv', label: 'Trade Republic CSV' },
  { value: 'generic-csv', label: 'Allgemeines CSV' },
];

const PRESETS: { label: string; name: string; type: AccountType; platform: string; country: string }[] = [
  { label: 'Trade Republic (Depot)', name: 'Trade Republic', type: 'Broker', platform: 'TradeRepublic', country: 'DE' },
  { label: 'Bitget (Börse)', name: 'Bitget', type: 'Exchange', platform: 'Bitget', country: 'SC' },
];

@Component({
  selector: 'ks-import-page',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h1 i18n="@@import.title">Import</h1>

    <mat-card>
      <mat-card-content class="form">
        @if (accounts().length === 0) {
          <p data-testid="no-accounts" i18n="@@import.noAccounts">Lege zuerst ein Konto an:</p>
          <div class="presets">
            @for (p of presets; track p.platform) {
              <button matButton="outlined" type="button" (click)="createPreset(p)">{{ p.label }}</button>
            }
          </div>
        } @else {
          <mat-form-field>
            <mat-label i18n="@@import.account">Konto</mat-label>
            <mat-select [value]="accountId()" (valueChange)="accountId.set($event)" data-testid="account">
              @for (a of accounts(); track a.id) {
                <mat-option [value]="a.id">{{ a.name }} ({{ a.platform }})</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label i18n="@@import.source">Dateityp</mat-label>
            <mat-select [value]="source()" (valueChange)="source.set($event)">
              @for (s of sources; track s.value) {
                <mat-option [value]="s.value">{{ s.label }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label i18n="@@import.tz">Zeitzone der Datei (optional, z. B. UTC+2)</mat-label>
            <input matInput [ngModel]="timeZone()" (ngModelChange)="timeZone.set($event)" name="tz" />
          </mat-form-field>
          <input type="file" data-testid="file" (change)="pick($event)" accept=".csv,.json,.txt" />
          <div>
            <button matButton="filled" type="button" [disabled]="busy() || !file() || !accountId()" (click)="preview()" data-testid="preview" i18n="@@import.preview">
              Vorschau erzeugen
            </button>
          </div>
        }
      </mat-card-content>
    </mat-card>

    @if (error(); as e) {
      <p class="error" role="alert" data-testid="error">{{ e }}</p>
    }

    @if (run(); as r) {
      <mat-card class="report">
        <mat-card-header>
          <mat-card-title>{{ r.fileName }} – {{ r.source }}</mat-card-title>
        </mat-card-header>
        <mat-card-content>
          <dl data-testid="report">
            <dt i18n="@@import.rows">Zeilen gelesen</dt><dd>{{ r.report.rowsRead }}</dd>
            <dt i18n="@@import.new">Neue Buchungen</dt><dd>{{ r.report.new }}</dd>
            <dt i18n="@@import.dups">Bereits vorhanden</dt><dd>{{ r.report.duplicates }}</dd>
            <dt i18n="@@import.review">Zur Prüfung</dt><dd>{{ r.report.needsReview }}</dd>
            <dt i18n="@@import.errors">Fehler</dt><dd>{{ r.report.errors.length }}</dd>
          </dl>
          @for (w of r.report.warnings; track w) {
            <p class="warn">{{ w }}</p>
          }
          @if (r.report.errors.length > 0) {
            <details>
              <summary i18n="@@import.errorList">Fehlerzeilen</summary>
              <ul>
                @for (e of r.report.errors; track $index) {
                  <li>{{ e.lineNumber ?? '–' }}: {{ e.reason }}</li>
                }
              </ul>
            </details>
          }
          @if (r.report.unknownTypes.length > 0) {
            <p class="warn" i18n="@@import.unknown">Unbekannte Typen: {{ r.report.unknownTypes.join(', ') }}</p>
          }
        </mat-card-content>
        <mat-card-actions>
          @if (r.status === 'Committed') {
            <span class="ok" data-testid="committed" i18n="@@import.committed">Festgeschrieben.</span>
          } @else {
            <button matButton="filled" type="button" [disabled]="busy()" (click)="commit(r)" data-testid="commit" i18n="@@import.commit">Festschreiben</button>
            <button matButton type="button" [disabled]="busy()" (click)="discard(r)" data-testid="discard" i18n="@@import.discard">Verwerfen</button>
          }
        </mat-card-actions>
      </mat-card>
    }
  `,
  styles: `
    .form { display: flex; flex-direction: column; gap: 8px; align-items: flex-start; }
    .presets { display: flex; gap: 8px; flex-wrap: wrap; }
    .report { margin-top: 16px; }
    dl { display: grid; grid-template-columns: 1fr auto; gap: 4px 16px; max-width: 360px; }
    dd { margin: 0; text-align: right; }
    .error { color: var(--mat-sys-error); }
    .warn { color: var(--mat-sys-tertiary); }
    .ok { color: var(--mat-sys-primary); padding: 0 8px; }
  `,
})
export class ImportPage {
  private readonly api = inject(KsApi);

  protected readonly sources = SOURCES;
  protected readonly presets = PRESETS;
  protected readonly accounts = signal<Account[]>([]);
  protected readonly accountId = signal('');
  protected readonly source = signal('');
  protected readonly timeZone = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly run = signal<ImportRun | null>(null);

  constructor() {
    this.loadAccounts();
  }

  private loadAccounts(): void {
    this.api.listAccounts().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        if (!this.accountId() && accounts.length > 0) this.accountId.set(accounts[0].id);
      },
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }

  protected createPreset(p: (typeof PRESETS)[number]): void {
    this.api.createAccount({ name: p.name, type: p.type, platform: p.platform, country: p.country }).subscribe({
      next: () => this.loadAccounts(),
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }

  protected pick(event: Event): void {
    this.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected preview(): void {
    const file = this.file();
    if (!file) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.previewImport(file, this.accountId(), this.source() || null, this.timeZone().trim() || null).subscribe({
      next: (run) => {
        this.run.set(run);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(errorMessage(e));
        this.busy.set(false);
      },
    });
  }

  protected commit(run: ImportRun): void {
    this.busy.set(true);
    this.api.commitImport(run.id).subscribe({
      next: (done) => {
        this.run.set(done);
        this.busy.set(false);
      },
      error: (e: unknown) => {
        this.error.set(errorMessage(e));
        this.busy.set(false);
      },
    });
  }

  protected discard(run: ImportRun): void {
    this.api.discardImport(run.id).subscribe({
      next: () => this.run.set(null),
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }
}
