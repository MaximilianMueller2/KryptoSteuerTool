import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { HealthApi, HealthResponse } from '../api/health-api';

type HealthState =
  | { kind: 'loading' }
  | { kind: 'loaded'; report: HealthResponse }
  | { kind: 'unreachable' };

@Component({
  selector: 'ks-health-page',
  imports: [DatePipe, MatButtonModule, MatCardModule],
  template: `
    <mat-card>
      <mat-card-header>
        <mat-card-title i18n="@@health.title">Systemstatus</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @switch (state().kind) {
          @case ('loading') {
            <p i18n="@@health.loading">Prüfe Verbindung zur API …</p>
          }
          @case ('unreachable') {
            <p class="status" data-testid="api-status" i18n="@@health.unreachable">
              API nicht erreichbar.
            </p>
          }
          @default {
            @if (report(); as r) {
              <p class="status" data-testid="api-status">
                <ng-container i18n="@@health.api">API:</ng-container>
                {{ r.status }}
              </p>
              <p data-testid="db-status">
                <ng-container i18n="@@health.database">Datenbank:</ng-container>
                {{ r.database }}
              </p>
              <p>
                <ng-container i18n="@@health.version">Version:</ng-container>
                {{ r.version }}
              </p>
              <p>
                <ng-container i18n="@@health.serverTime">Serverzeit:</ng-container>
                {{ r.timeUtc | date: 'medium' }}
              </p>
            }
          }
        }
      </mat-card-content>
      <mat-card-actions>
        <button matButton (click)="refresh()" i18n="@@health.refresh">Erneut prüfen</button>
      </mat-card-actions>
    </mat-card>
  `,
})
export class HealthPage {
  private readonly api = inject(HealthApi);

  protected readonly state = signal<HealthState>({ kind: 'loading' });

  constructor() {
    this.refresh();
  }

  protected report(): HealthResponse | null {
    const current = this.state();
    return current.kind === 'loaded' ? current.report : null;
  }

  protected refresh(): void {
    this.state.set({ kind: 'loading' });
    this.api.getHealth().subscribe({
      next: (report) => this.state.set({ kind: 'loaded', report }),
      error: () => this.state.set({ kind: 'unreachable' }),
    });
  }
}
