import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

// rxResource loads in an effect after change detection; a macrotask lets it run.
const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  // whenStable() waits for pending requests, so the version request must be
  // answered first.
  async function flushVersion(version = '0.0.0-dev') {
    await tick();
    TestBed.inject(HttpTestingController).expectOne('/api/version').flush({ version });
  }

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render title', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await flushVersion();
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Plant Monitor');
  });

  it('shows the app version in the footer once loaded', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('footer')).toBeNull();

    await flushVersion('1.2.3');
    await fixture.whenStable();

    expect(compiled.querySelector('footer')?.textContent).toContain('v1.2.3');
  });
});
