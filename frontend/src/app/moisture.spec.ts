import { describe, expect, it } from 'vitest';
import { waterStatus } from './moisture';

describe('waterStatus', () => {
  it('returns null when no limits are set', () => {
    expect(waterStatus(10, null, null)).toBeNull();
    expect(waterStatus(90, null, null)).toBeNull();
  });

  it('flags must below the must-water limit', () => {
    expect(waterStatus(15, 20, 40)).toBe('must');
  });

  it('flags can between the limits', () => {
    expect(waterStatus(30, 20, 40)).toBe('can');
  });

  it('is ok at or above the can-water limit', () => {
    expect(waterStatus(40, 20, 40)).toBe('ok');
    expect(waterStatus(80, 20, 40)).toBe('ok');
  });

  it('works with only one limit set', () => {
    expect(waterStatus(10, 20, null)).toBe('must');
    expect(waterStatus(50, 20, null)).toBe('ok');
    expect(waterStatus(30, null, 40)).toBe('can');
    expect(waterStatus(50, null, 40)).toBe('ok');
  });
});
