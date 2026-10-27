import { describe, expect, it } from 'vitest';
import { formatAge, formatPrice } from './format';

const now = Date.parse('2026-10-26T15:00:00Z');

describe('formatAge', () => {
  it('uses minutes, hours and days', () => {
    expect(formatAge('2026-10-26T14:58:00Z', 'en', now)).toBe('2m');
    expect(formatAge('2026-10-26T12:00:00Z', 'en', now)).toBe('3h');
    expect(formatAge('2026-10-21T15:00:00Z', 'en', now)).toBe('5d');
  });

  it('never shows negative ages', () => {
    expect(formatAge('2026-10-26T15:05:00Z', 'en', now)).toBe('0m');
  });
});

describe('formatPrice', () => {
  it('formats euros per language', () => {
    expect(formatPrice(89, 'EUR', 'en')).toBe('€89.00');
    expect(formatPrice(89, 'EUR', 'it')).toMatch(/^89,00\s€$/);
  });
});
