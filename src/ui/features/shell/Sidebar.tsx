import React from 'react';
import {
  ChevronsLeftIcon,
  ChevronsRightIcon,
  MoreVerticalIcon,
  PlusIcon,
  FolderIcon,
  SettingsIcon,
  PinIcon,
} from 'lucide-react';
import {
  type ChatSessionSummary,
  type ChatTab,
  type ProjectItem,
  type ChatEntry,
} from '../../lib/types';
import {
  type IslandActivity,
} from '../../components/ui/dynamic-island';



export interface SidebarProps {
  sidebarOpen: boolean;
  isResizingSidebar: boolean;
  sidebarWidth: number;
  openNewChatTab: (overrideProjectId?: string) => void;
  appMode: "chat" | "code";
  toggleSidebarExpanded: () => void;
  setSidebarOpen: React.Dispatch<React.SetStateAction<boolean>>;
  chatSessions: ChatSessionSummary[];
  chatMeta: { pinned: Record<string, boolean>; unread: Record<string, boolean>; archived: Record<string, boolean>; titles: Record<string, string>; };
  activeTab: ChatTab;
  openChatMenu: { runId: string; top: number; left: number; } | null;
  renamingRunId: string | null;
  renameDraft: string;
  setRenameDraft: React.Dispatch<React.SetStateAction<string>>;
  commitRename: (sessionId: string) => Promise<void>;
  setRenamingRunId: React.Dispatch<React.SetStateAction<string | null>>;
  updateChatMeta: (updater: (prev: { pinned: Record<string, boolean>; unread: Record<string, boolean>; archived: Record<string, boolean>; titles: Record<string, string>; }) => { pinned: Record<string, boolean>; unread: Record<string, boolean>; archived: Record<string, boolean>; titles: Record<string, string>; }) => void;
  handleSelectChat: (sessionId: string) => Promise<void>;
  setOpenChatMenu: React.Dispatch<React.SetStateAction<{ runId: string; top: number; left: number; } | null>>;
  openProjectHub: (project?: ProjectItem) => void;
  projects: ProjectItem[];
  showArchivedProjects: boolean;
  expandedProjects: Record<string, boolean>;
  activeProjectId: string;
  islandActivities: IslandActivity[];
  setActiveProjectId: React.Dispatch<React.SetStateAction<string>>;
  activeMessages: ChatEntry[];
  setTabs: React.Dispatch<React.SetStateAction<ChatTab[]>>;
  setExpandedProjects: React.Dispatch<React.SetStateAction<Record<string, boolean>>>;
  setShowArchivedProjects: React.Dispatch<React.SetStateAction<boolean>>;
  archivedChats: { sessionId: string; title: string; mode: "chat" | "code"; daysRemaining: number; }[];
  setShowArchivedChats: React.Dispatch<React.SetStateAction<boolean>>;
  showArchivedChats: boolean;
  handleRestoreChat: (sessionId: string) => Promise<void>;
}

export function Sidebar({ sidebarOpen, isResizingSidebar, sidebarWidth, openNewChatTab, appMode, toggleSidebarExpanded, setSidebarOpen, chatSessions, chatMeta, activeTab, openChatMenu, renamingRunId, renameDraft, setRenameDraft, commitRename, setRenamingRunId, updateChatMeta, handleSelectChat, setOpenChatMenu, openProjectHub, projects, showArchivedProjects, expandedProjects, activeProjectId, islandActivities, setActiveProjectId, activeMessages, setTabs, setExpandedProjects, setShowArchivedProjects, archivedChats, setShowArchivedChats, showArchivedChats, handleRestoreChat }: SidebarProps) {
  return (
    <aside
      className={`pane pane-left ${sidebarOpen ? 'open' : 'closed'}${isResizingSidebar ? ' resizing' : ''}`}
      style={
        sidebarOpen
          ? ({
              '--sidebar-width': `${sidebarWidth}px`,
              width: `${sidebarWidth}px`,
              minWidth: `${sidebarWidth}px`,
            } as React.CSSProperties)
          : undefined
      }
    >
      <div className="pane-content" style={{ width: '100%', minWidth: 0 }}>
        <div className="rail-header" style={{ gap: '8px' }}>
          <button
            className="btn-new-chat"
            type="button"
            style={{ margin: 0, flex: 1 }}
            onClick={() => openNewChatTab()}
          >
            {appMode === 'code' ? '+ New code' : '+ New chat'}
          </button>
          <button
            className="btn-pane-toggle"
            type="button"
            title={sidebarWidth >= 340 ? 'Restore sidebar width' : 'Expand sidebar'}
            aria-label={sidebarWidth >= 340 ? 'Restore sidebar width' : 'Expand sidebar'}
            onClick={toggleSidebarExpanded}
          >
            {sidebarWidth >= 340 ? <ChevronsLeftIcon size={14} /> : <ChevronsRightIcon size={14} />}
          </button>
          <button
            className="btn-pane-toggle"
            type="button"
            title="Close chats"
            onClick={() => setSidebarOpen(false)}
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" /><path d="M9 3v18" /></svg>
          </button>
        </div>
        <div
          className="conversation-list"
          style={{ width: '100%', maxHeight: 'calc(100vh - 108px)', overflowY: 'auto' }}
        >
          {(() => {
            const visibleChats = chatSessions
              .filter((c) =>
                !chatMeta.archived[c.sessionId] &&
                (c.mode || 'chat') === appMode &&
                (appMode === 'code' || !c.projectId || c.projectId === 'local')
              )
              .sort((a, b) => {
                const pinDiff = Number(Boolean(chatMeta.pinned[b.sessionId])) - Number(Boolean(chatMeta.pinned[a.sessionId]));
                if (pinDiff !== 0) return pinDiff;
                return (Date.parse(b.updatedAt || '') || 0) - (Date.parse(a.updatedAt || '') || 0);
              });

            const now = new Date();
            const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
            const startOfYesterday = startOfToday - 86_400_000;
            const startOfThisWeek = startOfToday - 6 * 86_400_000;

            const formatChatTimestamp = (iso?: string): string => {
              const ms = iso ? Date.parse(iso) : NaN;
              if (!Number.isFinite(ms)) return '';
              const d = new Date(ms);
              if (ms >= startOfToday) {
                return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
              }
              if (ms >= startOfYesterday) return 'Yesterday';
              if (ms >= startOfThisWeek) {
                return d.toLocaleDateString([], { weekday: 'short' });
              }
              return d.toLocaleDateString([], { month: 'short', day: 'numeric' });
            };

            const groupChatsByTime = (chats: ChatSessionSummary[]) => {
              const buckets: Array<{ label: string; items: ChatSessionSummary[] }> = [
                { label: 'Today', items: [] },
                { label: 'Yesterday', items: [] },
                { label: 'This Week', items: [] },
                { label: 'Older', items: [] },
              ];
              for (const chat of chats) {
                const ms = Date.parse(chat.updatedAt || '');
                if (!Number.isFinite(ms) || ms >= startOfToday) {
                  buckets[0]!.items.push(chat);
                } else if (ms >= startOfYesterday) {
                  buckets[1]!.items.push(chat);
                } else if (ms >= startOfThisWeek) {
                  buckets[2]!.items.push(chat);
                } else {
                  buckets[3]!.items.push(chat);
                }
              }
              return buckets.filter((b) => b.items.length > 0);
            };

            const renderChatRow = (c: ChatSessionSummary) => {
              const isActive = activeTab.sessionId === c.sessionId;
              const isUnread = Boolean(chatMeta.unread[c.sessionId]);
              const displayTitle = chatMeta.titles[c.sessionId] || c.title || c.sessionId;
              const timeLabel = formatChatTimestamp(c.updatedAt);
              const menuOpen = openChatMenu?.runId === c.sessionId;

              return (
                <div
                  key={c.sessionId}
                  className={`group/conv relative flex items-center rounded-lg transition-colors ${
                    isActive
                      ? 'bg-[var(--bg-active)] font-medium text-[var(--text-primary)]'
                      : 'text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
                  }`}
                >
                  {renamingRunId === c.sessionId ? (
                    <input
                      type="text"
                      autoFocus
                      value={renameDraft}
                      onChange={(e) => setRenameDraft(e.target.value)}
                      onBlur={() => commitRename(c.sessionId)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          e.preventDefault();
                          commitRename(c.sessionId);
                        } else if (e.key === 'Escape') {
                          setRenamingRunId(null);
                        }
                      }}
                      className="w-full rounded-[var(--radius-sm)] border border-[var(--border-focus)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-[14.5px] text-[var(--text-primary)] focus:outline-none"
                    />
                  ) : (
                    <>
                      <button
                        type="button"
                        className="flex min-w-0 flex-1 items-center gap-2.5 bg-transparent px-2.5 py-2 text-left text-[14.5px] leading-snug focus:outline-none"
                        onClick={() => {
                          if (isUnread) {
                            updateChatMeta((prev) => ({
                              ...prev,
                              unread: { ...prev.unread, [c.sessionId]: false },
                            }));
                          }
                          handleSelectChat(c.sessionId);
                        }}
                      >
                        {isUnread && (
                          <span
                            className="h-1.5 w-1.5 shrink-0 rounded-full"
                            style={{ backgroundColor: 'var(--accent-text)' }}
                          />
                        )}
                        <span className="truncate">{displayTitle}</span>
                      </button>

                      {timeLabel && (
                        <span
                          className={`shrink-0 pr-2.5 text-xs tabular-nums text-[var(--text-muted)] ${
                            menuOpen ? 'hidden' : 'group-hover/conv:hidden'
                          }`}
                        >
                          {timeLabel}
                        </span>
                      )}

                      <div
                        className={`shrink-0 items-center pr-1 ${
                          menuOpen
                            ? 'flex'
                            : 'hidden group-hover/conv:flex'
                        }`}
                      >
                        <button
                          type="button"
                          title="More options"
                          aria-label="Conversation options"
                          className={`inline-flex h-6 w-6 items-center justify-center rounded-md text-[var(--text-muted)] hover:bg-[var(--bg-active)] hover:text-[var(--text-primary)] ${
                            menuOpen ? 'text-[var(--text-primary)]' : ''
                          }`}
                          onClick={(e) => {
                            e.stopPropagation();
                            const rect = e.currentTarget.getBoundingClientRect();
                            setOpenChatMenu((prev) =>
                              prev?.runId === c.sessionId
                                ? null
                                : { runId: c.sessionId, top: rect.bottom + 4, left: rect.left }
                            );
                          }}
                        >
                          <MoreVerticalIcon size={13} />
                        </button>
                      </div>
                    </>
                  )}
                </div>
              );
            };

            if (appMode === 'code') {
              const pinnedChats = visibleChats.filter((c) => Boolean(chatMeta.pinned[c.sessionId]));
              const localChats = visibleChats.filter(
                (c) => (!c.projectId || c.projectId === 'local') && !chatMeta.pinned[c.sessionId]
              );
              return (
                <div className="flex flex-col gap-3">
                  {pinnedChats.length > 0 && (
                    <div className="flex flex-col gap-1">
                      <div className="px-2 text-xs font-medium text-[var(--text-muted)]">Pinned</div>
                      {pinnedChats.map(renderChatRow)}
                    </div>
                  )}
                  <div className="flex flex-col gap-2">
                    <div className="flex items-center justify-between px-2">
                      <span className="text-xs font-medium text-[var(--text-muted)]">Projects</span>
                      <button
                        type="button"
                        title="New project"
                        className="inline-flex h-5 w-5 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]"
                        onClick={() => openProjectHub()}
                      >
                        <PlusIcon size={12} />
                      </button>
                    </div>
                    {projects
                      .filter((p) => p.id !== 'local' && (!p.archived || showArchivedProjects))
                      .map((proj) => {
                      const projChats = visibleChats.filter(
                        (c) => (c.projectId || 'local') === proj.id && !chatMeta.pinned[c.sessionId]
                      );
                      const expanded = Boolean(expandedProjects[proj.id]);
                      const shown = expanded ? projChats : projChats.slice(0, 3);
                      const isSelectedProj = activeProjectId === proj.id;
                      const projActivities = islandActivities.filter((a) => (a.projectId || 'local') === proj.id);
                      const projNeedsApproval = projActivities.some((a) => Boolean(a.pendingApproval) || a.stage === 'paused');
                      const projRunning = projActivities.some((a) => a.stage === 'executing' || a.isActive);
                      return (
                        <div key={proj.id} className="flex flex-col gap-0.5">
                          <div
                            className={`group/proj flex items-center justify-between rounded-md px-2 py-1.5 text-[13.5px] transition-colors ${
                              isSelectedProj
                                ? 'bg-[var(--bg-active)] font-semibold text-[var(--text-primary)]'
                                : 'font-medium text-[var(--text-muted)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)]'
                            }`}
                          >
                            <button
                              type="button"
                              className="flex min-w-0 flex-1 items-center gap-2 text-left"
                              onClick={() => {
                                setActiveProjectId(proj.id);
                                if (activeMessages.length === 0) {
                                  setTabs((prev) =>
                                    prev.map((t) => (t.id === activeTab.id ? { ...t, projectId: proj.id } : t))
                                  );
                                } else if (activeTab.projectId !== proj.id) {
                                  openNewChatTab(proj.id);
                                }
                              }}
                            >
                              <FolderIcon
                                size={14}
                                className={`shrink-0 ${
                                  isSelectedProj ? 'text-[var(--text-primary)]' : 'text-[var(--text-muted)]'
                                }`}
                              />
                              <span className="truncate">{proj.label}</span>
                              {projNeedsApproval ? (
                                <span
                                  title="Agent waiting for approval"
                                  className="h-2 w-2 shrink-0 rounded-full bg-amber-500"
                                />
                              ) : projRunning ? (
                                <span
                                  title="Agent running"
                                  className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-emerald-500"
                                />
                              ) : null}
                              {proj.files && proj.files.length > 0 && (
                                <span className="rounded bg-[var(--bg-hover)] px-1 text-[10px] text-[var(--text-muted)]">
                                  {proj.files.length}
                                </span>
                              )}
                            </button>
                            <div className="flex items-center gap-0.5 opacity-0 transition-opacity group-hover/proj:opacity-100">
                              <button
                                type="button"
                                title="New coding chat in project"
                                className="inline-flex h-5 w-5 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-active)] hover:text-[var(--text-primary)]"
                                onClick={() => openNewChatTab(proj.id)}
                              >
                                <PlusIcon size={11} />
                              </button>
                              <button
                                type="button"
                                title="Project files & settings"
                                className="inline-flex h-5 w-5 items-center justify-center rounded text-[var(--text-muted)] hover:bg-[var(--bg-active)] hover:text-[var(--text-primary)]"
                                onClick={() => openProjectHub(proj)}
                              >
                                <SettingsIcon size={11} />
                              </button>
                            </div>
                          </div>
                          <div className="flex flex-col gap-0.5 pl-4">
                            {shown.map(renderChatRow)}
                            {projChats.length > 3 && (
                              <button
                                type="button"
                                className="px-2 py-1 text-left text-xs text-[var(--text-muted)] hover:text-[var(--text-primary)]"
                                onClick={() => setExpandedProjects((prev) => ({ ...prev, [proj.id]: !expanded }))}
                              >
                                {expanded ? 'Show less' : 'Show more'}
                              </button>
                            )}
                          </div>
                        </div>
                      );
                    })}
                    {localChats.length > 0 && (
                      <div className="flex flex-col gap-1">
                        <div className="px-2 text-xs font-medium text-[var(--text-muted)]">Local chats</div>
                        {localChats.map(renderChatRow)}
                      </div>
                    )}
                    {projects.some((p) => p.archived) && (
                      <button
                        type="button"
                        className="px-2 py-1 text-left text-xs text-[var(--text-muted)] hover:text-[var(--text-primary)]"
                        onClick={() => setShowArchivedProjects((v) => !v)}
                      >
                        {showArchivedProjects
                          ? 'Hide archived projects'
                          : `Show archived (${projects.filter((p) => p.archived).length})`}
                      </button>
                    )}
                  </div>
                </div>
              );
            }

            const archivedInMode = archivedChats.filter((c) => (c.mode || 'chat') === appMode);
            const pinnedChats = visibleChats.filter((c) => Boolean(chatMeta.pinned[c.sessionId]));
            const unpinnedChats = visibleChats.filter((c) => !chatMeta.pinned[c.sessionId]);
            const timeGroups = groupChatsByTime(unpinnedChats);
            return (
              <>
                {visibleChats.length === 0 ? (
                  <div className="sidebar-empty-state flex flex-1 items-center justify-center text-center text-xs text-[var(--text-muted)]">
                    No recent chats
                  </div>
                ) : (
                  <div className="flex flex-col gap-4">
                    {pinnedChats.length > 0 && (
                      <div className="flex flex-col gap-1">
                        <div className="flex items-center gap-1.5 px-2.5 pb-1 text-xs font-medium text-[var(--text-muted)]">
                          <PinIcon size={12} />
                          <span>Pinned</span>
                        </div>
                        {pinnedChats.map(renderChatRow)}
                      </div>
                    )}
                    {timeGroups.map((group) => (
                      <div key={group.label} className="flex flex-col gap-1">
                        <div className="px-2.5 pb-1 text-xs font-medium text-[var(--text-muted)]">
                          {group.label}
                        </div>
                        {group.items.map(renderChatRow)}
                      </div>
                    ))}
                  </div>
                )}
                {archivedInMode.length > 0 && (
                  <div className="mt-2 flex flex-col gap-1 border-t border-[var(--border-color)] pt-2">
                    <button
                      type="button"
                      className="px-2 py-1 text-left text-xs text-[var(--text-muted)] hover:text-[var(--text-primary)] cursor-pointer"
                      onClick={() => setShowArchivedChats((v) => !v)}
                    >
                      {showArchivedChats
                        ? 'Hide archived chats'
                        : `Show archived (${archivedInMode.length})`}
                    </button>
                    {showArchivedChats &&
                      archivedInMode.map((ac) => (
                        <div
                          key={ac.sessionId}
                          className="flex items-center justify-between gap-2 rounded-md px-2 py-1.5 text-xs text-[var(--text-muted)] hover:bg-[var(--bg-hover)]"
                        >
                          <span className="truncate">{ac.title || ac.sessionId}</span>
                          <div className="flex shrink-0 items-center gap-1.5">
                            <span className="text-[10px] opacity-75">{ac.daysRemaining}d</span>
                            <button
                              type="button"
                              onClick={() => void handleRestoreChat(ac.sessionId)}
                              className="rounded border border-[var(--border-color)] px-1.5 py-0.5 text-[10px] font-medium text-[var(--text-primary)] hover:bg-[var(--bg-active)] cursor-pointer"
                            >
                              Restore
                            </button>
                          </div>
                        </div>
                      ))}
                  </div>
                )}
              </>
            );
          })()}
        </div>
      </div>
    </aside>
  );
}
