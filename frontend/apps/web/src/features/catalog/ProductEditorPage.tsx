import { zodResolver } from '@hookform/resolvers/zod';
import { Badge, Button, Card, FieldError, Input, Label, NativeSelect, Textarea } from '@nihasdesk/ui';
import { ArrowLeft, Plus, Trash2 } from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { useFieldArray, useForm, useWatch, type UseFormRegisterReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { z } from 'zod';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  fieldErrors,
  productStatuses,
  useProduct,
  useSaveProduct,
  type Product,
  type ProductInput,
} from './api';
import { moneyInput, parseMoney } from './money';
import { canManageCatalog, statusTone } from './roles';

const maxOptions = 3;
const maxVariants = 100;

type VariantForm = {
  id?: string | undefined;
  option1: string;
  option2: string;
  option3: string;
  sku: string;
  barcode: string;
  price: string;
  compareAtPrice: string;
  cost: string;
  trackInventory: boolean;
  stock: string;
  inventoryPolicy: 'deny' | 'continue';
  weightGrams: string;
  requiresShipping: boolean;
  currency: string;
};

type FormValues = {
  title: string;
  description: string;
  status: (typeof productStatuses)[number];
  productType: string;
  category: string;
  vendor: string;
  tags: string;
  optionNames: { name: string }[];
  variants: VariantForm[];
};

const emptyVariant = (): VariantForm => ({
  option1: '',
  option2: '',
  option3: '',
  sku: '',
  barcode: '',
  price: '',
  compareAtPrice: '',
  cost: '',
  trackInventory: false,
  stock: '',
  inventoryPolicy: 'deny',
  weightGrams: '',
  requiresShipping: true,
  currency: 'EUR',
});

function toForm(product: Product | undefined): FormValues {
  if (!product) {
    return {
      title: '',
      description: '',
      status: 'active',
      productType: '',
      category: '',
      vendor: '',
      tags: '',
      optionNames: [],
      variants: [emptyVariant()],
    };
  }

  return {
    title: product.title,
    description: product.description ?? '',
    status: product.status,
    productType: product.productType ?? '',
    category: product.category ?? '',
    vendor: product.vendor ?? '',
    tags: product.tags.join(', '),
    optionNames: product.optionNames.map((name) => ({ name })),
    variants: product.variants.map((v) => ({
      id: v.id,
      option1: v.option1 ?? '',
      option2: v.option2 ?? '',
      option3: v.option3 ?? '',
      sku: v.sku ?? '',
      barcode: v.barcode ?? '',
      price: moneyInput(v.price),
      compareAtPrice: moneyInput(v.compareAtPrice),
      cost: moneyInput(v.cost),
      trackInventory: v.trackInventory,
      stock: v.stock === null ? '' : String(v.stock),
      inventoryPolicy: v.inventoryPolicy,
      weightGrams: v.weightGrams === null ? '' : String(v.weightGrams),
      requiresShipping: v.requiresShipping,
      currency: v.currency,
    })),
  };
}

const text = (value: string) => value.trim() || null;
const integer = (value: string) => (value.trim() ? Number(value) : null);

function toInput(values: FormValues): ProductInput {
  const optionNames = values.optionNames.map((o) => o.name.trim()).filter(Boolean);
  return {
    title: values.title.trim(),
    description: text(values.description),
    status: values.status,
    productType: text(values.productType),
    category: text(values.category),
    vendor: text(values.vendor),
    tags: values.tags
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean),
    optionNames,
    variants: values.variants.map((v) => ({
      ...(v.id ? { id: v.id } : {}),
      sku: text(v.sku),
      barcode: text(v.barcode),
      option1: optionNames.length > 0 ? text(v.option1) : null,
      option2: optionNames.length > 1 ? text(v.option2) : null,
      option3: optionNames.length > 2 ? text(v.option3) : null,
      price: parseMoney(v.price) ?? 0,
      compareAtPrice: parseMoney(v.compareAtPrice) ?? null,
      cost: parseMoney(v.cost) ?? null,
      currency: v.currency.trim().toUpperCase() || 'EUR',
      trackInventory: v.trackInventory,
      stock: v.trackInventory ? (integer(v.stock) ?? 0) : null,
      inventoryPolicy: v.inventoryPolicy,
      weightGrams: integer(v.weightGrams),
      requiresShipping: v.requiresShipping,
    })),
  };
}

function Field({
  label,
  error,
  children,
  id,
}: {
  label: string;
  error?: string | undefined;
  children: (id: string) => ReactNode;
  id?: string;
}) {
  const generated = useId();
  const fieldId = id ?? generated;
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={fieldId}>{label}</Label>
      {children(fieldId)}
      {error && <FieldError>{error}</FieldError>}
    </div>
  );
}

function TextField({
  label,
  error,
  registration,
  type = 'text',
  inputMode,
}: {
  label: string;
  error?: string | undefined;
  registration: UseFormRegisterReturn;
  type?: string;
  inputMode?: 'decimal' | 'numeric';
}) {
  return (
    <Field label={label} error={error}>
      {(id) => (
        <Input
          id={id}
          type={type}
          inputMode={inputMode}
          aria-invalid={error ? true : undefined}
          {...registration}
        />
      )}
    </Field>
  );
}

/** Product editor (ADR 0018): details, up to three options, and the variants. */
export function ProductEditorPage() {
  const { t } = useTranslation();
  const { productId } = useParams();
  const { data: product, isPending, isError } = useProduct(productId);

  if (productId && isPending) return <div aria-busy="true" className="flex-1" />;
  if (productId && (isError || !product)) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center">
        <h1 className="text-base font-semibold">{t('products.notFound')}</h1>
        <Button asChild variant="outline" size="sm">
          <Link to="/products">{t('products.back')}</Link>
        </Button>
      </div>
    );
  }

  // Keyed so a saved product reloads the form with what the server stored.
  return <ProductForm key={product?.updatedAt ?? 'new'} product={product} />;
}

function ProductForm({ product }: { product: Product | undefined }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: session } = useSession();
  const manage = canManageCatalog(session?.role);
  const save = useSaveProduct(product?.id);

  const money = (required: boolean) =>
    z.string().refine((value) => {
      const cents = parseMoney(value);
      return cents !== undefined && (!required || cents !== null);
    }, t('products.errors.money'));
  const whole = z
    .string()
    .refine((value) => !value.trim() || /^-?\d{1,9}$/.test(value.trim()), t('products.errors.whole'));
  const schema = z.object({
    title: z.string().trim().min(1, t('products.errors.title')).max(255),
    description: z.string().max(10_000),
    status: z.enum(productStatuses),
    productType: z.string().max(100),
    category: z.string().max(255),
    vendor: z.string().max(100),
    tags: z.string().max(2600),
    optionNames: z
      .array(z.object({ name: z.string().trim().min(1, t('products.errors.optionName')).max(50) }))
      .max(maxOptions),
    variants: z
      .array(
        z.object({
          id: z.string().optional(),
          option1: z.string().max(100),
          option2: z.string().max(100),
          option3: z.string().max(100),
          sku: z.string().max(64),
          barcode: z.string().max(64),
          price: money(true),
          compareAtPrice: money(false),
          cost: money(false),
          trackInventory: z.boolean(),
          stock: whole,
          inventoryPolicy: z.enum(['deny', 'continue']),
          weightGrams: whole,
          requiresShipping: z.boolean(),
          currency: z.string().regex(/^[A-Za-z]{3}$/, t('products.errors.currency')),
        }),
      )
      .min(1)
      .max(maxVariants),
  });

  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: toForm(product) });
  const options = useFieldArray({ control: form.control, name: 'optionNames' });
  const variants = useFieldArray({ control: form.control, name: 'variants' });
  const optionNames = useWatch({ control: form.control, name: 'optionNames' });
  const watchedVariants = useWatch({ control: form.control, name: 'variants' });
  const errors = form.formState.errors;

  const submit = form.handleSubmit((values) =>
    save.mutate(toInput(values), {
      onSuccess: (saved) => {
        if (!product) void navigate(`/products/${saved.id}`, { replace: true });
      },
    }),
  );
  const serverErrors = fieldErrors(save.error);

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <form noValidate onSubmit={submit} className="flex max-w-5xl flex-col gap-6 p-4 md:p-8">
        <div className="flex flex-wrap items-center gap-3">
          <Button asChild variant="ghost" size="icon">
            <Link to="/products" aria-label={t('products.back')}>
              <ArrowLeft aria-hidden="true" />
            </Link>
          </Button>
          <h1 className="flex-1 truncate text-[28px] leading-9 font-semibold">
            {product?.title ?? t('products.new')}
          </h1>
          {product && (
            <Badge tone={statusTone[product.status]}>{t(`products.statuses.${product.status}`)}</Badge>
          )}
          {manage && (
            <Button type="submit" disabled={save.isPending}>
              {t('products.save')}
            </Button>
          )}
        </div>

        {!manage && (
          <p role="note" className="text-[13px] text-secondary-foreground">
            {t('products.readOnly')}
          </p>
        )}
        {save.isSuccess && product && (
          <p role="status" className="text-[13px] text-success-foreground">
            {t('products.saved')}
          </p>
        )}
        {save.error && (
          <div
            role="alert"
            className="flex flex-col gap-1 rounded-lg border bg-warning px-4 py-3 text-[13px] text-warning-foreground"
          >
            <p>{serverErrors.length > 0 ? t('products.errors.server') : authErrorMessage(save.error, t)}</p>
            {serverErrors.length > 0 && (
              <ul className="list-disc pl-5">
                {serverErrors.map((message) => (
                  <li key={message}>{message}</li>
                ))}
              </ul>
            )}
          </div>
        )}

        <fieldset disabled={!manage} className="flex flex-col gap-6">
          <Card className="flex flex-col gap-4">
            <h2 className="text-base font-semibold">{t('products.sections.details')}</h2>
            <TextField
              label={t('products.fields.title')}
              error={errors.title?.message}
              registration={form.register('title')}
            />
            <Field label={t('products.fields.description')} error={errors.description?.message}>
              {(id) => <Textarea id={id} rows={4} {...form.register('description')} />}
            </Field>
            <div className="grid gap-4 md:grid-cols-2">
              <Field label={t('products.fields.status')}>
                {(id) => (
                  <NativeSelect id={id} className="h-9" {...form.register('status')}>
                    {productStatuses.map((status) => (
                      <option key={status} value={status}>
                        {t(`products.statuses.${status}`)}
                      </option>
                    ))}
                  </NativeSelect>
                )}
              </Field>
              <TextField label={t('products.fields.vendor')} registration={form.register('vendor')} />
              <TextField
                label={t('products.fields.productType')}
                registration={form.register('productType')}
              />
              <TextField label={t('products.fields.category')} registration={form.register('category')} />
            </div>
            <TextField label={t('products.fields.tags')} registration={form.register('tags')} />
            {product && (
              <p className="text-xs text-muted-foreground">
                {t('products.handle', { handle: product.handle })}
              </p>
            )}
          </Card>

          <Card className="flex flex-col gap-4">
            <div className="flex items-center gap-3">
              <h2 className="flex-1 text-base font-semibold">{t('products.sections.options')}</h2>
              {manage && options.fields.length < maxOptions && (
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => options.append({ name: '' })}
                >
                  <Plus aria-hidden="true" />
                  {t('products.addOption')}
                </Button>
              )}
            </div>
            {options.fields.length === 0 && (
              <p className="text-[13px] text-secondary-foreground">{t('products.noOptions')}</p>
            )}
            {options.fields.map((option, index) => (
              <div key={option.id} className="flex items-end gap-2">
                <div className="flex-1">
                  <TextField
                    label={t('products.fields.optionName', { number: index + 1 })}
                    error={errors.optionNames?.[index]?.name?.message}
                    registration={form.register(`optionNames.${index}.name`)}
                  />
                </div>
                {manage && (
                  <Button
                    type="button"
                    size="icon"
                    variant="ghost"
                    aria-label={t('products.removeOption', { number: index + 1 })}
                    onClick={() => options.remove(index)}
                  >
                    <Trash2 aria-hidden="true" />
                  </Button>
                )}
              </div>
            ))}
          </Card>

          <Card className="flex flex-col gap-4">
            <div className="flex items-center gap-3">
              <h2 className="flex-1 text-base font-semibold">
                {t('products.sections.variants', { count: variants.fields.length })}
              </h2>
              {manage && variants.fields.length < maxVariants && (
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => variants.append(emptyVariant())}
                >
                  <Plus aria-hidden="true" />
                  {t('products.addVariant')}
                </Button>
              )}
            </div>
            <ol className="flex flex-col gap-4">
              {variants.fields.map((variant, index) => {
                const e = errors.variants?.[index];
                const tracked = watchedVariants[index]?.trackInventory ?? false;
                return (
                  <li
                    key={variant.id}
                    aria-label={t('products.variantNumber', { number: index + 1 })}
                    className="flex flex-col gap-3 rounded-lg border p-4"
                  >
                    <div className="flex items-center gap-2">
                      <h3 className="flex-1 text-sm font-medium">
                        {t('products.variantNumber', { number: index + 1 })}
                      </h3>
                      {manage && variants.fields.length > 1 && (
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          aria-label={t('products.removeVariant', { number: index + 1 })}
                          onClick={() => variants.remove(index)}
                        >
                          <Trash2 aria-hidden="true" />
                        </Button>
                      )}
                    </div>
                    {optionNames.length > 0 && (
                      <div className="grid gap-3 md:grid-cols-3">
                        {optionNames.map((option, o) => (
                          <TextField
                            key={o}
                            label={option.name || t('products.fields.optionName', { number: o + 1 })}
                            registration={form.register(`variants.${index}.option${(o + 1) as 1 | 2 | 3}`)}
                          />
                        ))}
                      </div>
                    )}
                    <div className="grid gap-3 md:grid-cols-4">
                      <TextField
                        label={t('products.fields.price')}
                        error={e?.price?.message}
                        inputMode="decimal"
                        registration={form.register(`variants.${index}.price`)}
                      />
                      <TextField
                        label={t('products.fields.compareAtPrice')}
                        error={e?.compareAtPrice?.message}
                        inputMode="decimal"
                        registration={form.register(`variants.${index}.compareAtPrice`)}
                      />
                      <TextField
                        label={t('products.fields.cost')}
                        error={e?.cost?.message}
                        inputMode="decimal"
                        registration={form.register(`variants.${index}.cost`)}
                      />
                      <TextField
                        label={t('products.fields.currency')}
                        error={e?.currency?.message}
                        registration={form.register(`variants.${index}.currency`)}
                      />
                      <TextField
                        label={t('products.fields.sku')}
                        error={e?.sku?.message}
                        registration={form.register(`variants.${index}.sku`)}
                      />
                      <TextField
                        label={t('products.fields.barcode')}
                        error={e?.barcode?.message}
                        registration={form.register(`variants.${index}.barcode`)}
                      />
                      <TextField
                        label={t('products.fields.weight')}
                        error={e?.weightGrams?.message}
                        inputMode="numeric"
                        registration={form.register(`variants.${index}.weightGrams`)}
                      />
                      <Field label={t('products.fields.inventoryPolicy')}>
                        {(id) => (
                          <NativeSelect
                            id={id}
                            className="h-9"
                            {...form.register(`variants.${index}.inventoryPolicy`)}
                          >
                            <option value="deny">{t('products.policy.deny')}</option>
                            <option value="continue">{t('products.policy.continue')}</option>
                          </NativeSelect>
                        )}
                      </Field>
                    </div>
                    <div className="flex flex-wrap items-end gap-4">
                      <label className="flex items-center gap-2 text-[13px] font-medium">
                        <input
                          type="checkbox"
                          className="size-4"
                          {...form.register(`variants.${index}.trackInventory`)}
                        />
                        {t('products.fields.trackInventory')}
                      </label>
                      {tracked && (
                        <div className="w-32">
                          <TextField
                            label={t('products.fields.stock')}
                            error={e?.stock?.message}
                            inputMode="numeric"
                            registration={form.register(`variants.${index}.stock`)}
                          />
                        </div>
                      )}
                      <label className="flex items-center gap-2 text-[13px] font-medium">
                        <input
                          type="checkbox"
                          className="size-4"
                          {...form.register(`variants.${index}.requiresShipping`)}
                        />
                        {t('products.fields.requiresShipping')}
                      </label>
                    </div>
                  </li>
                );
              })}
            </ol>
          </Card>
        </fieldset>
      </form>
    </div>
  );
}
