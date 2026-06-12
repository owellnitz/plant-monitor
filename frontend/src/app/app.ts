import { Component, ChangeDetectionStrategy } from '@angular/core';
import { ReadingsPage } from './readings-page/readings-page';

@Component({
  selector: 'app-root',
  imports: [ReadingsPage],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './app.css',
})
export class App {}
