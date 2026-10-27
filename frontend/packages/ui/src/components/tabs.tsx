import * as TabsPrimitive from '@radix-ui/react-tabs';
import type { ComponentProps } from 'react';
import { cn } from '../lib/cn';

export const Tabs = TabsPrimitive.Root;
export const TabsContent = TabsPrimitive.Content;

export function TabsList({ className, ...props }: ComponentProps<typeof TabsPrimitive.List>) {
  return (
    <TabsPrimitive.List className={cn('inline-flex flex-wrap items-center gap-1', className)} {...props} />
  );
}

/** Figma Tab: selected tab gets the lime "selected" surface. */
export function TabsTrigger({ className, ...props }: ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      className={cn(
        'inline-flex h-8 items-center gap-2 rounded-md px-3 text-[13px] font-medium text-secondary-foreground hover:bg-subtle focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring data-[state=active]:bg-selected data-[state=active]:text-selected-foreground',
        className,
      )}
      {...props}
    />
  );
}
