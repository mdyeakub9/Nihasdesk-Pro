import { Button, Card, FieldError } from '@nihasdesk/ui';
import { ArrowLeft, Download, FileUp } from 'lucide-react';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router';
import { authErrorMessage } from '../auth/errors';
import { useSession } from '../session/useSession';
import { templateUrl, useImport, type ImportReport } from './api';
import { canManageCatalog } from './roles';

const maxBytes = 2 * 1024 * 1024;

function Counts({ report }: { report: ImportReport }) {
  const { t } = useTranslation();
  const c = report.counts;
  return (
    <ul className="grid gap-2 text-[13px] md:grid-cols-4">
      <li>{t('products.import.productsCreated', { count: c.productsCreated })}</li>
      <li>{t('products.import.productsUpdated', { count: c.productsUpdated })}</li>
      <li>{t('products.import.variantsCreated', { count: c.variantsCreated })}</li>
      <li>{t('products.import.variantsUpdated', { count: c.variantsUpdated })}</li>
    </ul>
  );
}

/** CSV import (ADR 0018): template → choose a file → preview with issues per line → import. */
export function ImportPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const fileId = useId();
  const run = useImport();
  const [file, setFile] = useState<File | null>(null);
  const [tooLarge, setTooLarge] = useState(false);
  const report = run.data;

  if (!canManageCatalog(session?.role)) {
    return (
      <p role="note" className="p-8 text-sm text-secondary-foreground">
        {t('products.readOnly')}
      </p>
    );
  }

  const choose = (chosen: File | undefined) => {
    run.reset();
    setTooLarge(!!chosen && chosen.size > maxBytes);
    setFile(chosen && chosen.size <= maxBytes ? chosen : null);
  };

  return (
    <div className="min-h-0 flex-1 overflow-y-auto">
      <div className="flex max-w-4xl flex-col gap-6 p-4 md:p-8">
        <div className="flex items-center gap-3">
          <Button asChild variant="ghost" size="icon">
            <Link to="/products" aria-label={t('products.back')}>
              <ArrowLeft aria-hidden="true" />
            </Link>
          </Button>
          <h1 className="text-[28px] leading-9 font-semibold">{t('products.import.title')}</h1>
        </div>

        <Card className="flex flex-col gap-3">
          <h2 className="text-base font-semibold">{t('products.import.step1')}</h2>
          <p className="text-[13px] text-secondary-foreground">{t('products.import.templateHint')}</p>
          <div>
            <Button asChild variant="outline" size="sm">
              <a href={templateUrl} download>
                <Download aria-hidden="true" />
                {t('products.import.template')}
              </a>
            </Button>
          </div>
        </Card>

        <Card className="flex flex-col gap-3">
          <h2 className="text-base font-semibold">{t('products.import.step2')}</h2>
          <label htmlFor={fileId} className="text-[13px] font-medium">
            {t('products.import.file')}
          </label>
          <input
            id={fileId}
            type="file"
            accept=".csv,text/csv"
            className="text-[13px]"
            onChange={(event) => choose(event.target.files?.[0])}
          />
          {tooLarge && <FieldError>{t('products.import.tooLarge')}</FieldError>}
          <div>
            <Button
              variant="outline"
              disabled={!file || run.isPending}
              onClick={() => file && run.mutate({ file, apply: false })}
            >
              <FileUp aria-hidden="true" />
              {t('products.import.preview')}
            </Button>
          </div>
          {run.error && <FieldError>{authErrorMessage(run.error, t)}</FieldError>}
        </Card>

        {report && (
          <Card className="flex flex-col gap-4" aria-live="polite">
            <h2 className="text-base font-semibold">
              {report.applied ? t('products.import.done') : t('products.import.step3')}
            </h2>
            <Counts report={report} />
            {report.issues.length > 0 ? (
              <>
                <p className="text-[13px] text-warning-foreground">
                  {t('products.import.issues', { count: report.issues.length })}
                </p>
                <div className="overflow-x-auto rounded-lg border">
                  <table className="w-full text-left text-[13px]">
                    <caption className="sr-only">{t('products.import.issuesTable')}</caption>
                    <thead className="border-b text-xs text-secondary-foreground">
                      <tr>
                        <th className="px-3 py-2 font-medium">{t('products.import.line')}</th>
                        <th className="px-3 py-2 font-medium">{t('products.import.column')}</th>
                        <th className="px-3 py-2 font-medium">{t('products.import.problem')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {report.issues.map((issue, index) => (
                        <tr
                          key={`${issue.line}-${issue.column}-${index}`}
                          className="border-b last:border-b-0"
                        >
                          <td className="px-3 py-2">{issue.line}</td>
                          <td className="px-3 py-2">{issue.column ?? '—'}</td>
                          <td className="px-3 py-2">{issue.message}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            ) : report.applied ? (
              <div>
                <Button asChild>
                  <Link to="/products">{t('products.import.toProducts')}</Link>
                </Button>
              </div>
            ) : (
              <div className="flex flex-wrap items-center gap-3">
                <Button
                  disabled={!file || run.isPending}
                  onClick={() => file && run.mutate({ file, apply: true })}
                >
                  {t('products.import.apply')}
                </Button>
                <span className="text-xs text-muted-foreground">{t('products.import.allOrNothing')}</span>
              </div>
            )}
          </Card>
        )}
      </div>
    </div>
  );
}
