/** Locale-aware formatting helpers; always pass the active UI language. */

/** Compact age like "2m", "3h", "5d", in the language's narrow unit style. */
export function formatAge(iso: string, language: string, now = Date.now()): string {
  const minutes = Math.max(0, Math.round((now - Date.parse(iso)) / 60_000));
  const [value, unit] =
    minutes < 60
      ? [minutes, 'minute']
      : minutes < 24 * 60
        ? [Math.round(minutes / 60), 'hour']
        : [Math.round(minutes / 1440), 'day'];
  return new Intl.NumberFormat(language, { style: 'unit', unit, unitDisplay: 'narrow' }).format(value);
}

export function formatTime(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language, { hour: '2-digit', minute: '2-digit' }).format(new Date(iso));
}

export function formatDay(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language, { day: 'numeric', month: 'long' }).format(new Date(iso));
}

export function isSameDay(a: string, b: string): boolean {
  return new Date(a).toDateString() === new Date(b).toDateString();
}

/** File size like "820 kB" or "4.2 MB" (decimal units, as file managers show them). */
export function formatSize(bytes: number, language: string): string {
  const [value, unit] =
    bytes < 1_000
      ? [bytes, 'byte']
      : bytes < 1_000_000
        ? [bytes / 1_000, 'kilobyte']
        : [bytes / 1_000_000, 'megabyte'];
  return new Intl.NumberFormat(language, {
    style: 'unit',
    unit,
    unitDisplay: 'short',
    maximumFractionDigits: unit === 'megabyte' ? 1 : 0,
  }).format(value);
}

export function formatPrice(amount: number, currency: string, language: string): string {
  return new Intl.NumberFormat(language, { style: 'currency', currency }).format(amount);
}
