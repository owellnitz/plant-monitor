import { Component } from '@angular/core';
import { ReadingsPage } from './readings-page/readings-page';

@Component({
  selector: 'app-root',
  imports: [ReadingsPage],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {}
