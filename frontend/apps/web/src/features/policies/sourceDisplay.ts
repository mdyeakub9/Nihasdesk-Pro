import type { BadgeProps } from '@nihasdesk/ui';
import { useTranslation } from 'react-i18next';
import type { SourceStatus } from './api';

export const statusTone: Record<SourceStatus, BadgeProps['tone']> = {
  reading: 'neutral',
  review: 'info',
  live: 'success',
  failed: 'warning',
  archived: 'neutral',
};

/** A warning code in plain words: `pages_without_text:3,4`, `no_embeddings`. */
export function useWarningText() {
  const { t } = useTranslation();
  return (code: string) => {
    const [name, detail] = code.split(':');
    if (name === 'pages_without_text')
      return t('sources.warnings.pagesWithoutText', { pages: (detail ?? '').replaceAll(',', ', ') });
    if (name === 'no_embeddings') return t('sources.warnings.noEmbeddings');
    return code;
  };
}
