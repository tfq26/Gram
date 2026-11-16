import React from 'react';

export function Message({
  align = 'start',
  children,
}: {
  align?: 'start' | 'end';
  children: React.ReactNode;
}) {
  return (
    <div
      data-slot="message"
      data-align={align}
      className="group/message flex w-full flex-col gap-1"
      style={{ alignItems: align === 'end' ? 'flex-end' : 'flex-start' }}
    >
      {children}
    </div>
  );
}

export function MessageContent({
  children,
  className = '',
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`flex w-full flex-col gap-2 ${className}`}
    >
      {children}
    </div>
  );
}
