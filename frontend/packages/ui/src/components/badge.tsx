import { cva, type VariantProps } from 'class-variance-authority';
import type { HTMLAttributes } from 'react';
import { cn } from '../lib/cn';

const badgeVariants = cva(
  'inline-flex items-center gap-1 whitespace-nowrap rounded-sm px-2 py-0.5 text-xs [&_svg]:size-3.5',
  {
    variants: {
      // Status is always a text label plus color, so AI and human states stay distinguishable (Figma Badge).
      tone: {
        neutral: 'bg-subtle text-secondary-foreground',
        success: 'bg-success text-success-foreground',
        ai: 'bg-selected text-selected-foreground',
        info: 'bg-info text-info-foreground',
        warning: 'bg-warning text-warning-foreground',
      },
    },
    defaultVariants: {
      tone: 'neutral',
    },
  },
);

export type BadgeProps = HTMLAttributes<HTMLSpanElement> & VariantProps<typeof badgeVariants>;

export function Badge({ className, tone, ...props }: BadgeProps) {
  return <span className={cn(badgeVariants({ tone }), className)} {...props} />;
}
