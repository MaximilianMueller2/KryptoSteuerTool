import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Account,
  BitgetStatus,
  BitgetSyncResult,
  CreateAccountRequest,
  DecisionView,
  DisposalReport,
  EcbImportResult,
  Holding,
  ManualPrice,
  ImportRun,
  ReviewList,
  TaxRunRequest,
  TaxRunResult,
} from './models';

/** Liest die Fehlermeldung einer Problem-Antwort der API (`detail`) oder fällt auf einen Standardtext zurück. */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const detail = (error.error as { detail?: string } | null)?.detail;
    if (detail) return detail;
    if (error.status === 0) return 'API nicht erreichbar.';
  }
  return 'Die Anfrage ist fehlgeschlagen.';
}

/** Zugriff auf die Backend-API (handgeschrieben, bis der Client aus der OpenAPI-Spezifikation generiert wird). */
@Injectable({ providedIn: 'root' })
export class KsApi {
  private readonly http = inject(HttpClient);

  listAccounts(): Observable<Account[]> {
    return this.http.get<Account[]>('/api/accounts');
  }

  createAccount(request: CreateAccountRequest): Observable<Account> {
    return this.http.post<Account>('/api/accounts', request);
  }

  previewImport(file: File, accountId: string, source: string | null, timeZone: string | null): Observable<ImportRun> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('accountId', accountId);
    if (source) form.append('source', source);
    if (timeZone) form.append('timeZone', timeZone);
    return this.http.post<ImportRun>('/api/imports', form);
  }

  commitImport(id: string): Observable<ImportRun> {
    return this.http.post<ImportRun>(`/api/imports/${id}/commit`, {});
  }

  discardImport(id: string): Observable<void> {
    return this.http.delete<void>(`/api/imports/${id}`);
  }

  runTax(request: TaxRunRequest): Observable<TaxRunResult> {
    return this.http.post<TaxRunResult>('/api/tax/runs', request);
  }

  disposals(year: number, reform = false): Observable<DisposalReport> {
    return this.http.get<DisposalReport>(`/api/tax/years/${year}/disposals`, { params: { reform } });
  }

  /** Download-Adresse des Jahresberichts (`pdf`, `xlsx` oder `csv`); der Name erscheint auf dem Deckblatt. */
  reportUrl(year: number, format: 'pdf' | 'xlsx' | 'csv', name: string, reform: boolean): string {
    const query = new URLSearchParams({ reform: String(reform) });
    if (name.trim()) query.set('name', name.trim());
    return `/api/reports/${year}/${format}?${query.toString()}`;
  }

  holdings(): Observable<Holding[]> {
    return this.http.get<Holding[]>('/api/tax/holdings');
  }

  bitgetStatus(accountId: string): Observable<BitgetStatus> {
    return this.http.get<BitgetStatus>(`/api/accounts/${accountId}/bitget`);
  }

  saveBitgetCredentials(accountId: string, credentials: { apiKey: string; secret: string; passphrase: string }): Observable<BitgetStatus> {
    return this.http.put<BitgetStatus>(`/api/accounts/${accountId}/bitget`, credentials);
  }

  deleteBitgetCredentials(accountId: string): Observable<void> {
    return this.http.delete<void>(`/api/accounts/${accountId}/bitget`);
  }

  syncBitget(accountId: string): Observable<BitgetSyncResult> {
    return this.http.post<BitgetSyncResult>(`/api/accounts/${accountId}/bitget/sync`, {});
  }

  review(): Observable<ReviewList> {
    return this.http.get<ReviewList>('/api/review');
  }

  decideOwnTransfer(transactionId: string, body: { targetAccountId?: string; newAccountName?: string; note?: string }): Observable<DecisionView> {
    return this.http.put<DecisionView>(`/api/review/transfers/${transactionId}/own-account`, body);
  }

  acknowledge(transactionId: string, note: string | null): Observable<DecisionView> {
    return this.http.put<DecisionView>(`/api/review/transactions/${transactionId}/acknowledge`, { note });
  }

  revoke(transactionId: string): Observable<void> {
    return this.http.delete<void>(`/api/review/decisions/${transactionId}`);
  }

  uploadEcb(file: File): Observable<EcbImportResult> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<EcbImportResult>('/api/prices/ecb', form);
  }

  setManualPrice(request: { symbol: string; at: string; eurPrice: number; source: string | null }): Observable<ManualPrice> {
    return this.http.put<ManualPrice>('/api/prices', request);
  }

  fetchEcb(): Observable<EcbImportResult> {
    return this.http.post<EcbImportResult>('/api/prices/ecb/fetch', {});
  }
}
