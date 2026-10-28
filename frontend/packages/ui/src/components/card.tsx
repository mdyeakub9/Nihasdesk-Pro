import type { HTMLAttributes, LabelHTMLAttributes } from 'react';
import { cn } from '../lib/cn';

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn('rounded-lg border bg-surface p-6 shadow-sm', className)} {...props} />;
}

export function Label({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) {
  return <label className={cn('text-[13px] font-medium text-foreground', className)} {...props} />;
}

/** Field error text; give it an id and reference it from the input's aria-describedby. */
export function FieldError({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return <p role="alert" className={cn('text-xs text-destructive', className)} {...props} />;
}
