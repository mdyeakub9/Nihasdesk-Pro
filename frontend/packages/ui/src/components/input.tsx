import type { ComponentProps, InputHTMLAttributes, SelectHTMLAttributes } from 'react';
import { cn } from '../lib/cn';

const fieldClasses =
  'w-full rounded-md border bg-surface px-3 text-[13px] text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50';

export function Input({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(fieldClasses, 'h-9', className)} {...props} />;
}

/** `ref` is a normal prop in React 19, e.g. to put the caret back after inserting text. */
export function Textarea({ className, ...props }: ComponentProps<'textarea'>) {
  return <textarea className={cn(fieldClasses, 'min-h-16 resize-none py-2', className)} {...props} />;
}

/** Native select: keyboard and screen-reader support for free; styled like the Figma Select. */
export function NativeSelect({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={cn(fieldClasses, 'h-8 font-medium', className)} {...props} />;
}
