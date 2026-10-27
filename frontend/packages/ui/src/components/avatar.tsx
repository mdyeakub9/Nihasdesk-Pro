import type { ReactNode } from 'react';
import { cn } from '../lib/cn';
import { initials } from '../lib/initials';

type AvatarProps = {
  /** Full name; the avatar shows its initials. */
  name: string;
  size?: 'sm' | 'md';
  /** Small overlay in the bottom-right corner, e.g. the channel icon. */
  badge?: ReactNode;
  className?: string;
};

/** Initials fallback for customers and team members (Figma Avatar). */
export function Avatar({ name, size = 'sm', badge, className }: AvatarProps) {
  return (
    <span className={cn('relative inline-flex shrink-0', className)}>
      <span
        aria-hidden="true"
        className={cn(
          'inline-flex items-center justify-center rounded-full bg-selected font-medium text-selected-foreground',
          size === 'sm' ? 'size-7 text-xs' : 'size-9 text-[13px]',
        )}
      >
        {initials(name)}
      </span>
      {badge && <span className="absolute -right-1 -bottom-1 flex">{badge}</span>}
    </span>
  );
}
