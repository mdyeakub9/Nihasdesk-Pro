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
  'auth.invalid_mfa_code': 'authErrors.invalidMfaCode',
  'auth.mfa_challenge_expired': 'authErrors.mfaChallengeExpired',
  'auth.reauth_required': 'authErrors.reauthRequired',
  'team.forbidden': 'teamErrors.forbidden',
  'team.not_found': 'teamErrors.notFound',
  'team.already_member': 'teamErrors.alreadyMember',
  'team.owner_is_fixed': 'teamErrors.ownerIsFixed',
  'team.invitation_invalid': 'teamErrors.invitationInvalid',
  'team.email_has_account': 'teamErrors.emailHasAccount',
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
