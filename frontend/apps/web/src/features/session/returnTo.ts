/** Only same-app paths, so a crafted link can't send users elsewhere after login (open redirect). */
export function safeReturnTo(value: string | null): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\')
    ? value
    : '/inbox';
}
