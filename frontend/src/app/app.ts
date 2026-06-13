import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { PullToRefresh } from './pull-to-refresh/pull-to-refresh';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, PullToRefresh],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
