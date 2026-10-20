import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage } from './ks-api';

describe('errorMessage', () => {
  it('nimmt den Text aus der Problem-Antwort der API', () => {
    const error = new HttpErrorResponse({ status: 400, error: { detail: 'Name und Plattform sind Pflicht.' } });
    expect(errorMessage(error)).toBe('Name und Plattform sind Pflicht.');
  });

  it('meldet fehlende Verbindung', () => {
    expect(errorMessage(new HttpErrorResponse({ status: 0 }))).toBe('API nicht erreichbar.');
  });

  it('fällt bei unbekannten Fehlern auf einen Standardtext zurück', () => {
    expect(errorMessage(new Error('x'))).toBe('Die Anfrage ist fehlgeschlagen.');
  });
});
