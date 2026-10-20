import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { KsApi, errorMessage } from '../api/ks-api';
import { DisposalReport, TaxRunResult } from '../api/models';

@Component({
  selector: 'ks-overview-page',
  imports: [DecimalPipe, DatePipe, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatInputModule],
  template: `
    <h1 i18n="@@overview.title">Steuerjahr</h1>

    <form class="controls" (ngSubmit)="run()">
      <mat-form-field>
        <mat-label i18n="@@overview.year">Steuerjahr</mat-label>
        <input matInput type="number" name="year" [ngModel]="year()" (ngModelChange)="year.set($event)" min="2009" max="2100" />
      </mat-form-field>
      <mat-form-field>
        <mat-label i18n="@@overview.loss">Verlustvortrag aus Vorjahr (€)</mat-label>
        <input matInput type="number" name="loss" [ngModel]="lossCarry()" (ngModelChange)="lossCarry.set($event)" min="0" step="0.01" />
      </mat-form-field>
      <mat-form-field>
        <mat-label i18n="@@overview.other">Andere private Veräußerungsgeschäfte (€)</mat-label>
        <input matInput type="number" name="other" [ngModel]="otherSales()" (ngModelChange)="otherSales.set($event)" step="0.01" />
      </mat-form-field>
      <mat-checkbox name="reform" [ngModel]="reform()" (ngModelChange)="reform.set($event)" i18n="@@overview.reform">
        Reform-Entwurf 2027 anwenden (nicht beschlossen)
      </mat-checkbox>
      <button matButton="filled" type="submit" [disabled]="busy()" data-testid="run" i18n="@@overview.run">Berechnen</button>
    </form>

    @if (error(); as e) {
      <p class="error" role="alert" data-testid="error">{{ e }}</p>
    }

    @if (result(); as r) {
      @if (r.isDraft) {
        <p class="draft" data-testid="draft">
          <ng-container i18n="@@overview.draft">Entwurf: Es gibt offene Befunde.</ng-container>
          {{ ' ' }}<a routerLink="/pruefung">{{ r.counts.findings }} <ng-container i18n="@@overview.toReview">Befunde prüfen</ng-container></a>
        </p>
      } @else {
        <p class="ok" data-testid="final" i18n="@@overview.final">Keine offenen Befunde.</p>
      }

      <section class="cards">
        <mat-card>
          <mat-card-header><mat-card-title i18n="@@overview.s23">§ 23 EStG – private Veräußerungsgeschäfte</mat-card-title></mat-card-header>
          <mat-card-content>
            <dl>
              <dt i18n="@@overview.proceeds">Erlöse</dt><dd>{{ r.summary.section23.proceedsEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.cost">Anschaffungskosten</dt><dd>{{ r.summary.section23.costEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.fees">Werbungskosten (Gebühren)</dt><dd>{{ r.summary.section23.feesEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.gains">Gewinne</dt><dd>{{ r.summary.section23.gainsEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.losses">Verluste</dt><dd>{{ r.summary.section23.lossesEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.net">Saldo</dt><dd data-testid="net">{{ r.summary.section23.netEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.taxable">Steuerpflichtig</dt><dd data-testid="taxable"><strong>{{ r.summary.section23.taxableEur | number: '1.2-2' }} €</strong></dd>
            </dl>
            @if (r.summary.section23.belowFreigrenze) {
              <p class="hint" i18n="@@overview.below">Unter der Freigrenze von {{ r.summary.section23.freigrenzeEur | number: '1.0-0' }} €: steuerfrei.</p>
            } @else {
              <p class="hint" i18n="@@overview.above">Freigrenze von {{ r.summary.section23.freigrenzeEur | number: '1.0-0' }} € überschritten: der gesamte Gewinn ist steuerpflichtig.</p>
            }
          </mat-card-content>
        </mat-card>

        <mat-card>
          <mat-card-header><mat-card-title i18n="@@overview.s22">§ 22 Nr. 3 EStG – sonstige Einkünfte</mat-card-title></mat-card-header>
          <mat-card-content>
            <dl>
              <dt i18n="@@overview.income">Einnahmen</dt><dd>{{ r.summary.section22.incomeEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.taxable22">Steuerpflichtig</dt><dd><strong>{{ r.summary.section22.taxableEur | number: '1.2-2' }} €</strong></dd>
            </dl>
            <p class="hint" i18n="@@overview.s22hint">Freigrenze {{ r.summary.section22.freigrenzeEur | number: '1.0-0' }} €.</p>
          </mat-card-content>
        </mat-card>

        <mat-card>
          <mat-card-header><mat-card-title i18n="@@overview.futures">Termingeschäfte (Futures)</mat-card-title></mat-card-header>
          <mat-card-content>
            <dl>
              <dt i18n="@@overview.futuresNet">Saldo</dt><dd data-testid="futures-net">{{ r.summary.futures.netEur | number: '1.2-2' }} €</dd>
              <dt i18n="@@overview.records">Buchungen</dt><dd>{{ r.summary.futures.records }}</dd>
            </dl>
            <p class="hint" i18n="@@overview.futuresHint">Ohne EUR-Kurse (EZB) fehlen USDT-Buchungen in dieser Summe; siehe Kurse.</p>
          </mat-card-content>
        </mat-card>
      </section>

      <p class="meta">
        <span i18n="@@overview.rules">Regelsatz</span> {{ r.ruleSet }} ·
        <span i18n="@@overview.hash">Eingabe-Hash</span> {{ r.inputHash.slice(0, 12) }} ·
        {{ r.createdAtUtc | date: 'medium' }}
        @if (r.inputUnchanged) { · <span i18n="@@overview.unchanged">unverändert gegenüber dem letzten Lauf</span> }
      </p>
      @if (r.diffToPrevious && r.diffToPrevious.length > 0) {
        <details data-testid="diff">
          <summary i18n="@@overview.diff">{{ r.diffToPrevious.length }} Änderungen gegenüber dem letzten Lauf</summary>
          <ul>
            @for (d of r.diffToPrevious; track d.path) {
              <li>{{ d.path }}: {{ d.before ?? '–' }} → {{ d.after ?? '–' }}</li>
            }
          </ul>
        </details>
      }

      <h2 i18n="@@overview.disposals">Einzelaufstellung der Veräußerungen</h2>
      <p>
        <a matButton href="{{ csvUrl() }}" download data-testid="csv" i18n="@@overview.csv">Als CSV herunterladen</a>
      </p>
      @if (report(); as rep) {
        @if (rep.rows.length === 0) {
          <p i18n="@@overview.noDisposals">Keine Veräußerungen in diesem Jahr.</p>
        } @else {
          <div class="table-wrap">
            <table data-testid="disposals">
              <thead>
                <tr>
                  <th i18n="@@t.asset">Asset</th><th i18n="@@t.account">Konto</th><th class="num" i18n="@@t.qty">Menge</th>
                  <th i18n="@@t.acquired">Angeschafft</th><th i18n="@@t.disposed">Veräußert</th><th class="num" i18n="@@t.days">Tage</th>
                  <th class="num" i18n="@@t.cost">Kosten €</th><th class="num" i18n="@@t.proceeds">Erlös €</th><th class="num" i18n="@@t.gain">Gewinn €</th>
                  <th i18n="@@t.taxable">Steuerpflichtig</th>
                </tr>
              </thead>
              <tbody>
                @for (row of rep.rows; track $index) {
                  <tr [class.free]="!row.taxable">
                    <td>{{ row.asset }}</td><td>{{ row.account }}</td><td class="num">{{ row.quantity | number: '1.0-8' }}</td>
                    <td>{{ row.acquired | date: 'dd.MM.yyyy' }}</td><td>{{ row.disposed | date: 'dd.MM.yyyy' }}</td><td class="num">{{ row.holdingDays }}</td>
                    <td class="num">{{ row.costEur | number: '1.2-2' }}</td><td class="num">{{ row.proceedsEur | number: '1.2-2' }}</td>
                    <td class="num">{{ row.gainEur | number: '1.2-2' }}</td>
                    <td>{{ row.taxable ? 'ja' : 'nein' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    }
  `,
  styles: `
    .controls { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
    .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 16px; margin: 16px 0; }
    dl { display: grid; grid-template-columns: 1fr auto; gap: 4px 16px; margin: 0; }
    dd { margin: 0; text-align: right; font-variant-numeric: tabular-nums; }
    .hint, .meta { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
    .error { color: var(--mat-sys-error); }
    .draft { background: var(--mat-sys-tertiary-container); color: var(--mat-sys-on-tertiary-container); padding: 8px 12px; border-radius: 8px; }
    .ok { color: var(--mat-sys-primary); }
    .table-wrap { overflow-x: auto; }
    table { border-collapse: collapse; width: 100%; font: var(--mat-sys-body-medium); }
    th, td { padding: 6px 10px; border-bottom: 1px solid var(--mat-sys-outline-variant); text-align: left; white-space: nowrap; }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
    tr.free { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class OverviewPage {
  private readonly api = inject(KsApi);

  protected readonly year = signal(new Date().getFullYear() - 1);
  protected readonly lossCarry = signal(0);
  protected readonly otherSales = signal(0);
  protected readonly reform = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<TaxRunResult | null>(null);
  protected readonly report = signal<DisposalReport | null>(null);
  protected readonly csvUrl = computed(() => this.api.disposalsCsvUrl(this.result()?.year ?? this.year()));

  protected run(): void {
    this.busy.set(true);
    this.error.set(null);
    this.api
      .runTax({
        year: this.year(),
        applyReform2027: this.reform(),
        lossCarryForwardInEur: this.lossCarry() || 0,
        otherPrivateSalesGainEur: this.otherSales() || 0,
      })
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.api.disposals(result.year, this.reform()).subscribe({
            next: (report) => this.report.set(report),
            error: (e: unknown) => this.error.set(errorMessage(e)),
          });
          this.busy.set(false);
        },
        error: (e: unknown) => {
          this.error.set(errorMessage(e));
          this.busy.set(false);
        },
      });
  }
}
