import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./plants-page/plants-page').then((m) => m.PlantsPage),
  },
  {
    path: 'plant/new',
    loadComponent: () => import('./plant-form-page/plant-form-page').then((m) => m.PlantFormPage),
  },
  {
    path: 'plant/:id/edit',
    loadComponent: () => import('./plant-form-page/plant-form-page').then((m) => m.PlantFormPage),
  },
  {
    path: 'plant/:id',
    loadComponent: () =>
      import('./plant-detail-page/plant-detail-page').then((m) => m.PlantDetailPage),
  },
  {
    path: 'unassigned',
    loadComponent: () =>
      import('./unassigned-sensors-page/unassigned-sensors-page').then(
        (m) => m.UnassignedSensorsPage,
      ),
  },
  {
    path: 'sensor/:deviceId',
    loadComponent: () =>
      import('./sensor-detail-page/sensor-detail-page').then((m) => m.SensorDetailPage),
  },
  { path: '**', redirectTo: '' },
];
