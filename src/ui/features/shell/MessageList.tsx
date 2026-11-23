import {
  DottedGlowBackground,
} from '../../components/ui/dotted-glow-background';
import {
  Empty,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
  EmptyDescription,
} from '../../components/ui/empty';
import {
  MessageCircleDashedIcon,
  CircleAlertIcon,
  TerminalIcon,
  RotateCcwIcon,
  ChevronRightIcon,
  CopyIcon,
  LoaderCircleIcon,
  FileTextIcon,
  CheckIcon,
  ReplyIcon,
  Volume2Icon,
} from 'lucide-react';
import {
  MessageScroller,
  MessageScrollerViewport,
  MessageScrollerContent,
  MessageScrollerItem,
  MessageScrollerButton,
} from '../../components/ui/message-scroller';
import {
  type ChatEntry,
  type ChatTab,
} from '../../lib/types';
import {
  Message,
  MessageContent,
} from '../../components/ui/message';
import {
  BubbleGroup,
  Bubble,
  BubbleContent,
} from '../../components/ui/bubble';
import {
  Trace,
  TraceLine,
  TraceIcon,
  TraceTitle,
} from '../../components/ui/trace';
import {
  TaskRow,
  TraceStepList,
} from '../chat/TraceRows';
import {
  Item,
  ItemMedia,
  ItemContent,
  ItemTitle,
  ItemDescription,
  ItemActions,
} from '../../components/ui/item';
import {
  Button,
} from '../../components/ui/button';
import {
  MessageResponse,
} from '../../components/ai-elements/Message';
import React, {
  lazy,
} from 'react';
import {
  type IslandActivity,
} from '../../components/ui/dynamic-island';
import {
  type DockSurface,
} from '../workspace/WorkspaceDock';

const PlanArtifactCard = lazy(() =>
  import('../workspace/PlanArtifactCard').then((m) => ({ default: m.PlanArtifactCard }))
);

export interface MessageListProps {
  isPlanMode: boolean;
  activeMessages: ChatEntry[];
  planBySession: Record<string, string>;
  activeTab: ChatTab;
  planBrainDumpBySession: Record<string, boolean>;
  setPlanBrainDumpBySession: React.Dispatch<React.SetStateAction<Record<string, boolean>>>;
  activeProjectId: string;
  activeProjectLabel: string;
  currentPlanIdeas: string[];
  setPrompt: React.Dispatch<React.SetStateAction<string>>;
  textareaRef: React.RefObject<HTMLTextAreaElement | null>;
  appMode: "chat" | "code";
  chatEmptyCopy: { title: string; description: string; };
  isExecuting: boolean;
  copiedMessageId: string | null;
  handleApproveActivity: (activity: IslandActivity, approved: boolean) => Promise<void>;
  handleRetryMessage: (userMsg: ChatEntry) => Promise<void>;
  setActiveDockSurface: React.Dispatch<React.SetStateAction<DockSurface | null>>;
  handleApprovePlanAndBuild: (opts: { markdown: string; isolateWorktree: boolean; }) => void;
  handoffBusyId: string | null;
  handleContinueInCode: (sourceMsg: ChatEntry) => Promise<void>;
  handleCopyMessage: (msg: ChatEntry) => void;
  handleReplyMessage: (msg: ChatEntry) => void;
  speakingMessageId: string | null;
  stopSpeechPlayback: () => void;
  speakSummary: (text: string, messageId?: string) => Promise<void>;
  handleEscalate: (promptText?: string) => Promise<void>;
  handleOpenPreview: (url: string) => void;
  handlePullLyraPatch: (runId: string, sessionId?: string) => Promise<void>;
  handleCommitLyraLayer: (message: ChatEntry) => Promise<void>;
  handleRollbackLyraLayer: (message: ChatEntry) => Promise<void>;
  planSectionRef: React.RefObject<HTMLDivElement | null>;
  setPlanBySession: React.Dispatch<React.SetStateAction<Record<string, string>>>;
  saveSessionPlan: (sessionId: string, markdown: string) => Promise<void>;
}

export function MessageList({ isPlanMode, activeMessages, planBySession, activeTab, planBrainDumpBySession, setPlanBrainDumpBySession, activeProjectId, activeProjectLabel, currentPlanIdeas, setPrompt, textareaRef, appMode, chatEmptyCopy, isExecuting, copiedMessageId, handleApproveActivity, handleRetryMessage, setActiveDockSurface, handleApprovePlanAndBuild, handoffBusyId, handleContinueInCode, handleCopyMessage, handleReplyMessage, speakingMessageId, stopSpeechPlayback, speakSummary, handleEscalate, handleOpenPreview, handlePullLyraPatch, handleCommitLyraLayer, handleRollbackLyraLayer, planSectionRef, setPlanBySession, saveSessionPlan }: MessageListProps) {
  return (
    <div className="relative" style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        {isPlanMode && (
          <DottedGlowBackground
            className="pointer-events-none mask-radial-to-90% mask-radial-at-center opacity-55 [[data-theme=bone]_&]:opacity-35"
            opacity={0.8}
            gap={12}
            radius={1.5}
            colorLightVar="--color-neutral-500"
            glowColorLightVar="--color-neutral-600"
            colorDarkVar="--color-neutral-500"
            glowColorDarkVar="--color-sky-800"
            backgroundOpacity={0}
            speedMin={0.3}
            speedMax={1.4}
            speedScale={1}
          />
        )}
        {isPlanMode && activeMessages.length === 0 && !planBySession[activeTab.sessionId] ? (
          <div className="relative z-10 flex w-full flex-1 flex-col justify-start gap-6 px-6 pt-10 pb-8 overflow-y-auto">
            <div className="mx-auto flex w-full max-w-[900px] flex-col gap-6">
              {planBrainDumpBySession[activeTab.sessionId] ? (
                <div className="mx-auto flex w-full max-w-[720px] flex-col items-center gap-3 py-8 text-center">
                  <h1 className="text-4xl font-semibold tracking-tight text-[var(--text-primary)]">
                    What do you want to build?
                  </h1>
                  <p className="mt-1 max-w-[620px] text-lg leading-relaxed text-[var(--text-muted)]">
                    Jot down your rough thoughts, goals, or features in the box below. I’ll break them into components, give feedback, and help shape a structured plan.
                  </p>
                  <button
                    type="button"
                    onClick={() =>
                      setPlanBrainDumpBySession((prev) => ({ ...prev, [activeTab.sessionId]: false }))
                    }
                    className="mt-2 text-xs text-[var(--text-muted)] hover:text-[var(--text-primary)] cursor-pointer"
                  >
                    ← Back to ideas
                  </button>
                </div>
              ) : (
                <>
                  <div className="text-center">
                    <h1 className="text-4xl font-semibold tracking-tight text-[var(--text-primary)]">
                      Plan mode
                    </h1>
                    <p className="mt-1.5 text-lg text-[var(--text-muted)]">
                      Read-only codebase inspection, architecture design, and step-by-step planning.
                    </p>
                  </div>
                  <div className="mx-auto flex w-full max-w-[860px] flex-col gap-5 py-2">
                    <div className="text-center text-sm font-medium text-[var(--text-secondary)]">
                      Explore an idea for {activeProjectId !== 'local' ? activeProjectLabel : 'this workspace'}
                    </div>
                    <div className="grid gap-3.5 sm:grid-cols-3">
                      {currentPlanIdeas.map((idea) => (
                        <button
                          key={idea}
                          type="button"
                          onClick={() => {
                            setPrompt(`Let's plan: ${idea} — `);
                            requestAnimationFrame(() => textareaRef.current?.focus());
                          }}
                          className="flex items-center justify-center rounded-xl border border-[color-mix(in_srgb,var(--border-color)_45%,transparent)] bg-[var(--bg-panel)] px-4 py-3 text-center transition-colors hover:bg-[var(--bg-hover)] cursor-pointer"
                        >
                          <span className="truncate text-base font-medium text-[var(--text-primary)]">
                            {idea}
                          </span>
                        </button>
                      ))}
                    </div>
                    <div className="flex justify-center pt-1">
                      <button
                        type="button"
                        onClick={() => {
                          setPlanBrainDumpBySession((prev) => ({ ...prev, [activeTab.sessionId]: true }));
                          requestAnimationFrame(() => textareaRef.current?.focus());
                        }}
                        className="rounded-lg border border-[var(--border-color)] px-3.5 py-1.5 text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
                      >
                        Write from scratch
                      </button>
                    </div>
                  </div>
                </>
              )}
            </div>
          </div>
        ) : activeMessages.length === 0 && !planBySession[activeTab.sessionId] ? (
          <Empty>
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <MessageCircleDashedIcon size={20} />
              </EmptyMedia>
              <EmptyTitle>
                {appMode === 'chat'
                  ? chatEmptyCopy.title
                  : activeProjectId !== 'local'
                    ? `What should we build in ${activeProjectLabel}?`
                    : 'What should we build?'}
              </EmptyTitle>
              <EmptyDescription>
                {appMode === 'chat'
                  ? chatEmptyCopy.description
                  : 'Ask the agent to read your code, run commands, and make changes.'}
              </EmptyDescription>
            </EmptyHeader>
          </Empty>
        ) : (
          <div className="relative z-10 flex min-h-0 flex-1 flex-col">
          <MessageScroller>
            <MessageScrollerViewport>
              <MessageScrollerContent aria-busy={isExecuting}>
                {activeMessages
                  .reduce<ChatEntry[][]>((groups, msg) => {
                    const lastGroup = groups[groups.length - 1];
                    if (lastGroup && lastGroup[0]?.role === msg.role) {
                      lastGroup.push(msg);
                    } else {
                      groups.push([msg]);
                    }
                    return groups;
                  }, [])
                  .map((group) => {
                    const firstMsg = group[0]!;
                    const isUser = firstMsg.role === 'user';
                    return (
                      <MessageScrollerItem
                        key={firstMsg.id}
                        messageId={firstMsg.id}
                        scrollAnchor={isUser}
                      >
                        <Message align={isUser ? 'end' : 'start'}>
                          <MessageContent>
                            <BubbleGroup className={isUser ? 'items-end' : 'items-start'}>
                              {group.map((msg) => {
                                const streaming =
                                  !isUser &&
                                  isExecuting &&
                                  msg === activeMessages[activeMessages.length - 1];
                                const isCopied = copiedMessageId === msg.id;

                                return (
                                  <div
                                    key={msg.id}
                                    className={`group/msg flex w-full flex-col gap-1.5 ${isUser ? 'items-end' : 'items-start'}`}
                                  >
                                    {msg.syncConflictOf && (
                                      <div
                                        role="status"
                                        className="flex max-w-[85%] items-center gap-1.5 rounded-md border border-amber-500/25 bg-amber-500/[0.06] px-2 py-1 text-[10px] text-[var(--text-muted)]"
                                      >
                                        <CircleAlertIcon size={11} className="shrink-0 text-amber-500" />
                                        <span>Different text arrived from another device. This version is preserved alongside the original.</span>
                                      </div>
                                    )}
                                    {isUser ? (
                                      <Bubble align="end" variant="secondary">
                                        <BubbleContent className="!bg-[var(--bg-panel-subtle)] !border-[var(--border-color)] text-[var(--text-primary)]">
                                          {msg.replyTo && (
                                            <div className="mb-1.5 border-l-2 border-border pl-2 text-xs text-muted-foreground">
                                              {msg.replyTo.content.slice(0, 120)}
                                              {msg.replyTo.content.length > 120 ? '...' : ''}
                                            </div>
                                          )}
                                          <div>{msg.content}</div>
                                        </BubbleContent>
                                      </Bubble>
                                    ) : (
                                      <div
                                        className={`flex w-full flex-col gap-3 text-sm leading-relaxed text-[var(--text-primary)] ${
                                          streaming ? 'gram-message-turn--streaming' : 'gram-message-turn'
                                        }`}
                                      >
                                        {msg.tasks && msg.tasks.length > 0 && (
                                          <Trace>
                                            {msg.tasks.map((taskItem) => (
                                              <TaskRow key={taskItem.id} task={taskItem} streaming={streaming} />
                                            ))}
                                          </Trace>
                                        )}
                                        {msg.steps && msg.steps.length > 0 && (
                                          <TraceStepList steps={msg.steps} streaming={streaming} busy={isExecuting} />
                                        )}
                                        {msg.approval && msg.approval.state === 'approval-requested' && (
                                          <Item variant="outline" size="sm">
                                            <ItemMedia variant="icon">
                                              <TerminalIcon size={14} />
                                            </ItemMedia>
                                            <ItemContent>
                                              <ItemTitle>Run {msg.approval.command}?</ItemTitle>
                                              <ItemDescription>
                                                {msg.approval.description || 'The command runs in this project.'}
                                              </ItemDescription>
                                            </ItemContent>
                                            <ItemActions>
                                              <Button
                                                type="button"
                                                size="sm"
                                                variant="outline"
                                                onClick={() =>
                                                  handleApproveActivity(
                                                    {
                                                      runId: msg.approval!.runId,
                                                      sessionId: msg.approval!.sessionId,
                                                      title: msg.approval!.command,
                                                      stage: 'paused',
                                                    },
                                                    false
                                                  )
                                                }
                                              >
                                                Deny
                                              </Button>
                                              <Button
                                                type="button"
                                                size="sm"
                                                onClick={() =>
                                                  handleApproveActivity(
                                                    {
                                                      runId: msg.approval!.runId,
                                                      sessionId: msg.approval!.sessionId,
                                                      title: msg.approval!.command,
                                                      stage: 'paused',
                                                    },
                                                    true
                                                  )
                                                }
                                              >
                                                Approve
                                              </Button>
                                            </ItemActions>
                                          </Item>
                                        )}
                                        {msg.approval && msg.approval.state === 'approval-responded' && (
                                          <Trace>
                                            <TraceLine>
                                              <TraceIcon>
                                                <TerminalIcon size={13} />
                                              </TraceIcon>
                                              <TraceTitle>
                                                {msg.approval.approved ? 'Ran' : 'Denied'} {msg.approval.command}
                                              </TraceTitle>
                                            </TraceLine>
                                          </Trace>
                                        )}
                                         {msg.isError ? (
                                           <div
                                             role="alert"
                                             className="rounded-xl border border-red-500/20 bg-red-500/[0.05] px-4 py-3 text-sm text-[var(--text-primary)]"
                                           >
                                             <div className="flex items-center justify-between gap-3">
                                               <div className="flex items-center gap-2 text-xs font-medium text-red-500">
                                                 <CircleAlertIcon size={14} className="shrink-0" />
                                                 <span>Message failed</span>
                                               </div>
                                               {!isExecuting && (() => {
                                                 const msgIdx = activeMessages.findIndex((m) => m.id === msg.id);
                                                 const prevUser = msgIdx > 0
                                                   ? [...activeMessages.slice(0, msgIdx)].reverse().find((m) => m.role === 'user' && m.content.trim())
                                                   : undefined;
                                                 return prevUser ? (
                                                   <button
                                                     type="button"
                                                     onClick={() => handleRetryMessage(prevUser)}
                                                     className="inline-flex items-center gap-1.5 rounded-lg bg-red-500/12 px-2.5 py-1 text-[11px] font-medium text-red-500 transition-colors hover:bg-red-500/20 cursor-pointer"
                                                   >
                                                     <RotateCcwIcon size={11} />
                                                     <span>Retry</span>
                                                   </button>
                                                 ) : null;
                                               })()}
                                             </div>
                                             <div className="mt-1.5 text-[13px] leading-relaxed">
                                               <MessageResponse isStreaming={false}>
                                                 {msg.content || 'Execution failed.'}
                                               </MessageResponse>
                                             </div>
                                             {msg.diagnostics && (
                                               <details className="group mt-2.5 border-t border-red-500/12 pt-2 text-[11px]">
                                                 <summary className="flex cursor-pointer list-none select-none items-center justify-between gap-2 text-[var(--text-muted)] transition-colors hover:text-[var(--text-secondary)]">
                                                   <span className="inline-flex items-center gap-1.5 min-w-0">
                                                     <ChevronRightIcon
                                                       size={12}
                                                       className="shrink-0 transition-transform duration-150 group-open:rotate-90"
                                                     />
                                                     <span className="font-medium text-[var(--text-secondary)]">Diagnostics</span>
                                                     <span className="truncate opacity-80">
                                                       {String((msg.diagnostics as any).requestedModelId || 'auto')} →{' '}
                                                       {String((msg.diagnostics as any).resolvedModelId || 'unknown')}
                                                       {(msg.diagnostics as any).phase ? ` · ${String((msg.diagnostics as any).phase)}` : ''}
                                                       {(msg.diagnostics as any).elapsedMs !== undefined
                                                         ? ` · ${String((msg.diagnostics as any).elapsedMs)}ms`
                                                         : ''}
                                                     </span>
                                                   </span>
                                                   <button
                                                     type="button"
                                                     onClick={(e) => {
                                                       e.preventDefault();
                                                       e.stopPropagation();
                                                       void navigator.clipboard?.writeText(
                                                         JSON.stringify(msg.diagnostics, null, 2)
                                                       );
                                                     }}
                                                     className="inline-flex shrink-0 items-center gap-1 rounded px-1.5 py-0.5 text-[11px] font-medium text-[var(--text-muted)] transition-colors hover:bg-red-500/10 hover:text-[var(--text-primary)] cursor-pointer"
                                                   >
                                                     <CopyIcon size={11} />
                                                     <span>Copy JSON</span>
                                                   </button>
                                                 </summary>
                                                 <pre className="mt-2 max-h-56 overflow-auto whitespace-pre-wrap break-all font-mono text-[10.5px] leading-relaxed text-[var(--text-secondary)]">
                                                   {JSON.stringify(msg.diagnostics, null, 2)}
                                                 </pre>
                                               </details>
                                             )}
                                           </div>
                                         ) : msg.content ? (
                                           <MessageResponse isStreaming={false}>
                                             {msg.content}
                                           </MessageResponse>
                                         ) : streaming ? (
                                           <div className="flex w-full items-center justify-center py-6">
                                             <div className="inline-flex items-center gap-2 rounded-full border border-[var(--border-color)] bg-[var(--bg-panel)] px-3.5 py-1.5 text-xs font-medium text-[var(--text-secondary)]">
                                               <LoaderCircleIcon size={13} className="animate-spin text-[var(--accent-text)]" />
                                               <span>{isPlanMode ? 'Planning…' : 'Thinking…'}</span>
                                             </div>
                                           </div>
                                         ) : null}
                                        {!isUser &&
                                          !streaming &&
                                          !msg.isError &&
                                          msg.id === activeMessages[activeMessages.length - 1]?.id &&
                                          Boolean(planBySession[activeTab.sessionId]) &&
                                          /##\s+Tasks\b/i.test(planBySession[activeTab.sessionId] || '') &&
                                          !/\b(?:critical questions|please answer|what would you like to lock in)\b/i.test(
                                            msg.content || ''
                                          ) && (
                                            <div className="mt-2 flex w-full flex-col gap-2.5 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-3.5 text-xs">
                                              <div className="flex items-center justify-between gap-2">
                                                <span className="font-semibold text-[var(--text-primary)]">
                                                  I&apos;ve compiled a plan — here&apos;s a summary
                                                </span>
                                                <button
                                                  type="button"
                                                  onClick={() => setActiveDockSurface('plan')}
                                                  className="inline-flex items-center gap-1 rounded-md border border-[var(--border-color)] px-2 py-1 text-[11px] font-medium text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
                                                >
                                                  <FileTextIcon size={12} />
                                                  <span>View full doc</span>
                                                </button>
                                              </div>
                                              <div className="flex flex-wrap items-center gap-2 pt-0.5">
                                                <Button
                                                  type="button"
                                                  size="sm"
                                                  onClick={() =>
                                                    handleApprovePlanAndBuild({
                                                      markdown: planBySession[activeTab.sessionId] || '',
                                                      isolateWorktree: false,
                                                    })
                                                  }
                                                >
                                                  Yes, go ahead
                                                </Button>
                                                <Button
                                                  type="button"
                                                  size="sm"
                                                  variant="outline"
                                                  onClick={() => {
                                                    requestAnimationFrame(() => textareaRef.current?.focus());
                                                  }}
                                                >
                                                  No, here&apos;s some more
                                                </Button>
                                              </div>
                                            </div>
                                          )}
                                        {msg.workers && msg.workers.length > 0 && (
                                          <div className="cloud-progress-list">
                                            {msg.workers.map((w, idx) => (
                                              <div key={w.id || idx} className="cloud-progress-row">
                                                <span>{w.name || w.id}</span>
                                                <span className="status-text">{w.status || 'running'}</span>
                                              </div>
                                            ))}
                                          </div>
                                        )}
                                        {!isUser && appMode === 'chat' && msg.codingHandoffPrompt && (
                                          <div className="mt-2 flex items-center justify-between gap-3 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel-subtle)] px-3.5 py-2.5 text-xs">
                                            <div className="flex flex-col gap-0.5">
                                              <span className="font-semibold text-[var(--text-primary)]">Continue in a new coding chat?</span>
                                              <span className="text-[var(--text-secondary)]">Summarize this conversation and open a Code workspace with file, terminal, and browser tools.</span>
                                            </div>
                                            <button
                                              type="button"
                                              className="btn-new-chat shrink-0"
                                              style={{ margin: 0, width: 'auto', padding: '6px 12px', fontSize: '12px' }}
                                              disabled={handoffBusyId === msg.id}
                                              onClick={() => handleContinueInCode(msg)}
                                            >
                                              {handoffBusyId === msg.id ? 'Summarizing...' : 'Continue in Code →'}
                                            </button>
                                          </div>
                                        )}
                                      </div>
                                    )}

                                    <div
                                      className={`result-actions flex items-center gap-1.5 ${isUser ? 'justify-end' : 'justify-start'}`}
                                      style={{ marginTop: 0 }}
                                    >
                                      {!msg.isError && msg.content && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          onClick={() => handleCopyMessage(msg)}
                                        >
                                          {isCopied ? <CheckIcon size={12} /> : <CopyIcon size={12} />}
                                          <span>{isCopied ? 'Copied' : 'Copy'}</span>
                                        </button>
                                      )}
                                      {!msg.isError && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          onClick={() => handleReplyMessage(msg)}
                                        >
                                          <ReplyIcon size={12} />
                                          <span>Reply</span>
                                        </button>
                                      )}
                                      {!isUser && !msg.isError && msg.content && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          onClick={() => {
                                            if (speakingMessageId === msg.id) stopSpeechPlayback();
                                            else void speakSummary(msg.content, msg.id);
                                          }}
                                        >
                                          <Volume2Icon size={12} />
                                          <span>{speakingMessageId === msg.id ? 'Stop' : 'Listen'}</span>
                                        </button>
                                      )}
                                      {isUser && msg.content && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          disabled={isExecuting}
                                          onClick={() => handleRetryMessage(msg)}
                                          title="Retry this prompt"
                                        >
                                          <RotateCcwIcon size={12} />
                                          <span>Retry</span>
                                        </button>
                                      )}
                                      {!isUser && msg.steps && msg.steps.length > 0 && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          onClick={() => handleEscalate(msg.originalPrompt)}
                                        >
                                          Escalate to swarm
                                        </button>
                                      )}
                                      {!isUser && msg.previewUrl && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          onClick={() => handleOpenPreview(msg.previewUrl!)}
                                        >
                                          Open preview
                                        </button>
                                      )}
                                      {!isUser && msg.runId && (msg.lane === 'full-swarm' || (msg.workers && msg.workers.length > 0)) && (
                                        <button
                                          className="message-action-btn"
                                          type="button"
                                          onClick={() => void handlePullLyraPatch(msg.runId!, msg.remoteSessionId)}
                                        >
                                          Pull swarm patch
                                        </button>
                                      )}
                                      {!isUser && msg.reviewLayerId && (
                                        <>
                                          <button className="message-action-btn" type="button" onClick={() => void handleCommitLyraLayer(msg)}>
                                            Commit to workspace
                                          </button>
                                          <button className="message-action-btn" type="button" onClick={() => void handleRollbackLyraLayer(msg)}>
                                            Discard overlay
                                          </button>
                                        </>
                                      )}
                                    </div>
                                  </div>
                                );
                              })}
                            </BubbleGroup>
                          </MessageContent>
                        </Message>
                      </MessageScrollerItem>
                    );
                  })}
                {isPlanMode && planBySession[activeTab.sessionId] && (
                  <MessageScrollerItem>
                    <div ref={planSectionRef} className="pt-4 pb-6">
                      <PlanArtifactCard
                        markdown={planBySession[activeTab.sessionId] || ''}
                        onChange={(nextMd) =>
                          setPlanBySession((prev) => ({ ...prev, [activeTab.sessionId]: nextMd }))
                        }
                        onSave={(md) => saveSessionPlan(activeTab.sessionId, md)}
                        onApproveAndBuild={handleApprovePlanAndBuild}
                      />
                    </div>
                  </MessageScrollerItem>
                )}
              </MessageScrollerContent>
            </MessageScrollerViewport>
            <MessageScrollerButton />
          </MessageScroller>
          </div>
        )}
      </div>
  );
}
