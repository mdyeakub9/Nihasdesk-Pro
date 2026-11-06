import { Badge, Button, buttonVariants, Card, cn, FieldError, Textarea } from '@nihasdesk/ui';
import { ArrowLeft, Ban, Combine, Pencil, TriangleAlert, Undo2 } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  useEditSection,
  useMergeNextSection,
  useSetSectionExcluded,
  useSourceReview,
  useSourceSections,
  type ReviewConflict,
  type ReviewSection,
} from './api';
import { statusTone, useWarningText } from './sourceDisplay';
import { SourceUse, TestQuestion } from './SourceUse';

const maxSectionLength = 20000;
const sectionAnchor = (id: string) => `section-${id}`;

function ConflictItem({
  sourceId,
  conflict,
  canEdit,
  onFixSection,
}: {
  sourceId: string;
  conflict: ReviewConflict;
  canEdit: boolean;
  onFixSection: (sectionId: string) => void;
}) {
  const { t } = useTranslation();
  const exclude = useSetSectionExcluded(sourceId);

  return (
    <li className="flex flex-col gap-2 border-b px-4 py-3 last:border-b-0">
      <p className="text-[13px]">
        {t(`sources.review.conflicts.${conflict.kind}`, {
          found: conflict.found,
          expected: conflict.expected,
        })}
        {conflict.page ? ` ${t('sources.review.onPage', { page: conflict.page })}` : ''}
      </p>
      {canEdit && (
        <div className="flex flex-wrap gap-2">
          {conflict.kind !== 'payment_method' && (
            <Link to="/settings/knowledge" className={buttonVariants({ size: 'sm', variant: 'outline' })}>
              {t('sources.review.fixSettings')}
            </Link>
          )}
          <Button size="sm" variant="outline" onClick={() => onFixSection(conflict.sectionId)}>
            {t('sources.review.fixSection')}
          </Button>
          <Button
            size="sm"
            variant="ghost"
            disabled={exclude.isPending}
            onClick={() => exclude.mutate({ sectionId: conflict.sectionId, excluded: true })}
          >
            {t('sources.review.exclude')}
          </Button>
        </div>
      )}
      {exclude.error && <FieldError>{authErrorMessage(exclude.error, t)}</FieldError>}
    </li>
  );
}

function SectionItem({
  sourceId,
  section,
  canEdit,
  isLast,
  startEditing,
  highlighted,
}: {
  sourceId: string;
  section: ReviewSection;
  canEdit: boolean;
  isLast: boolean;
  /** Opened by "Fix section": the page remounts this item with a new key each time. */
  startEditing: boolean;
  highlighted: boolean;
}) {
  const { t } = useTranslation();
  const edit = useEditSection(sourceId);
  const exclude = useSetSectionExcluded(sourceId);
  const merge = useMergeNextSection(sourceId);
  const fieldId = useId();
  const [draft, setDraft] = useState<string | null>(startEditing && canEdit ? section.text : null);
  const error = edit.error ?? exclude.error ?? merge.error;
  const details = [
    t(`sources.review.kinds.${section.kind}`),
    section.page ? t('sources.review.page', { page: section.page }) : null,
  ].filter(Boolean);

  return (
    <li
      id={sectionAnchor(section.id)}
      className={cn(
        'flex flex-col gap-2 border-b px-4 py-3 last:border-b-0',
        section.excluded && 'bg-subtle',
        highlighted && 'ring-2 ring-ring ring-inset',
      )}
    >
      <div className="flex flex-wrap items-center gap-2">
        <p className="min-w-0 flex-1 text-xs font-medium break-words text-secondary-foreground">
          {section.headingPath ?? t('sources.review.noHeading')}
        </p>
        {section.excluded && <Badge tone="warning">{t('sources.review.excluded')}</Badge>}
        <span className="text-xs text-muted-foreground">{details.join(' · ')}</span>
      </div>

      {draft === null ? (
        // Imported text is shown as plain text, never as HTML (SEC-02).
        <p
          className={cn(
            'text-[13px] break-words whitespace-pre-wrap',
            section.excluded && 'text-muted-foreground',
          )}
        >
          {section.text}
        </p>
      ) : (
        <div className="flex flex-col gap-2">
          <label htmlFor={fieldId} className="sr-only">
            {t('sources.review.sectionText')}
          </label>
          <Textarea
            id={fieldId}
            rows={Math.min(14, Math.max(3, draft.split('\n').length + 2))}
            maxLength={maxSectionLength}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
          />
          <div className="flex gap-2">
            <Button
              size="sm"
              disabled={!draft.trim() || edit.isPending}
              onClick={() =>
                edit.mutate({ sectionId: section.id, text: draft }, { onSuccess: () => setDraft(null) })
              }
            >
              {t('sources.review.save')}
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setDraft(null)}>
              {t('sources.cancel')}
            </Button>
          </div>
        </div>
      )}

      {!section.searchable && !section.excluded && (
        <p className="text-xs text-muted-foreground">{t('sources.review.keywordsOnly')}</p>
      )}

      {canEdit && draft === null && (
        <div className="flex flex-wrap gap-1">
          <Button size="sm" variant="ghost" onClick={() => setDraft(section.text)}>
            <Pencil aria-hidden="true" />
            {t('sources.review.edit')}
          </Button>
          <Button
            size="sm"
            variant="ghost"
            disabled={exclude.isPending}
            onClick={() => exclude.mutate({ sectionId: section.id, excluded: !section.excluded })}
          >
            {section.excluded ? <Undo2 aria-hidden="true" /> : <Ban aria-hidden="true" />}
            {section.excluded ? t('sources.review.include') : t('sources.review.exclude')}
          </Button>
          {!isLast && (
            <Button
              size="sm"
              variant="ghost"
              disabled={merge.isPending}
              onClick={() => merge.mutate(section.id)}
            >
              <Combine aria-hidden="true" />
              {t('sources.review.merge')}
            </Button>
          )}
        </div>
      )}
      {error && <FieldError>{authErrorMessage(error, t)}</FieldError>}
    </li>
  );
}

/**
 * Settings → Shop knowledge → a source: exactly what the AI will be able to find (PRD 4.7). Warnings
 * first, then the sections. Everyone reads it; Owner and Admin correct it. Saved directly until
 * draft and publish exist (Issue #55).
 */
export function SourceReviewPage() {
  const { t } = useTranslation();
  const { sourceId = '' } = useParams();
  const { data: session } = useSession();
  const canEdit = session?.role === 'owner' || session?.role === 'admin';
  const review = useSourceReview(sourceId);
  const sections = useSourceSections(sourceId);
  const warningText = useWarningText();
  const [fixRequest, setFixRequest] = useState({ sectionId: null as string | null, count: 0 });
  const fixing = fixRequest.sectionId;

  const items = sections.data?.pages.flatMap((page) => page.items) ?? [];
  const fixingLoaded = fixing !== null && items.some((section) => section.id === fixing);
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = sections;

  // A warning can point at a section further down: load until it is there, then go to it.
  useEffect(() => {
    if (fixing === null) return;
    if (fixingLoaded) {
      document.getElementById(sectionAnchor(fixing))?.scrollIntoView?.({ block: 'center' });
    } else if (hasNextPage && !isFetchingNextPage) {
      void fetchNextPage();
    }
  }, [fixing, fixingLoaded, hasNextPage, isFetchingNextPage, fetchNextPage]);

  const back = (
    <Link
      to="/settings/knowledge"
      className={cn(buttonVariants({ size: 'sm', variant: 'ghost' }), 'self-start')}
    >
      <ArrowLeft aria-hidden="true" />
      {t('sources.review.back')}
    </Link>
  );

  if (review.isError) {
    return (
      <div className="flex max-w-3xl flex-col gap-3">
        {back}
        <p role="alert" className="text-sm text-secondary-foreground">
          {authErrorMessage(review.error, t)}
        </p>
      </div>
    );
  }

  if (!review.data) {
    return <p className="text-sm text-muted-foreground">{t('sources.review.loading')}</p>;
  }

  const { source, conflicts, priceListSections } = review.data;
  const canUse = canEdit && (source.status === 'review' || source.status === 'live');
  const warnings = source.warnings.map(warningText);
  if (priceListSections.length > 0) warnings.push(t('sources.review.priceList'));

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      {back}
      <Card className="flex flex-col gap-2">
        <div className="flex items-start gap-2">
          <h1 className="min-w-0 flex-1 text-lg font-semibold break-words">{source.title}</h1>
          <Badge tone={statusTone[source.status]}>{t(`sources.status.${source.status}`)}</Badge>
        </div>
        <p className="text-[13px] text-secondary-foreground">{t('sources.review.intro')}</p>
        <p className="text-[13px] text-secondary-foreground">{t('sources.review.savedDirectly')}</p>
        {source.failureReason && (
          <p className="text-[13px] text-destructive">
            {t(`sources.failures.${source.failureReason}`, {
              defaultValue: t('sources.failures.read_failed'),
            })}
          </p>
        )}
        {canUse && <SourceUse source={source} conflicts={conflicts.length} />}
      </Card>

      {canUse && <TestQuestion sourceId={sourceId} />}

      {warnings.length > 0 && (
        <section
          aria-label={t('sources.review.warnings')}
          className="flex flex-col gap-1 rounded-lg border bg-warning px-4 py-3"
        >
          {warnings.map((warning) => (
            <p key={warning} className="flex items-start gap-2 text-[13px] text-warning-foreground">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
              {warning}
            </p>
          ))}
        </section>
      )}

      {conflicts.length > 0 && (
        <section aria-labelledby="conflicts-heading" className="flex flex-col gap-2">
          <h2 id="conflicts-heading" className="text-sm font-semibold">
            {t('sources.review.conflictsTitle', { count: conflicts.length })}
          </h2>
          <p className="text-[13px] text-secondary-foreground">{t('sources.review.conflictsIntro')}</p>
          <ul aria-labelledby="conflicts-heading" className="rounded-lg border bg-surface">
            {conflicts.map((conflict) => (
              <ConflictItem
                key={`${conflict.kind}-${conflict.sectionId}-${conflict.found}`}
                sourceId={sourceId}
                conflict={conflict}
                canEdit={canEdit}
                onFixSection={(sectionId) => setFixRequest({ sectionId, count: fixRequest.count + 1 })}
              />
            ))}
          </ul>
        </section>
      )}

      <section aria-labelledby="sections-heading" className="flex flex-col gap-2">
        <h2 id="sections-heading" className="text-sm font-semibold">
          {t('sources.review.sectionsTitle', { count: source.sectionCount })}
        </h2>
        {items.length > 0 && (
          <ol aria-labelledby="sections-heading" className="rounded-lg border bg-surface">
            {items.map((section, index) => (
              <SectionItem
                key={fixing === section.id ? `${section.id}-${fixRequest.count}` : section.id}
                sourceId={sourceId}
                section={section}
                canEdit={canEdit}
                isLast={index === items.length - 1 && !hasNextPage}
                startEditing={fixing === section.id}
                highlighted={fixing === section.id}
              />
            ))}
          </ol>
        )}
        {hasNextPage && (
          <Button
            variant="ghost"
            size="sm"
            className="self-center"
            disabled={isFetchingNextPage}
            onClick={() => void fetchNextPage()}
          >
            {t('sources.review.loadMore')}
          </Button>
        )}
        {sections.error && <FieldError>{authErrorMessage(sections.error, t)}</FieldError>}
      </section>
    </div>
  );
}
