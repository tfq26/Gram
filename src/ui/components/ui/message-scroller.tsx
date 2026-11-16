import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { ArrowDown } from 'lucide-react';

interface ScrollerContextValue {
  viewportRef: React.RefObject<HTMLDivElement | null>;
  isAtBottom: boolean;
  scrollToBottom: () => void;
  setIsAtBottom: (val: boolean) => void;
}

const ScrollerContext = createContext<ScrollerContextValue | null>(null);

export function MessageScrollerProvider({ children }: { children: React.ReactNode }) {
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const [isAtBottom, setIsAtBottom] = useState(true);

  const scrollToBottom = useCallback(() => {
    const el = viewportRef.current;
    if (!el) return;
    el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' });
    setIsAtBottom(true);
  }, []);

  return (
    <ScrollerContext.Provider value={{ viewportRef, isAtBottom, scrollToBottom, setIsAtBottom }}>
      {children}
    </ScrollerContext.Provider>
  );
}

export function MessageScroller({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ position: 'relative', height: '100%', display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      {children}
    </div>
  );
}

export function MessageScrollerViewport({ children }: { children: React.ReactNode }) {
  const ctx = useContext(ScrollerContext);

  const handleScroll = () => {
    const el = ctx?.viewportRef.current;
    if (!el || !ctx) return;
    const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
    ctx.setIsAtBottom(distance < 32);
  };

  useEffect(() => {
    const el = ctx?.viewportRef.current;
    if (!el || !ctx?.isAtBottom) return;
    el.scrollTop = el.scrollHeight;
  });

  return (
    <div
      ref={ctx?.viewportRef}
      onScroll={handleScroll}
      className="messages-container"
      style={{ flex: 1, minHeight: 0, overflowY: 'auto' }}
    >
      {children}
    </div>
  );
}

export function MessageScrollerContent({
  children,
  className = '',
  'aria-busy': ariaBusy,
}: {
  children: React.ReactNode;
  className?: string;
  'aria-busy'?: boolean;
}) {
  return (
    <div className={`v2-message-list ${className}`} aria-busy={ariaBusy}>
      {children}
    </div>
  );
}

export function MessageScrollerItem({
  children,
}: {
  messageId?: string;
  scrollAnchor?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', width: '100%' }}>
      {children}
    </div>
  );
}

export function MessageScrollerButton() {
  const ctx = useContext(ScrollerContext);
  if (!ctx || ctx.isAtBottom) return null;

  return (
    <button
      type="button"
      onClick={ctx.scrollToBottom}
      aria-label="Scroll to bottom"
      style={{
        position: 'absolute',
        bottom: '12px',
        left: '50%',
        transform: 'translateX(-50%)',
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '28px',
        height: '28px',
        borderRadius: '9999px',
        background: 'var(--bg-panel)',
        border: '1px solid var(--border-color)',
        color: 'var(--text-secondary)',
        cursor: 'pointer',
      }}
    >
      <ArrowDown size={13} />
    </button>
  );
}
