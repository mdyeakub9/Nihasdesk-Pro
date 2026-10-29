import { Button, Card, FieldError, Input, Label, Textarea } from '@nihasdesk/ui';
import { Plus, Trash2, Truck } from 'lucide-react';
import { useId, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage } from '../auth/errors';
import { Answers } from '../ai/Answers';
import { moneyInput, parseMoney } from '../catalog/money';
import { useSession } from '../session/useSession';
import {
  usePolicies,
  useSavePolicies,
  useSaveShippingZones,
  useShippingZones,
  type ShippingZone,
  type ShopPolicies,
  type ZoneInput,
} from './api';

const maxZones = 30;

/** Owner and Admin change what the AI tells customers (docs/standards/permissions.md); the API checks it again. */
const canManage = (role: string | undefined) => role === 'owner' || role === 'admin';

function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: (id: string) => ReactNode;
}) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children(id)}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}

type ZoneRow = {
  key: string;
  id?: string | undefined;
  name: string;
  areas: string;
  price: string;
  freeOver: string;
  minDays: string;
  maxDays: string;
  currency: string;
};

let rowKey = 0;
const newKey = () => `zone-${++rowKey}`;

const toRow = (zone: ShippingZone): ZoneRow => ({
  key: zone.id,
  id: zone.id,
  name: zone.name,
  areas: zone.areas ?? '',
  price: moneyInput(zone.price),
  freeOver: moneyInput(zone.freeOver),
  minDays: zone.minDays === null ? '' : String(zone.minDays),
  maxDays: zone.maxDays === null ? '' : String(zone.maxDays),
  currency: zone.currency,
});

const days = (text: string) => {
  const value = text.trim();
  if (!value) return null;
  return /^\d{1,3}$/.test(value) && Number(value) <= 365 ? Number(value) : undefined;
};

/** The rows as the API wants them, or the key of the first row that isn't valid. */
function toInput(rows: ZoneRow[]): { zones: ZoneInput[] } | { invalid: string } {
  const zones: ZoneInput[] = [];
  for (const row of rows) {
    const price = parseMoney(row.price);
    const freeOver = parseMoney(row.freeOver);
    const minDays = days(row.minDays);
    const maxDays = days(row.maxDays);
    if (
      !row.name.trim() ||
      price === null ||
      price === undefined ||
      freeOver === undefined ||
      minDays === undefined ||
      maxDays === undefined ||
      (minDays !== null && maxDays !== null && maxDays < minDays)
    ) {
      return { invalid: row.key };
    }
    zones.push({
      ...(row.id ? { id: row.id } : {}),
      name: row.name.trim(),
      areas: row.areas.trim() || null,
      price,
      currency: row.currency,
      freeOver,
      minDays,
      maxDays,
    });
  }
  return { zones };
}

function ZoneEditor({
  row,
  invalid,
  onChange,
  onRemove,
}: {
  row: ZoneRow;
  invalid: boolean;
  onChange: (row: ZoneRow) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const set = (field: keyof ZoneRow) => (e: { target: { value: string } }) =>
    onChange({ ...row, [field]: e.target.value });

  return (
    <li className="flex flex-col gap-3 border-b px-4 py-4 last:border-b-0">
      <div className="grid gap-3 md:grid-cols-2">
        <Field label={t('policies.zones.name')}>
          {(id) => (
            <Input
              id={id}
              maxLength={100}
              placeholder={t('policies.zones.namePlaceholder')}
              value={row.name}
              onChange={set('name')}
            />
          )}
        </Field>
        <Field label={t('policies.zones.areas')}>
          {(id) => (
            <Input
              id={id}
              maxLength={500}
              placeholder={t('policies.zones.areasPlaceholder')}
              value={row.areas}
              onChange={set('areas')}
            />
          )}
        </Field>
      </div>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Field label={t('policies.zones.price', { currency: row.currency })}>
          {(id) => <Input id={id} inputMode="decimal" value={row.price} onChange={set('price')} />}
        </Field>
        <Field label={t('policies.zones.freeOver', { currency: row.currency })}>
          {(id) => <Input id={id} inputMode="decimal" value={row.freeOver} onChange={set('freeOver')} />}
        </Field>
        <Field label={t('policies.zones.minDays')}>
          {(id) => <Input id={id} inputMode="numeric" value={row.minDays} onChange={set('minDays')} />}
        </Field>
        <Field label={t('policies.zones.maxDays')}>
          {(id) => <Input id={id} inputMode="numeric" value={row.maxDays} onChange={set('maxDays')} />}
        </Field>
      </div>
      <div className="flex items-center gap-3">
        {invalid && <FieldError className="flex-1">{t('policies.zones.invalid')}</FieldError>}
        <Button size="sm" variant="ghost" className="ml-auto" onClick={onRemove}>
          <Trash2 aria-hidden="true" />
          {t('policies.zones.remove')}
        </Button>
      </div>
    </li>
  );
}

function ShippingZones({ zones, canEdit }: { zones: ShippingZone[]; canEdit: boolean }) {
  const { t } = useTranslation();
  const save = useSaveShippingZones();
  const [rows, setRows] = useState(() => zones.map(toRow));
  const [invalid, setInvalid] = useState<string | null>(null);

  function submit() {
    const result = toInput(rows);
    if ('invalid' in result) {
      setInvalid(result.invalid);
      return;
    }
    setInvalid(null);
    save.mutate(result.zones, { onSuccess: (saved) => setRows(saved.map(toRow)) });
  }

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex items-start gap-3">
        <Truck className="mt-0.5 size-5 text-secondary-foreground" aria-hidden="true" />
        <div className="flex flex-1 flex-col gap-1">
          <h2 className="text-base font-semibold">{t('policies.zones.title')}</h2>
          <p className="text-[13px] text-secondary-foreground">{t('policies.zones.intro')}</p>
        </div>
      </div>
      <fieldset disabled={!canEdit} className="flex flex-col gap-3">
        {rows.length === 0 ? (
          <p className="rounded-lg border bg-subtle px-4 py-6 text-center text-[13px] text-secondary-foreground">
            {t('policies.zones.empty')}
          </p>
        ) : (
          <ul aria-label={t('policies.zones.title')} className="rounded-lg border">
            {rows.map((row) => (
              <ZoneEditor
                key={row.key}
                row={row}
                invalid={invalid === row.key}
                onChange={(next) => setRows((current) => current.map((r) => (r.key === row.key ? next : r)))}
                onRemove={() => setRows((current) => current.filter((r) => r.key !== row.key))}
              />
            ))}
          </ul>
        )}
        {canEdit && (
          <div className="flex flex-wrap items-center gap-3">
            <Button
              variant="outline"
              size="sm"
              disabled={rows.length >= maxZones}
              onClick={() =>
                setRows((current) => [
                  ...current,
                  {
                    key: newKey(),
                    name: '',
                    areas: '',
                    price: '',
                    freeOver: '',
                    minDays: '',
                    maxDays: '',
                    currency: 'EUR',
                  },
                ])
              }
            >
              <Plus aria-hidden="true" />
              {t('policies.zones.add')}
            </Button>
            <Button size="sm" disabled={save.isPending} onClick={submit}>
              {t('policies.zones.save')}
            </Button>
            {save.isSuccess && (
              <span className="text-[13px] text-success-foreground">{t('aiSettings.saved')}</span>
            )}
          </div>
        )}
        {save.error && <FieldError>{authErrorMessage(save.error, t)}</FieldError>}
      </fieldset>
    </Card>
  );
}

function PoliciesForm({ policies, canEdit }: { policies: ShopPolicies; canEdit: boolean }) {
  const { t } = useTranslation();
  const save = useSavePolicies();
  const [values, setValues] = useState({
    shippingNotes: policies.shippingNotes ?? '',
    returnDays: policies.returnDays === null ? '' : String(policies.returnDays),
    returnPolicy: policies.returnPolicy ?? '',
    paymentNotes: policies.paymentNotes ?? '',
    contactPhone: policies.contactPhone ?? '',
    contactEmail: policies.contactEmail ?? '',
    contactAddress: policies.contactAddress ?? '',
    openingHours: policies.openingHours ?? '',
  });
  const [daysInvalid, setDaysInvalid] = useState(false);
  const set = (field: keyof typeof values) => (e: { target: { value: string } }) =>
    setValues((current) => ({ ...current, [field]: e.target.value }));
  const text = (value: string) => value.trim() || null;

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h2 className="text-base font-semibold">{t('policies.title')}</h2>
        <p className="text-[13px] text-secondary-foreground">{t('policies.intro')}</p>
      </div>
      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          const returnDays = days(values.returnDays);
          setDaysInvalid(returnDays === undefined);
          if (returnDays === undefined) return;
          save.mutate({
            shippingNotes: text(values.shippingNotes),
            returnDays,
            returnPolicy: text(values.returnPolicy),
            paymentNotes: text(values.paymentNotes),
            contactPhone: text(values.contactPhone),
            contactEmail: text(values.contactEmail),
            contactAddress: text(values.contactAddress),
            openingHours: text(values.openingHours),
          });
        }}
      >
        <fieldset disabled={!canEdit} className="flex flex-col gap-6">
          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold">{t('policies.shipping')}</h3>
            <Field label={t('policies.shippingNotes')} hint={t('policies.shippingNotesHint')}>
              {(id) => (
                <Textarea
                  id={id}
                  rows={2}
                  maxLength={1000}
                  value={values.shippingNotes}
                  onChange={set('shippingNotes')}
                />
              )}
            </Field>
          </section>
          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold">{t('policies.returns')}</h3>
            <Field label={t('policies.returnDays')}>
              {(id) => (
                <Input
                  id={id}
                  className="md:w-40"
                  inputMode="numeric"
                  aria-invalid={daysInvalid || undefined}
                  value={values.returnDays}
                  onChange={set('returnDays')}
                />
              )}
            </Field>
            {daysInvalid && <FieldError>{t('policies.returnDaysInvalid')}</FieldError>}
            <Field label={t('policies.returnPolicy')}>
              {(id) => (
                <Textarea
                  id={id}
                  rows={3}
                  maxLength={2000}
                  placeholder={t('policies.returnPolicyPlaceholder')}
                  value={values.returnPolicy}
                  onChange={set('returnPolicy')}
                />
              )}
            </Field>
          </section>
          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold">{t('policies.payment')}</h3>
            <p className="text-[13px]">
              {policies.paymentMethods.map((m) => t(`policies.methods.${m}`, { defaultValue: m })).join(', ')}
              <span className="text-secondary-foreground"> · {t('policies.paymentFixed')}</span>
            </p>
            <Field label={t('policies.paymentNotes')}>
              {(id) => (
                <Textarea
                  id={id}
                  rows={2}
                  maxLength={1000}
                  value={values.paymentNotes}
                  onChange={set('paymentNotes')}
                />
              )}
            </Field>
          </section>
          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold">{t('policies.contact')}</h3>
            <p className="text-[13px] text-secondary-foreground">{t('policies.contactIntro')}</p>
            <div className="grid gap-3 md:grid-cols-2">
              <Field label={t('policies.phone')}>
                {(id) => (
                  <Input
                    id={id}
                    type="tel"
                    maxLength={40}
                    value={values.contactPhone}
                    onChange={set('contactPhone')}
                  />
                )}
              </Field>
              <Field label={t('policies.email')}>
                {(id) => (
                  <Input
                    id={id}
                    type="email"
                    maxLength={254}
                    value={values.contactEmail}
                    onChange={set('contactEmail')}
                  />
                )}
              </Field>
            </div>
            <Field label={t('policies.address')}>
              {(id) => (
                <Input
                  id={id}
                  maxLength={300}
                  value={values.contactAddress}
                  onChange={set('contactAddress')}
                />
              )}
            </Field>
            <Field label={t('policies.openingHours')}>
              {(id) => (
                <Textarea
                  id={id}
                  rows={2}
                  maxLength={500}
                  placeholder={t('policies.openingHoursPlaceholder')}
                  value={values.openingHours}
                  onChange={set('openingHours')}
                />
              )}
            </Field>
          </section>
          {canEdit && (
            <div className="flex items-center gap-3">
              <Button type="submit" disabled={save.isPending}>
                {t('aiSettings.save')}
              </Button>
              {save.isSuccess && (
                <span className="text-[13px] text-success-foreground">{t('aiSettings.saved')}</span>
              )}
            </div>
          )}
          {save.error && <FieldError>{authErrorMessage(save.error, t)}</FieldError>}
        </fieldset>
      </form>
    </Card>
  );
}

/**
 * Settings → Shop knowledge (PRD 4.7, ADR 0020): shipping zones, policies and the FAQ — what the
 * AI may tell customers. Everyone sees it; Owner and Admin change it.
 */
export function PoliciesPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const { data: zones } = useShippingZones();
  const { data: policies } = usePolicies();
  const canEdit = canManage(session?.role);

  return (
    <div className="flex max-w-3xl flex-col gap-6">
      {!canEdit && (
        <p role="note" className="text-sm text-secondary-foreground">
          {t('policies.readOnly')}
        </p>
      )}
      {zones && <ShippingZones zones={zones} canEdit={canEdit} />}
      {policies && <PoliciesForm policies={policies} canEdit={canEdit} />}
      <Answers canEdit={canEdit} />
    </div>
  );
}
