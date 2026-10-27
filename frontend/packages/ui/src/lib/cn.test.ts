import { describe, expect, it } from 'vitest';
import { cn } from './cn';

describe('cn', () => {
  it('drops falsy values', () => {
    expect(cn('px-2', false, undefined, 'py-1')).toBe('px-2 py-1');
  });

  it('lets the later Tailwind class win', () => {
    expect(cn('px-2', 'px-4')).toBe('px-4');
  });
});
