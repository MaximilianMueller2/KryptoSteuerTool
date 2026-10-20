import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./health/health-page').then((m) => m.HealthPage),
    title: 'Krypto-Steuer',
  },
  { path: '**', redirectTo: '' },
];
