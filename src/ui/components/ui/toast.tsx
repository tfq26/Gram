import React, { useEffect, useState } from 'react';
import {
  AlertTriangleIcon,
  CircleAlertIcon,
  CircleCheckIcon,
  InfoIcon,
  XIcon,
} from 'lucide-react';

export type SystemToastVariant = 'info' | 'success' | 'warning' | 'error';

export interface SystemToastInput {
  title: string;
  description?: string;
  variant?: SystemToastVariant;
  durationMs?: number;
}

interface SystemToastItem extends SystemToastInput {
  id: string;
  variant: SystemToastVariant;
}

const TOAST_EVENT = 'gram:system-toast';

export function emitSystemToast(input: SystemToastInput): void {
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent<SystemToastInput>(TOAST_EVENT, { detail: input }));
}

export function SystemToastViewport() {
  const [toasts, setToasts] = useState<SystemToastItem[]>([]);

  useEffect(() => {
    const timers = new Map<string, number>();

    const onToast = (event: Event) => {
      const detail = (event as CustomEvent<SystemToastInput>).detail;
      if (!detail?.title) return;
      const variant: SystemToastVariant = detail.variant || 'info';
      const id = `toast-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`;
      const durationMs =
        detail.durationMs ?? (variant === 'error' || variant === 'warning' ? 4800 : 3000);

      setToasts((prev) => [...prev.slice(-2), { ...detail, id, variant }]);

      const timer = window.setTimeout(() => {
        setToasts((prev) => prev.filter((t) => t.id !== id));
        timers.delete(id);
      }, durationMs);
      timers.set(id, timer);
    };

    window.addEventListener(TOAST_EVENT, onToast);
    return () => {
      window.removeEventListener(TOAST_EVENT, onToast);
      for (const timer of timers.values()) {
        window.clearTimeout(timer);
      }
    };
  }, []);

  if (toasts.length === 0) return null;

  const dismiss = (id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  };

  return (
    <div
      aria-live="polite"
      className="pointer-events-none fixed top-14 left-1/2 z-[120] flex w-full max-w-[420px] -translate-x-1/2 flex-col items-center gap-2 px-4"
    >
      {toasts.map((toast) => {
        const icon =
          toast.variant === 'success' ? (
            <CircleCheckIcon size={15} className="shrink-0 text-emerald-500" />
          ) : toast.variant === 'error' ? (
            <CircleAlertIcon size={15} className="shrink-0 text-red-500" />
          ) : toast.variant === 'warning' ? (
            <AlertTriangleIcon size={15} className="shrink-0 text-amber-500" />
          ) : (
            <InfoIcon size={15} className="shrink-0 text-[var(--text-secondary)]" />
          );

        const accentBorder =
          toast.variant === 'error'
            ? 'border-red-500/35'
            : toast.variant === 'warning'
              ? 'border-amber-500/35'
              : toast.variant === 'success'
                ? 'border-emerald-500/30'
                : 'border-[var(--border-color)]';

        return (
          <div
            key={toast.id}
            role="status"
            className={`pointer-events-auto flex w-full items-start gap-2.5 rounded-xl border ${accentBorder} bg-[var(--bg-panel)] px-3.5 py-2.5 text-[var(--text-primary)] shadow-lg backdrop-blur-md transition-all duration-200`}
          >
            <div className="mt-0.5">{icon}</div>
            <div className="min-w-0 flex-1">
              <div className="text-xs font-medium leading-snug text-[var(--text-primary)]">
                {toast.title}
              </div>
              {toast.description && (
                <div className="mt-0.5 truncate text-[11px] text-[var(--text-secondary)]">
                  {toast.description}
                </div>
              )}
            </div>
            <button
              type="button"
              onClick={() => dismiss(toast.id)}
              aria-label="Dismiss notification"
              className="mt-0.5 rounded p-0.5 text-[var(--text-muted)] transition-colors hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
            >
              <XIcon size={13} />
            </button>
          </div>
        );
      })}
    </div>
  );
}
