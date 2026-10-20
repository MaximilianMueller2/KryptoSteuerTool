import { PricesPage } from './prices-page';
import { el, setup, text } from '../testing';

describe('PricesPage', () => {
  it('ruft die EZB-Kurse online ab und zeigt das Ergebnis', async () => {
    const { http, create } = setup(PricesPage);
    const fixture = create();
    await fixture.whenStable();

    (el(fixture, 'fetch') as HTMLButtonElement).click();
    http.expectOne((r) => r.method === 'POST' && r.url === '/api/prices/ecb/fetch').flush({ rates: 6500, assets: ['USDT', 'USDC'], pricesStored: 13000, warning: null });
    await fixture.whenStable();

    expect(text(fixture, 'result')).toContain('6500');
    expect(text(fixture, 'result')).toContain('USDT, USDC');
  });

  it('lädt eine Datei hoch', async () => {
    const { http, create } = setup(PricesPage);
    const fixture = create();
    await fixture.whenStable();

    const input = el(fixture, 'file') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['Date,USD\n'], 'eurofxref-hist.csv')] });
    input.dispatchEvent(new Event('change'));
    const req = http.expectOne('/api/prices/ecb');
    expect((req.request.body as FormData).get('file')).toBeInstanceOf(File);
    req.flush({ rates: 1, assets: ['USDT'], pricesStored: 1, warning: null });
    await fixture.whenStable();

    expect(text(fixture, 'result')).toContain('USDT');
  });

  it('meldet Abruffehler', async () => {
    const { http, create } = setup(PricesPage);
    const fixture = create();
    await fixture.whenStable();

    (el(fixture, 'fetch') as HTMLButtonElement).click();
    http.expectOne('/api/prices/ecb/fetch').flush({ detail: 'Abruf der EZB-Kurse fehlgeschlagen: x' }, { status: 502, statusText: 'Bad Gateway' });
    await fixture.whenStable();

    expect(text(fixture, 'error')).toContain('fehlgeschlagen');
  });
});
