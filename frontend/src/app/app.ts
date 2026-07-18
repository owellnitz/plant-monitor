import { Component, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink, RouterOutlet } from '@angular/router';
import { PlantApi } from './plant-api';
import { PullToRefresh } from './pull-to-refresh/pull-to-refresh';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, PullToRefresh],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly api = inject(PlantApi);

  // Footer version; the footer stays hidden until loaded (and on error).
  protected readonly version = rxResource({
    stream: () => this.api.getVersion(),
  });
}
