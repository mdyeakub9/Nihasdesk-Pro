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
  'channel.not_configured': 'channelSettings.errors.notConfigured',
  'channel.not_found': 'channelSettings.errors.notFound',
  'catalog.product_not_found': 'products.errors.notFound',
  'ai.not_configured': 'aiSettings.notConfigured',
  'ai.test_limit': 'aiTest.limit',
  'ai.question_resolved': 'aiSettings.questions.resolved',
  'ai.not_found': 'aiSettings.questions.notFound',
  'catalog.sku_taken': 'products.errors.skuTaken',
  'catalog.handle_taken': 'products.errors.handleTaken',
  'catalog.import_no_file': 'products.import.errors.noFile',
  'catalog.import_too_large': 'products.import.tooLarge',
  'catalog.import_not_csv': 'products.import.errors.notCsv',
  'source.no_file': 'sources.errors.noFile',
  'source.too_large': 'sources.errors.tooLarge',
  'source.unsupported_type': 'sources.errors.unsupported',
  'source.limit_reached': 'sources.errors.limit',
  'source.duplicate': 'sources.errors.duplicate',
  'source.invalid_url': 'sources.errors.invalidUrl',
  'source.blocked_url': 'sources.errors.blockedUrl',
  'source.unreachable_url': 'sources.errors.unreachableUrl',
  'source.not_found': 'sources.errors.notFound',
  'source.no_next_section': 'sources.errors.noNextSection',
  'source.section_too_long': 'sources.errors.sectionTooLong',
  'source.not_ready': 'sources.errors.notReady',
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
