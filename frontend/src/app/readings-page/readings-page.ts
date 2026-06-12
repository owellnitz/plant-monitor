import { DatePipe } from '@angular/common';
import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { PlantApi } from '../plant-api';
import { Reading } from '../reading';

@Component({
  selector: 'app-readings-page',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  templateUrl: './readings-page.html',
})
export class ReadingsPage {
  private readonly api = inject(PlantApi);

  protected readonly sensors = signal<string[]>([]);
  protected readonly readings = signal<Reading[]>([]);
  protected readonly selectedDevice = signal('');

  constructor() {
    this.api.getSensors().subscribe((sensors) => this.sensors.set(sensors));
    this.loadReadings();
  }

  protected onFilterChange(deviceId: string): void {
    this.selectedDevice.set(deviceId);
    this.loadReadings();
  }

  private loadReadings(): void {
    this.api
      .getReadings(this.selectedDevice() || undefined)
      .subscribe((readings) => this.readings.set(readings));
  }
}
