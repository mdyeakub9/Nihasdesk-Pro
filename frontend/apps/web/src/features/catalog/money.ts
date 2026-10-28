/** Prices are integer cents in the API (ADR 0018); people type "89.00" or "89,00". */

/** "89", "89.5", "89,50" → 8950; empty → null; anything else → undefined (invalid). */
export function parseMoney(text: string): number | null | undefined {
  const value = text.trim();
  if (!value) return null;
  const normalized = value.includes('.') ? value : value.replace(',', '.');
  if (!/^\d{1,8}(\.\d{1,2})?$/.test(normalized)) return undefined;
  const [whole = '0', fraction = ''] = normalized.split('.');
  return Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
}

/** 8950 → "89.50" for an input field. */
export function moneyInput(cents: number | null | undefined): string {
  if (cents === null || cents === undefined) return '';
  return `${Math.floor(cents / 100)}.${String(cents % 100).padStart(2, '0')}`;
}

/** 8950, EUR → "€89.50" in the UI language. */
export function formatMoney(cents: number, currency: string, language: string): string {
  return new Intl.NumberFormat(language, { style: 'currency', currency }).format(cents / 100);
}
