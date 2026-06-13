import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { RefreshService } from '../refresh';

const THRESHOLD = 70; // px pulled past the top before a release refreshes
const MAX = 90; // px the indicator travels at most
const SPIN_MS = 800; // how long the spinner shows after a refresh

/**
 * Pull-to-refresh for the standalone PWA, where the browser's native gesture is
 * gone. At the top of any page, drag down past a threshold to refresh the
 * current page's data (via RefreshService). A floating indicator follows the pull.
 */
@Component({
  selector: 'app-pull-to-refresh',
  template: `
    <div
      class="ptr"
      [class.ptr--smooth]="!dragging()"
      [style.transform]="'translateY(' + pull() + 'px)'"
      [style.opacity]="progress()"
    >
      <span
        class="ptr__icon"
        [class.ptr__icon--armed]="armed()"
        [class.ptr__icon--spin]="refreshing()"
        aria-hidden="true"
        >↻</span
      >
    </div>
  `,
  styles: `
    .ptr {
      position: fixed;
      top: -2.5rem;
      left: 0;
      right: 0;
      z-index: 50;
      display: flex;
      justify-content: center;
      pointer-events: none;
    }
    .ptr--smooth {
      transition:
        transform 0.25s ease,
        opacity 0.25s ease;
    }
    .ptr__icon {
      display: grid;
      place-items: center;
      width: 2rem;
      height: 2rem;
      border-radius: 9999px;
      background: var(--color-base-100);
      box-shadow: 0 1px 6px rgb(0 0 0 / 0.15);
      color: var(--color-primary);
      font-size: 1.1rem;
      transition: transform 0.2s ease;
    }
    .ptr__icon--armed {
      transform: rotate(180deg);
    }
    .ptr__icon--spin {
      animation: ptr-spin 0.7s linear infinite;
    }
    @keyframes ptr-spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
})
export class PullToRefresh {
  private readonly refresh = inject(RefreshService);

  protected readonly pull = signal(0);
  protected readonly dragging = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly armed = computed(() => this.pull() >= THRESHOLD);
  protected readonly progress = computed(() => Math.min(1, this.pull() / THRESHOLD));

  private startY = 0;
  private tracking = false;

  private get atTop(): boolean {
    return (document.scrollingElement?.scrollTop ?? 0) <= 0;
  }

  private get standalone(): boolean {
    return (
      matchMedia('(display-mode: standalone)').matches ||
      (navigator as unknown as { standalone?: boolean }).standalone === true
    );
  }

  @HostListener('window:touchstart', ['$event'])
  onTouchStart(event: TouchEvent): void {
    this.tracking = this.standalone && !this.refreshing() && this.atTop;
    if (this.tracking) {
      this.startY = event.touches[0].clientY;
      this.dragging.set(true);
    }
  }

  @HostListener('window:touchmove', ['$event'])
  onTouchMove(event: TouchEvent): void {
    if (!this.tracking) {
      return;
    }
    const delta = event.touches[0].clientY - this.startY;
    // A scroll has started, or the user is pulling up — let the page move.
    this.pull.set(delta > 0 && this.atTop ? Math.min(MAX, delta * 0.5) : 0);
  }

  @HostListener('window:touchend')
  onTouchEnd(): void {
    if (!this.tracking) {
      return;
    }
    this.tracking = false;
    this.dragging.set(false);
    if (this.pull() >= THRESHOLD) {
      this.trigger();
    } else {
      this.pull.set(0);
    }
  }

  private trigger(): void {
    this.refreshing.set(true);
    this.pull.set(THRESHOLD); // hold the indicator visible while refreshing
    this.refresh.refresh();
    setTimeout(() => {
      this.refreshing.set(false);
      this.pull.set(0);
    }, SPIN_MS);
  }
}
