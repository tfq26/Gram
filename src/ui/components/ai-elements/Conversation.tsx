import React, { useEffect, useRef } from 'react';

export interface ConversationProps {
  children: React.ReactNode;
  className?: string;
}

export function Conversation({ children, className = '' }: ConversationProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  });

  return (
    <div ref={containerRef} className={`messages-container ${className}`}>
      {children}
    </div>
  );
}

export function ConversationContent({ children }: { children: React.ReactNode }) {
  return <div className="v2-message-list">{children}</div>;
}

export function ConversationEmptyState({
  title = 'How can I help?',
  subtitle = 'Type a message or attach context.',
}: {
  title?: string;
  subtitle?: string;
}) {
  return (
    <div className="harness-empty-state">
      <h2>{title}</h2>
      <p>{subtitle}</p>
    </div>
  );
}
