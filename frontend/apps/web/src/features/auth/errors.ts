import type { TFunction } from 'i18next';
import { ApiError } from '../../lib/api';

/** API error codes (docs/standards/errors.md) → translation keys. */
const messages: Record<string, string> = {
  'auth.invalid_credentials': 'authErrors.invalidCredentials',
  'auth.email_not_verified': 'authErrors.emailNotVerified',
  'auth.invalid_token': 'authErrors.invalidToken',
  'auth.no_workspace': 'authErrors.noWorkspace',
  'auth.weak_password': 'authErrors.weakPassword',
  'auth.breached_password': 'authErrors.breachedPassword',
};

export function authErrorMessage(error: unknown, t: TFunction): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return t('authErrors.rateLimited');
    const key = error.code ? messages[error.code] : undefined;
    if (key) return t(key);
  }
  return t('authErrors.generic');
}

export function isErrorCode(error: unknown, code: string): boolean {
  return error instanceof ApiError && error.code === code;
}
