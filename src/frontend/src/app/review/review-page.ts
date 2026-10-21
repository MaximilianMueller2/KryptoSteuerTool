import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { forkJoin } from 'rxjs';
import { KsApi, errorMessage } from '../api/ks-api';
import { Account, ReviewList, ReviewItem } from '../api/models';

const NEW_ACCOUNT = '__new__';

const KIND_LABELS: Record<string, string> = {
  UnmatchedTransferOut: 'Auszahlung ohne Gegenstück',
  UnmatchedTransferIn: 'Eingang ohne Gegenstück',
  TransactionNeedsReview: 'Buchung zur Prüfung',
  MissingPrice: 'Kurs fehlt',
  NegativeHolding: 'Bestand reicht nicht',
  AssetBlocked: 'Berechnung gestoppt',
};

@Component({
  selector: 'ks-review-page',
  imports: [FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h1 i18n="@@review.title">Prüfung</h1>
    <p class="hint" i18n="@@review.intro">
      Offene Befunde der Berechnung. Deine Entscheidungen werden mit Audit-Eintrag gespeichert; die Rohdaten bleiben unverändert.
    </p>

    @if (error(); as e) {
      <p class="error" role="alert" data-testid="error">{{ e }}</p>
    }

    @if (list(); as l) {
      @if (l.open.length === 0) {
        <p class="ok" data-testid="empty" i18n="@@review.empty">Keine offenen Befunde.</p>
      }

      @if (acknowledgeable(l.open).length > 1) {
        <p>
          <button matButton="outlined" type="button" [disabled]="busy()" (click)="acknowledgeAll(l.open)" data-testid="acknowledge-all">
            {{ acknowledgeable(l.open).length }} <ng-container i18n="@@review.ackAll">Prüfbuchungen als geprüft bestätigen (keine Auswirkung)</ng-container>
          </button>
        </p>
      }

      @for (item of l.open; track item.id) {
        <mat-card class="item" data-testid="item">
          <mat-card-header><mat-card-subtitle>{{ label(item.kind) }}@if (item.taxYear) { · {{ item.taxYear }} }</mat-card-subtitle></mat-card-header>
          <mat-card-content>
            <p>{{ item.message }}</p>
            @if (item.actions.includes('transfer-to-own-account') && item.transactionId) {
              <div class="action">
                <mat-form-field>
                  <mat-label i18n="@@review.target">Ging auf mein Konto</mat-label>
                  <mat-select [value]="target(item)" (valueChange)="setTarget(item, $event)" data-testid="target">
                    @for (a of otherAccounts(item); track a.id) {
                      <mat-option [value]="a.id">{{ a.name }}</mat-option>
                    }
                    <mat-option [value]="newAccount">Neues Wallet anlegen …</mat-option>
                  </mat-select>
                </mat-form-field>
                @if (target(item) === newAccount) {
                  <mat-form-field>
                    <mat-label i18n="@@review.newName">Name des Wallets</mat-label>
                    <input matInput [ngModel]="newName()" (ngModelChange)="newName.set($event)" name="newName" data-testid="new-name" />
                  </mat-form-field>
                }
                <button matButton="filled" type="button" [disabled]="busy()" (click)="ownTransfer(item)" data-testid="own-transfer" i18n="@@review.confirm">
                  Als eigenen Transfer markieren
                </button>
              </div>
            }
            @if (item.actions.includes('acknowledge') && item.transactionId) {
              <button matButton type="button" [disabled]="busy()" (click)="acknowledge(item)" data-testid="acknowledge" i18n="@@review.ack">
                Geprüft, keine Auswirkung
              </button>
            }
          </mat-card-content>
        </mat-card>
      }

      @if (l.decisions.length > 0) {
        <h2 i18n="@@review.decisions">Getroffene Entscheidungen</h2>
        <ul data-testid="decisions">
          @for (d of l.decisions; track d.transactionId) {
            <li>
              {{ d.kind === 'TransferToOwnAccount' ? 'Transfer auf eigenes Konto' : 'Geprüft' }}
              @if (d.targetAccountId) { → {{ accountName(d.targetAccountId) }} }
              @if (d.note) { ({{ d.note }}) }
              <button matButton type="button" (click)="revoke(d.transactionId)" i18n="@@review.revoke">Zurücknehmen</button>
            </li>
          }
        </ul>
      }
    }
  `,
  styles: `
    .item { margin-bottom: 12px; }
    .action { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .hint { color: var(--mat-sys-on-surface-variant); }
    .error { color: var(--mat-sys-error); }
    .ok { color: var(--mat-sys-primary); }
  `,
})
export class ReviewPage {
  private readonly api = inject(KsApi);

  protected readonly newAccount = NEW_ACCOUNT;
  protected readonly list = signal<ReviewList | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly targets = signal<Record<string, string>>({});
  protected readonly newName = signal('Bitget Wallet');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.api.listAccounts().subscribe({ next: (a) => this.accounts.set(a), error: (e: unknown) => this.error.set(errorMessage(e)) });
    this.reload();
  }

  protected reload(): void {
    this.api.review().subscribe({
      next: (list) => this.list.set(list),
      error: (e: unknown) => this.error.set(errorMessage(e)),
    });
  }

  protected otherAccounts(item: ReviewItem): Account[] {
    return this.accounts().filter((a) => a.id !== item.accountId);
  }

  protected accountName(id: string): string {
    return this.accounts().find((a) => a.id === id)?.name ?? id.slice(0, 8);
  }

  protected target(item: ReviewItem): string {
    return this.targets()[item.id] ?? this.otherAccounts(item)[0]?.id ?? NEW_ACCOUNT;
  }

  protected setTarget(item: ReviewItem, value: string): void {
    this.targets.update((t) => ({ ...t, [item.id]: value }));
  }

  protected ownTransfer(item: ReviewItem): void {
    if (!item.transactionId) return;
    const target = this.target(item);
    const body = target === NEW_ACCOUNT ? { newAccountName: this.newName().trim() } : { targetAccountId: target };
    this.act(this.api.decideOwnTransfer(item.transactionId, body), true);
  }

  protected acknowledge(item: ReviewItem): void {
    if (item.transactionId) this.act(this.api.acknowledge(item.transactionId, null), false);
  }

  protected label(kind: string): string {
    return KIND_LABELS[kind] ?? kind;
  }

  protected acknowledgeable(items: ReviewItem[]): ReviewItem[] {
    return items.filter((i) => i.actions.includes('acknowledge') && i.transactionId);
  }

  protected acknowledgeAll(items: ReviewItem[]): void {
    const requests = this.acknowledgeable(items).map((i) => this.api.acknowledge(i.transactionId as string, null));
    this.act(forkJoin(requests), false);
  }

  protected revoke(transactionId: string): void {
    this.act(this.api.revoke(transactionId), false);
  }

  private act(request: { subscribe: (o: { next: () => void; error: (e: unknown) => void }) => unknown }, reloadAccounts: boolean): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        if (reloadAccounts) this.api.listAccounts().subscribe((a) => this.accounts.set(a));
        this.reload();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }
}
