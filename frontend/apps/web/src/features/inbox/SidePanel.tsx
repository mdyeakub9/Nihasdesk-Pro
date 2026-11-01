import { Tabs, TabsContent, TabsList, TabsTrigger } from '@nihasdesk/ui';
import { Sparkles, User } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AssistantPanel } from './AssistantPanel';
import { CustomerPanel } from './CustomerPanel';
import type { Conversation } from './types';

type Tab = 'customer' | 'assistant';

/** The conversation's side panel (PRD 4.4): Customer details, or ✨ AI assistant — one click apart. */
export function SidePanel({ conversation }: { conversation: Conversation }) {
  const { t } = useTranslation();
  const [tab, setTab] = useState<Tab>('customer');

  return (
    <Tabs value={tab} onValueChange={(value) => setTab(value as Tab)} className="flex flex-col">
      <TabsList aria-label={t('sidePanel.tabs')} className="border-b px-3 py-2">
        <TabsTrigger value="customer">
          <User className="size-4" aria-hidden="true" />
          {t('sidePanel.customer')}
        </TabsTrigger>
        <TabsTrigger value="assistant">
          <Sparkles className="size-4" aria-hidden="true" />
          {t('sidePanel.assistant')}
        </TabsTrigger>
      </TabsList>
      <TabsContent value="customer">
        <CustomerPanel conversation={conversation} />
      </TabsContent>
      {/* Kept mounted, so switching back to Customer and back keeps the suggestion. */}
      <TabsContent value="assistant" forceMount hidden={tab !== 'assistant'}>
        <AssistantPanel conversation={conversation} />
      </TabsContent>
    </Tabs>
  );
}
