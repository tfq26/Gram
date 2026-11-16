import React from 'react';

export function CodeBlock({
  children,
}: {
  size?: 'sm' | 'md';
  children: React.ReactNode;
}) {
  return (
    <div
      style={{
        marginTop: '4px',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-sm)',
        background: 'var(--bg-panel-subtle)',
        overflow: 'hidden',
      }}
    >
      {children}
    </div>
  );
}

export function CodeBlockContent({
  children,
  wrap = false,
  className = '',
}: {
  children: React.ReactNode;
  wrap?: boolean;
  className?: string;
}) {
  return (
    <pre
      className={className}
      style={{
        margin: 0,
        padding: '8px 10px',
        fontFamily: 'var(--font-mono)',
        fontSize: '11.5px',
        lineHeight: 1.5,
        color: 'var(--text-secondary)',
        whiteSpace: wrap ? 'pre-wrap' : 'pre',
        overflowX: 'auto',
        maxHeight: '240px',
      }}
    >
      <code>{children}</code>
    </pre>
  );
}
