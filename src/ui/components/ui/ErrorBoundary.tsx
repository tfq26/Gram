import React, { useState } from 'react';
import { CheckIcon, ChevronDownIcon, ChevronRightIcon, CircleAlertIcon, CopyIcon, RotateCcwIcon } from 'lucide-react';

export interface AppErrorScreenProps {
  title?: string;
  message?: string;
  details?: string;
  onRetry?: () => void;
  retryLabel?: string;
  onReload?: () => void;
}

export function AppErrorScreen({
  title = 'Something went wrong',
  message = 'Gram ran into an unexpected error and could not display this view.',
  details,
  onRetry,
  retryLabel = 'Try again',
  onReload,
}: AppErrorScreenProps) {
  const [showDetails, setShowDetails] = useState(false);
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    const payload = [title, message, details].filter(Boolean).join('\n\n');
    navigator.clipboard?.writeText(payload).then(() => {
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1800);
    }).catch(() => {});
  };

  return (
    <div
      role="alert"
      className="fixed inset-0 z-[9999] flex min-h-screen w-full items-center justify-center bg-[#0e0f10] px-6 py-10 text-[#ececec]"
    >
      <div className="w-full max-w-md rounded-2xl border border-white/10 bg-[#151719] p-6 shadow-2xl">
        <div className="mb-4 flex items-center gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-red-500/25 bg-red-500/10 text-red-400">
            <CircleAlertIcon size={20} />
          </div>
          <div className="min-w-0">
            <h1 className="text-base font-semibold tracking-tight text-[#ececec]">
              {title}
            </h1>
            <p className="text-xs text-[#9ca3af]">
              Gram Studio
            </p>
          </div>
        </div>

        <p className="text-sm leading-relaxed text-[#d1d5db]">
          {message}
        </p>

        {details && (
          <div className="mt-4">
            <button
              type="button"
              onClick={() => setShowDetails((prev) => !prev)}
              className="inline-flex items-center gap-1.5 text-xs font-medium text-[#9ca3af] hover:text-[#ececec] cursor-pointer"
            >
              {showDetails ? <ChevronDownIcon size={13} /> : <ChevronRightIcon size={13} />}
              <span>{showDetails ? 'Hide technical details' : 'Show technical details'}</span>
            </button>
            {showDetails && (
              <pre className="mt-2 max-h-48 overflow-auto rounded-xl border border-white/10 bg-[#0b0c0d] p-3 font-mono text-[11px] leading-relaxed text-[#fca5a5] whitespace-pre-wrap break-words">
                {details}
              </pre>
            )}
          </div>
        )}

        <div className="mt-6 flex flex-wrap items-center gap-2">
          {onRetry && (
            <button
              type="button"
              onClick={onRetry}
              className="inline-flex items-center gap-1.5 rounded-xl bg-[#ececec] px-3.5 py-2 text-xs font-semibold text-[#0e0f10] transition hover:bg-white cursor-pointer"
            >
              <RotateCcwIcon size={13} />
              <span>{retryLabel}</span>
            </button>
          )}
          <button
            type="button"
            onClick={onReload || (() => window.location.reload())}
            className="inline-flex items-center gap-1.5 rounded-xl border border-white/15 bg-white/5 px-3.5 py-2 text-xs font-medium text-[#ececec] transition hover:bg-white/10 cursor-pointer"
          >
            <RotateCcwIcon size={13} />
            <span>Reload app</span>
          </button>
          <button
            type="button"
            onClick={handleCopy}
            className="ml-auto inline-flex items-center gap-1.5 rounded-xl border border-white/10 px-3 py-2 text-xs font-medium text-[#9ca3af] transition hover:bg-white/5 hover:text-[#ececec] cursor-pointer"
          >
            {copied ? <CheckIcon size={13} className="text-emerald-400" /> : <CopyIcon size={13} />}
            <span>{copied ? 'Copied' : 'Copy error'}</span>
          </button>
        </div>
      </div>
    </div>
  );
}

interface AppErrorBoundaryProps {
  children: React.ReactNode;
}

interface AppErrorBoundaryState {
  error: Error | null;
  componentStack?: string;
}

export class AppErrorBoundary extends React.Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  constructor(props: AppErrorBoundaryProps) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error): AppErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    document.getElementById('gram-preboot')?.remove();
    this.setState({ error, componentStack: info.componentStack || undefined });
    console.error('[Gram ErrorBoundary]', error, info);
  }

  render() {
    if (this.state.error) {
      const details = [
        this.state.error.stack || this.state.error.message,
        this.state.componentStack ? `Component stack:${this.state.componentStack}` : '',
      ]
        .filter(Boolean)
        .join('\n\n');

      return (
        <AppErrorScreen
          title="Gram encountered an unexpected error"
          message={this.state.error.message || 'An unexpected rendering error occurred.'}
          details={details}
          onRetry={() => this.setState({ error: null, componentStack: undefined })}
        />
      );
    }

    return this.props.children;
  }
}
