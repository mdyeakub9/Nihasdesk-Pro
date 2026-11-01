import { CreateOrderDialog } from '../orders/CreateOrderDialog';
import type { Conversation } from './types';

/** Create order from the chat's + menu: the contact's name, phone and city are filled in. */
export function CreateOrderModal({
  conversation,
  onClose,
}: {
  conversation: Conversation;
  onClose: () => void;
}) {
  return (
    <CreateOrderDialog
      prefill={{
        conversationId: conversation.id,
        customerName: conversation.contact.name,
        phone: conversation.contact.phone ?? '',
        city: conversation.contact.city ?? '',
      }}
      onClose={onClose}
    />
  );
}
