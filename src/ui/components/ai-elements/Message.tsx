import React, { useState } from 'react';
import { Streamdown } from 'streamdown';
import { createMermaidPlugin } from '@streamdown/mermaid';
import { Check, Copy } from 'lucide-react';

const mermaidPlugin = createMermaidPlugin({
  config: {
    theme: 'dark',
    securityLevel: 'strict',
  },
});

export interface MessageProps {
  from: 'user' | 'assistant';
  author?: string;
  status?: string;
  statusTone?: 'default' | 'success' | 'danger';
  children: React.ReactNode;
}

export function Message({
  from,
  author,
  status,
  statusTone = 'default',
  children,
}: MessageProps) {
  if (from === 'user') {
    return (
      <div className="message-card user-card">
        <div className="card-body">{children}</div>
      </div>
    );
  }

  const statusColor =
    statusTone === 'success'
      ? 'var(--success)'
      : statusTone === 'danger'
        ? 'var(--danger)'
        : 'var(--text-muted)';

  return (
    <div className="message-card">
      {(author || status) && (
        <div className="card-header">
          <div className="card-author">
            <span>{author || 'Gram'}</span>
          </div>
          {status && <span style={{ color: statusColor }}>{status}</span>}
        </div>
      )}
      <div className="card-body">{children}</div>
    </div>
  );
}

export function MessageContent({ children }: { children: React.ReactNode }) {
  return <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>{children}</div>;
}

function CodeBlock({
  className,
  children,
}: {
  className?: string;
  children?: React.ReactNode;
}) {
  const [copied, setCopied] = useState(false);
  const codeText = String(children || '').replace(/\n$/, '');
  const isBlock = Boolean(className?.startsWith('language-')) || codeText.includes('\n');

  if (!isBlock) {
    return (
      <code
        style={{
          fontFamily: 'var(--font-mono)',
          fontSize: '12px',
          padding: '1px 4px',
          borderRadius: '4px',
          background: 'var(--bg-panel-subtle)',
          border: '1px solid var(--border-color)',
        }}
      >
        {children}
      </code>
    );
  }

  const lang = className?.replace('language-', '') || '';

  const handleCopy = () => {
    navigator.clipboard?.writeText(codeText);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  return (
    <div
      style={{
        margin: '6px 0',
        border: '1px solid var(--border-color)',
        borderRadius: 'var(--radius-sm)',
        background: 'var(--bg-panel-subtle)',
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '4px 10px',
          borderBottom: '1px solid var(--border-color)',
          fontSize: '11px',
          color: 'var(--text-muted)',
        }}
      >
        <span>{lang || 'code'}</span>
        <button
          type="button"
          onClick={handleCopy}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '4px',
            background: 'transparent',
            border: 0,
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            fontSize: '11px',
          }}
        >
          {copied ? <Check size={12} /> : <Copy size={12} />}
          <span>{copied ? 'Copied' : 'Copy'}</span>
        </button>
      </div>
      <pre
        style={{
          margin: 0,
          padding: '10px 12px',
          overflowX: 'auto',
          fontFamily: 'var(--font-mono)',
          fontSize: '12px',
          lineHeight: 1.5,
          color: 'var(--text-primary)',
        }}
      >
        <code>{codeText}</code>
      </pre>
    </div>
  );
}

/**
 * Ensures lines inside an indented fenced code block (common inside numbered/bulleted lists)
 * keep at least the opening fence's indentation so CommonMark doesn't prematurely close the fence.
 */
export function normalizeMarkdownFences(markdown: string): string {
  const lines = markdown.split('\n');
  let openFence: { indent: string; char: string; len: number } | null = null;
  const out: string[] = [];

  for (const line of lines) {
    if (!openFence) {
      const openMatch = line.match(/^([ \t]*)(`{3,}|~{3,})(.*)$/);
      if (openMatch) {
        const [, indent, fence] = openMatch;
        openFence = { indent, char: fence[0], len: fence.length };
      }
      out.push(line);
      continue;
    }

    const closeMatch = line.match(/^([ \t]*)(`{3,}|~{3,})[ \t]*$/);
    if (closeMatch && closeMatch[2][0] === openFence.char && closeMatch[2].length >= openFence.len) {
      out.push(`${openFence.indent}${closeMatch[2]}`);
      openFence = null;
      continue;
    }

    if (openFence.indent.length > 0 && line.trim().length > 0) {
      const leading = line.match(/^[ \t]*/)?.[0] || '';
      if (leading.length < openFence.indent.length) {
        out.push(`${openFence.indent}${line.slice(leading.length)}`);
        continue;
      }
    }
    out.push(line);
  }

  return out.join('\n');
}

export function MessageResponse({
  children,
  isStreaming = false,
}: {
  children: string;
  isStreaming?: boolean;
}) {
  const normalized = normalizeMarkdownFences(children);
  return (
    <div className="gram-markdown" style={{ fontSize: '13px', lineHeight: 1.6 }}>
      <Streamdown
        plugins={{ mermaid: mermaidPlugin }}
        isAnimating={isStreaming}
        animated={
          isStreaming
            ? {
                animation: 'fadeIn',
                duration: 120,
                easing: 'ease-out',
                stagger: 12,
                sep: 'word',
                maxBacklogMs: 180,
              }
            : false
        }
        shikiTheme={['github-dark', 'github-dark']}
        components={{
          p({ children: pChildren }) {
            return <p style={{ margin: '0 0 8px 0' }}>{pChildren}</p>;
          },
          ul({ children: ulChildren }) {
            return <ul style={{ margin: '0 0 8px 0', paddingLeft: '18px' }}>{ulChildren}</ul>;
          },
          ol({ children: olChildren }) {
            return <ol style={{ margin: '0 0 8px 0', paddingLeft: '18px' }}>{olChildren}</ol>;
          },
        }}
      >
        {normalized}
      </Streamdown>
    </div>
  );
}
