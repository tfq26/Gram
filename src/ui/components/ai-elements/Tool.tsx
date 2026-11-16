import React, { useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';

export interface ToolProps {
  name: string;
  status?: 'running' | 'completed' | 'error';
  input?: string;
  output?: string;
}

export function Tool({ name, status = 'completed', input, output }: ToolProps) {
  const [open, setOpen] = useState(false);

  const statusText =
    status === 'running' ? 'running' : status === 'error' ? 'failed' : 'done';

  return (
    <div
      style={{
        borderTop: '1px solid var(--border-color)',
        padding: '6px 0',
      }}
    >
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        style={{
          display: 'flex',
          width: '100%',
          alignItems: 'center',
          justifyContent: 'space-between',
          background: 'transparent',
          border: 0,
          padding: 0,
          color: 'var(--text-secondary)',
          fontSize: '12px',
          cursor: 'pointer',
        }}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', fontFamily: 'var(--font-mono)' }}>
          {open ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          <span>{name}</span>
        </span>
        <span style={{ color: status === 'error' ? 'var(--danger)' : 'var(--text-muted)', fontSize: '11px' }}>
          {statusText}
        </span>
      </button>
      {open && (input || output) && (
        <div
          style={{
            marginTop: '6px',
            padding: '8px 10px',
            background: 'var(--bg-panel-subtle)',
            border: '1px solid var(--border-color)',
            borderRadius: 'var(--radius-sm)',
            fontFamily: 'var(--font-mono)',
            fontSize: '11.5px',
            color: 'var(--text-secondary)',
            whiteSpace: 'pre-wrap',
            overflowX: 'auto',
          }}
        >
          {input && <div style={{ marginBottom: output ? '6px' : 0 }}>{input}</div>}
          {output && <div style={{ color: 'var(--text-primary)' }}>{output}</div>}
        </div>
      )}
    </div>
  );
}
