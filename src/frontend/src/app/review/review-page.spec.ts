import { ReviewPage } from './review-page';
import { el, setup, text } from '../testing';

const accounts = [
  { id: 'tr', name: 'Trade Republic', type: 'Broker', platform: 'TradeRepublic', country: 'DE', subAccount: null },
  { id: 'w', name: 'Wallet', type: 'Wallet', platform: 'Wallet', country: null, subAccount: null },
];
const item = (kind: string, actions: string[], tx: string) => ({
  id: `${kind}:${tx}`, kind, message: `Befund ${kind}`, transactionId: tx, assetId: null, accountId: 'tr', taxYear: 2026, actions,
});

describe('ReviewPage', () => {
  it('zeigt Befunde, markiert eine Auszahlung als eigenen Transfer und lädt neu', async () => {
    const { http, create } = setup(ReviewPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush(accounts);
    http.expectOne('/api/review').flush({ open: [item('UnmatchedTransferOut', ['transfer-to-own-account'], 't1')], decisions: [] });
    await fixture.whenStable();

    expect(text(fixture, 'item')).toContain('Befund UnmatchedTransferOut');
    (el(fixture, 'own-transfer') as HTMLButtonElement).click();
    const put = http.expectOne('/api/review/transfers/t1/own-account');
    expect(put.request.body).toEqual({ targetAccountId: 'w' });
    put.flush({ transactionId: 't1', kind: 'TransferToOwnAccount', targetAccountId: 'w', note: null, createdAtUtc: '2026-10-20T10:00:00Z' });
    http.expectOne('/api/review').flush({
      open: [],
      decisions: [{ transactionId: 't1', kind: 'TransferToOwnAccount', targetAccountId: 'w', note: null, createdAtUtc: '2026-10-20T10:00:00Z' }],
    });
    await fixture.whenStable();

    expect(el(fixture, 'empty')).not.toBeNull();
    expect(text(fixture, 'decisions')).toContain('Transfer auf eigenes Konto');
    expect(text(fixture, 'decisions')).toContain('Wallet');
  });

  it('bestätigt eine Prüfbuchung', async () => {
    const { http, create } = setup(ReviewPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush(accounts);
    http.expectOne('/api/review').flush({ open: [item('TransactionNeedsReview', ['acknowledge'], 't2')], decisions: [] });
    await fixture.whenStable();

    expect(el(fixture, 'own-transfer')).toBeNull();
    (el(fixture, 'acknowledge') as HTMLButtonElement).click();
    http.expectOne('/api/review/transactions/t2/acknowledge').flush({});
    http.expectOne('/api/review').flush({ open: [], decisions: [] });
    await fixture.whenStable();

    expect(el(fixture, 'empty')).not.toBeNull();
  });

  it('zeigt Fehler beim Speichern einer Entscheidung', async () => {
    const { http, create } = setup(ReviewPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush(accounts);
    http.expectOne('/api/review').flush({ open: [item('UnmatchedTransferOut', ['transfer-to-own-account'], 't1')], decisions: [] });
    await fixture.whenStable();

    (el(fixture, 'own-transfer') as HTMLButtonElement).click();
    http.expectOne('/api/review/transfers/t1/own-account').flush({ detail: 'Zielkonto nicht gefunden.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(text(fixture, 'error')).toContain('Zielkonto');
  });

  it('bestätigt alle Prüfbuchungen auf einmal', async () => {
    const { http, create } = setup(ReviewPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush(accounts);
    http.expectOne('/api/review').flush({
      open: [item('TransactionNeedsReview', ['acknowledge'], 't1'), item('TransactionNeedsReview', ['acknowledge'], 't2')],
      decisions: [],
    });
    await fixture.whenStable();

    expect(text(fixture, 'acknowledge-all')).toContain('2');
    (el(fixture, 'acknowledge-all') as HTMLButtonElement).click();
    http.expectOne('/api/review/transactions/t1/acknowledge').flush({});
    http.expectOne('/api/review/transactions/t2/acknowledge').flush({});
    http.expectOne('/api/review').flush({ open: [], decisions: [] });
    await fixture.whenStable();

    expect(el(fixture, 'empty')).not.toBeNull();
  });
});
