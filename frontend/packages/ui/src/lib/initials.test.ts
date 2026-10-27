import { describe, expect, it } from 'vitest';
import { initials } from './initials';

describe('initials', () => {
  it('uses the first and last name', () => {
    expect(initials('Giulia Maria Rossi')).toBe('GR');
  });

  it('handles single names and extra spaces', () => {
    expect(initials('  almanaam ')).toBe('A');
    expect(initials('')).toBe('');
  });
});
