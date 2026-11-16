import React from 'react';
import { cn } from '@/lib/utils';

export function InputGroup({
  className,
  children,
}: {
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mx-auto w-full max-w-[780px] px-4 pb-4 pt-2">
      <div
        data-slot="input-group"
        className={cn(
          'flex w-full flex-col rounded-2xl border border-[var(--border-color)] bg-[var(--bg-panel)] transition-colors focus-within:border-[var(--border-focus)]',
          className
        )}
      >
        {children}
      </div>
    </div>
  );
}

export const InputGroupTextarea = React.forwardRef<
  HTMLTextAreaElement,
  React.TextareaHTMLAttributes<HTMLTextAreaElement>
>(function InputGroupTextarea({ className, ...props }, ref) {
  return (
    <textarea
      ref={ref}
      data-slot="input-group-textarea"
      rows={1}
      className={cn(
        'w-full resize-none border-0 bg-transparent px-3.5 pt-3 pb-2 text-sm leading-relaxed text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none',
        className
      )}
      {...props}
    />
  );
});

export function InputGroupAddon({
  align = 'block-end',
  children,
  className,
}: {
  align?: 'block-start' | 'block-end';
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div
      data-slot="input-group-addon"
      data-align={align}
      className={cn(
        'flex items-center gap-1.5 px-3',
        align === 'block-start' ? 'flex-wrap pt-2.5 pb-1' : 'pt-1 pb-2.5',
        className
      )}
    >
      {children}
    </div>
  );
}

export function InputGroupButton({
  variant = 'outline',
  size = 'icon-sm',
  className,
  children,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'default' | 'outline';
  size?: 'icon-sm' | 'sm';
}) {
  const isDefault = variant === 'default';
  return (
    <button
      data-slot="input-group-button"
      className={cn(
        'inline-flex shrink-0 items-center justify-center gap-1.5 text-xs font-medium transition-colors focus-visible:outline-none disabled:pointer-events-none disabled:opacity-40',
        size === 'icon-sm' ? 'h-7 w-7 rounded-full' : 'h-7 rounded-lg px-2.5',
        isDefault
          ? 'bg-[var(--btn-primary-bg)] text-[var(--btn-primary-text)] hover:opacity-90'
          : 'border border-[var(--border-color)] bg-transparent text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]',
        className
      )}
      {...props}
    >
      {children}
    </button>
  );
}
