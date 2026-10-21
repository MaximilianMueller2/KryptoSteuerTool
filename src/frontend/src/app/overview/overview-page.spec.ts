import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import { OverviewPage } from './overview-page';
import { el, setup, text } from '../testing';

registerLocaleData(localeDe);

const summary = {
  year: 2025,
  lawRuleSet: 'DE-23EStG',
  lawVersion: '2026.1',
  section23: {
    proceedsEur: 2643.01, costEur: 2477.51, feesEur: 4, gainsEur: 289.35, lossesEur: 127.85, netEur: 161.5, freigrenzeEur: 1000,
    belowFreigrenze: true, lossCarryInEur: 0, lossUsedEur: 0, taxableEur: 0, lossCarryOutEur: 0, taxFreeAfterPeriodNetEur: 0,
  },
  section22: { incomeEur: 0, expensesEur: 0, netEur: 0, freigrenzeEur: 256, belowFreigrenze: true, taxableEur: 0 },
  futures: { realizedGainsEur: 0, realizedLossesEur: 0, fundingNetEur: 0, tradingFeesEur: 0, liquidationEur: 0, netEur: 0, records: 0 },
  openFindings: [],
};

const run = (findings: number) => ({
  id: 'r1', year: 2025, createdAtUtc: '2026-10-20T10:00:00Z', ruleSet: 'DE-23EStG@2026.1', inputHash: 'abcdef0123456789',
  inputUnchanged: false,
  counts: { lots: 1, disposals: 1, incomes: 0, futuresRecords: 0, transfers: 0, findings, transactions: 5 },
  summary, findings: [], diffToPrevious: null, isDraft: findings > 0,
});

const report = {
  year: 2025,
  rows: [{
    asset: 'SOL', account: 'Trade Republic', quantity: 3.021854, acquired: '2024-11-05', disposed: '2025-01-04', holdingDays: 60,
    costEur: 466.05, proceedsEur: 629.93, feeEur: 0.85, gainEur: 163.03, taxable: true, treatment: 'Section23Taxable', priceSource: 'EUR', transactionId: 'tr:1',
  }],
  taxableGainEur: 163.03, taxableLossEur: 0, taxableNetEur: 163.03, taxFreeNetEur: 0,
};

describe('OverviewPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: LOCALE_ID, useValue: 'de' }] });
  });

  it('berechnet ein Jahr, zeigt Summen auf Deutsch und lädt die Einzelaufstellung', async () => {
    const { http, create } = setup(OverviewPage);
    const fixture = create();
    await fixture.whenStable();

    (el(fixture, 'run') as HTMLButtonElement).click();
    const req = http.expectOne('/api/tax/runs');
    expect(req.request.body.applyReform2027).toBe(false);
    req.flush(run(0));
    http.expectOne((r) => r.url === '/api/tax/years/2025/disposals').flush(report);
    await fixture.whenStable();

    expect(text(fixture, 'net')).toContain('161,50');
    expect(text(fixture, 'taxable')).toContain('0,00');
    expect(el(fixture, 'final')).not.toBeNull();
    expect(el(fixture, 'draft')).toBeNull();
    expect(text(fixture, 'disposals')).toContain('SOL');
    expect(text(fixture, 'disposals')).toContain('04.01.2025');
    expect(el(fixture, 'pdf')?.getAttribute('href')).toBe('/api/reports/2025/pdf?reform=false');
    expect(el(fixture, 'xlsx')?.getAttribute('href')).toBe('/api/reports/2025/xlsx?reform=false');
    expect(el(fixture, 'csv')?.getAttribute('href')).toBe('/api/reports/2025/csv?reform=false');
  });

  it('kennzeichnet einen Lauf mit offenen Befunden als Entwurf', async () => {
    const { http, create } = setup(OverviewPage);
    const fixture = create();
    await fixture.whenStable();

    (el(fixture, 'run') as HTMLButtonElement).click();
    http.expectOne('/api/tax/runs').flush(run(14));
    http.expectOne((r) => r.url === '/api/tax/years/2025/disposals').flush({ ...report, rows: [] });
    await fixture.whenStable();

    expect(text(fixture, 'draft')).toContain('14');
  });

  it('zeigt Fehlermeldungen der API', async () => {
    const { http, create } = setup(OverviewPage);
    const fixture = create();
    await fixture.whenStable();

    (el(fixture, 'run') as HTMLButtonElement).click();
    http.expectOne('/api/tax/runs').flush({ detail: 'Steuerjahr außerhalb des gültigen Bereichs.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(text(fixture, 'error')).toContain('außerhalb');
  });

  it('gibt den Namen für das Deckblatt in den Download-Adressen mit', async () => {
    const { http, create } = setup(OverviewPage);
    const fixture = create();
    await fixture.whenStable();

    const input = (fixture.nativeElement as HTMLElement).querySelector('input[name="taxpayer"]') as HTMLInputElement;
    input.value = 'Erika Muster';
    input.dispatchEvent(new Event('input'));
    (el(fixture, 'run') as HTMLButtonElement).click();
    http.expectOne('/api/tax/runs').flush(run(0));
    http.expectOne((r) => r.url === '/api/tax/years/2025/disposals').flush(report);
    await fixture.whenStable();

    expect(el(fixture, 'pdf')?.getAttribute('href')).toBe('/api/reports/2025/pdf?reform=false&name=Erika+Muster');
  });
});
