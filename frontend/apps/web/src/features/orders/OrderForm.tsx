import { Button, FieldError, Input, Label, NativeSelect, Textarea } from '@nihasdesk/ui';
import { Plus, Search, Trash2 } from 'lucide-react';
import { useDeferredValue, useId, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useProduct, useProducts } from '../catalog/api';
import { formatMoney } from '../catalog/money';
import { useShippingZones } from '../policies/api';
import { useQuote, type OrderInput } from './api';
import { orderErrorMessage } from './errors';

export type DraftLine = { productId: string; variantId: string; label: string; quantity: number };

const maxQuantity = 99;

/** 1–99; the box may be empty while typing, and goes back to the last valid number on blur. */
function QuantityInput({
  id,
  value,
  onChange,
}: {
  id: string;
  value: number;
  onChange: (quantity: number) => void;
}) {
  const [text, setText] = useState(String(value));
  return (
    <Input
      id={id}
      type="number"
      inputMode="numeric"
      min={1}
      max={maxQuantity}
      className="h-8 w-16"
      value={text}
      onChange={(e) => {
        setText(e.target.value);
        const quantity = Number(e.target.value);
        if (Number.isInteger(quantity) && quantity >= 1 && quantity <= maxQuantity) onChange(quantity);
      }}
      onBlur={() => setText(String(value))}
    />
  );
}

/** Pick a variant that can be ordered now (PRD 4.9: only in-stock variants are offered). */
function AddProduct({ onAdd }: { onAdd: (line: Omit<DraftLine, 'quantity'>) => void }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const query = useDeferredValue(search.trim());
  const products = useProducts(query, 'active');
  const product = useProduct(open ?? undefined);
  const items = products.data?.pages.flatMap((p) => p.items) ?? [];
  const searchId = useId();

  return (
    <div className="flex flex-col gap-2 rounded-md border p-2">
      <div className="relative">
        <Search className="absolute top-2.5 left-2.5 size-4 text-muted-foreground" aria-hidden="true" />
        <label htmlFor={searchId} className="sr-only">
          {t('orders.form.searchProducts')}
        </label>
        <Input
          id={searchId}
          type="search"
          className="h-9 pl-8"
          placeholder={t('orders.form.searchProducts')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && e.preventDefault()}
        />
      </div>
      <ul aria-label={t('orders.form.products')} className="max-h-48 overflow-y-auto">
        {items.map((p) => (
          <li key={p.id}>
            <button
              type="button"
              aria-expanded={open === p.id}
              className="w-full rounded px-2 py-1.5 text-left text-[13px] font-medium hover:bg-subtle"
              onClick={() => setOpen(open === p.id ? null : p.id)}
            >
              {p.title}
            </button>
            {open === p.id && product.data && (
              <ul className="flex flex-col gap-1 py-1 pl-3">
                {product.data.variants.filter((v) => v.available).length === 0 && (
                  <li className="text-xs text-muted-foreground">{t('orders.form.noneInStock')}</li>
                )}
                {product.data.variants
                  .filter((v) => v.available)
                  .map((v) => {
                    const name = [v.option1, v.option2, v.option3].filter(Boolean).join(' / ');
                    const label = name ? `${p.title} — ${name}` : p.title;
                    return (
                      <li key={v.id} className="flex items-center gap-2 text-[13px]">
                        <span className="flex-1">
                          {name || t('orders.form.standard')} · {formatMoney(v.price, v.currency, language)}
                        </span>
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          aria-label={t('orders.form.add', { name: label })}
                          onClick={() => onAdd({ productId: p.id, variantId: v.id, label })}
                        >
                          <Plus aria-hidden="true" />
                        </Button>
                      </li>
                    );
                  })}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * The order form (PRD 4.9): default template fields, products with quantities, delivery zone, and
 * the total from the server's quote — the browser never computes prices (SEC-41).
 */
export function OrderForm({
  initial,
  initialLines,
  submitLabel,
  pending,
  error,
  onSubmit,
  onCancel,
}: {
  initial: Omit<OrderInput, 'items'>;
  initialLines: DraftLine[];
  submitLabel: string;
  pending: boolean;
  error: unknown;
  onSubmit: (input: OrderInput) => void;
  onCancel: () => void;
}) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const [details, setDetails] = useState(initial);
  const [lines, setLines] = useState<DraftLine[]>(initialLines);
  const { data: zones = [] } = useShippingZones();
  const items = lines.map(({ productId, variantId, quantity }) => ({ productId, variantId, quantity }));
  const quote = useQuote({ shippingZoneId: details.shippingZoneId, items });
  const id = useId();

  const field = (name: keyof typeof details) => ({
    id: `${id}-${name}`,
    className: 'h-9',
    value: (details[name] as string | null) ?? '',
    onChange: (e: { target: { value: string } }) => setDetails({ ...details, [name]: e.target.value }),
  });

  function add(line: Omit<DraftLine, 'quantity'>) {
    setLines((current) =>
      current.some((l) => l.variantId === line.variantId)
        ? current.map((l) =>
            l.variantId === line.variantId ? { ...l, quantity: Math.min(l.quantity + 1, maxQuantity) } : l,
          )
        : [...current, { ...line, quantity: 1 }],
    );
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    onSubmit({
      ...details,
      province: details.province?.trim() || null,
      notes: details.notes?.trim() || null,
      items,
    });
  }

  const ready =
    lines.length > 0 &&
    !!details.shippingZoneId &&
    ['customerName', 'phone', 'street', 'postalCode', 'city', 'country'].every((k) =>
      (details[k as keyof typeof details] as string | null)?.trim(),
    );

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-medium">{t('orders.form.products')}</legend>
        {lines.length > 0 && (
          <ul aria-label={t('orders.form.lines')} className="flex flex-col gap-1">
            {lines.map((line) => (
              <li key={line.variantId} className="flex items-center gap-2 text-[13px]">
                <span className="flex-1">{line.label}</span>
                <label className="sr-only" htmlFor={`${id}-qty-${line.variantId}`}>
                  {t('orders.form.quantity', { name: line.label })}
                </label>
                <QuantityInput
                  id={`${id}-qty-${line.variantId}`}
                  value={line.quantity}
                  onChange={(quantity) =>
                    setLines((current) =>
                      current.map((l) => (l.variantId === line.variantId ? { ...l, quantity } : l)),
                    )
                  }
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="size-8"
                  aria-label={t('orders.form.remove', { name: line.label })}
                  onClick={() => setLines((current) => current.filter((l) => l.variantId !== line.variantId))}
                >
                  <Trash2 aria-hidden="true" />
                </Button>
              </li>
            ))}
          </ul>
        )}
        <AddProduct onAdd={add} />
      </fieldset>

      <fieldset className="grid gap-3 sm:grid-cols-2">
        <legend className="mb-1 text-sm font-medium">{t('orders.form.customer')}</legend>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-customerName`}>{t('orders.form.name')}</Label>
          <Input {...field('customerName')} autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-phone`}>{t('orders.form.phone')}</Label>
          <Input {...field('phone')} type="tel" autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor={`${id}-street`}>{t('orders.form.street')}</Label>
          <Input {...field('street')} autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-postalCode`}>{t('orders.form.postalCode')}</Label>
          <Input {...field('postalCode')} autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-city`}>{t('orders.form.city')}</Label>
          <Input {...field('city')} autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-province`}>{t('orders.form.province')}</Label>
          <Input {...field('province')} autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`${id}-country`}>{t('orders.form.country')}</Label>
          <Input {...field('country')} maxLength={2} autoComplete="off" />
        </div>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor={`${id}-zone`}>{t('orders.form.zone')}</Label>
          <NativeSelect
            id={`${id}-zone`}
            className="h-9"
            value={details.shippingZoneId}
            onChange={(e) => setDetails({ ...details, shippingZoneId: e.target.value })}
          >
            <option value="">{t('orders.form.chooseZone')}</option>
            {zones.map((zone) => (
              <option key={zone.id} value={zone.id}>
                {zone.name}
              </option>
            ))}
          </NativeSelect>
          {zones.length === 0 && <p className="text-xs text-muted-foreground">{t('orders.form.noZones')}</p>}
        </div>
        <div className="flex flex-col gap-1 sm:col-span-2">
          <Label htmlFor={`${id}-notes`}>{t('orders.form.notes')}</Label>
          <Textarea
            id={`${id}-notes`}
            rows={2}
            value={details.notes ?? ''}
            onChange={(e) => setDetails({ ...details, notes: e.target.value })}
          />
        </div>
      </fieldset>

      <section
        aria-label={t('orders.form.summary')}
        aria-live="polite"
        className="rounded-md bg-subtle px-3 py-2 text-[13px]"
      >
        {quote.data ? (
          <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-1">
            <dt>{t('orders.subtotal')}</dt>
            <dd>{formatMoney(quote.data.subtotal, quote.data.currency, language)}</dd>
            <dt>{t('orders.shipping', { zone: quote.data.shippingZoneName })}</dt>
            <dd>{formatMoney(quote.data.shippingCost, quote.data.currency, language)}</dd>
            <dt className="font-semibold">{t('orders.totalCod')}</dt>
            <dd className="font-semibold">{formatMoney(quote.data.total, quote.data.currency, language)}</dd>
          </dl>
        ) : quote.error ? (
          <FieldError>{orderErrorMessage(quote.error, t)}</FieldError>
        ) : (
          <p className="text-muted-foreground">{t('orders.form.totalHint')}</p>
        )}
      </section>

      {error ? <FieldError>{orderErrorMessage(error, t)}</FieldError> : null}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" onClick={onCancel}>
          {t('orders.cancel')}
        </Button>
        <Button type="submit" disabled={!ready || pending}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
