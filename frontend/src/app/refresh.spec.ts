import { TestBed } from '@angular/core/testing';
import { RefreshService } from './refresh';

describe('RefreshService', () => {
  it('increments the version on each refresh', () => {
    const service = TestBed.inject(RefreshService);

    expect(service.version()).toBe(0);
    service.refresh();
    expect(service.version()).toBe(1);
    service.refresh();
    expect(service.version()).toBe(2);
  });
});
