import { useState } from 'react';
import { createPortal } from 'react-dom';
import {
  ArchiveIcon,
  ChevronRightIcon,
  CopyIcon,
  FileTextIcon,
  GitBranchIcon,
  HashIcon,
  MessageSquareDotIcon,
  MessageSquareTextIcon,
  PencilIcon,
  PinIcon,
  SparklesIcon,
} from 'lucide-react';
import type { ChatSessionSummary } from '../../lib/types';

export type ChatCopyMode = 'last' | 'full' | 'summary' | 'id';

export interface ChatContextMenuProps {
  menu: { runId: string; top: number; left: number };
  chat: ChatSessionSummary;
  displayTitle: string;
  isPinned: boolean;
  isUnread: boolean;
  onClose: () => void;
  onRename: (sessionId: string, currentTitle: string) => void;
  onTogglePin: (sessionId: string) => void;
  onToggleUnread: (sessionId: string) => void;
  onFork: (sessionId: string) => void;
  onCopyChat: (chat: ChatSessionSummary, mode: ChatCopyMode) => void;
  onArchive: (sessionId: string) => void;
}

export function ChatContextMenu({
  menu,
  chat,
  displayTitle,
  isPinned,
  isUnread,
  onClose,
  onRename,
  onTogglePin,
  onToggleUnread,
  onFork,
  onCopyChat,
  onArchive,
}: ChatContextMenuProps) {
  const [copyMenuOpen, setCopyMenuOpen] = useState(false);

  return createPortal(
    <div
      className="fixed z-[9999] flex w-[188px] flex-col gap-0.5 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-1.5 shadow-2xl"
      style={{ top: menu.top, left: menu.left }}
      onClick={(e) => e.stopPropagation()}
    >
      <button
        type="button"
        className="shadcn-context-menu-item gap-2"
        onClick={() => {
          onClose();
          onRename(chat.sessionId, displayTitle);
        }}
      >
        <PencilIcon size={13} />
        <span>Rename</span>
      </button>
      <button
        type="button"
        className="shadcn-context-menu-item gap-2"
        onClick={() => onTogglePin(chat.sessionId)}
      >
        <PinIcon size={13} />
        <span>{isPinned ? 'Unpin' : 'Pin'}</span>
      </button>
      <button
        type="button"
        className="shadcn-context-menu-item gap-2"
        onClick={() => onToggleUnread(chat.sessionId)}
      >
        <MessageSquareDotIcon size={13} />
        <span>{isUnread ? 'Mark Read' : 'Mark Unread'}</span>
      </button>
      <button
        type="button"
        className="shadcn-context-menu-item gap-2"
        onClick={() => {
          onClose();
          onFork(chat.sessionId);
        }}
      >
        <GitBranchIcon size={13} />
        <span>Fork chat</span>
      </button>
      <div className="shadcn-context-menu-separator" />
      <div
        className="relative"
        onMouseEnter={() => setCopyMenuOpen(true)}
        onMouseLeave={() => setCopyMenuOpen(false)}
      >
        <button
          type="button"
          className="shadcn-context-menu-item justify-between"
          onClick={() => setCopyMenuOpen((v) => !v)}
        >
          <span className="flex items-center gap-2">
            <CopyIcon size={13} />
            <span>Copy</span>
          </span>
          <ChevronRightIcon size={12} className="text-[var(--text-muted)]" />
        </button>

        {copyMenuOpen && (
          <div className="absolute left-[calc(100%+4px)] top-0 z-[10000] flex w-[212px] flex-col gap-0.5 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-1.5 shadow-2xl">
            <button
              type="button"
              className="shadcn-context-menu-item gap-2"
              onClick={() => {
                onClose();
                onCopyChat(chat, 'last');
              }}
            >
              <MessageSquareTextIcon size={13} />
              <span>Copy last interaction</span>
            </button>
            <button
              type="button"
              className="shadcn-context-menu-item gap-2"
              onClick={() => {
                onClose();
                onCopyChat(chat, 'full');
              }}
            >
              <FileTextIcon size={13} />
              <span>Copy entire chat</span>
            </button>
            <button
              type="button"
              className="shadcn-context-menu-item gap-2"
              onClick={() => {
                onClose();
                onCopyChat(chat, 'summary');
              }}
            >
              <SparklesIcon size={13} />
              <span>Copy chat summary</span>
            </button>
            <div className="shadcn-context-menu-separator" />
            <button
              type="button"
              className="shadcn-context-menu-item gap-2"
              onClick={() => {
                onClose();
                onCopyChat(chat, 'id');
              }}
            >
              <HashIcon size={13} />
              <span>Copy chat ID</span>
            </button>
          </div>
        )}
      </div>
      <div className="shadcn-context-menu-separator" />
      <button
        type="button"
        title="Archive chat (restorable for 30 days)"
        className="shadcn-context-menu-item justify-between gap-2"
        onClick={() => onArchive(chat.sessionId)}
      >
        <span className="flex items-center gap-2">
          <ArchiveIcon size={13} />
          <span>Archive</span>
        </span>
        <span className="text-[10px] text-[var(--text-muted)]">30d</span>
      </button>
    </div>,
    document.body
  );
}
