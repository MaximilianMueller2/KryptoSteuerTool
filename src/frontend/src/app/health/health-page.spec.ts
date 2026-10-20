import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HealthPage } from './health-page';

describe('HealthPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HealthPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function text(fixture: { nativeElement: unknown }, testId: string): string {
    const el = (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);
    return el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  it('ruft /api/health auf und zeigt den Status', async () => {
    const fixture = TestBed.createComponent(HealthPage);
    await fixture.whenStable();

    http.expectOne('/api/health').flush({
      status: 'Healthy',
      database: 'Up',
      version: '1.2.3',
      timeUtc: '2026-10-20T09:00:00+00:00',
    });
    await fixture.whenStable();

    expect(text(fixture, 'api-status')).toContain('Healthy');
    expect(text(fixture, 'db-status')).toContain('Up');
  });

  it('zeigt auch bei 503 den Datenbankstatus aus dem Body', async () => {
    const fixture = TestBed.createComponent(HealthPage);
    await fixture.whenStable();

    http.expectOne('/api/health').flush(
      { status: 'Unhealthy', database: 'Down', version: '1.2.3', timeUtc: '2026-10-20T09:00:00+00:00' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();

    expect(text(fixture, 'api-status')).toContain('Unhealthy');
    expect(text(fixture, 'db-status')).toContain('Down');
  });

  it('meldet "nicht erreichbar" bei Netzwerkfehlern', async () => {
    const fixture = TestBed.createComponent(HealthPage);
    await fixture.whenStable();

    http.expectOne('/api/health').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(text(fixture, 'api-status')).toContain('nicht erreichbar');
  });
});
