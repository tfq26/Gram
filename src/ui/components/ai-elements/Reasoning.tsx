import React, { useEffect, useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';

export interface ReasoningProps {
  content?: string;
  isStreaming?: boolean;
  durationSeconds?: number;
}

export function Reasoning({ content, isStreaming = false, durationSeconds }: ReasoningProps) {
  const [open, setOpen] = useState(isStreaming);

  useEffect(() => {
    if (!isStreaming) {
      setOpen(false);
    }
  }, [isStreaming]);

  if (!content && !isStreaming) return null;

  const label = isStreaming
    ? 'Thinking...'
    : durationSeconds
      ? `Thought for ${durationSeconds}s`
      : 'Thought process';

  return (
    <div style={{ marginBottom: '4px' }}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          background: 'transparent',
          border: 0,
          padding: '2px 0',
          color: 'var(--text-muted)',
          fontSize: '12px',
          cursor: 'pointer',
        }}
      >
        {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
        <span>{label}</span>
      </button>
      {open && content && (
        <div
          style={{
            marginTop: '4px',
            paddingLeft: '12px',
            borderLeft: '1px solid var(--border-color)',
            color: 'var(--text-muted)',
            fontSize: '12px',
            lineHeight: 1.5,
            whiteSpace: 'pre-wrap',
          }}
        >
          {content}
        </div>
      )}
    </div>
  );
}
