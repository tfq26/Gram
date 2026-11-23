import React, { useState } from 'react';
import { ArrowUpIcon, XIcon } from 'lucide-react';

export function QuickCaptureBar({
  onClose,
}: {
  onClose: () => void;
}) {
  const [text, setText] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const prompt = text.trim();
    if (!prompt || submitting) return;
    setSubmitting(true);
    try {
      const sessionId = `capture-${Date.now()}`;
      await fetch('/api/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sessionId,
          prompt,
          mode: 'chat',
        }),
      });
      setText('');
      onClose();
    } catch {
      setSubmitting(false);
    }
  }

  return (
    <form
      onSubmit={(e) => void handleSubmit(e)}
      data-tauri-drag-region
      className="flex h-screen w-screen items-center gap-2.5 px-3.5 text-[var(--text-primary)]"
    >
      <span className="h-2 w-2 shrink-0 rounded-full bg-[var(--accent)]" />
      <input
        autoFocus
        type="text"
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Escape') onClose();
        }}
        placeholder="Quick capture to Gram... (Enter to send, Esc to close)"
        className="flex-1 bg-transparent text-sm text-[var(--text-primary)] placeholder:text-[var(--text-muted)] outline-none"
      />
      <button
        type="submit"
        disabled={submitting || !text.trim()}
        aria-label="Send quick capture"
        className="inline-flex h-7 w-7 items-center justify-center rounded-md border border-[var(--border-color)] bg-[var(--bg-active)] text-[var(--text-primary)] disabled:opacity-40 cursor-pointer"
      >
        <ArrowUpIcon size={13} />
      </button>
      <button
        type="button"
        onClick={onClose}
        aria-label="Close quick capture"
        className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
      >
        <XIcon size={13} />
      </button>
    </form>
  );
}
