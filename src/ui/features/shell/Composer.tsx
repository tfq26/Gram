import {
  CornerDownRightIcon,
  PencilIcon,
  XIcon,
  FileTextIcon,
  PlusIcon,
  PaperclipIcon,
  TelescopeIcon,
  GlobeIcon,
  ChevronDownIcon,
  CheckIcon,
  Volume2Icon,
  MicIcon,
  ArrowUpIcon,
  SquareIcon,
} from 'lucide-react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupTextarea,
  InputGroupButton,
} from '../../components/ui/input-group';
import {
  Message,
} from '../../components/ui/message';
import {
  getModelLogoPath,
} from '../../lib/modelLogos';
import {
  Input,
} from '../../components/ui/input';
import React from 'react';
import {
  type ChatTab,
  type ReplyReference,
  type ModelItem,
} from '../../lib/types';



export interface ComposerProps {
  formRef: React.RefObject<HTMLFormElement | null>;
  onSubmit: (event: React.FormEvent<HTMLFormElement>) => Promise<void>;
  fileInputRef: React.RefObject<HTMLInputElement | null>;
  setAttachments: React.Dispatch<React.SetStateAction<{ name: string; }[]>>;
  messageQueue: { id: string; tabId: string; content: string; replyTo?: { id: string; role: "user" | "assistant"; content: string; }; }[];
  activeTab: ChatTab;
  editingQueueId: string | null;
  editingQueueText: string;
  setEditingQueueText: React.Dispatch<React.SetStateAction<string>>;
  handleSaveQueuedEdit: (id: string) => void;
  setEditingQueueId: React.Dispatch<React.SetStateAction<string | null>>;
  handleSteerQueuedMessage: (item: { id: string; tabId: string; content: string; replyTo?: { id: string; role: "user" | "assistant"; content: string; }; }) => Promise<void>;
  handleRemoveQueuedMessage: (id: string) => void;
  replyingTo: ReplyReference | null;
  setReplyingTo: React.Dispatch<React.SetStateAction<ReplyReference | null>>;
  attachments: { name: string; }[];
  voiceError: string | null;
  textareaRef: React.RefObject<HTMLTextAreaElement | null>;
  prompt: string;
  setPrompt: React.Dispatch<React.SetStateAction<string>>;
  voiceRecording: boolean;
  isPlanMode: boolean;
  planBrainDumpBySession: Record<string, boolean>;
  planBySession: Record<string, string>;
  composerPlaceholder: string;
  setContextMenuOpen: React.Dispatch<React.SetStateAction<boolean>>;
  contextMenuOpen: boolean;
  appMode: "chat" | "code";
  selectedModel: ModelItem;
  setComposerModelOpen: React.Dispatch<React.SetStateAction<boolean>>;
  setEffortMenuOpen: React.Dispatch<React.SetStateAction<boolean>>;
  composerModelOpen: boolean;
  selectModel: (modelId: string) => void;
  byokMenuModels: ModelItem[];
  openRouterMenuModels: ModelItem[];
  hideBuiltinModels: boolean;
  builtinMenuModels: ModelItem[];
  handleHideBuiltinModelsChange: (hide: boolean) => void;
  effort: "low" | "medium" | "high";
  permissionMode: "autonomous" | "safe" | "ask";
  updatePermissionMode: (next: "autonomous" | "safe" | "ask") => void;
  effortMenuOpen: boolean;
  setEffort: React.Dispatch<React.SetStateAction<"low" | "medium" | "high">>;
  contextUsageBySession: Record<string, { inputTokens: number; outputTokens: number; cacheReadTokens: number; cacheWriteTokens: number; estimatedContextTokens: number; contextWindow: number; usagePercent: number; estimatedCostUsd: number; compactedCount: number; }>;
  voiceMode: boolean;
  setVoiceMode: React.Dispatch<React.SetStateAction<boolean>>;
  stopSpeechPlayback: () => void;
  setVoiceError: React.Dispatch<React.SetStateAction<string | null>>;
  toggleVoiceRecording: () => Promise<void>;
  isExecuting: boolean;
  handleStop: (opts?: { fromSteer?: boolean; }) => Promise<void>;
}

export function Composer({ formRef, onSubmit, fileInputRef, setAttachments, messageQueue, activeTab, editingQueueId, editingQueueText, setEditingQueueText, handleSaveQueuedEdit, setEditingQueueId, handleSteerQueuedMessage, handleRemoveQueuedMessage, replyingTo, setReplyingTo, attachments, voiceError, textareaRef, prompt, setPrompt, voiceRecording, isPlanMode, planBrainDumpBySession, planBySession, composerPlaceholder, setContextMenuOpen, contextMenuOpen, appMode, selectedModel, setComposerModelOpen, setEffortMenuOpen, composerModelOpen, selectModel, byokMenuModels, openRouterMenuModels, hideBuiltinModels, builtinMenuModels, handleHideBuiltinModelsChange, effort, permissionMode, updatePermissionMode, effortMenuOpen, setEffort, contextUsageBySession, voiceMode, setVoiceMode, stopSpeechPlayback, setVoiceError, toggleVoiceRecording, isExecuting, handleStop }: ComposerProps) {
  return (
    <form ref={formRef} onSubmit={onSubmit} className="mx-auto w-full max-w-[836px] px-7 pb-4">
        <input
          ref={fileInputRef}
          type="file"
          multiple
          hidden
          onChange={(e) => {
            const files = e.target.files;
            if (!files) return;
            setAttachments((prev) => [...prev, ...Array.from(files).map((f) => ({ name: f.name }))]);
          }}
        />
        {(() => {
          const tabQueue = messageQueue.filter((q) => q.tabId === activeTab.id);
          if (tabQueue.length === 0) return null;
          return (
            <div className="mb-2 rounded-xl border border-border bg-[var(--bg-panel)] px-3 py-2 text-xs">
              <div className="mb-1.5 flex items-center justify-between text-[11px] text-muted-foreground">
                <span>Queued ({tabQueue.length})</span>
                <span>Runs in order · Steer interrupts current turn</span>
              </div>
              <div className="flex flex-col gap-1.5">
                {tabQueue.map((item) => (
                  <div
                    key={item.id}
                    className="flex items-center justify-between gap-2 rounded-lg border border-border/70 bg-[var(--bg-panel-subtle)] px-2.5 py-1.5"
                  >
                    {editingQueueId === item.id ? (
                      <div className="flex flex-1 items-center gap-1.5">
                        <input
                          type="text"
                          value={editingQueueText}
                          autoFocus
                          onChange={(e) => setEditingQueueText(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') {
                              e.preventDefault();
                              handleSaveQueuedEdit(item.id);
                            } else if (e.key === 'Escape') {
                              e.preventDefault();
                              setEditingQueueId(null);
                              setEditingQueueText('');
                            }
                          }}
                          className="min-w-0 flex-1 rounded border border-border bg-[var(--bg-panel)] px-2 py-1 text-xs text-foreground outline-none"
                        />
                        <button
                          type="button"
                          onClick={() => handleSaveQueuedEdit(item.id)}
                          className="rounded border border-border px-2 py-0.5 text-[11px] text-foreground hover:bg-[var(--bg-hover)]"
                        >
                          Save
                        </button>
                        <button
                          type="button"
                          onClick={() => {
                            setEditingQueueId(null);
                            setEditingQueueText('');
                          }}
                          className="rounded px-1.5 py-0.5 text-[11px] text-muted-foreground hover:text-foreground"
                        >
                          Cancel
                        </button>
                      </div>
                    ) : (
                      <>
                        <span className="min-w-0 flex-1 truncate text-foreground" title={item.content}>
                          {item.content}
                        </span>
                        <div className="flex shrink-0 items-center gap-1">
                          <button
                            type="button"
                            title="Steer now (interrupts current run and sends this)"
                            onClick={() => void handleSteerQueuedMessage(item)}
                            className="inline-flex items-center gap-1 rounded border border-border px-1.5 py-0.5 text-[11px] text-foreground hover:bg-[var(--bg-hover)]"
                          >
                            <CornerDownRightIcon size={11} />
                            <span>Steer</span>
                          </button>
                          <button
                            type="button"
                            title="Edit queued message"
                            aria-label="Edit queued message"
                            onClick={() => {
                              setEditingQueueId(item.id);
                              setEditingQueueText(item.content);
                            }}
                            className="rounded p-1 text-muted-foreground hover:text-foreground"
                          >
                            <PencilIcon size={11} />
                          </button>
                          <button
                            type="button"
                            title="Remove from queue"
                            aria-label="Remove from queue"
                            onClick={() => handleRemoveQueuedMessage(item.id)}
                            className="rounded p-1 text-muted-foreground hover:text-foreground"
                          >
                            <XIcon size={12} />
                          </button>
                        </div>
                      </>
                    )}
                  </div>
                ))}
              </div>
            </div>
          );
        })()}
        <InputGroup>
          {replyingTo && (
            <div className="flex items-center justify-between gap-2 border-b border-border px-3 py-1.5 text-xs text-muted-foreground">
              <div className="min-w-0 truncate">
                Replying to {replyingTo.role === 'user' ? 'you' : 'assistant'}:{' '}
                <span className="text-foreground">{replyingTo.content.slice(0, 120)}</span>
              </div>
              <button
                type="button"
                aria-label="Cancel reply"
                className="shrink-0 rounded p-0.5 text-muted-foreground hover:text-foreground"
                onClick={() => setReplyingTo(null)}
              >
                <XIcon size={12} />
              </button>
            </div>
          )}
          {attachments.length > 0 && (
            <InputGroupAddon align="block-start">
              {attachments.map((file, idx) => (
                <span
                  key={`${file.name}-${idx}`}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel-subtle)] px-2 py-1 text-xs text-[var(--text-primary)]"
                >
                  <FileTextIcon size={12} className="text-[var(--text-muted)]" />
                  <span className="max-w-[160px] truncate">{file.name}</span>
                  <button
                    type="button"
                    aria-label={`Remove ${file.name}`}
                    className="rounded p-0.5 text-[var(--text-muted)] hover:text-[var(--text-primary)]"
                    onClick={() => setAttachments((prev) => prev.filter((_, i) => i !== idx))}
                  >
                    <XIcon size={12} />
                  </button>
                </span>
              ))}
            </InputGroupAddon>
          )}
          {voiceError && (
            <div className="px-3 pt-1 text-xs text-[var(--danger)]" role="status">{voiceError}</div>
          )}
          <InputGroupTextarea
            ref={textareaRef}
            value={prompt}
            onChange={(e) => {
              setPrompt(e.target.value);
              e.target.style.height = 'auto';
              e.target.style.height = `${Math.min(e.target.scrollHeight, 160)}px`;
            }}
            placeholder={
              voiceRecording
                ? 'Listening... speak naturally'
                : isPlanMode && planBrainDumpBySession[activeTab.sessionId] && !planBySession[activeTab.sessionId]
                  ? 'Jot down what you want to build — rough thoughts, features, goals...'
                  : composerPlaceholder
            }
            aria-label="Message"
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                formRef.current?.requestSubmit();
              }
            }}
          />
          <InputGroupAddon align="block-end">
            <div style={{ position: 'relative' }} data-popover-root="context">
              <InputGroupButton
                type="button"
                size="icon-sm"
                variant="outline"
                aria-label="Add context"
                onClick={() => setContextMenuOpen((v) => !v)}
              >
                <PlusIcon size={15} />
              </InputGroupButton>
              {contextMenuOpen && (
                <div
                  className="user-profile-popover"
                  style={{
                    display: 'flex',
                    position: 'absolute',
                    left: 0,
                    bottom: 'calc(100% + 6px)',
                    width: '185px',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--border-color)',
                    borderRadius: 'var(--radius-md)',
                    padding: '4px',
                    zIndex: 9999,
                    flexDirection: 'column',
                    gap: '2px',
                  }}
                >
                  <button
                    type="button"
                    className="shadcn-context-menu-item"
                    onClick={() => {
                      setContextMenuOpen(false);
                      fileInputRef.current?.click();
                    }}
                  >
                    <PaperclipIcon size={13} style={{ marginRight: '6px' }} />
                    Add Photos & Files
                  </button>
                  <div className="shadcn-context-menu-separator" />
                  <button
                    type="button"
                    className="shadcn-context-menu-item"
                    onClick={() => {
                      setContextMenuOpen(false);
                      setPrompt((p) => (p ? `${p} ` : '') + '@research ');
                    }}
                  >
                    <TelescopeIcon size={13} style={{ marginRight: '6px' }} />
                    Deep Research
                  </button>
                  <button
                    type="button"
                    className="shadcn-context-menu-item"
                    onClick={() => {
                      setContextMenuOpen(false);
                      setPrompt((p) => (p ? `${p} ` : '') + '@web ');
                    }}
                  >
                    <GlobeIcon size={13} style={{ marginRight: '6px' }} />
                    Web Search
                  </button>
                </div>
              )}
            </div>
            <div className="ml-auto flex items-center gap-1.5">
              {appMode === 'code' && (
              <div style={{ position: 'relative' }} data-popover-root="composer-model">
                <InputGroupButton
                  type="button"
                  size="sm"
                  variant="outline"
                  aria-label={`Model: ${selectedModel.name}`}
                  onClick={() => {
                    setComposerModelOpen((v) => !v);
                    setEffortMenuOpen(false);
                    setContextMenuOpen(false);
                  }}
                >
                  <img
                    src={getModelLogoPath(selectedModel.id, selectedModel.provider)}
                    alt=""
                    width={13}
                    height={13}
                    className="shrink-0"
                  />
                  <span className="max-w-[140px] truncate">{selectedModel.name}</span>
                  <ChevronDownIcon size={12} />
                </InputGroupButton>
                {composerModelOpen && (
                  <div
                    className="user-profile-popover"
                    style={{
                      display: 'flex',
                      position: 'absolute',
                      right: 0,
                      bottom: 'calc(100% + 6px)',
                      width: '240px',
                      maxHeight: '300px',
                      overflowY: 'auto',
                      background: 'var(--bg-panel)',
                      border: '1px solid var(--border-color)',
                      borderRadius: 'var(--radius-md)',
                      padding: '4px',
                      zIndex: 9999,
                      flexDirection: 'column',
                      gap: '2px',
                    }}
                  >
                    <button
                      key="auto"
                      type="button"
                      className="shadcn-context-menu-item justify-between gap-2"
                      onClick={() => {
                        selectModel('auto');
                        setComposerModelOpen(false);
                      }}
                    >
                      <span className="flex min-w-0 items-center gap-2">
                        <img src={getModelLogoPath('auto', 'bedrock + openrouter')} alt="" width={13} height={13} className="shrink-0" />
                        <span className="truncate">Auto</span>
                      </span>
                      {activeTab.modelId === 'auto' && <CheckIcon size={12} className="shrink-0" />}
                    </button>

                    {byokMenuModels.length > 0 && (
                      <>
                        <div className="my-1 border-t border-[var(--border-color)]" />
                        <div className="px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-[var(--text-muted)]">
                          Your BYOK
                        </div>
                        {byokMenuModels.map((m) => (
                          <button
                            key={m.id}
                            type="button"
                            className="shadcn-context-menu-item justify-between gap-2"
                            onClick={() => {
                              selectModel(m.id);
                              setComposerModelOpen(false);
                            }}
                          >
                            <span className="flex min-w-0 items-center gap-2">
                              <img src={getModelLogoPath(m.id, m.provider)} alt="" width={13} height={13} className="shrink-0" />
                              <span className="truncate">{m.name}</span>
                            </span>
                            {m.id === activeTab.modelId && <CheckIcon size={12} className="shrink-0" />}
                          </button>
                        ))}
                      </>
                    )}

                    {openRouterMenuModels.length > 0 && (
                      <>
                        <div className="my-1 border-t border-[var(--border-color)]" />
                        <div className="px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-[var(--text-muted)]">
                          OpenRouter
                        </div>
                        {openRouterMenuModels.map((m) => (
                          <button
                            key={m.id}
                            type="button"
                            className="shadcn-context-menu-item justify-between gap-2"
                            onClick={() => {
                              selectModel(m.id);
                              setComposerModelOpen(false);
                            }}
                          >
                            <span className="flex min-w-0 items-center gap-2">
                              <img src={getModelLogoPath(m.id, m.provider)} alt="" width={13} height={13} className="shrink-0" />
                              <span className="truncate">{m.name}</span>
                            </span>
                            {m.id === activeTab.modelId && <CheckIcon size={12} className="shrink-0" />}
                          </button>
                        ))}
                      </>
                    )}

                    {!hideBuiltinModels && builtinMenuModels.length > 0 && (
                      <>
                        <div className="my-1 border-t border-[var(--border-color)]" />
                        <div className="px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-[var(--text-muted)]">
                          Gram / Lyra
                        </div>
                        {builtinMenuModels.map((m) => (
                          <button
                            key={m.id}
                            type="button"
                            className="shadcn-context-menu-item justify-between gap-2"
                            onClick={() => {
                              selectModel(m.id);
                              setComposerModelOpen(false);
                            }}
                          >
                            <span className="flex min-w-0 items-center gap-2">
                              <img src={getModelLogoPath(m.id, m.provider)} alt="" width={13} height={13} className="shrink-0" />
                              <span className="truncate">{m.name}</span>
                            </span>
                            {m.id === activeTab.modelId && <CheckIcon size={12} className="shrink-0" />}
                          </button>
                        ))}
                      </>
                    )}

                    <div className="my-1 border-t border-[var(--border-color)]" />
                    <button
                      type="button"
                      className="shadcn-context-menu-item justify-between gap-2 text-[11px] text-[var(--text-muted)]"
                      onClick={() => handleHideBuiltinModelsChange(!hideBuiltinModels)}
                    >
                      <span>{hideBuiltinModels ? 'Show Gram / Lyra models' : 'Hide Gram / Lyra models'}</span>
                    </button>
                  </div>
                )}
              </div>
              )}

              <div style={{ position: 'relative' }} data-popover-root="effort">
                <InputGroupButton
                  type="button"
                  size="sm"
                  variant="outline"
                  title="Click for Effort & Permissions · ⌘+Click to cycle permission mode"
                  aria-label={`Effort: ${effort}, Permissions: ${permissionMode}`}
                  onClick={(e) => {
                    if (e.metaKey || e.ctrlKey) {
                      e.preventDefault();
                      const order: Array<'autonomous' | 'safe' | 'ask'> = ['autonomous', 'safe', 'ask'];
                      const next = order[(order.indexOf(permissionMode) + 1) % order.length]!;
                      updatePermissionMode(next);
                      return;
                    }
                    setEffortMenuOpen((v) => !v);
                    setComposerModelOpen(false);
                    setContextMenuOpen(false);
                  }}
                >
                  <span className="text-[var(--text-muted)]">Effort</span>
                  <span className="capitalize">{effort}</span>
                  {permissionMode !== 'autonomous' && (
                    <span className="text-[10px] text-amber-400 capitalize">· {permissionMode}</span>
                  )}
                  <ChevronDownIcon size={12} />
                </InputGroupButton>
                {effortMenuOpen && (
                  <div
                    className="user-profile-popover"
                    style={{
                      display: 'flex',
                      position: 'absolute',
                      right: 0,
                      bottom: 'calc(100% + 6px)',
                      width: '188px',
                      background: 'var(--bg-panel)',
                      border: '1px solid var(--border-color)',
                      borderRadius: 'var(--radius-md)',
                      padding: '4px',
                      zIndex: 9999,
                      flexDirection: 'column',
                      gap: '2px',
                    }}
                  >
                    <div className="px-2 py-1 text-[10px] font-medium uppercase tracking-[0.06em] text-[var(--text-muted)]">
                      Effort
                    </div>
                    {(['low', 'medium', 'high'] as const).map((level) => (
                      <button
                        key={level}
                        type="button"
                        className="shadcn-context-menu-item justify-between capitalize"
                        onClick={() => {
                          setEffort(level);
                          setEffortMenuOpen(false);
                        }}
                      >
                        <span>{level}</span>
                        {effort === level && <CheckIcon size={12} />}
                      </button>
                    ))}
                    <div className="my-1 border-t border-[var(--border-color)]" />
                    <div className="flex items-center justify-between px-2 py-1 text-[10px] font-medium uppercase tracking-[0.06em] text-[var(--text-muted)]">
                      <span>Permissions</span>
                      <span className="font-mono text-[9px] opacity-70">⌘+Click</span>
                    </div>
                    {([
                      { id: 'autonomous', label: 'Autonomous' },
                      { id: 'safe', label: 'Safe' },
                      { id: 'ask', label: 'Ask' },
                    ] as const).map((perm) => (
                      <button
                        key={perm.id}
                        type="button"
                        className="shadcn-context-menu-item justify-between"
                        onClick={() => {
                          updatePermissionMode(perm.id);
                          setEffortMenuOpen(false);
                        }}
                      >
                        <span>{perm.label}</span>
                        {permissionMode === perm.id && <CheckIcon size={12} />}
                      </button>
                    ))}
                  </div>
                )}
              </div>

              {(() => {
                const ctx = contextUsageBySession[activeTab.sessionId];
                if (!ctx || !ctx.contextWindow) return null;
                const fmtK = (n: number) =>
                  n >= 1_000_000 ? `${(n / 1_000_000).toFixed(1)}M` : n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
                const pct = Math.min(100, Math.max(0, ctx.usagePercent || 0));
                const tone =
                  pct >= 85
                    ? 'border-red-500/35 text-red-400'
                    : pct >= 70
                      ? 'border-amber-500/35 text-amber-400'
                      : 'border-[var(--border-color)] text-[var(--text-muted)]';
                const title = [
                  `Context: ${ctx.estimatedContextTokens.toLocaleString()} / ${ctx.contextWindow.toLocaleString()} (${pct}%)`,
                  `Input: ${ctx.inputTokens.toLocaleString()} · Output: ${ctx.outputTokens.toLocaleString()}`,
                  ctx.cacheReadTokens > 0 ? `Cache read: ${ctx.cacheReadTokens.toLocaleString()}` : '',
                  ctx.estimatedCostUsd > 0 ? `Est. cost: $${ctx.estimatedCostUsd.toFixed(4)}` : '',
                  ctx.compactedCount > 0 ? `Compacted: ${ctx.compactedCount}x` : '',
                ]
                  .filter(Boolean)
                  .join(' | ');
                return (
                  <span
                    title={title}
                    className={`inline-flex h-7 items-center rounded-md border px-2 font-mono text-[11px] tabular-nums ${tone}`}
                  >
                    {fmtK(ctx.estimatedContextTokens)} / {fmtK(ctx.contextWindow)} ({pct}%)
                  </span>
                );
              })()}

              <InputGroupButton
                type="button"
                size="icon-sm"
                variant={voiceMode ? 'default' : 'outline'}
                aria-label={voiceMode ? 'Turn voice responses off' : 'Turn voice responses on'}
                aria-pressed={voiceMode}
                onClick={() => {
                  setVoiceMode((enabled) => {
                    if (enabled) stopSpeechPlayback();
                    return !enabled;
                  });
                  setVoiceError(null);
                }}
              >
                <Volume2Icon size={14} />
              </InputGroupButton>

              <InputGroupButton
                type="button"
                size="icon-sm"
                variant={voiceRecording ? 'default' : 'outline'}
                aria-label={voiceRecording ? 'Stop voice recording' : 'Record voice message'}
                aria-pressed={voiceRecording}
                onClick={() => void toggleVoiceRecording()}
              >
                <MicIcon size={14} />
              </InputGroupButton>

              {!isExecuting ? (
                <InputGroupButton
                  type="submit"
                  variant="default"
                  size="icon-sm"
                  disabled={!prompt.trim()}
                  aria-label="Send"
                >
                  <ArrowUpIcon size={14} />
                </InputGroupButton>
              ) : (
                <>
                  {prompt.trim() && (
                    <InputGroupButton
                      type="submit"
                      variant="outline"
                      size="icon-sm"
                      aria-label="Add to queue"
                      title="Add to queue (Enter)"
                    >
                      <ArrowUpIcon size={14} />
                    </InputGroupButton>
                  )}
                  <InputGroupButton
                    type="button"
                    variant="outline"
                    size="icon-sm"
                    className="border-red-500/40 bg-red-500/15 text-red-400 hover:bg-red-500/25 hover:text-red-300"
                    onClick={() => void handleStop()}
                    aria-label="Stop"
                    title="Stop (Esc)"
                  >
                    <SquareIcon size={11} fill="currentColor" />
                  </InputGroupButton>
                </>
              )}
            </div>
          </InputGroupAddon>
        </InputGroup>
      </form>
  );
}
