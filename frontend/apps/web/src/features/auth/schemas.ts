import type { TFunction } from 'i18next';
import { z } from 'zod';

/** Same rules as the API (ADR 0009): 12–128 characters, no composition rules. */
export const passwordMin = 12;
export const passwordMax = 128;

export function emailField(t: TFunction) {
  return z.string().trim().min(1, t('validation.required')).max(256).email(t('validation.email'));
}

export function newPasswordField(t: TFunction) {
  return z
    .string()
    .min(passwordMin, t('validation.passwordLength'))
    .max(passwordMax, t('validation.passwordLength'));
}

export function requiredText(t: TFunction, max: number) {
  return z.string().trim().min(1, t('validation.required')).max(max);
}
