import { useTranslation } from 'react-i18next';
import instagram from '../assets/channels/instagram.svg';
import messengerBadge from '../assets/channels/messenger-badge.svg';
import messenger from '../assets/channels/messenger.svg';
import website from '../assets/channels/website.svg';
import whatsapp from '../assets/channels/whatsapp.svg';
import type { Channel } from '../features/inbox/types';

/** Channel logos from Figma. The 16px Messenger variant has the white ring used on avatars. */
const icons: Record<Channel, { full: string; badge: string }> = {
  messenger: { full: messenger, badge: messengerBadge },
  instagram: { full: instagram, badge: instagram },
  whatsapp: { full: whatsapp, badge: whatsapp },
  website: { full: website, badge: website },
};

type ChannelIconProps = {
  channel: Channel;
  size?: 16 | 20 | 24;
  variant?: 'full' | 'badge';
  /** Decorative icons sit next to the channel name; labelled ones stand alone. */
  labelled?: boolean;
};

export function ChannelIcon({ channel, size = 16, variant = 'full', labelled = false }: ChannelIconProps) {
  const { t } = useTranslation();
  return (
    <img
      src={icons[channel][variant]}
      width={size}
      height={size}
      alt={labelled ? t(`channels.${channel}`) : ''}
      className="shrink-0"
    />
  );
}
