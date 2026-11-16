import React, { useRef } from 'react';
import { ArrowUp, Plus, Square } from 'lucide-react';

export interface PromptInputProps {
  value: string;
  onChange: (value: string) => void;
  onSubmit: () => void;
  onStop?: () => void;
  onAttachFiles?: (files: FileList | null) => void;
  attachments?: Array<{ name: string }>;
  onRemoveAttachment?: (index: number) => void;
  isExecuting?: boolean;
  placeholder?: string;
}

export function PromptInput({
  value,
  onChange,
  onSubmit,
  onStop,
  onAttachFiles,
  attachments = [],
  onRemoveAttachment,
  isExecuting = false,
  placeholder = 'Message Gram (type @ for context mentions)...',
}: PromptInputProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (!isExecuting && value.trim()) {
        onSubmit();
      }
    }
  };

  const handleInput = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    onChange(e.target.value);
    const el = textareaRef.current;
    if (el) {
      el.style.height = 'auto';
      el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
    }
  };

  return (
    <div className="composer-wrapper">
      <div className="composer-dock-row">
        <div className="composer-box">
          <input
            ref={fileInputRef}
            type="file"
            multiple
            hidden
            onChange={(e) => onAttachFiles?.(e.target.files)}
          />
          {attachments.length > 0 && (
            <div className="attachment-tray" style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', padding: '6px 10px 0' }}>
              {attachments.map((file, idx) => (
                <button
                  key={`${file.name}-${idx}`}
                  type="button"
                  className="result-action-btn"
                  onClick={() => onRemoveAttachment?.(idx)}
                  title="Remove attachment"
                >
                  {file.name} ×
                </button>
              ))}
            </div>
          )}
          <div className="composer-inner">
            <div className="composer-actions-left">
              <button
                className="btn-composer-icon"
                type="button"
                title="Attach files"
                aria-label="Attach files"
                onClick={() => fileInputRef.current?.click()}
              >
                <Plus size={18} />
              </button>
            </div>
            <textarea
              ref={textareaRef}
              className="composer-textarea"
              rows={1}
              value={value}
              placeholder={placeholder}
              onKeyDown={handleKeyDown}
              onChange={handleInput}
            />
            <div className="composer-actions-right">
              {!isExecuting ? (
                <button
                  className="btn-composer-send"
                  type="button"
                  title="Send"
                  aria-label="Send"
                  onClick={onSubmit}
                >
                  <ArrowUp size={14} />
                </button>
              ) : (
                <button
                  className="btn-composer-send stop-btn"
                  type="button"
                  title="Stop generation"
                  aria-label="Stop generation"
                  onClick={onStop}
                >
                  <Square size={11} fill="currentColor" />
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
