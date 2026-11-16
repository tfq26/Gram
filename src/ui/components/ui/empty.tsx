import React from 'react';

export function Empty({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <div className={`harness-empty-state ${className}`}>{children}</div>;
}

export function EmptyHeader({ children }: { children: React.ReactNode }) {
  return <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '6px' }}>{children}</div>;
}

export function EmptyMedia({ children }: { variant?: 'icon'; children: React.ReactNode }) {
  return (
    <div style={{ color: 'var(--text-muted)', marginBottom: '4px', display: 'inline-flex' }}>
      {children}
    </div>
  );
}

export function EmptyTitle({ children }: { children: React.ReactNode }) {
  return <h2 style={{ margin: 0 }}>{children}</h2>;
}

export function EmptyDescription({ children }: { children: React.ReactNode }) {
  return <p style={{ margin: 0 }}>{children}</p>;
}
