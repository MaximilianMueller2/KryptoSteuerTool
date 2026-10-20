import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, throwError } from 'rxjs';

/** Entspricht `HealthResponse` der API (siehe /openapi/v1.json). */
export interface HealthResponse {
  status: 'Healthy' | 'Unhealthy';
  database: 'Up' | 'Down';
  version: string;
  timeUtc: string;
}

/**
 * Schlanker Zugriff auf /api/health.
 *
 * TODO: Sobald der Client aus der OpenAPI-Spezifikation generiert ist (`npm run generate:api`,
 * siehe README), diese Datei durch den generierten Client ersetzen.
 */
@Injectable({ providedIn: 'root' })
export class HealthApi {
  private readonly http = inject(HttpClient);

  getHealth(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>('/api/health').pipe(
      // Die API antwortet bei fehlender Datenbank mit 503 und trotzdem mit einem Health-Body.
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 503 && error.error
          ? of(error.error as HealthResponse)
          : throwError(() => error),
      ),
    );
  }
}
