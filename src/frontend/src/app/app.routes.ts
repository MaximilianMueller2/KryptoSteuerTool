import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./overview/overview-page').then((m) => m.OverviewPage),
    title: 'Steuerjahr – Krypto-Steuer',
  },
  {
    path: 'konten',
    loadComponent: () => import('./accounts/accounts-page').then((m) => m.AccountsPage),
    title: 'Konten – Krypto-Steuer',
  },
  {
    path: 'import',
    loadComponent: () => import('./imports/import-page').then((m) => m.ImportPage),
    title: 'Import – Krypto-Steuer',
  },
  {
    path: 'pruefung',
    loadComponent: () => import('./review/review-page').then((m) => m.ReviewPage),
    title: 'Prüfung – Krypto-Steuer',
  },
  {
    path: 'kurse',
    loadComponent: () => import('./prices/prices-page').then((m) => m.PricesPage),
    title: 'Kurse – Krypto-Steuer',
  },
  {
    path: 'status',
    loadComponent: () => import('./health/health-page').then((m) => m.HealthPage),
    title: 'Status – Krypto-Steuer',
  },
  { path: '**', redirectTo: '' },
];
