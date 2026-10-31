import { Button, Card, FieldError, Input, Label, Textarea } from '@nihasdesk/ui';
import { Plus } from 'lucide-react';
import { useId, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { authErrorMessage, isErrorCode } from '../auth/errors';
import { useSession } from '../session/useSession';
import {
  useDeleteSavedReply,
  useSaveSavedReply,
  useSavedReplies,
  type SavedReply,
  type SavedReplyInput,
} from './api';

const empty: SavedReplyInput = { shortcut: '', title: '', textEn: '', textIt: '' };

/** Create or edit one saved reply. Texts are plain text (SEC-02); `{first_name}`, `{shop_name}` are filled in the composer. */
function ReplyForm({ initial, id, onDone }: { initial: SavedReplyInput; id?: string; onDone: () => void }) {
  const { t } = useTranslation();
  const save = useSaveSavedReply();
  const [value, setValue] = useState(initial);
  const ids = { shortcut: useId(), title: useId(), en: useId(), it: useId() };
  const ready = value.shortcut.trim() && value.title.trim() && (value.textEn?.trim() || value.textIt?.trim());

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!ready) return;
    save.mutate(
      {
        ...(id ? { id } : {}),
        shortcut: value.shortcut.trim().replace(/^\//, ''),
        title: value.title.trim(),
        textEn: value.textEn?.trim() || null,
        textIt: value.textIt?.trim() || null,
      },
      { onSuccess: onDone },
    );
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-3 px-4 py-3">
      <div className="grid gap-3 sm:grid-cols-[12rem_1fr]">
        <div className="flex flex-col gap-1">
          <Label htmlFor={ids.shortcut}>{t('savedReplies.shortcut')}</Label>
          <Input
            id={ids.shortcut}
            className="h-9"
            placeholder="ship"
            value={value.shortcut}
            onChange={(e) => setValue({ ...value, shortcut: e.target.value })}
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={ids.title}>{t('savedReplies.titleLabel')}</Label>
          <Input
            id={ids.title}
            className="h-9"
            value={value.title}
            onChange={(e) => setValue({ ...value, title: e.target.value })}
          />
        </div>
      </div>
      <Label htmlFor={ids.en}>{t('savedReplies.textEn')}</Label>
      <Textarea
        id={ids.en}
        rows={3}
        value={value.textEn ?? ''}
        onChange={(e) => setValue({ ...value, textEn: e.target.value })}
      />
      <Label htmlFor={ids.it}>{t('savedReplies.textIt')}</Label>
      <Textarea
        id={ids.it}
        rows={3}
        value={value.textIt ?? ''}
        onChange={(e) => setValue({ ...value, textIt: e.target.value })}
      />
      <p className="text-xs text-muted-foreground">{t('savedReplies.variables')}</p>
      {save.error && (
        <FieldError>
          {isErrorCode(save.error, 'saved_reply.shortcut_taken')
            ? t('savedReplies.shortcutTaken')
            : authErrorMessage(save.error, t)}
        </FieldError>
      )}
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={!ready || save.isPending}>
          {t('savedReplies.save')}
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={onDone}>
          {t('savedReplies.cancel')}
        </Button>
      </div>
    </form>
  );
}

function ReplyItem({ reply, canEdit }: { reply: SavedReply; canEdit: boolean }) {
  const { t } = useTranslation();
  const remove = useDeleteSavedReply();
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);

  if (editing) {
    return (
      <li className="border-b last:border-b-0">
        <ReplyForm
          id={reply.id}
          initial={{
            shortcut: reply.shortcut,
            title: reply.title,
            textEn: reply.textEn,
            textIt: reply.textIt,
          }}
          onDone={() => setEditing(false)}
        />
      </li>
    );
  }

  return (
    <li className="flex flex-col gap-1 border-b px-4 py-3 last:border-b-0">
      <p className="text-sm font-medium">
        <span className="text-secondary-foreground">/{reply.shortcut}</span> · {reply.title}
      </p>
      {reply.textEn && (
        <p className="text-[13px] whitespace-pre-line text-secondary-foreground">
          <span className="font-medium">EN</span> {reply.textEn}
        </p>
      )}
      {reply.textIt && (
        <p className="text-[13px] whitespace-pre-line text-secondary-foreground">
          <span className="font-medium">IT</span> {reply.textIt}
        </p>
      )}
      {canEdit && (
        <div className="mt-1 flex flex-wrap items-center gap-2">
          <Button size="sm" variant="outline" onClick={() => setEditing(true)}>
            {t('savedReplies.edit')}
          </Button>
          {confirming ? (
            <>
              <span className="text-xs">{t('savedReplies.deleteConfirm')}</span>
              <Button
                size="sm"
                variant="destructive"
                disabled={remove.isPending}
                onClick={() => remove.mutate(reply.id)}
              >
                {t('savedReplies.delete')}
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setConfirming(false)}>
                {t('savedReplies.cancel')}
              </Button>
            </>
          ) : (
            <Button size="sm" variant="ghost" onClick={() => setConfirming(true)}>
              {t('savedReplies.delete')}
            </Button>
          )}
          {remove.error && <FieldError>{authErrorMessage(remove.error, t)}</FieldError>}
        </div>
      )}
    </li>
  );
}

/**
 * Settings → Saved replies (PRD 4.4, ADR 0022). Everyone sees them; Owner and Admin change them
 * (the API checks the role, SEC-10).
 */
export function SavedRepliesPage() {
  const { t } = useTranslation();
  const { data: session } = useSession();
  const { data: replies = [], isPending } = useSavedReplies();
  const [adding, setAdding] = useState(false);
  const canEdit = session?.role === 'owner' || session?.role === 'admin';

  return (
    <section className="flex max-w-3xl flex-col gap-4">
      <div className="flex items-start gap-3">
        <div className="flex-1">
          <h2 className="text-base font-semibold">{t('savedReplies.title')}</h2>
          <p className="text-[13px] text-secondary-foreground">{t('savedReplies.intro')}</p>
        </div>
        {canEdit && !adding && (
          <Button size="sm" onClick={() => setAdding(true)}>
            <Plus aria-hidden="true" />
            {t('savedReplies.add')}
          </Button>
        )}
      </div>
      {adding && (
        <Card>
          <ReplyForm initial={empty} onDone={() => setAdding(false)} />
        </Card>
      )}
      <Card>
        {isPending ? (
          <p className="px-4 py-3 text-[13px] text-muted-foreground" aria-busy="true">
            {t('savedReplies.loading')}
          </p>
        ) : replies.length === 0 ? (
          <p className="px-4 py-3 text-[13px] text-muted-foreground">{t('savedReplies.empty')}</p>
        ) : (
          <ul aria-label={t('savedReplies.title')}>
            {replies.map((reply) => (
              <ReplyItem key={reply.id} reply={reply} canEdit={canEdit} />
            ))}
          </ul>
        )}
      </Card>
    </section>
  );
}
