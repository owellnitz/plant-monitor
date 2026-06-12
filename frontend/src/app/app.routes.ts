import { Routes } from '@angular/router';
import { SensorsPage } from './sensors-page/sensors-page';
import { SensorDetailPage } from './sensor-detail-page/sensor-detail-page';

export const routes: Routes = [
  { path: '', component: SensorsPage },
  { path: 'sensor/:deviceId', component: SensorDetailPage },
  { path: '**', redirectTo: '' },
];
