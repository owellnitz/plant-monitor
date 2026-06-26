import { limitAnnotations } from './moisture-chart';

// The component reads these from the theme's CSS variables at runtime; the tests
// pass them explicitly so the annotation logic can be checked without a canvas.
const ERROR = '#c0392b';
const WARNING = '#d9980f';

describe('limitAnnotations', () => {
  it('draws a line at each limit', () => {
    const annotations = limitAnnotations(30, 60, ERROR, WARNING);

    expect(annotations['Must water']).toMatchObject({ yMin: 30, yMax: 30 });
    expect(annotations['Can water']).toMatchObject({ yMin: 60, yMax: 60 });
  });

  it('colors must-water as error and can-water as warning', () => {
    const annotations = limitAnnotations(30, 60, ERROR, WARNING);

    expect(annotations['Must water'].borderColor).toBe(ERROR);
    expect(annotations['Can water'].borderColor).toBe(WARNING);
  });

  it('draws only the line for a set limit', () => {
    const annotations = limitAnnotations(30, null, ERROR, WARNING);

    expect(annotations['Must water']).toBeTruthy();
    expect(annotations['Can water']).toBeUndefined();
  });

  it('draws no lines when both limits are unset', () => {
    const annotations = limitAnnotations(null, null, ERROR, WARNING);

    expect(Object.keys(annotations)).toHaveLength(0);
  });
});
