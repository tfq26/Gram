import React, { createContext, useContext, useState } from 'react';
import { ChevronRight } from 'lucide-react';

export function Trace({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
      {children}
    </div>
  );
}

interface TraceCollapsibleContextValue {
  open: boolean;
  toggle: () => void;
}

const TraceCollapsibleContext = createContext<TraceCollapsibleContextValue>({
  open: false,
  toggle: () => {},
});

export function TraceCollapsible({
  open: controlledOpen,
  onOpenChange,
  defaultOpen = false,
  children,
}: {
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  defaultOpen?: boolean;
  children: React.ReactNode;
}) {
  const [internalOpen, setInternalOpen] = useState(defaultOpen);
  const open = controlledOpen !== undefined ? controlledOpen : internalOpen;

  const toggle = () => {
    const next = !open;
    if (controlledOpen === undefined) setInternalOpen(next);
    onOpenChange?.(next);
  };

  return (
    <TraceCollapsibleContext.Provider value={{ open, toggle }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>{children}</div>
    </TraceCollapsibleContext.Provider>
  );
}

export function TraceCollapsibleTrigger({ children }: { children: React.ReactNode }) {
  const { open, toggle } = useContext(TraceCollapsibleContext);
  return (
    <button
      type="button"
      onClick={toggle}
      style={{
        display: 'flex',
        width: '100%',
        alignItems: 'center',
        justifyContent: 'space-between',
        background: 'transparent',
        border: 0,
        padding: '2px 0',
        cursor: 'pointer',
        textAlign: 'left',
      }}
    >
      {children}
      <span className="gram-chevron" data-open={open ? 'true' : 'false'}>
        <ChevronRight size={13} />
      </span>
    </button>
  );
}

export function TraceCollapsibleContent({
  children,
  className = '',
  keepMounted = false,
}: {
  children: React.ReactNode;
  className?: string;
  keepMounted?: boolean;
}) {
  const { open } = useContext(TraceCollapsibleContext);
  const [hasOpened, setHasOpened] = useState(open);
  if (open && !hasOpened) {
    setHasOpened(true);
  }
  const shouldRenderInner = open || keepMounted || hasOpened;

  return (
    <div className="gram-collapsible-grid" data-open={open ? 'true' : 'false'}>
      <div className="gram-collapsible-inner">
        {shouldRenderInner && (
          <div
            className={className}
            style={{
              marginLeft: '8px',
              paddingLeft: '12px',
              paddingTop: '2px',
              paddingBottom: '4px',
              borderLeft: '1px solid var(--border-color)',
              color: 'var(--text-muted)',
              fontSize: '12px',
              lineHeight: 1.5,
            }}
          >
            {children}
          </div>
        )}
      </div>
    </div>
  );
}

export function TraceLine({
  status,
  children,
}: {
  status?: 'running' | 'error';
  children: React.ReactNode;
}) {
  const color =
    status === 'error'
      ? 'var(--danger)'
      : status === 'running'
        ? 'var(--text-primary)'
        : 'var(--text-muted)';

  return (
    <div
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '8px',
        fontSize: '12px',
        color,
      }}
    >
      {children}
    </div>
  );
}

export function TraceIcon({ children }: { children: React.ReactNode }) {
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', width: '14px', height: '14px' }}>
      {children}
    </span>
  );
}

export function TraceTitle({ children }: { children: React.ReactNode }) {
  return <span style={{ fontSize: '12px' }}>{children}</span>;
}
