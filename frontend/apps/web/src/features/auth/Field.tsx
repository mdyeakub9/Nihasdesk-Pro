import { FieldError, Input, Label } from '@nihasdesk/ui';
import { useId, type InputHTMLAttributes } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';

type FieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> & {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string | undefined;
  hint?: string;
};

/** Label, input, hint and error wired together for screen readers. */
export function Field({ label, registration, error, hint, ...input }: FieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        {...input}
        {...registration}
      />
      {hint && !error && (
        <p id={hintId} className="text-xs text-muted-foreground">
          {hint}
        </p>
      )}
      {error && <FieldError id={errorId}>{error}</FieldError>}
    </div>
  );
}
