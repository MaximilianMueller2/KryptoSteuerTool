import { AccountsPage } from './accounts-page';
import { el, setup, text } from '../testing';

const bitget = { id: 'b1', name: 'Bitget', type: 'Exchange', platform: 'Bitget', country: 'SC', subAccount: null };
const tr = { id: 't1', name: 'Trade Republic', type: 'Broker', platform: 'TradeRepublic', country: 'DE', subAccount: null };
const holding = { accountId: 't1', account: 'Trade Republic', asset: 'XRP', quantity: 146.728991, costEur: 70, lots: 1, oldestAcquired: '2024-11-04', nextTaxFreeFrom: '2025-11-05' };
const status = (configured: boolean) => ({ configured, accountType: configured ? 'uta' : null, warnings: configured ? ['Schreibrecht aktiv'] : [], lastSyncAtUtc: null, lastSyncSummary: null });
const syncResult = {
  import: { rowsRead: 10, new: 8, duplicates: 2, needsReview: 0, errors: [], unknownTypes: [], warnings: [] },
  failedSections: ['Earn'], warnings: ['Hinweis'],
  balanceMismatches: [{ symbol: 'USDT', exchangeBalance: 100, computedHolding: 90, difference: 10 }], balanceCheckSkipped: null,
};

describe('AccountsPage', () => {
  it('zeigt den Bestand und bietet nur für Bitget-Konten die API-Anbindung an', async () => {
    const { http, create } = setup(AccountsPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([tr, bitget]);
    http.expectOne('/api/tax/holdings').flush([holding]);
    http.expectOne('/api/accounts/b1/bitget').flush(status(false));
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="account"]').length).toBe(2);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="api-key"]').length).toBe(1);
    expect(text(fixture, 'holdings')).toContain('XRP');
    expect(text(fixture, 'holdings')).toContain('146,728991');
  });

  it('speichert den Key, leert danach das Formular und zeigt Rechte-Warnungen', async () => {
    const { http, create } = setup(AccountsPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([bitget]);
    http.expectOne('/api/tax/holdings').flush([]);
    http.expectOne('/api/accounts/b1/bitget').flush(status(false));
    await fixture.whenStable();

    for (const [id, value] of [['api-key', 'k'], ['secret', 's'], ['passphrase', 'p']]) {
      const input = el(fixture, id) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    await fixture.whenStable();
    (el(fixture, 'save-key') as HTMLButtonElement).click();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/accounts/b1/bitget');
    expect(put.request.body).toEqual({ apiKey: 'k', secret: 's', passphrase: 'p' });
    put.flush(status(true));
    await fixture.whenStable();

    expect(el(fixture, 'api-key')).toBeNull();
    expect(text(fixture, 'configured')).toContain('uta');
    expect(text(fixture, 'key-warning')).toContain('Schreibrecht');
  });

  it('synchronisiert und zeigt Ergebnis, Fehlabschnitte und Saldo-Abweichungen', async () => {
    const { http, create } = setup(AccountsPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([bitget]);
    http.expectOne('/api/tax/holdings').flush([]);
    http.expectOne('/api/accounts/b1/bitget').flush(status(true));
    await fixture.whenStable();

    (el(fixture, 'sync') as HTMLButtonElement).click();
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/accounts/b1/bitget/sync').flush(syncResult);
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/accounts/b1/bitget').flush(status(true));
    http.expectOne('/api/tax/holdings').flush([holding]);
    await fixture.whenStable();

    expect(text(fixture, 'sync-result')).toContain('8');
    expect(text(fixture, 'failed-sections')).toContain('Earn');
    expect(text(fixture, 'mismatches')).toContain('USDT');
    expect(text(fixture, 'holdings')).toContain('XRP');
  });

  it('zeigt Fehler des Syncs', async () => {
    const { http, create } = setup(AccountsPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([bitget]);
    http.expectOne('/api/tax/holdings').flush([]);
    http.expectOne('/api/accounts/b1/bitget').flush(status(true));
    await fixture.whenStable();

    (el(fixture, 'sync') as HTMLButtonElement).click();
    http.expectOne('/api/accounts/b1/bitget/sync').flush({ detail: 'Bitget lehnt den Key ab.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(text(fixture, 'error')).toContain('lehnt');
  });
});
