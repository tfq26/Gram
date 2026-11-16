import { createPortal } from 'react-dom';
import {
  ArchiveIcon,
  ChevronRightIcon,
  Columns2Icon,
  CopyIcon,
  MessageSquareDotIcon,
  PencilIcon,
  PinIcon,
  Trash2Icon,
} from 'lucide-react';
import type { ChatSessionSummary } from '../../lib/types';

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
  onSplit: (chat: ChatSessionSummary) => void;
  onArchive: (sessionId: string) => void;
  onDelete: (chat: ChatSessionSummary) => void;
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
  onSplit,
  onArchive,
  onDelete,
}: ChatContextMenuProps) {
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
      <div className="shadcn-context-menu-separator" />
      <button
        type="button"
        className="shadcn-context-menu-item justify-between"
        onClick={() => {
          navigator.clipboard?.writeText(displayTitle).catch(() => {});
          onClose();
        }}
      >
        <span className="flex items-center gap-2">
          <CopyIcon size={13} />
          <span>Copy</span>
        </span>
        <ChevronRightIcon size={12} className="text-[var(--text-muted)]" />
      </button>
      <button
        type="button"
        className="shadcn-context-menu-item justify-between"
        onClick={() => onSplit(chat)}
      >
        <span className="flex items-center gap-2">
          <Columns2Icon size={13} />
          <span>Split</span>
        </span>
        <ChevronRightIcon size={12} className="text-[var(--text-muted)]" />
      </button>
      <div className="shadcn-context-menu-separator" />
      <button
        type="button"
        className="shadcn-context-menu-item gap-2"
        onClick={() => onArchive(chat.sessionId)}
      >
        <ArchiveIcon size={13} />
        <span>Archive</span>
      </button>
      <button
        type="button"
        className="shadcn-context-menu-item gap-2"
        style={{ color: 'var(--danger)' }}
        onClick={() => onDelete(chat)}
      >
        <Trash2Icon size={13} />
        <span>Delete</span>
      </button>
    </div>,
    document.body
  );
}
