import { Routes } from '@angular/router';
import { SensorsPage } from './sensors-page/sensors-page';

export const routes: Routes = [
  { path: '', component: SensorsPage },
  { path: '**', redirectTo: '' },
];
