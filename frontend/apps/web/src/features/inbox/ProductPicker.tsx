import { Button, Checkbox, FieldError, Input } from '@nihasdesk/ui';
import { Search, X } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatPrice } from '../../lib/format';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { useProduct, useProducts, type Variant } from '../catalog/api';
import { ProductThumb } from '../catalog/ProductImages';
import { useSendProducts } from './api';

const maxItems = 5;

type Picked = { productId: string; variantId: string; label: string };

function variantName(variant: Variant) {
  return [variant.option1, variant.option2, variant.option3].filter(Boolean).join(' / ');
}

/** One product's variants that can be ordered now; the others are not offered (PRD 4.4). */
function ProductVariants({
  productId,
  title,
  picked,
  onToggle,
}: {
  productId: string;
  title: string;
  picked: Picked[];
  onToggle: (item: Picked) => void;
}) {
  const { t, i18n } = useTranslation();
  const { data: product, isPending } = useProduct(productId);
  const language = i18n.resolvedLanguage ?? 'en';
  if (isPending)
    return <p className="px-3 py-1 text-xs text-muted-foreground">{t('actions.products.loading')}</p>;
  const available = product?.variants.filter((v) => v.available) ?? [];
  if (available.length === 0)
    return <p className="px-3 py-1 text-xs text-muted-foreground">{t('actions.products.noneInStock')}</p>;

  return (
    <ul className="flex flex-col gap-1 px-3 pb-2">
      {available.map((variant) => {
        const name = variantName(variant);
        const label = name ? `${title} — ${name}` : title;
        const checked = picked.some((p) => p.variantId === variant.id);
        const full = picked.length >= maxItems && !checked;
        return (
          <li key={variant.id} className="flex items-center gap-2 text-[13px]">
            <Checkbox
              id={`pick-${variant.id}`}
              checked={checked}
              disabled={full}
              onCheckedChange={() => onToggle({ productId, variantId: variant.id, label })}
            />
            <label htmlFor={`pick-${variant.id}`} className="flex-1">
              {name || t('actions.products.single')} ·{' '}
              {formatPrice(variant.price / 100, variant.currency, language)}
            </label>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * Send products from the catalog (PRD 4.4, ADR 0024): search, pick up to 5 in-stock variants,
 * send. Only IDs go to the server; it builds the cards with catalog prices (rule 5).
 */
export function ProductPicker({ conversationId, onClose }: { conversationId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const [picked, setPicked] = useState<Picked[]>([]);
  const { data, isPending } = useProducts(query.trim(), 'active');
  const send = useSendProducts(conversationId);
  const searchId = useId();
  const products = data?.pages.flatMap((p) => p.items) ?? [];

  function toggle(item: Picked) {
    setPicked((current) =>
      current.some((p) => p.variantId === item.variantId)
        ? current.filter((p) => p.variantId !== item.variantId)
        : [...current, item],
    );
  }

  return (
    <section
      aria-label={t('actions.products.title')}
      className="flex flex-col gap-2 rounded-md border bg-surface p-3"
      onKeyDown={(event) => {
        if (event.key === 'Escape') onClose();
      }}
    >
      <div className="flex items-center gap-2">
        <h3 className="flex-1 text-sm font-medium">{t('actions.products.title')}</h3>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="size-7"
          aria-label={t('actions.close')}
          onClick={onClose}
        >
          <X aria-hidden="true" />
        </Button>
      </div>
      <div className="relative">
        <Search className="absolute top-2.5 left-2.5 size-4 text-muted-foreground" aria-hidden="true" />
        <label htmlFor={searchId} className="sr-only">
          {t('actions.products.search')}
        </label>
        <Input
          id={searchId}
          type="search"
          autoFocus
          className="h-9 pl-8"
          placeholder={t('actions.products.search')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          // The picker sits inside the composer's form: Enter must not send the reply.
          onKeyDown={(e) => e.key === 'Enter' && e.preventDefault()}
        />
      </div>
      <ul aria-label={t('actions.products.results')} className="max-h-56 overflow-y-auto rounded-md border">
        {isPending && (
          <li className="px-3 py-2 text-xs text-muted-foreground">{t('actions.products.loading')}</li>
        )}
        {!isPending && products.length === 0 && (
          <li className="px-3 py-2 text-xs text-muted-foreground">{t('actions.products.empty')}</li>
        )}
        {products.map((product) => (
          <li key={product.id} className="border-b last:border-b-0">
            <button
              type="button"
              aria-expanded={open === product.id}
              className="flex w-full items-center gap-2 px-3 py-2 text-left text-[13px] font-medium hover:bg-subtle"
              onClick={() => setOpen(open === product.id ? null : product.id)}
            >
              <ProductThumb url={product.thumbnailUrl} className="size-8" />
              <span className="min-w-0 flex-1 truncate">{product.title}</span>
            </button>
            {open === product.id && (
              <ProductVariants
                productId={product.id}
                title={product.title}
                picked={picked}
                onToggle={toggle}
              />
            )}
          </li>
        ))}
      </ul>
      {picked.length > 0 && (
        <p className="text-xs text-secondary-foreground">
          {t('actions.products.picked', { count: picked.length, max: maxItems })}:{' '}
          {picked.map((p) => p.label).join(', ')}
        </p>
      )}
      {send.error && (
        <FieldError>
          {isErrorCode(send.error, 'product.not_available')
            ? t('actions.products.notAvailable')
            : isErrorCode(send.error, 'conversation.outside_window')
              ? t('composer.outsideWindow')
              : authErrorMessage(send.error, t)}
        </FieldError>
      )}
      <div className="flex justify-end">
        <Button
          type="button"
          size="sm"
          disabled={picked.length === 0 || send.isPending}
          onClick={() =>
            send.mutate(
              picked.map(({ productId, variantId }) => ({ productId, variantId })),
              { onSuccess: onClose },
            )
          }
        >
          {t('actions.products.send', { count: picked.length })}
        </Button>
      </div>
    </section>
  );
}
