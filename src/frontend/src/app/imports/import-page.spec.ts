import { ImportPage } from './import-page';
import { el, setup, text } from '../testing';

const account = { id: 'a1', name: 'Trade Republic', type: 'Broker', platform: 'TradeRepublic', country: 'DE', subAccount: null };
const run = (status: string) => ({
  id: 'i1', source: 'traderepublic-csv', fileName: 'export.csv', accountId: 'a1', status,
  createdAtUtc: '2026-10-20T10:00:00Z', committedAtUtc: null,
  report: { rowsRead: 753, new: 773, duplicates: 0, needsReview: 13, errors: [], unknownTypes: [], warnings: ['Hinweis'] },
});

describe('ImportPage', () => {
  it('bietet ohne Konto Voreinstellungen an und legt das Konto an', async () => {
    const { http, create } = setup(ImportPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([]);
    await fixture.whenStable();

    expect(el(fixture, 'no-accounts')).not.toBeNull();
    (fixture.nativeElement as HTMLElement).querySelector('button')?.click();
    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/accounts');
    expect(post.request.body.platform).toBe('TradeRepublic');
    post.flush(account);
    http.expectOne((r) => r.method === 'GET' && r.url === '/api/accounts').flush([account]);
    await fixture.whenStable();

    expect(el(fixture, 'no-accounts')).toBeNull();
  });

  it('erzeugt die Vorschau und schreibt sie nach Bestätigung fest', async () => {
    const { http, create } = setup(ImportPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([account]);
    await fixture.whenStable();

    const input = el(fixture, 'file') as HTMLInputElement;
    const file = new File(['a;b'], 'export.csv', { type: 'text/csv' });
    Object.defineProperty(input, 'files', { value: [file] });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    (el(fixture, 'preview') as HTMLButtonElement).click();
    const preview = http.expectOne('/api/imports');
    expect((preview.request.body as FormData).get('accountId')).toBe('a1');
    expect((preview.request.body as FormData).has('source')).toBe(false);
    preview.flush(run('Preview'));
    await fixture.whenStable();

    expect(text(fixture, 'report')).toContain('773');
    expect(el(fixture, 'committed')).toBeNull();

    (el(fixture, 'commit') as HTMLButtonElement).click();
    http.expectOne('/api/imports/i1/commit').flush(run('Committed'));
    await fixture.whenStable();

    expect(el(fixture, 'committed')).not.toBeNull();
    expect(el(fixture, 'commit')).toBeNull();
  });

  it('verwirft eine Vorschau', async () => {
    const { http, create } = setup(ImportPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([account]);
    await fixture.whenStable();
    const input = el(fixture, 'file') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'x.csv')] });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    (el(fixture, 'preview') as HTMLButtonElement).click();
    http.expectOne('/api/imports').flush(run('Preview'));
    await fixture.whenStable();

    (el(fixture, 'discard') as HTMLButtonElement).click();
    http.expectOne((r) => r.method === 'DELETE' && r.url === '/api/imports/i1').flush(null);
    await fixture.whenStable();

    expect(el(fixture, 'report')).toBeNull();
  });

  it('zeigt Fehler der Vorschau', async () => {
    const { http, create } = setup(ImportPage);
    const fixture = create();
    http.expectOne('/api/accounts').flush([account]);
    await fixture.whenStable();
    const input = el(fixture, 'file') as HTMLInputElement;
    Object.defineProperty(input, 'files', { value: [new File(['x'], 'x.csv')] });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    (el(fixture, 'preview') as HTMLButtonElement).click();
    http.expectOne('/api/imports').flush({ detail: 'Kein Importer erkennt diese Datei.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(text(fixture, 'error')).toContain('Kein Importer');
  });
});
