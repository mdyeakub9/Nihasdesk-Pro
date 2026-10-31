import { Badge, Switch } from '@nihasdesk/ui';
import {
  ChevronDown,
  Mail,
  MapPin,
  Phone,
  Sparkles,
  StickyNote,
  Target,
  ShoppingBag,
  UserCheck,
} from 'lucide-react';
import { useId, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { ChannelIcon } from '../../components/ChannelIcon';
import { formatAge } from '../../lib/format';
import { useUpdateConversation } from './api';
import { NoteCount, TeamNotes } from './TeamNotes';
import type { Conversation, LeadStatus } from './types';

const leadTone: Record<LeadStatus, 'ai' | 'success' | 'info' | 'neutral' | 'warning'> = {
  new: 'info',
  hot: 'ai',
  customer: 'success',
  support: 'neutral',
  spam: 'warning',
};

function Section({
  icon,
  title,
  aside,
  children,
}: {
  icon: ReactNode;
  title: string;
  aside?: ReactNode;
  children: ReactNode;
}) {
  return (
    <details open className="group border-b px-4 py-3">
      <summary className="flex cursor-pointer list-none items-center gap-2 text-sm font-medium [&::-webkit-details-marker]:hidden">
        {icon}
        <span className="flex-1">{title}</span>
        {aside && <span className="text-xs font-normal text-muted-foreground">{aside}</span>}
        <ChevronDown
          className="size-4 text-muted-foreground transition-transform group-open:rotate-180"
          aria-hidden="true"
        />
      </summary>
      <div className="mt-2 text-[13px] text-secondary-foreground">{children}</div>
    </details>
  );
}

const iconClass = 'size-4 text-secondary-foreground';

export function CustomerPanel({ conversation }: { conversation: Conversation }) {
  const { t, i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? 'en';
  const update = useUpdateConversation(conversation.id);
  const aiSwitchId = useId();
  const { contact } = conversation;
  const place = [contact.city, contact.country].filter(Boolean).join(', ');

  return (
    <div className="flex flex-col">
      <h2 className="flex h-14 items-center border-b px-4 text-base font-semibold">{t('customer.title')}</h2>

      <div className="flex flex-col gap-2 border-b px-4 py-3 text-[13px] text-secondary-foreground">
        <p className="flex items-center gap-2 text-foreground">
          <ChannelIcon channel={conversation.channel} />
          {t(`channels.${conversation.channel}`)}
        </p>
        {contact.phone && (
          <p className="flex items-center gap-2 text-foreground">
            <Phone className={iconClass} aria-hidden="true" />
            {contact.phone}
          </p>
        )}
        {contact.email && (
          <p className="flex items-center gap-2">
            <Mail className={iconClass} aria-hidden="true" />
            {contact.email}
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          {t('customer.seen', {
            firstSeen: formatAge(contact.firstSeenAt, language),
            active: formatAge(contact.lastActiveAt, language),
          })}
        </p>
        {(place || contact.language) && (
          <p className="flex items-center gap-2 text-xs text-muted-foreground">
            <MapPin className="size-3.5" aria-hidden="true" />
            {[place, contact.language].filter(Boolean).join(' · ')}
          </p>
        )}
        {conversation.labels.length > 0 && (
          <ul aria-label={t('customer.labels')} className="flex flex-wrap gap-1.5 pt-1">
            {conversation.labels.map((label) => (
              <li key={label}>
                <Badge>{label}</Badge>
              </li>
            ))}
          </ul>
        )}
      </div>

      <Section
        icon={<StickyNote className={iconClass} aria-hidden="true" />}
        title={t('customer.teamNote')}
        aside={<NoteCount conversationId={conversation.id} />}
      >
        <TeamNotes conversationId={conversation.id} />
      </Section>

      <Section icon={<Sparkles className={iconClass} aria-hidden="true" />} title={t('customer.assistant')}>
        {conversation.aiSummary ?? t('customer.noSummary')}
      </Section>

      <Section
        icon={<Target className={iconClass} aria-hidden="true" />}
        title={t('customer.lead')}
        aside={
          <Badge tone={leadTone[conversation.leadStatus]}>{t(`leadStatus.${conversation.leadStatus}`)}</Badge>
        }
      >
        {t('customer.leadHint')}
      </Section>

      <Section
        icon={<ShoppingBag className={iconClass} aria-hidden="true" />}
        title={t('customer.orders')}
        aside={t('customer.orderCount', { count: conversation.orderCount })}
      >
        {t('customer.ordersHint')}
      </Section>

      <Section icon={<UserCheck className={iconClass} aria-hidden="true" />} title={t('customer.aiOrPerson')}>
        <p>{t(conversation.responder === 'ai' ? 'customer.aiAnswering' : 'customer.personAnswering')}</p>
        <div className="mt-2 flex items-center gap-2">
          <Switch
            id={aiSwitchId}
            checked={conversation.aiEnabled}
            disabled={update.isPending}
            // Turning the AI off hands the conversation to the team.
            onCheckedChange={(checked) =>
              update.mutate(checked ? { aiEnabled: true } : { aiEnabled: false, responder: 'human' })
            }
          />
          <label htmlFor={aiSwitchId} className="text-[13px] font-medium text-foreground">
            {t('customer.aiAssistance')}
          </label>
        </div>
        <p className="mt-2 text-xs text-muted-foreground">
          {t('customer.handoffs', { count: conversation.handoffCount })}
        </p>
      </Section>
    </div>
  );
}
