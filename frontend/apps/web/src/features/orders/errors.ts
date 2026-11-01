import type { TFunction } from 'i18next';
import { authErrorMessage, isErrorCode } from '../auth/errors';

const known = [
  'product.not_available',
  'product.not_found',
  'order.zone_not_found',
  'order.mixed_currency',
  'order.not_pending',
];

/** Server error codes the team can act on, in plain words (ADR 0025). */
export function orderErrorMessage(error: unknown, t: TFunction) {
  const code = known.find((c) => isErrorCode(error, c));
  return code ? t(`orders.errors.${code.replace('.', '_')}`) : authErrorMessage(error, t);
}
