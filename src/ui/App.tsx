import React, { useEffect, useRef, useState } from 'react';
import { ProjectDialog } from './features/workspace/ProjectDialog';
import {
  ArchiveIcon,
  ArrowUpIcon,
  CheckIcon,
  ChevronDownIcon,
  ChevronRightIcon,
  ChevronsLeftIcon,
  ChevronsRightIcon,
  CircleAlertIcon,
  CircleCheckIcon,
  Columns2Icon,
  CopyIcon,
  CornerDownRightIcon,
  FileTextIcon,
  FolderIcon,
  FolderPlusIcon,
  GlobeIcon,
  LoaderCircleIcon,
  MicIcon,
  MessageCircleDashedIcon,
  MessageSquareDotIcon,
  MoreVerticalIcon,
  PaperclipIcon,
  PencilIcon,
  PinIcon,
  PlusIcon,
  ReplyIcon,
  RotateCcwIcon,
  SettingsIcon,
  SquareIcon,
  TelescopeIcon,
  TerminalIcon,
  UploadIcon,
  Volume2Icon,
  Trash2Icon,
  XIcon,
} from 'lucide-react';
import { MessageResponse } from './components/ai-elements/Message';
import { AppErrorScreen } from './components/ui/ErrorBoundary';
import { SettingsView, type LayoutPreset } from './features/settings/SettingsView';
import { WorkspaceDock, type DockSurface } from './features/workspace/WorkspaceDock';
import { PlanArtifactCard } from './features/workspace/PlanArtifactCard';
import { AuthPage, GramMark } from './features/auth/AuthPage';
import { OnboardingWizard } from './features/auth/OnboardingWizard';
import { getModelLogoPath } from './lib/modelLogos';
import { Bubble, BubbleContent, BubbleGroup } from './components/ui/bubble';
import { Button } from './components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from './components/ui/dialog';
import { Field, FieldGroup } from './components/ui/field';
import { Input } from './components/ui/input';
import { Label } from './components/ui/label';
import { DynamicIslandBar, type IslandActivity } from './components/ui/dynamic-island';
import { SystemToastViewport, emitSystemToast } from './components/ui/toast';
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemMedia,
  ItemTitle,
} from './components/ui/item';
import { Message, MessageContent } from './components/ui/message';
import {
  MessageScroller,
  MessageScrollerButton,
  MessageScrollerContent,
  MessageScrollerItem,
  MessageScrollerProvider,
  MessageScrollerViewport,
} from './components/ui/message-scroller';
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from './components/ui/empty';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupTextarea,
} from './components/ui/input-group';
import {
  Trace,
  TraceIcon,
  TraceLine,
  TraceTitle,
} from './components/ui/trace';

import type {
  ApprovalItem,
  AuthState,
  ChatEntry,
  ChatSessionSummary,
  ChatTab,
  DeviceItem,
  ManagedProcessItem,
  ModelItem,
  ProjectItem,
  ReplyReference,
  TaskItem,
  TraceStep,
} from './lib/types';
import { checkpointInspectionSteps, StepRow, TaskRow } from './features/chat/TraceRows';
import { isCodingWorkPrompt } from './features/chat/chatHelpers';
import { ShortcutsDialog } from './features/chat/ShortcutsDialog';
import { ChatContextMenu } from './features/chat/ChatContextMenu';

function createChatMessageId(prefix: string): string {
  const unique = globalThis.crypto?.randomUUID?.() || Math.random().toString(36).slice(2, 12);
  return `${prefix}-${Date.now()}-${unique}`;
}

export function App() {
  const [layoutPreset, setLayoutPreset] = useState<LayoutPreset>(() => {
    try {
      const saved = localStorage.getItem('gram.settings.layoutPreset');
      return saved === 'mid' || saved === 'power' ? saved : 'beginner';
    } catch {
      return 'beginner';
    }
  });
  const [hideBuiltinModels, setHideBuiltinModels] = useState<boolean>(() => {
    try {
      return localStorage.getItem('gram.settings.hideBuiltinModels') === 'true';
    } catch {
      return false;
    }
  });
  const [sidebarOpen, setSidebarOpen] = useState(() => layoutPreset === 'beginner');
  const [sidebarWidth, setSidebarWidth] = useState<number>(() => {
    try {
      const saved = Number(localStorage.getItem('gram.sidebarWidth'));
      if (Number.isFinite(saved) && saved >= 220 && saved <= 520) return saved;
    } catch {}
    return 260;
  });
  const [isResizingSidebar, setIsResizingSidebar] = useState(false);

  const startSidebarResize = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!sidebarOpen) return;
    e.preventDefault();
    const startX = e.clientX;
    const startWidth = sidebarWidth;
    setIsResizingSidebar(true);
    let latestWidth = startWidth;
    const prevCursor = document.body.style.cursor;
    const prevSelect = document.body.style.userSelect;
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';

    const onMouseMove = (moveEvent: MouseEvent) => {
      const next = Math.min(520, Math.max(220, Math.round(startWidth + (moveEvent.clientX - startX))));
      latestWidth = next;
      setSidebarWidth(next);
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = prevCursor;
      document.body.style.userSelect = prevSelect;
      setIsResizingSidebar(false);
      try {
        localStorage.setItem('gram.sidebarWidth', String(latestWidth));
      } catch {}
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  };

  const toggleSidebarExpanded = () => {
    const next = sidebarWidth >= 340 ? 260 : 380;
    setSidebarWidth(next);
    try {
      localStorage.setItem('gram.sidebarWidth', String(next));
    } catch {}
  };
  const [activePage, setActivePage] = useState<'chat' | 'settings' | 'auth' | 'onboarding'>(() => {
    if (typeof window === 'undefined') return 'chat';
    const p = window.location.pathname;
    if (p === '/settings') return 'settings';
    if (p === '/login' || p === '/signup') return 'auth';
    if (p === '/welcome' || p === '/onboarding') return 'onboarding';
    return 'chat';
  });
  const [models, setModels] = useState<ModelItem[]>([]);
  const [modelMenuOpen, setModelMenuOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const [contextMenuOpen, setContextMenuOpen] = useState(false);
  const [authSubmitting, setAuthSubmitting] = useState(false);
  const [googleSubmitting, setGoogleSubmitting] = useState(false);
  const [previewModalUrl, setPreviewModalUrl] = useState<string | null>(null);

  const [tabs, setTabs] = useState<ChatTab[]>(() => {
    const id = `chat-${Date.now()}`;
    let defaultModel = 'auto';
    try {
      defaultModel = localStorage.getItem('gram.settings.defaultModel') || 'auto';
    } catch {}
    return [{ id, title: 'New chat', sessionId: `session-${Date.now()}`, modelId: defaultModel }];
  });
  const [activeTabId, setActiveTabId] = useState<string>(() => tabs[0]!.id);
  const [messagesByTab, setMessagesByTab] = useState<Record<string, ChatEntry[]>>({});

  const [recentTasks, setRecentTasks] = useState<IslandActivity[]>(() => {
    try {
      const raw = localStorage.getItem('gram.cache.recentTasks');
      const parsed = raw ? JSON.parse(raw) : null;
      if (Array.isArray(parsed)) return parsed;
    } catch {}
    return [];
  });
  const [chatSessions, setChatSessions] = useState<ChatSessionSummary[]>(() => {
    try {
      const raw = localStorage.getItem('gram.cache.chatSessions');
      const parsed = raw ? JSON.parse(raw) : null;
      if (Array.isArray(parsed)) return parsed;
    } catch {}
    return [];
  });
  const [projects, setProjects] = useState<ProjectItem[]>(() => {
    try {
      const raw = localStorage.getItem('gram.cache.projects');
      const parsed = raw ? JSON.parse(raw) : null;
      if (Array.isArray(parsed) && parsed.length > 0) return parsed;
    } catch {}
    return [{ id: 'local', label: 'Local workspace' }];
  });
  const [activeProjectId, setActiveProjectId] = useState('local');
  const [devices, setDevices] = useState<DeviceItem[]>(() => {
    try {
      const raw = localStorage.getItem('gram.cache.devices');
      const parsed = raw ? JSON.parse(raw) : null;
      if (Array.isArray(parsed) && parsed.length > 0) return parsed;
    } catch {}
    return [
      { deviceId: 'studio-local', deviceName: 'Local machine', deviceType: 'desktop', kind: 'local', isExecutionHost: true },
    ];
  });
  const [activeDeviceId, setActiveDeviceId] = useState('studio-local');
  const [expandedProjects, setExpandedProjects] = useState<Record<string, boolean>>({});
  const [projectHubId, setProjectHubId] = useState<string | null>(null);
  const [projectHubTab, setProjectHubTab] = useState<'general' | 'git' | 'settings'>('general');
  const [projectGitInfo, setProjectGitInfo] = useState<{
    loading: boolean;
    isGitRepo: boolean;
    branch?: string;
    lastCommit?: string;
    hasGramGuide?: boolean;
    remotes: Array<{ name: string; rawUrl: string; webUrl: string | null }>;
  }>({ loading: false, isGitRepo: false, remotes: [] });
  const [confirmDeleteProject, setConfirmDeleteProject] = useState(false);
  const [showArchivedProjects, setShowArchivedProjects] = useState(false);
  const [archivedChats, setArchivedChats] = useState<
    Array<{ sessionId: string; title: string; mode: 'chat' | 'code'; daysRemaining: number }>
  >([]);
  const [showArchivedChats, setShowArchivedChats] = useState(false);
  const [projectDraft, setProjectDraft] = useState<{ id?: string; label: string; workspacePath: string; instructions: string }>({
    label: '',
    workspacePath: '',
    instructions: '',
  });
  const [pickingFolder, setPickingFolder] = useState(false);
  const [authState, setAuthState] = useState<AuthState>({ connected: false, authenticated: false, user: null });

  const [prompt, setPrompt] = useState('');
  const [activePromptTitle, setActivePromptTitle] = useState<string | null>(null);
  const [attachments, setAttachments] = useState<Array<{ name: string }>>([]);
  const [replyingTo, setReplyingTo] = useState<ReplyReference | null>(null);
  const [copiedMessageId, setCopiedMessageId] = useState<string | null>(null);
  const [effort, setEffort] = useState<'low' | 'medium' | 'high'>(() => {
    try {
      const saved = localStorage.getItem('gram.settings.defaultEffort');
      if (saved === 'low' || saved === 'medium' || saved === 'high') return saved;
    } catch {}
    return 'medium';
  });
  const [composerModelOpen, setComposerModelOpen] = useState(false);
  const [effortMenuOpen, setEffortMenuOpen] = useState(false);
  const [contextUsageBySession, setContextUsageBySession] = useState<
    Record<
      string,
      {
        inputTokens: number;
        outputTokens: number;
        cacheReadTokens: number;
        cacheWriteTokens: number;
        estimatedContextTokens: number;
        contextWindow: number;
        usagePercent: number;
        estimatedCostUsd: number;
        compactedCount: number;
      }
    >
  >({});
  const [appMode, setAppMode] = useState<'chat' | 'code'>(() => {
    try {
      const saved = localStorage.getItem('gram.appMode');
      if (saved === 'code' || saved === 'chat') return saved;
    } catch {}
    return 'chat';
  });
  const [activeDockSurface, setActiveDockSurface] = useState<DockSurface | null>(null);
  const [planBySession, setPlanBySession] = useState<Record<string, string>>({});
  const [planIdeasByKey, setPlanIdeasByKey] = useState<Record<string, string[]>>({});
  const fetchedPlanIdeasRef = useRef<Set<string>>(new Set());
  const [showInlinePlan, setShowInlinePlan] = useState(true);
  const [dockPreviewUrl, setDockPreviewUrl] = useState('http://localhost:5173');
  const planSectionRef = useRef<HTMLDivElement>(null);
  const wasPlanModeRef = useRef(false);
  const isPlanMode = appMode === 'code' && activeDockSurface === 'plan';

  useEffect(() => {
    if (isPlanMode && !wasPlanModeRef.current) {
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          planSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        });
      });
    }
    wasPlanModeRef.current = isPlanMode;
  }, [isPlanMode]);
  const [handoffBusyId, setHandoffBusyId] = useState<string | null>(null);
  useEffect(() => {
    try {
      localStorage.setItem('gram.appMode', appMode);
    } catch {}
  }, [appMode]);
  const [chatMeta, setChatMeta] = useState<{
    pinned: Record<string, boolean>;
    unread: Record<string, boolean>;
    archived: Record<string, boolean>;
    titles: Record<string, string>;
  }>(() => {
    try {
      const raw = localStorage.getItem('gram.chatMeta');
      if (raw) return JSON.parse(raw);
    } catch {}
    return { pinned: {}, unread: {}, archived: {}, titles: {} };
  });
  const [openChatMenu, setOpenChatMenu] = useState<{
    runId: string;
    top: number;
    left: number;
  } | null>(null);
  const [renamingRunId, setRenamingRunId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [theme, setTheme] = useState<string>(() => {
    try {
      return localStorage.getItem('gram.settings.theme') || 'obsidian';
    } catch {
      return 'carbon';
    }
  });
  const [isExecuting, setIsExecuting] = useState(false);
  const [managedProcesses, setManagedProcesses] = useState<ManagedProcessItem[]>([]);
  const [processesOpen, setProcessesOpen] = useState(false);
  const [selectedProcessLogs, setSelectedProcessLogs] = useState<{ id: string; logs: string } | null>(null);
  const [isAppLoading, setIsAppLoading] = useState(true);
  const [activeRunId, setActiveRunId] = useState<string | null>(null);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const [messageQueue, setMessageQueue] = useState<
    Array<{
      id: string;
      tabId: string;
      content: string;
      replyTo?: { id: string; role: 'user' | 'assistant'; content: string };
    }>
  >([]);
  const [editingQueueId, setEditingQueueId] = useState<string | null>(null);
  const [editingQueueText, setEditingQueueText] = useState('');
  const [voiceMode, setVoiceMode] = useState(false);
  const [voiceRecording, setVoiceRecording] = useState(false);
  const [voiceError, setVoiceError] = useState<string | null>(null);
  const [bootError, setBootError] = useState<{ title: string; message: string; details?: string } | null>(null);
  const [actionError, setActionError] = useState<{ message: string; details?: string } | null>(null);
  const [systemWarnings, setSystemWarnings] = useState<
    Array<{
      id: string;
      label: string;
      severity: 'critical' | 'warning';
      ok: boolean;
      message: string;
      action?: 'retry' | 'signin' | 'settings';
    }>
  >([]);
  const [systemWarningsDismissed, setSystemWarningsDismissed] = useState(false);

  const [authMode, setAuthMode] = useState<'signin' | 'signup'>(() =>
    typeof window !== 'undefined' && window.location.pathname === '/signup' ? 'signup' : 'signin'
  );
  const [signupName, setSignupName] = useState('');
  const [loginEmail, setLoginEmail] = useState('');
  const [loginPassword, setLoginPassword] = useState('');
  const [loginError, setLoginError] = useState('');

  const fileInputRef = useRef<HTMLInputElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const activeRequestRef = useRef<AbortController | null>(null);
  const speechRequestRef = useRef<AbortController | null>(null);
  const speechAudioRef = useRef<HTMLAudioElement | null>(null);
  const speechUrlRef = useRef<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const speechRecognitionRef = useRef<any>(null);
  const voiceDraftBaseRef = useRef<string>('');
  const voiceSilenceTimerRef = useRef<number | null>(null);
  const voiceStreamRef = useRef<MediaStream | null>(null);
  const voiceChunksRef = useRef<Blob[]>([]);
  const [speakingMessageId, setSpeakingMessageId] = useState<string | null>(null);
  const messagesByTabRef = useRef(messagesByTab);
  messagesByTabRef.current = messagesByTab;
  const chatSaveQueuesRef = useRef(new Map<string, Promise<void>>());
  const manualTitleOverridesRef = useRef(new Set<string>());
  const steeringQueueItemRef = useRef<{
    id: string;
    tabId: string;
    content: string;
    replyTo?: { id: string; role: 'user' | 'assistant'; content: string };
  } | null>(null);
  const manualStopRef = useRef(false);
  const shortcutActionsRef = useRef<{
    openNewChatTab: () => void;
    switchAppMode: (nextMode: 'chat' | 'code') => void;
    handleRetryMessage: (msg: ChatEntry) => void;
    handleStop: () => void;
    navigateToPage: (page: 'chat' | 'settings' | 'auth' | 'onboarding', opts?: { authMode?: 'signin' | 'signup'; direction?: 'forward' | 'backward' }) => void;
    isExecuting: boolean;
    appMode: 'chat' | 'code';
    activeMessages: ChatEntry[];
    shortcutsOpen: boolean;
    projectHubId: string | null;
  } | null>(null);

  function stopSpeechPlayback() {
    speechRequestRef.current?.abort();
    speechRequestRef.current = null;
    if (speechAudioRef.current) {
      speechAudioRef.current.pause();
      speechAudioRef.current.src = '';
      speechAudioRef.current = null;
    }
    if (speechUrlRef.current) {
      URL.revokeObjectURL(speechUrlRef.current);
      speechUrlRef.current = null;
    }
    if (typeof window !== 'undefined' && 'speechSynthesis' in window) {
      try {
        window.speechSynthesis.cancel();
      } catch {}
    }
    setSpeakingMessageId(null);
  }

  function cancelActiveTurnForBargeIn() {
    stopSpeechPlayback();
    activeRequestRef.current?.abort();
    activeRequestRef.current = null;
    if (activeRunId) {
      void fetch('/api/cancel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runId: activeRunId }),
      }).catch(() => {});
    }
    setIsExecuting(false);
  }

  function speechSummary(raw: string): string {
    let summary = raw
      .replace(/<thinking>[\s\S]*?<\/thinking>/gi, ' ')
      .replace(/```[\s\S]*?```/g, ' ')
      .replace(/`[^`]*`/g, ' ')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/^\s*(?:diff --git|index |--- |\+\+\+ |@@ ).*$/gm, ' ')
      .replace(/^\s*[+-](?=\S).*$/gm, ' ')
      .replace(/^\s*#{1,6}\s*/gm, '')
      .replace(/[>*_~]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
    if (summary.length > 1100) {
      summary = summary.slice(0, 1050).replace(/\s+\S*$/, '') + '. I can show the full response in the chat.';
    }
    return summary;
  }

  function playVoiceChime() {
    try {
      if (localStorage.getItem('gram.settings.playVoiceChime') !== 'true') return;
      const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      const context = new AudioCtx();
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      oscillator.frequency.value = 660;
      gain.gain.value = 0.035;
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start();
      oscillator.stop(context.currentTime + 0.07);
      oscillator.onended = () => {
        void context.close().catch(() => {});
      };
    } catch {}
  }

  async function speakSummary(text: string, messageId?: string) {
    const summary = speechSummary(text);
    if (!summary) return;
    stopSpeechPlayback();
    setSpeakingMessageId(messageId || '__auto__');
    const controller = new AbortController();
    speechRequestRef.current = controller;
    let voiceId: string | undefined;
    let speed: number | undefined;
    try {
      voiceId = localStorage.getItem('gram.settings.elevenLabsVoiceId') || undefined;
      const rawSpeed = Number(localStorage.getItem('gram.settings.elevenLabsSpeed'));
      if (Number.isFinite(rawSpeed) && rawSpeed > 0) speed = rawSpeed;
    } catch {}
    try {
      const response = await fetch('/api/voice/speak', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: summary, voiceId, speed }),
        signal: controller.signal,
      });
      if (!response.ok) {
        if (response.status === 503 && typeof window !== 'undefined' && 'speechSynthesis' in window) {
          const utterance = new SpeechSynthesisUtterance(summary);
          utterance.onend = () => setSpeakingMessageId(null);
          utterance.onerror = () => setSpeakingMessageId(null);
          window.speechSynthesis.speak(utterance);
          return;
        }
        throw new Error((await response.json().catch(() => null))?.error || `Speech request failed (${response.status}).`);
      }
      const audioBlob = await response.blob();
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(audioBlob);
      speechUrlRef.current = url;
      const audio = new Audio(url);
      speechAudioRef.current = audio;
      audio.onended = () => stopSpeechPlayback();
      await audio.play();
    } catch (error: any) {
      if (error?.name !== 'AbortError') {
        setSpeakingMessageId(null);
        const msg = error?.message || 'Could not play voice response.';
        setVoiceError(msg);
        emitSystemToast({ title: 'Voice playback failed', description: msg, variant: 'error' });
      }
    }
  }

  function stopLiveDictation() {
    if (voiceSilenceTimerRef.current) {
      window.clearTimeout(voiceSilenceTimerRef.current);
      voiceSilenceTimerRef.current = null;
    }
    if (speechRecognitionRef.current) {
      try {
        speechRecognitionRef.current.onend = null;
        speechRecognitionRef.current.stop();
      } catch {}
      speechRecognitionRef.current = null;
    }
    if (recorderRef.current?.state === 'recording') {
      recorderRef.current.stop();
    }
    setVoiceRecording(false);
  }

  async function toggleVoiceRecording() {
    setVoiceError(null);
    if (voiceRecording || speechRecognitionRef.current || recorderRef.current?.state === 'recording') {
      stopLiveDictation();
      playVoiceChime();
      return;
    }
    cancelActiveTurnForBargeIn();
    voiceDraftBaseRef.current = prompt.trim();

    const startMediaRecorderFallback = async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        voiceStreamRef.current = stream;
        voiceChunksRef.current = [];
        const recorder = new MediaRecorder(stream);
        recorderRef.current = recorder;
        recorder.ondataavailable = (event) => { if (event.data.size) voiceChunksRef.current.push(event.data); };
        recorder.onstop = async () => {
          stream.getTracks().forEach((track) => track.stop());
          voiceStreamRef.current = null;
          recorderRef.current = null;
          setVoiceRecording(false);
          const audio = new Blob(voiceChunksRef.current, { type: recorder.mimeType || 'audio/webm' });
          voiceChunksRef.current = [];
          if (!audio.size) return;
          const controller = new AbortController();
          try {
            const response = await fetch('/api/voice/transcribe', { method: 'POST', headers: { 'Content-Type': audio.type }, body: audio, signal: controller.signal });
            const data = await response.json();
            if (!response.ok) throw new Error(data.error || `Transcription failed (${response.status}).`);
            const transcript = String(data.text || '').trim();
            if (transcript) {
              setPrompt([voiceDraftBaseRef.current, transcript].filter(Boolean).join(' '));
            }
            textareaRef.current?.focus();
          } catch (error: any) {
            if (error?.name !== 'AbortError') {
              const msg = error?.message || 'Transcription failed.';
              setVoiceError(msg);
              emitSystemToast({ title: 'Transcription failed', description: msg, variant: 'error' });
            }
          }
        };
        recorder.start();
        setVoiceRecording(true);
        playVoiceChime();
      } catch (error: any) {
        setVoiceRecording(false);
        const msg = error?.message || 'Microphone access is unavailable.';
        setVoiceError(msg);
        emitSystemToast({ title: 'Microphone unavailable', description: msg, variant: 'error' });
      }
    };

    const SpeechRec =
      (window as unknown as { SpeechRecognition?: any; webkitSpeechRecognition?: any }).SpeechRecognition ||
      (window as unknown as { webkitSpeechRecognition?: any }).webkitSpeechRecognition;

    if (SpeechRec) {
      try {
        const recognition = new SpeechRec();
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.lang = navigator.language || 'en-US';
        speechRecognitionRef.current = recognition;
        setVoiceRecording(true);
        playVoiceChime();
        textareaRef.current?.focus();

        recognition.onresult = (event: any) => {
          let finalPart = '';
          let interimPart = '';
          for (let i = 0; i < event.results.length; i++) {
            const segment = String(event.results[i]?.[0]?.transcript || '');
            if (event.results[i]?.isFinal) finalPart += segment;
            else interimPart += segment;
          }
          const spoken = `${finalPart}${interimPart}`.trim();
          setPrompt([voiceDraftBaseRef.current, spoken].filter(Boolean).join(' '));

          let autoSend = false;
          try {
            autoSend = localStorage.getItem('gram.settings.autoSendVoice') === 'true';
          } catch {}
          if (autoSend && finalPart.trim()) {
            if (voiceSilenceTimerRef.current) window.clearTimeout(voiceSilenceTimerRef.current);
            voiceSilenceTimerRef.current = window.setTimeout(() => {
              stopLiveDictation();
              formRef.current?.requestSubmit();
            }, 1300);
          }
        };

        recognition.onerror = (event: any) => {
          const code = String(event?.error || '');
          if (code === 'network' || code === 'service-not-allowed' || code === 'audio-capture') {
            stopLiveDictation();
            void startMediaRecorderFallback();
          } else if (code !== 'aborted' && code !== 'no-speech') {
            stopLiveDictation();
            setVoiceError(`Speech recognition error: ${code}`);
          }
        };

        recognition.onend = () => {
          if (speechRecognitionRef.current === recognition) {
            try {
              recognition.start();
            } catch {
              stopLiveDictation();
            }
          }
        };

        recognition.start();
        return;
      } catch {
        speechRecognitionRef.current = null;
      }
    }

    await startMediaRecorderFallback();
  }

  useEffect(() => () => {
    activeRequestRef.current?.abort();
    stopSpeechPlayback();
    stopLiveDictation();
    voiceStreamRef.current?.getTracks().forEach((track) => track.stop());
  }, []);

  const activeTab = tabs.find((t) => t.id === activeTabId) || tabs[0]!;
  const activeMessages = messagesByTab[activeTab.id] || [];

  const composerPlaceholder = React.useMemo(() => {
    const codePrompts = [
      'Whisper a wild idea to the agent...',
      'Summon the agent for a little coding magic...',
      'What are we building or untangling today?',
      'Drop a thought, a bug, or a daydream...',
      'Poke the agent with your next quest...',
      'Tell the agent what’s brewing...',
      'Hand the agent a puzzle to solve...',
      'Toss an idea into the workshop...',
      'Ask the agent to chase a hunch...',
      'What shall we conjure up next?',
      'Pitch the next move to the agent...',
      'Ask the agent anything, big or tiny...',
    ];
    const chatPrompts = [
      'Share a thought, a story, or a random question...',
      'What’s on your mind today?',
      'Ask anything, brainstorm, or just say hi...',
      'Toss a curiosity into the chat...',
      'What rabbit hole are we exploring today?',
      'Drop a daydream, a debate, or a quick question...',
      'Tell me what’s on your mind...',
      'Start a thread about anything at all...',
    ];
    const pool = appMode === 'chat' ? chatPrompts : codePrompts;
    const seed = `${activeTab.id}:${appMode}:${isPlanMode ? 'plan' : 'main'}`;
    let hash = Math.floor(Math.random() * pool.length);
    for (let i = 0; i < seed.length; i++) {
      hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
    }
    return pool[hash % pool.length]!;
  }, [activeTab.id, appMode, isPlanMode]);

  const chatEmptyCopy = React.useMemo(() => {
    const options = [
      {
        title: 'What’s on your mind?',
        description: 'Start a conversation, brainstorm an idea, or ask anything at all.',
      },
      {
        title: 'Ready for a good chat',
        description: 'Share a thought, a story, or a question you’ve been mulling over.',
      },
      {
        title: 'Where shall we wander today?',
        description: 'No codebase needed — bring a curiosity, a plan, or just say hello.',
      },
      {
        title: 'Penny for your thoughts?',
        description: 'Ask a quick question, bounce around ideas, or talk something through.',
      },
      {
        title: 'What are we thinking through?',
        description: 'Drop a topic, a debate, or a spark of inspiration to get started.',
      },
      {
        title: 'Pull up a chair',
        description: 'Whether it’s big plans or random musings, I’m all ears.',
      },
    ];
    const seed = `${activeTab.id}:chat-empty`;
    let hash = Math.floor(Math.random() * options.length);
    for (let i = 0; i < seed.length; i++) {
      hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
    }
    return options[hash % options.length]!;
  }, [activeTab.id]);

  const planIdeaSessionKey = `${activeProjectId}:${activeTab.sessionId}`;
  const lastActiveMessageId = activeMessages[activeMessages.length - 1]?.id || '';
  const planIdeaFetchKey = `${planIdeaSessionKey}:${activeMessages.length}:${lastActiveMessageId}`;

  useEffect(() => {
    if (!isPlanMode || planBySession[activeTab.sessionId]) return;
    if (fetchedPlanIdeasRef.current.has(planIdeaFetchKey)) return;
    fetchedPlanIdeasRef.current.add(planIdeaFetchKey);
    const controller = new AbortController();
    void fetch('/api/plan/ideas', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      signal: controller.signal,
      body: JSON.stringify({
        projectId: activeProjectId,
        recentMessages: activeMessages.slice(-4).map((m) => ({
          role: m.role,
          content: (m.content || '').slice(0, 260),
          isError: Boolean(m.isError),
        })),
      }),
    })
      .then((res) => (res.ok ? res.json() : null))
      .then((data: { ideas?: string[] } | null) => {
        if (data?.ideas && Array.isArray(data.ideas) && data.ideas.length > 0) {
          setPlanIdeasByKey((prev) => ({ ...prev, [planIdeaSessionKey]: data.ideas! }));
        }
      })
      .catch(() => {
        fetchedPlanIdeasRef.current.delete(planIdeaFetchKey);
      });
    return () => controller.abort();
  }, [isPlanMode, planIdeaFetchKey, planIdeaSessionKey, activeProjectId, activeTab.sessionId, planBySession, activeMessages]);

  const currentPlanIdeas = planIdeasByKey[planIdeaSessionKey] || [
    `Design new ${activeProjectId !== 'local' ? (projects.find((p) => p.id === activeProjectId)?.label || 'workspace') : 'workspace'} feature spec`,
    'Plan a safe architecture refactor',
    'Trace a bug and map the fix',
  ];

  const visibleCatalogModels = hideBuiltinModels
    ? models.filter((m) => m.source === 'byok' || m.source === 'openrouter')
    : models;
  const allModelItems: ModelItem[] = [
    { id: 'auto', name: 'Auto', provider: 'bedrock + openrouter' },
    ...visibleCatalogModels,
  ];
  const byokMenuModels = visibleCatalogModels.filter((m) => m.source === 'byok');
  const openRouterMenuModels = visibleCatalogModels.filter((m) => m.source === 'openrouter');
  const builtinMenuModels = visibleCatalogModels.filter(
    (m) => m.source !== 'byok' && m.source !== 'openrouter'
  );
  const selectedModel =
    [{ id: 'auto', name: 'Auto', provider: 'bedrock + openrouter' }, ...models].find(
      (m) => m.id === activeTab.modelId
    ) || allModelItems[0]!;

  async function reloadModels() {
    try {
      const d = await fetch('/api/models').then((r) => r.json());
      const loaded: ModelItem[] = Array.isArray(d.models) ? d.models : [];
      setModels(loaded);
    } catch {}
  }

  function handleHideBuiltinModelsChange(hide: boolean) {
    setHideBuiltinModels(hide);
    try {
      localStorage.setItem('gram.settings.hideBuiltinModels', String(hide));
    } catch {}
  }

  function handleLayoutPresetChange(preset: LayoutPreset) {
    setLayoutPreset(preset);
    try {
      localStorage.setItem('gram.settings.layoutPreset', preset);
    } catch {}
    if (preset === 'beginner') {
      setSidebarOpen(true);
    } else {
      setSidebarOpen(false);
    }
  }

  useEffect(() => {
    let settled = false;
    const finishBoot = () => {
      if (!settled) {
        settled = true;
        setIsAppLoading(false);
      }
    };
    const bootTimeout = window.setTimeout(finishBoot, 3500);

    const consumeAuthTokenFromHash = async () => {
      try {
        const hash = window.location.hash || '';
        if (!hash.startsWith('#')) return;
        const params = new URLSearchParams(hash.slice(1));
        const token = params.get('auth_token');
        if (!token) return;
        window.history.replaceState(null, '', window.location.pathname + window.location.search);
        await fetch('/api/auth/session-token', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ token }),
        });
      } catch {}
    };

    const initFlow = async () => {
      setBootError(null);
      await consumeAuthTokenFromHash();
      let lastSessionId = '';
      try {
        lastSessionId = localStorage.getItem('gram.activeSessionId') || '';
      } catch {}

      const bootstrapPromise = fetch(
        `/api/bootstrap${lastSessionId ? `?sessionId=${encodeURIComponent(lastSessionId)}` : ''}`
      )
        .then(async (r) => {
          if (!r.ok) throw new Error(`Bootstrap request failed (${r.status})`);
          return r.json();
        })
        .then(async (data) => {
          if (!data) throw new Error('Bootstrap empty');
          if (data.auth) setAuthState(data.auth as AuthState);
          if (Array.isArray(data.projects) && data.projects.length > 0) {
            setProjects(data.projects);
            const firstRealProj = data.projects.find((p: { id: string; archived?: boolean }) => p.id !== 'local' && !p.archived);
            if (firstRealProj) {
              setActiveProjectId((curr) => (curr === 'local' ? firstRealProj.id : curr));
            }
            try {
              localStorage.setItem('gram.cache.projects', JSON.stringify(data.projects));
            } catch {}
          }
          if (Array.isArray(data.tasks)) {
            setRecentTasks(data.tasks);
            try {
              localStorage.setItem('gram.cache.recentTasks', JSON.stringify(data.tasks));
            } catch {}
          }
          if (Array.isArray(data.processes)) {
            setManagedProcesses(data.processes);
          }
          if (Array.isArray(data.devices)) {
            setDevices(data.devices);
          }
          if (data.models?.models) {
            const loaded: ModelItem[] = Array.isArray(data.models.models) ? data.models.models : [];
            setModels(loaded);
            if (loaded.length > 0) {
              const validIds = new Set(['auto', ...loaded.map((m) => m.id)]);
              setTabs((prev) =>
                prev.map((t) => (validIds.has(t.modelId) ? t : { ...t, modelId: 'auto' }))
              );
              try {
                const savedDefault = localStorage.getItem('gram.settings.defaultModel');
                if (savedDefault && !validIds.has(savedDefault)) {
                  localStorage.setItem('gram.settings.defaultModel', 'auto');
                }
              } catch {}
            }
          }
          if (Array.isArray(data.chats)) {
            setChatSessions(data.chats);
            try {
              localStorage.setItem('gram.cache.chatSessions', JSON.stringify(data.chats));
            } catch {}
            if (
              lastSessionId &&
              data.chats.some((c: ChatSessionSummary) => c.sessionId === lastSessionId)
            ) {
              if (data.activeChat?.sessionId === lastSessionId) {
                await handleSelectChat(lastSessionId);
              }
            }
          }
          if (typeof data.activePlan === 'string' && data.activePlan && lastSessionId) {
            setPlanBySession((prev) => ({ ...prev, [lastSessionId]: data.activePlan }));
          }
          if (Array.isArray(data.systemChecks)) {
            applySystemChecks(data.systemChecks);
          } else if (typeof navigator !== 'undefined' && navigator.onLine === false) {
            setSystemWarnings([
              {
                id: 'internet_connection',
                label: 'Internet connection',
                severity: 'warning',
                ok: false,
                message: 'No internet connection detected. Cloud models, web search, and cloud sync may not work.',
                action: 'retry',
              },
            ]);
          }
        })
        .catch(async (bootstrapErr: any) => {
          const chatInitPromise = loadChatSessions().then((chats) => {
            if (lastSessionId && Array.isArray(chats) && chats.some((c) => c.sessionId === lastSessionId)) {
              return handleSelectChat(lastSessionId);
            }
          });
          const fallbackResults = await Promise.allSettled([
            fetch('/api/models').then(async (r) => {
              if (!r.ok) throw new Error(`Models failed (${r.status})`);
              const d = await r.json();
              setModels(Array.isArray(d.models) ? d.models : []);
            }),
            refreshAuth(),
            loadManagedProcesses(),
            loadProjects(),
            loadRecentTasks(),
            loadDevices(),
            chatInitPromise,
          ]);
          const modelsFailed = fallbackResults[0]?.status === 'rejected';
          const projectsFailed = fallbackResults[3]?.status === 'rejected';
          if (modelsFailed && projectsFailed) {
            setBootError({
              title: 'Unable to connect to Gram',
              message: 'The app could not reach the local Gram server during startup.',
              details: bootstrapErr?.message || String(bootstrapErr || 'Bootstrap and fallback requests failed.'),
            });
          }
        });

      const fontsReady =
        typeof document !== 'undefined' && document.fonts?.ready
          ? document.fonts.ready.catch(() => {})
          : Promise.resolve();

      await Promise.allSettled([bootstrapPromise, fontsReady]);
      window.clearTimeout(bootTimeout);
      finishBoot();
    };
    void initFlow();

    const pollTimer = window.setInterval(async () => {
      const [syncRes] = await Promise.all([
        fetch('/api/sync', { method: 'POST' }).then((r) => (r.ok ? r.json() : null)).catch(() => null),
        loadDevices(),
      ]);
      if (syncRes) {
        if (Array.isArray(syncRes.tasks)) {
          setRecentTasks(syncRes.tasks);
          try { localStorage.setItem('gram.cache.recentTasks', JSON.stringify(syncRes.tasks)); } catch {}
        }
        if (Array.isArray(syncRes.chats)) {
          setChatSessions(syncRes.chats);
          try { localStorage.setItem('gram.cache.chatSessions', JSON.stringify(syncRes.chats)); } catch {}
        }
        if (Array.isArray(syncRes.projects) && syncRes.projects.length > 0) {
          setProjects(syncRes.projects);
          try { localStorage.setItem('gram.cache.projects', JSON.stringify(syncRes.projects)); } catch {}
        }
      } else {
        void Promise.all([loadRecentTasks(), loadChatSessions(), loadProjects()]);
      }
    }, 3000);
    const syncTheme = () => {
      try {
        setTheme(localStorage.getItem('gram.settings.theme') || 'obsidian');
      } catch {}
    };
    window.addEventListener('storage', syncTheme);
    window.addEventListener('focus', syncTheme);
    const handleGlobalClick = (e: MouseEvent) => {
      const target = e.target as HTMLElement | null;
      setOpenChatMenu(null);
      if (!target?.closest('[data-popover-root="user"]')) {
        setUserMenuOpen(false);
      }
      if (!target?.closest('[data-popover-root="model"]')) {
        setModelMenuOpen(false);
      }
      if (!target?.closest('[data-popover-root="context"]')) {
        setContextMenuOpen(false);
      }
      if (!target?.closest('[data-popover-root="composer-model"]')) {
        setComposerModelOpen(false);
      }
      if (!target?.closest('[data-popover-root="effort"]')) {
        setEffortMenuOpen(false);
      }
    };
    const handleGlobalKeyDown = (e: KeyboardEvent) => {
      const actions = shortcutActionsRef.current;
      if (e.key === 'Escape') {
        const hadOpenOverlay = Boolean(
          actions?.shortcutsOpen || actions?.projectHubId
        );
        setOpenChatMenu(null);
        setUserMenuOpen(false);
        setModelMenuOpen(false);
        setContextMenuOpen(false);
        setComposerModelOpen(false);
        setEffortMenuOpen(false);
        setShortcutsOpen(false);
        setEditingQueueId(null);
        if (!hadOpenOverlay && actions?.isExecuting) {
          actions.handleStop();
        }
        return;
      }

      const mod = e.metaKey || e.ctrlKey;
      if (!mod || e.altKey || !actions) return;
      const key = e.key.toLowerCase();

      if (key === 'n' && !e.shiftKey) {
        e.preventDefault();
        actions.openNewChatTab();
      } else if (key === 'b' && !e.shiftKey) {
        e.preventDefault();
        setSidebarOpen((v) => !v);
      } else if (key === 'm' && e.shiftKey) {
        e.preventDefault();
        actions.switchAppMode(actions.appMode === 'chat' ? 'code' : 'chat');
      } else if (key === 'l' && !e.shiftKey) {
        e.preventDefault();
        textareaRef.current?.focus();
      } else if (e.key === ',' && !e.shiftKey) {
        e.preventDefault();
        actions.navigateToPage('settings', { direction: 'forward' });
      } else if (key === 'r' && e.shiftKey) {
        e.preventDefault();
        if (!actions.isExecuting) {
          const lastUserMsg = [...actions.activeMessages].reverse().find((m) => m.role === 'user');
          if (lastUserMsg) actions.handleRetryMessage(lastUserMsg);
        }
      } else if (e.key === '/' || e.key === '?') {
        e.preventDefault();
        setShortcutsOpen((v) => !v);
      }
    };
    const syncRoute = () => {
      const p = window.location.pathname;
      if (p === '/settings') {
        setActivePage('settings');
      } else if (p === '/login') {
        setAuthMode('signin');
        setActivePage('auth');
      } else if (p === '/signup') {
        setAuthMode('signup');
        setActivePage('auth');
      } else if (p === '/welcome' || p === '/onboarding') {
        setActivePage('onboarding');
      } else {
        setActivePage('chat');
      }
    };
    window.addEventListener('click', handleGlobalClick);
    window.addEventListener('keydown', handleGlobalKeyDown);
    window.addEventListener('popstate', syncRoute);
    return () => {
      window.clearInterval(pollTimer);
      window.removeEventListener('storage', syncTheme);
      window.removeEventListener('focus', syncTheme);
      window.removeEventListener('click', handleGlobalClick);
      window.removeEventListener('keydown', handleGlobalKeyDown);
      window.removeEventListener('popstate', syncRoute);
    };
  }, []);

  function navigateToPage(
    page: 'chat' | 'settings' | 'auth' | 'onboarding',
    opts?: { authMode?: 'signin' | 'signup'; direction?: 'forward' | 'backward' }
  ) {
    const nextAuthMode = opts?.authMode ?? authMode;
    const nextPath =
      page === 'settings'
        ? '/settings'
        : page === 'auth'
          ? nextAuthMode === 'signup'
            ? '/signup'
            : '/login'
          : page === 'onboarding'
            ? '/welcome'
            : '/';

    const applyUpdate = () => {
      if (opts?.authMode) {
        setAuthMode(opts.authMode);
        setLoginError('');
      }
      setActivePage(page);
      setUserMenuOpen(false);
      if (typeof window !== 'undefined' && window.location.pathname !== nextPath) {
        window.history.pushState({}, '', nextPath);
      }
    };

    const doc = typeof document !== 'undefined' ? (document as Document & {
      startViewTransition?: (opts: { update: () => void; types?: string[] } | (() => void)) => { finished?: Promise<unknown> };
    }) : null;

    if (doc?.startViewTransition && doc.visibilityState === 'visible') {
      try {
        const transition = doc.startViewTransition({
          update: applyUpdate,
          types: [opts?.direction ?? 'forward'],
        });
        transition?.finished?.catch(() => {});
        return;
      } catch {
        // Fall through if browser only supports callback form
      }
    }
    applyUpdate();
  }

  function openSettings() {
    navigateToPage('settings', { direction: 'forward' });
  }

  function closeSettings() {
    navigateToPage('chat', { direction: 'backward' });
  }

  function handleThemeChange(nextTheme: string) {
    setTheme(nextTheme);
    try {
      localStorage.setItem('gram.settings.theme', nextTheme);
    } catch {}
  }

  useEffect(() => {
    try {
      if (activeTab?.sessionId) {
        localStorage.setItem('gram.activeSessionId', activeTab.sessionId);
      }
    } catch {}
  }, [activeTab?.sessionId]);

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    const tauriInvoke = (window as any)?.__TAURI_INTERNALS__?.invoke;
    if (typeof tauriInvoke === 'function') {
      void Promise.resolve(tauriInvoke('set_window_theme', { theme })).catch(() => {});
    }
  }, [theme]);

  function applySystemChecks(rawChecks: any[]) {
    const checks = rawChecks.map((c: any) =>
      c.id === 'internet_connection' && typeof navigator !== 'undefined' && navigator.onLine === false
        ? { ...c, ok: false, message: 'Browser reports no internet connection. Cloud models and sync may not work.' }
        : c
    );
    const criticalFailures = checks.filter((c: any) => c.severity === 'critical' && !c.ok);
    const warningFailures = checks.filter((c: any) => c.severity === 'warning' && !c.ok);
    if (criticalFailures.length > 0) {
      setBootError({
        title: criticalFailures[0].label || 'System check failed',
        message: criticalFailures.map((c: any) => c.message).join(' '),
        details: criticalFailures.map((c: any) => `[${c.id}] ${c.message}`).join('\n'),
      });
    } else {
      setBootError(null);
    }
    setSystemWarnings((prev) => {
      const prevIds = new Set(prev.map((w) => w.id));
      const hasNewWarning = warningFailures.some((w: any) => !prevIds.has(w.id));
      if (hasNewWarning) setSystemWarningsDismissed(false);
      return warningFailures;
    });
  }

  async function refreshSystemChecks() {
    try {
      const res = await fetch('/api/system/checks');
      if (!res.ok) return;
      const data = await res.json().catch(() => null);
      if (data?.auth) setAuthState(data.auth as AuthState);
      if (Array.isArray(data?.systemChecks)) {
        applySystemChecks(data.systemChecks);
      }
    } catch {}
  }

  useEffect(() => {
    if (authState.authenticated) {
      setSystemWarnings((prev) => prev.filter((w) => w.id !== 'auth_required'));
    }
  }, [authState.authenticated]);

  useEffect(() => {
    if (models.length > 0) {
      setSystemWarnings((prev) => prev.filter((w) => w.id !== 'ai_providers'));
    }
  }, [models.length]);

  useEffect(() => {
    if (!actionError) return;
    const timer = window.setTimeout(() => setActionError(null), 12000);
    return () => window.clearTimeout(timer);
  }, [actionError]);

  useEffect(() => {
    if (systemWarnings.length === 0 && !bootError) return;
    const interval = window.setInterval(() => {
      if (document.visibilityState === 'visible') {
        void refreshSystemChecks();
      }
    }, 15000);
    const onFocusOrVisible = () => {
      if (document.visibilityState === 'visible') {
        void refreshSystemChecks();
      }
    };
    window.addEventListener('focus', onFocusOrVisible);
    document.addEventListener('visibilitychange', onFocusOrVisible);
    return () => {
      window.clearInterval(interval);
      window.removeEventListener('focus', onFocusOrVisible);
      document.removeEventListener('visibilitychange', onFocusOrVisible);
    };
  }, [systemWarnings.length, bootError]);

  useEffect(() => {
    const onWindowError = (event: ErrorEvent) => {
      if (!event?.message) return;
      if (/ResizeObserver loop/i.test(event.message)) return;
      setActionError({
        message: event.message,
        details: event.error?.stack || `${event.filename || 'unknown'}:${event.lineno || 0}`,
      });
    };
    const onUnhandledRejection = (event: PromiseRejectionEvent) => {
      const reason = event.reason;
      if (reason?.name === 'AbortError') return;
      const msg = reason instanceof Error ? reason.message : String(reason || 'An unexpected error occurred.');
      if (/View transition was skipped/i.test(msg)) return;
      setActionError({
        message: msg,
        details: reason instanceof Error ? reason.stack : undefined,
      });
    };
    window.addEventListener('error', onWindowError);
    window.addEventListener('unhandledrejection', onUnhandledRejection);
    const onOffline = () => {
      setSystemWarningsDismissed(false);
      setSystemWarnings((prev) => {
        if (prev.some((w) => w.id === 'internet_connection')) return prev;
        return [
          ...prev,
          {
            id: 'internet_connection',
            label: 'Internet connection',
            severity: 'warning',
            ok: false,
            message: 'No internet connection detected. Cloud models, web search, and cloud sync may not work.',
            action: 'retry',
          },
        ];
      });
    };
    const onOnline = () => {
      setSystemWarnings((prev) => prev.filter((w) => w.id !== 'internet_connection'));
      setActionError((prev) =>
        prev && /offline|network|internet|fetch|connect/i.test(prev.message) ? null : prev
      );
      void refreshSystemChecks();
    };
    window.addEventListener('offline', onOffline);
    window.addEventListener('online', onOnline);
    return () => {
      window.removeEventListener('error', onWindowError);
      window.removeEventListener('unhandledrejection', onUnhandledRejection);
      window.removeEventListener('offline', onOffline);
      window.removeEventListener('online', onOnline);
    };
  }, []);

  async function handleSignOut() {
    await fetch('/api/auth/sign-out', { method: 'POST' }).catch(() => {});
    refreshAuth();
  }

  function updateChatMeta(
    updater: (prev: {
      pinned: Record<string, boolean>;
      unread: Record<string, boolean>;
      archived: Record<string, boolean>;
      titles: Record<string, string>;
    }) => {
      pinned: Record<string, boolean>;
      unread: Record<string, boolean>;
      archived: Record<string, boolean>;
      titles: Record<string, string>;
    }
  ) {
    setChatMeta((prev) => {
      const next = updater(prev);
      try {
        localStorage.setItem('gram.chatMeta', JSON.stringify(next));
      } catch {}
      return next;
    });
  }

  function handleTogglePin(runId: string) {
    updateChatMeta((prev) => ({
      ...prev,
      pinned: { ...prev.pinned, [runId]: !prev.pinned[runId] },
    }));
    setOpenChatMenu(null);
  }

  function handleToggleUnread(runId: string) {
    updateChatMeta((prev) => ({
      ...prev,
      unread: { ...prev.unread, [runId]: !prev.unread[runId] },
    }));
    setOpenChatMenu(null);
  }

  async function handleArchiveTask(sessionId: string) {
    setOpenChatMenu(null);
    updateChatMeta((prev) => ({
      ...prev,
      archived: { ...prev.archived, [sessionId]: true },
    }));
    setChatSessions((prev) => prev.filter((item) => item.sessionId !== sessionId));

    // Close any open tab showing this chat so it disappears from the screen immediately
    const closingTabs = tabs.filter((t) => t.sessionId === sessionId);
    if (closingTabs.length > 0) {
      const remainingTabs = tabs.filter((t) => t.sessionId !== sessionId);
      const closingActive = closingTabs.some((t) => t.id === activeTabId);
      if (remainingTabs.length > 0) {
        setTabs(remainingTabs);
        if (closingActive) {
          setActiveTabId(remainingTabs[0]!.id);
        }
      } else {
        const freshId = `${appMode}-${Date.now()}`;
        const freshTab: ChatTab = {
          id: freshId,
          title: appMode === 'code' ? 'New code' : 'New chat',
          sessionId: `session-${Date.now()}`,
          modelId: activeTab?.modelId || 'auto',
          mode: appMode,
          projectId: appMode === 'code' ? activeProjectId : undefined,
        };
        setTabs([freshTab]);
        setActiveTabId(freshId);
      }
      setMessagesByTab((prev) => {
        const next = { ...prev };
        for (const t of closingTabs) delete next[t.id];
        return next;
      });
    }

    try {
      await fetch('/api/chat/archive', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId, archived: true }),
      });
      await loadChatSessions();
      loadRecentTasks();
    } catch (err: any) {
      setActionError({ message: err?.message || 'Could not archive chat.' });
    }
  }

  async function handleRestoreChat(sessionId: string) {
    updateChatMeta((prev) => {
      const nextArchived = { ...prev.archived };
      delete nextArchived[sessionId];
      return { ...prev, archived: nextArchived };
    });
    try {
      await fetch('/api/chat/archive', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId, archived: false }),
      });
      await loadChatSessions();
    } catch (err: any) {
      setActionError({ message: err?.message || 'Could not restore chat.' });
    }
  }

  async function handleSplitChat(chat: ChatSessionSummary) {
    setOpenChatMenu(null);
    const id = `split-${Date.now()}`;
    const newSessionId = `session-${Date.now()}`;
    const displayTitle = chatMeta.titles[chat.sessionId] || chat.title || chat.sessionId;
    let cloned: ChatEntry[] = [];
    try {
      const res = await fetch(`/api/chat?sessionId=${encodeURIComponent(chat.sessionId)}`);
      if (!res.ok) throw new Error(`Could not load chat to split (${res.status}).`);
      const saved = await res.json();
      if (saved && Array.isArray(saved.messages)) {
        cloned = saved.messages;
      }
    } catch (err: any) {
      setActionError({ message: err?.message || 'Could not split chat.' });
      return;
    }
    const splitTitle = `${displayTitle.slice(0, 26)} (split)`;
    setMessagesByTab((prev) => ({ ...prev, [id]: cloned }));
    setTabs((prev) => [
      ...prev,
      {
        id,
        title: splitTitle,
        sessionId: newSessionId,
        modelId: chat.modelId || activeTab.modelId || 'auto',
        mode: chat.mode || 'chat',
        projectId: chat.projectId === 'local' ? undefined : chat.projectId,
      },
    ]);
    setActiveTabId(id);
    if (cloned.length > 0) {
      saveChatSession({
        sessionId: newSessionId,
        title: splitTitle,
        projectId: chat.projectId,
        projectLabel: chat.projectLabel,
        modelId: chat.modelId,
        mode: chat.mode || 'chat',
        messages: cloned,
      });
    }
  }

  async function commitRename(sessionId: string) {
    const trimmed = renameDraft.trim();
    if (trimmed) {
      manualTitleOverridesRef.current.add(sessionId);
      const previousSessionTitle = chatSessions.find((c) => c.sessionId === sessionId)?.title;
      const previousMetaTitle = chatMeta.titles[sessionId];
      updateChatMeta((prev) => ({
        ...prev,
        titles: { ...prev.titles, [sessionId]: trimmed },
      }));
      setChatSessions((prev) =>
        prev.map((c) => (c.sessionId === sessionId ? { ...c, title: trimmed } : c))
      );
      setTabs((prev) =>
        prev.map((t) => (t.sessionId === sessionId ? { ...t, title: trimmed } : t))
      );
      try {
        const res = await fetch(`/api/chat?sessionId=${encodeURIComponent(sessionId)}`);
        if (!res.ok) throw new Error(`Could not load chat for rename (${res.status}).`);
        const existing = await res.json();
        if (existing) {
          await saveChatSession({
            sessionId,
            title: trimmed,
            projectId: existing.projectId,
            projectLabel: existing.projectLabel,
            modelId: existing.modelId,
            messages: existing.messages || [],
          });
        }
      } catch (err: any) {
        manualTitleOverridesRef.current.delete(sessionId);
        updateChatMeta((prev) => {
          const nextTitles = { ...prev.titles };
          if (previousMetaTitle !== undefined) nextTitles[sessionId] = previousMetaTitle;
          else delete nextTitles[sessionId];
          return { ...prev, titles: nextTitles };
        });
        if (previousSessionTitle !== undefined) {
          setChatSessions((prev) =>
            prev.map((c) => (c.sessionId === sessionId ? { ...c, title: previousSessionTitle } : c))
          );
          setTabs((prev) =>
            prev.map((t) => (t.sessionId === sessionId ? { ...t, title: previousSessionTitle } : t))
          );
        }
        setActionError({ message: err?.message || 'Could not rename chat.' });
      }
    }
    setRenamingRunId(null);
  }

  async function refreshAuth() {
    try {
      const res = await fetch('/api/auth/state');
      const data = (await res.json()) as AuthState;
      setAuthState(data);
    } catch {
      setAuthState({ connected: false, authenticated: false, user: null });
    }
  }

  async function loadProjects() {
    try {
      const res = await fetch('/api/projects');
      const data = await res.json();
      if (Array.isArray(data) && data.length > 0) {
        setProjects(data);
        const firstRealProj = data.find((p: { id: string; archived?: boolean }) => p.id !== 'local' && !p.archived);
        if (firstRealProj) {
          setActiveProjectId((curr) => (curr === 'local' ? firstRealProj.id : curr));
        }
        try {
          localStorage.setItem('gram.cache.projects', JSON.stringify(data));
        } catch {}
      }
    } catch {
      emitSystemToast({
        title: 'Connection issue',
        description: 'Could not load projects from Gram server.',
        variant: 'error',
      });
    }
  }

  async function loadManagedProcesses() {
    try {
      const res = await fetch('/api/processes');
      if (!res.ok) return;
      const data = await res.json();
      if (Array.isArray(data?.processes)) {
        setManagedProcesses(data.processes);
      }
    } catch {}
  }

  async function handleViewProcessLogs(processId: string) {
    try {
      const res = await fetch(`/api/processes/${encodeURIComponent(processId)}/logs?tail=80`);
      if (!res.ok) return;
      const data = await res.json();
      setSelectedProcessLogs({ id: processId, logs: String(data?.logs || '(no logs yet)') });
    } catch {}
  }

  async function handleStopProcess(processId: string) {
    try {
      await fetch(`/api/processes/${encodeURIComponent(processId)}/stop`, { method: 'POST' });
      await loadManagedProcesses();
      if (selectedProcessLogs?.id === processId) {
        await handleViewProcessLogs(processId);
      }
    } catch {}
  }

  async function loadDevices() {
    try {
      const res = await fetch('/api/connect');
      const data = await res.json();
      if (Array.isArray(data?.devices) && data.devices.length > 0) {
        setDevices(data.devices);
        try {
          localStorage.setItem('gram.cache.devices', JSON.stringify(data.devices));
        } catch {}
        setActiveDeviceId((prev) =>
          data.devices.some((d: DeviceItem) => d.deviceId === prev) ? prev : data.devices[0].deviceId
        );
      }
    } catch {}
  }

  async function loadRecentTasks() {
    try {
      const res = await fetch('/api/tasks');
      const data = await res.json();
      if (Array.isArray(data)) {
        setRecentTasks(data);
        try {
          localStorage.setItem('gram.cache.recentTasks', JSON.stringify(data));
        } catch {}
      }
    } catch {}
  }

  async function loadChatSessions(): Promise<ChatSessionSummary[]> {
    try {
      const [res, archRes] = await Promise.all([
        fetch('/api/chats'),
        fetch('/api/chats/archived').catch(() => null),
      ]);
      if (!res.ok) throw new Error(`Could not load chats (${res.status}).`);
      const data = await res.json();
      if (archRes?.ok) {
        const archData = await archRes.json().catch(() => null);
        if (Array.isArray(archData)) setArchivedChats(archData);
      }
      if (Array.isArray(data)) {
        setChatSessions(data);
        try {
          localStorage.setItem('gram.cache.chatSessions', JSON.stringify(data));
        } catch {}
        return data;
      }
    } catch {}
    return [];
  }

  async function saveChatSession(payload: {
    sessionId: string;
    title: string;
    projectId?: string;
    projectLabel?: string;
    modelId?: string;
    mode?: 'chat' | 'plan' | 'code';
    messages: ChatEntry[];
  }) {
    const previousSave = chatSaveQueuesRef.current.get(payload.sessionId) || Promise.resolve();
    const queuedSave = previousSave.catch(() => {}).then(async () => {
      const res = await fetch('/api/chat/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...payload, mode: payload.mode || appMode }),
      });
      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.error || `Could not save chat (${res.status}).`);
      }

      setChatSessions((prev) => {
        const existing = prev.find((chat) => chat.sessionId === payload.sessionId);
        const summary: ChatSessionSummary = {
          sessionId: payload.sessionId,
          title: payload.title || payload.sessionId,
          projectId: payload.projectId ?? existing?.projectId ?? 'local',
          projectLabel: payload.projectLabel ?? existing?.projectLabel ?? 'Local workspace',
          modelId: payload.modelId || existing?.modelId || 'auto',
          mode:
            payload.mode === 'code' ||
            payload.mode === 'plan' ||
            (payload.projectId ?? existing?.projectId ?? 'local') !== 'local'
              ? 'code'
              : 'chat',
          updatedAt: new Date().toISOString(),
        };
        const next = [summary, ...prev.filter((chat) => chat.sessionId !== payload.sessionId)].slice(0, 200);
        try {
          localStorage.setItem('gram.cache.chatSessions', JSON.stringify(next));
        } catch {}
        return next;
      });
    });
    chatSaveQueuesRef.current.set(payload.sessionId, queuedSave);
    try {
      await queuedSave;
    } catch (error: any) {
      setActionError({ message: error?.message || 'Could not save chat.' });
    } finally {
      if (chatSaveQueuesRef.current.get(payload.sessionId) === queuedSave) {
        chatSaveQueuesRef.current.delete(payload.sessionId);
      }
    }
  }

  async function requestChatTitle(promptText: string, modelId: string): Promise<string> {
    const response = await fetch('/api/chat/title', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: promptText, modelId }),
    });
    const data = await response.json().catch(() => null);
    if (!response.ok || typeof data?.title !== 'string' || !data.title.trim()) {
      throw new Error(data?.error || `Could not generate a chat title (${response.status}).`);
    }
    return data.title.trim();
  }

  function fallbackChatTitle(promptText: string): string {
    const words = promptText.trim().replace(/\s+/g, ' ').split(' ').filter(Boolean).slice(0, 7);
    let title = words.join(' ').replace(/[.!?;:,]+$/, '').trim();
    if (title.length > 52) title = title.slice(0, 52).replace(/\s+\S*$/, '').trim();
    return title || 'New chat';
  }

  function switchAppMode(nextMode: 'chat' | 'code') {
    setAppMode(nextMode);
    if (nextMode === 'chat') {
      setActiveDockSurface(null);
    }
    const matchingTab = tabs.find((t) => (t.mode || 'chat') === nextMode);
    if (matchingTab) {
      setActiveTabId(matchingTab.id);
      return;
    }
    const id = `${nextMode}-${Date.now()}`;
    const newTab: ChatTab = {
      id,
      title: nextMode === 'code' ? 'New code' : 'New chat',
      sessionId: `session-${Date.now()}`,
      modelId: activeTab.modelId || 'auto',
      mode: nextMode,
      projectId: nextMode === 'code' && activeProjectId !== 'local' ? activeProjectId : undefined,
    };
    setTabs((prev) => [...prev, newTab]);
    setActiveTabId(id);
    emitSystemToast({
      title: appMode === 'code' ? 'New code session started' : 'New chat started',
      variant: 'info',
    });
  }

  async function handleSelectChat(sessionId: string) {
    const existingTab = tabs.find((tab) => tab.sessionId === sessionId);
    if (existingTab && (messagesByTab[existingTab.id]?.length ?? 0) > 0) {
      setActiveTabId(existingTab.id);
      return;
    }

    const tabId = existingTab ? existingTab.id : `chat-${sessionId}`;
    try {
      const res = await fetch(`/api/chat?sessionId=${encodeURIComponent(sessionId)}`);
      const saved = res.ok ? await res.json() : null;
      if (saved) {
        if (saved.projectId && saved.projectId !== activeProjectId) {
          setActiveProjectId(saved.projectId);
        }
        const loadedMessages = Array.isArray(saved.messages) ? (saved.messages as ChatEntry[]) : [];
        setMessagesByTab((prev) => ({ ...prev, [tabId]: loadedMessages }));
        if (!existingTab) {
          setTabs((prev) => [
            ...prev,
            {
              id: tabId,
              title: saved.title || 'Chat',
              sessionId,
              modelId: saved.modelId || activeTab.modelId || 'auto',
              mode: saved.mode === 'code' ? 'code' : 'chat',
              projectId: saved.projectId === 'local' ? undefined : saved.projectId,
            },
          ]);
        }
        setActiveTabId(tabId);
        setAppMode(saved.mode === 'code' ? 'code' : 'chat');
        if (!planBySession[sessionId]) {
          fetch(`/api/plans/${encodeURIComponent(sessionId)}`)
            .then((r) => (r.ok ? r.json() : null))
            .then((planData) => {
              if (planData?.content) {
                setPlanBySession((prev) => ({ ...prev, [sessionId]: planData.content }));
              }
            })
            .catch(() => {});
        }
        return;
      }
    } catch {}

    setActiveTabId(tabId);
  }

  useEffect(() => {
    const firstAssistant = [...activeMessages].reverse().find((m) => m.role === 'assistant' && m.content.trim());
    if (!firstAssistant) return;
    if (isPlanMode || /##\s+(?:Requirements|Design|Tasks)\b/i.test(firstAssistant.content)) {
      setPlanBySession((prev) => {
        if (prev[activeTab.sessionId] === firstAssistant.content) return prev;
        return { ...prev, [activeTab.sessionId]: firstAssistant.content };
      });
    }
  }, [activeMessages, activeTab.sessionId, appMode]);

  async function saveSessionPlan(sessionId: string, markdown: string) {
    setPlanBySession((prev) => ({ ...prev, [sessionId]: markdown }));
    try {
      const res = await fetch(`/api/plans/${encodeURIComponent(sessionId)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: markdown }),
      });
      if (!res.ok) throw new Error(`Save failed (${res.status})`);
      emitSystemToast({ title: 'Plan saved', variant: 'success' });
    } catch {
      emitSystemToast({
        title: 'Could not save plan',
        description: 'Check connection to the Gram server.',
        variant: 'error',
      });
    }
  }

  function handleApprovePlanAndBuild(opts: { markdown: string; isolateWorktree: boolean }) {
    void saveSessionPlan(activeTab.sessionId, opts.markdown);
    setActiveDockSurface(null);
    setAppMode('code');
    const updatedTab: ChatTab = { ...activeTab, mode: 'code', projectId: activeProjectId };
    setTabs((prev) => prev.map((t) => (t.id === activeTab.id ? updatedTab : t)));
    const buildPrompt = `Execute the approved plan below in Code mode${opts.isolateWorktree ? ' (isolated worktree)' : ''}:\n\n${opts.markdown}`;
    const userMsg: ChatEntry = { id: createChatMessageId('u'), role: 'user', content: buildPrompt };
    const nextWithUser = [...activeMessages, userMsg];
    setMessagesByTab((prev) => ({ ...prev, [activeTab.id]: nextWithUser }));
    void executePromptRun({
      tabId: activeTab.id,
      trimmedPrompt: buildPrompt,
      effectivePrompt: buildPrompt,
      nextWithUser,
      nextTitle: activeTab.title || 'Execute plan',
      speakFinalResponse: voiceMode,
    });
  }

  function openNewChatTab(overrideProjectId?: string) {
    const targetProjectId = appMode === 'code'
      ? (overrideProjectId || (activeProjectId !== 'local' ? activeProjectId : undefined))
      : undefined;
    const id = `${appMode}-${Date.now()}`;
    const newTab: ChatTab = {
      id,
      title: appMode === 'code' ? 'New code' : 'New chat',
      sessionId: `session-${Date.now()}`,
      modelId: activeTab.modelId || 'auto',
      mode: appMode,
      projectId: targetProjectId,
    };
    setTabs((prev) => [...prev, newTab]);
    setActiveTabId(id);
    emitSystemToast({
      title: appMode === 'code' ? 'New code session started' : 'New chat started',
      variant: 'info',
    });
  }

  async function loadProjectGitInfo(projectId: string) {
    setProjectGitInfo({ loading: true, isGitRepo: false, remotes: [] });
    try {
      const res = await fetch(`/api/projects/${encodeURIComponent(projectId)}/git`);
      const data = await res.json().catch(() => ({}));
      setProjectGitInfo({
        loading: false,
        isGitRepo: Boolean(data?.isGitRepo),
        branch: data?.branch,
        lastCommit: data?.lastCommit,
        hasGramGuide: Boolean(data?.hasGramGuide),
        remotes: Array.isArray(data?.remotes) ? data.remotes : [],
      });
    } catch {
      setProjectGitInfo({ loading: false, isGitRepo: false, remotes: [] });
    }
  }

  async function handleArchiveProject(projectId: string, archived: boolean) {
    try {
      const res = await fetch('/api/projects/archive', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: projectId, archived }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not update project (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      if (archived && activeProjectId === projectId) {
        setActiveProjectId('local');
      }
      emitSystemToast({
        title: archived ? 'Project archived' : 'Project restored',
        variant: 'info',
      });
    } catch (err: any) {
      const msg = err?.message || 'Could not archive project.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Project update failed', description: msg, variant: 'error' });
    }
  }

  async function handleDeleteProject(projectId: string) {
    try {
      const res = await fetch('/api/projects/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: projectId }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not delete project (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      if (activeProjectId === projectId) {
        setActiveProjectId('local');
      }
      setConfirmDeleteProject(false);
      setProjectHubId(null);
      emitSystemToast({ title: 'Project deleted', variant: 'info' });
    } catch (err: any) {
      const msg = err?.message || 'Could not delete project.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Could not delete project', description: msg, variant: 'error' });
    }
  }

  function openProjectHub(project?: ProjectItem) {
    setProjectHubTab('general');
    setConfirmDeleteProject(false);
    if (project) {
      setProjectHubId(project.id);
      setProjectDraft({
        id: project.id,
        label: project.label,
        workspacePath: project.workspacePath || '',
        instructions: project.instructions || '',
      });
      void loadProjectGitInfo(project.id);
    } else {
      setProjectHubId('__new__');
      setProjectDraft({ label: '', workspacePath: '', instructions: '' });
      setProjectGitInfo({ loading: false, isGitRepo: false, remotes: [] });
    }
  }

  async function saveProjectHub(e: React.FormEvent) {
    e.preventDefault();
    if (!projectDraft.label.trim()) {
      setActionError({ message: 'Project name is required.' });
      return;
    }
    const isCreating = !projectDraft.id || projectHubId === '__new__';
    try {
      const res = await fetch('/api/projects', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(projectDraft),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not save project (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      if (data?.project?.id) {
        setActiveProjectId(data.project.id);
        setProjectHubId(isCreating ? null : data.project.id);
      }
      emitSystemToast({
        title: isCreating ? 'Project created' : 'Project settings saved',
        description: data?.project?.label || projectDraft.label.trim(),
        variant: 'success',
      });
    } catch (err: any) {
      const msg = err?.message || 'Could not save project.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Could not save project', description: msg, variant: 'error' });
    }
  }

  async function handleUploadProjectFiles(projectId: string, fileList: FileList | null) {
    if (!fileList || fileList.length === 0) return;
    try {
      for (const file of Array.from(fileList)) {
        const contentText = await file.text().catch(() => '');
        const res = await fetch('/api/projects/files', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            projectId,
            filename: file.name,
            mimeType: file.type || 'text/plain',
            contentText,
          }),
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) throw new Error(data?.error || `Could not upload ${file.name} (${res.status}).`);
        if (data?.projects) setProjects(data.projects);
      }
      emitSystemToast({
        title: `Uploaded ${fileList.length} project file${fileList.length === 1 ? '' : 's'}`,
        variant: 'success',
      });
    } catch (err: any) {
      const msg = err?.message || 'Could not upload project file.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Upload failed', description: msg, variant: 'error' });
    }
  }

  async function handleDeleteProjectFile(fileId: string) {
    try {
      const res = await fetch('/api/projects/files/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not delete project file (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      emitSystemToast({ title: 'Project file removed', variant: 'info' });
    } catch (err: any) {
      const msg = err?.message || 'Could not delete project file.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Could not remove file', description: msg, variant: 'error' });
    }
  }

  async function handleSelectTask(task: IslandActivity) {
    if (task.projectId && task.projectId !== activeProjectId) {
      setActiveProjectId(task.projectId);
    }

    // First try opening the parent chat session directly if it exists in chat_sessions
    if (task.sessionId) {
      const existingTab = tabs.find((tab) => tab.sessionId === task.sessionId);
      if (existingTab && (messagesByTab[existingTab.id]?.length ?? 0) > 0) {
        setActiveTabId(existingTab.id);
        return;
      }
      try {
        const chatRes = await fetch(`/api/chat?sessionId=${encodeURIComponent(task.sessionId)}`);
        const savedChat = chatRes.ok ? await chatRes.json() : null;
        if (savedChat && Array.isArray(savedChat.messages) && savedChat.messages.length > 0) {
          await handleSelectChat(task.sessionId);
          return;
        }
      } catch {}
    }

    const existingTab = tabs.find(
      (tab) => tab.id === `task-${task.runId}` || (task.sessionId && tab.sessionId === task.sessionId)
    );
    if (existingTab && (messagesByTab[existingTab.id]?.length ?? 0) > 0) {
      setActiveTabId(existingTab.id);
      try {
        const response = await fetch(`/api/task?runId=${encodeURIComponent(task.runId)}`);
        const checkpoint = response.ok ? await response.json() : null;
        const inspectorSteps = checkpointInspectionSteps(checkpoint);
        if (inspectorSteps.length) {
          setMessagesByTab((prev) => {
            const messages = prev[existingTab.id] || [];
            const lastAssistantIndex = messages.map((message) => message.role).lastIndexOf('assistant');
            if (lastAssistantIndex < 0) return prev;
            const current = messages[lastAssistantIndex]!;
            const currentSteps = current.steps || [];
            const seen = new Set(currentSteps.map((step) => step.type === 'tool-runCommand' ? step.command : ''));
            const unique = inspectorSteps.filter((step) => step.type !== 'tool-runCommand' || !seen.has(step.command));
            const next = [...messages];
            next[lastAssistantIndex] = { ...current, steps: [...currentSteps, ...unique] };
            return { ...prev, [existingTab.id]: next };
          });
        }
      } catch {}
      return;
    }

    const tabId = existingTab ? existingTab.id : `task-${task.runId}`;
    const tabTitle = (task.title || task.prompt || task.runId).slice(0, 32);

    try {
      const res = await fetch(`/api/task?runId=${encodeURIComponent(task.runId)}`);
      const checkpoint = res.ok ? await res.json() : null;

      const userPrompt = checkpoint?.prompt || task.prompt || task.title || task.runId;
      const planSummary = Array.isArray(checkpoint?.activePlan) && checkpoint.activePlan.length > 0
        ? checkpoint.activePlan.map((p: { title?: string; status?: string }) => `- ${p.title || 'Task'} (${p.status || 'completed'})`).join('\n')
        : `Stage: ${checkpoint?.stage || 'completed'}`;
      const loadedApproval: ApprovalItem | undefined = checkpoint?.pendingApproval
        ? {
            id: checkpoint.pendingApproval.id,
            runId: task.runId,
            sessionId: task.sessionId,
            command: checkpoint.pendingApproval.command || checkpoint.pendingApproval.title,
            description: checkpoint.pendingApproval.description,
            state: 'approval-requested',
          }
        : undefined;
      const assistantOutput = loadedApproval ? '' : checkpoint?.output || planSummary;
      const loadedSteps = checkpointInspectionSteps(checkpoint);

      const loadedMessages: ChatEntry[] = [
        {
          id: `u-${task.runId}`,
          role: 'user',
          content: userPrompt,
        },
        {
          id: `a-${task.runId}`,
          role: 'assistant',
          content: assistantOutput,
          steps: loadedSteps.length ? loadedSteps : undefined,
          approval: loadedApproval,
          workers: checkpoint?.workers,
          originalPrompt: userPrompt,
        },
      ];

      setMessagesByTab((prev) => ({ ...prev, [tabId]: loadedMessages }));
    } catch {
      const fallbackMessages: ChatEntry[] = [
        {
          id: `u-${task.runId}`,
          role: 'user',
          content: task.prompt || task.title || task.runId,
        },
      ];
      setMessagesByTab((prev) => ({ ...prev, [tabId]: fallbackMessages }));
    }

    if (!existingTab) {
      setTabs((prev) => [
        ...prev,
        {
          id: tabId,
          title: tabTitle,
          sessionId: task.sessionId || task.runId,
          modelId: activeTab.modelId || 'auto',
        },
      ]);
    }
    setActiveTabId(tabId);
  }

  async function handleApproveActivity(activity: IslandActivity, approved: boolean) {
    try {
      const res = await fetch('/api/task/approve', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          runId: activity.runId,
          sessionId: activity.sessionId,
          approved,
        }),
      });
      const data = await res.json();
      await loadRecentTasks();

      const updatedOutput =
        data?.checkpoint?.output ||
        (approved ? `Approved and completed: ${activity.title}` : `Okay, I left it in place.`);

      setMessagesByTab((prev) => {
        const next = { ...prev };
        for (const tab of tabs) {
          if (tab.sessionId === activity.sessionId || tab.id === `task-${activity.runId}`) {
            const list = next[tab.id];
            if (list && list.length > 0) {
              const updatedList = [...list];
              const lastIdx = updatedList.length - 1;
              const prevEntry = updatedList[lastIdx];
              if (prevEntry?.role === 'assistant') {
                updatedList[lastIdx] = {
                  ...prevEntry,
                  content: updatedOutput,
                  approval: prevEntry.approval
                    ? { ...prevEntry.approval, state: 'approval-responded', approved }
                    : undefined,
                };
              } else {
                updatedList.push({
                  id: createChatMessageId('apr-res'),
                  role: 'assistant',
                  content: updatedOutput,
                });
              }
              next[tab.id] = updatedList;
              const projectId = tab.projectId || (tab.mode === 'code' ? activeProjectId : undefined);
              saveChatSession({
                sessionId: tab.sessionId,
                title: tab.title,
                projectId,
                projectLabel: projects.find((p) => p.id === projectId)?.label || 'Local workspace',
                modelId: tab.modelId,
                messages: updatedList,
              });
            }
          }
        }
        return next;
      });
    } catch {}
  }

  function selectModel(modelId: string) {
    setTabs((prev) => prev.map((t) => (t.id === activeTab.id ? { ...t, modelId } : t)));
    setModelMenuOpen(false);
  }

  function handleCopyMessage(msg: ChatEntry) {
    const text = msg.content || '';
    if (!text) return;
    navigator.clipboard?.writeText(text).catch(() => {});
    setCopiedMessageId(msg.id);
    window.setTimeout(() => {
      setCopiedMessageId((prev) => (prev === msg.id ? null : prev));
    }, 1800);
  }

  function handleReplyMessage(msg: ChatEntry) {
    setReplyingTo({
      id: msg.id,
      role: msg.role,
      content: msg.content,
    });
    textareaRef.current?.focus();
  }

  async function dispatchQueuedMessage(item: {
    id: string;
    tabId: string;
    content: string;
    replyTo?: { id: string; role: 'user' | 'assistant'; content: string };
  }) {
    const trimmed = item.content.trim();
    if (!trimmed) return;
    manualStopRef.current = false;
    const effectivePrompt = item.replyTo
      ? `Replying to ${item.replyTo.role === 'user' ? 'previous message' : 'assistant'} ("${item.replyTo.content.slice(0, 240)}"):\n\n${trimmed}`
      : trimmed;
    const userMsg: ChatEntry = {
      id: createChatMessageId('u'),
      role: 'user',
      content: trimmed,
      replyTo: item.replyTo,
    };
    const existing = messagesByTabRef.current[item.tabId] || [];
    const targetTab = tabs.find((t) => t.id === item.tabId) || activeTab;
    const nextTitle = existing.some((message) => message.role === 'user')
      ? targetTab.title
      : (targetTab.mode || appMode) === 'code' ? 'New code' : 'New chat';
    const nextWithUser = [...existing, userMsg];

    await executePromptRun({
      tabId: item.tabId,
      trimmedPrompt: trimmed,
      effectivePrompt,
      nextWithUser,
      nextTitle,
      speakFinalResponse: voiceMode,
    });
  }

  function handleRemoveQueuedMessage(id: string) {
    setMessageQueue((prev) => prev.filter((item) => item.id !== id));
    if (editingQueueId === id) {
      setEditingQueueId(null);
      setEditingQueueText('');
    }
  }

  function handleSaveQueuedEdit(id: string) {
    const nextText = editingQueueText.trim();
    if (!nextText) {
      handleRemoveQueuedMessage(id);
      return;
    }
    setMessageQueue((prev) =>
      prev.map((item) => (item.id === id ? { ...item, content: nextText } : item))
    );
    setEditingQueueId(null);
    setEditingQueueText('');
  }

  async function handleSteerQueuedMessage(item: {
    id: string;
    tabId: string;
    content: string;
    replyTo?: { id: string; role: 'user' | 'assistant'; content: string };
  }) {
    setMessageQueue((prev) => prev.filter((q) => q.id !== item.id));
    if (editingQueueId === item.id) {
      setEditingQueueId(null);
      setEditingQueueText('');
    }
    if (!isExecuting) {
      await dispatchQueuedMessage(item);
      return;
    }
    steeringQueueItemRef.current = item;
    manualStopRef.current = false;
    await handleStop({ fromSteer: true });
  }

  useEffect(() => {
    if (isExecuting) return;
    if (steeringQueueItemRef.current) {
      const steerItem = steeringQueueItemRef.current;
      steeringQueueItemRef.current = null;
      void dispatchQueuedMessage(steerItem);
      return;
    }
    if (manualStopRef.current || messageQueue.length === 0) return;
    const nextForTab = messageQueue.find((q) => q.tabId === activeTab.id) || messageQueue[0];
    if (!nextForTab) return;
    setMessageQueue((prev) => prev.filter((q) => q.id !== nextForTab.id));
    void dispatchQueuedMessage(nextForTab);
  }, [isExecuting, messageQueue]);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = prompt.trim();
    if (!trimmed) return;

    const tabId = activeTab.id;
    const replyContext = replyingTo;

    if (isExecuting) {
      setMessageQueue((prev) => [
        ...prev,
        {
          id: `q-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
          tabId,
          content: trimmed,
          replyTo: replyContext || undefined,
        },
      ]);
      setPrompt('');
      setAttachments([]);
      setReplyingTo(null);
      if (textareaRef.current) textareaRef.current.style.height = 'auto';
      return;
    }

    manualStopRef.current = false;
    const speakFinalResponse = voiceMode;
    const effectivePrompt = replyContext
      ? `Replying to ${replyContext.role === 'user' ? 'previous message' : 'assistant'} ("${replyContext.content.slice(0, 240)}"):\n\n${trimmed}`
      : trimmed;
    const userMsg: ChatEntry = {
      id: createChatMessageId('u'),
      role: 'user',
      content: trimmed,
      replyTo: replyContext || undefined,
    };
    const nextWithUser = [...(messagesByTab[tabId] || []), userMsg];
    const nextTitle = nextWithUser.filter((message) => message.role === 'user').length === 1
      ? (activeTab.mode || appMode) === 'code' ? 'New code' : 'New chat'
      : activeTab.title;

    setPrompt('');
    setAttachments([]);
    setReplyingTo(null);
    if (textareaRef.current) textareaRef.current.style.height = 'auto';

    await executePromptRun({
      tabId,
      trimmedPrompt: trimmed,
      effectivePrompt,
      nextWithUser,
      nextTitle,
      speakFinalResponse,
    });
  }

  async function handleRetryMessage(userMsg: ChatEntry) {
    if (isExecuting || userMsg.role !== 'user' || !userMsg.content.trim()) return;
    const tabId = activeTab.id;
    const current = messagesByTab[tabId] || [];
    const idx = current.findIndex((m) => m.id === userMsg.id);
    if (idx < 0) return;

    const nextWithUser = current.slice(0, idx + 1);
    const trimmed = userMsg.content.trim();
    const effectivePrompt = userMsg.replyTo
      ? `Replying to ${userMsg.replyTo.role === 'user' ? 'previous message' : 'assistant'} ("${userMsg.replyTo.content.slice(0, 240)}"):\n\n${trimmed}`
      : trimmed;

    await executePromptRun({
      tabId,
      trimmedPrompt: trimmed,
      effectivePrompt,
      nextWithUser,
      nextTitle: activeTab.title,
      speakFinalResponse: voiceMode,
    });
  }

  async function executePromptRun(params: {
    tabId: string;
    trimmedPrompt: string;
    effectivePrompt: string;
    nextWithUser: ChatEntry[];
    nextTitle: string;
    speakFinalResponse: boolean;
  }) {
    const { tabId, trimmedPrompt: trimmed, effectivePrompt, nextWithUser, nextTitle, speakFinalResponse } = params;
    setActionError(null);
    const targetTab = tabs.find((tab) => tab.id === tabId) || activeTab;
    const baseMode = targetTab.mode || appMode;
    const runMode = baseMode === 'code' && activeDockSurface === 'plan' ? 'plan' : baseMode;
    const projectId = targetTab.projectId || (baseMode === 'code' ? activeProjectId : undefined);
    const projectLabel = projectId ? projects.find((p) => p.id === projectId)?.label || 'Project' : '';

    const pendingAssistantId = createChatMessageId('a');
    const streamingAssistantMsg: ChatEntry = {
      id: pendingAssistantId,
      role: 'assistant',
      content: '',
      steps: [{ type: 'reasoning', text: '', state: 'streaming' }],
      originalPrompt: trimmed,
    };

    setTabs((prev) => prev.map((t) => (t.id === tabId ? { ...t, title: nextTitle } : t)));
    setMessagesByTab((prev) => ({ ...prev, [tabId]: [...nextWithUser, streamingAssistantMsg] }));
    setIsExecuting(true);
    let resolvedTitle = nextTitle;
    let latestMessagesForTitle: ChatEntry[] | null = null;
    const shouldGenerateChatTitle =
      nextWithUser.filter((message) => message.role === 'user').length === 1 &&
      /^New (?:chat|code)$/i.test(targetTab.title || nextTitle) &&
      !chatMeta.titles[targetTab.sessionId] &&
      !manualTitleOverridesRef.current.has(targetTab.sessionId);
    void saveChatSession({
      sessionId: targetTab.sessionId,
      title: nextTitle,
      projectId,
      projectLabel,
      modelId: targetTab.modelId,
      mode: runMode,
      messages: nextWithUser,
    });
    if (shouldGenerateChatTitle) {
      void requestChatTitle(trimmed, targetTab.modelId)
        .catch(() => fallbackChatTitle(trimmed))
        .then((generatedTitle) => {
          if (!generatedTitle || manualTitleOverridesRef.current.has(targetTab.sessionId)) return;
          resolvedTitle = generatedTitle;
          setTabs((prev) => prev.map((tab) =>
            tab.id === tabId && !manualTitleOverridesRef.current.has(targetTab.sessionId)
              ? { ...tab, title: generatedTitle }
              : tab
          ));
          const messagesToSave = latestMessagesForTitle || messagesByTabRef.current[tabId] || nextWithUser;
          void saveChatSession({
            sessionId: targetTab.sessionId,
            title: generatedTitle,
            projectId,
            projectLabel,
            modelId: targetTab.modelId,
            mode: runMode,
            messages: messagesToSave,
          });
        });
    }

    try {
      let permissionMode = 'autonomous';
      let personaTone = 'programmer';
      let systemPrompt = '';
      let rules = '';
      try {
        permissionMode = localStorage.getItem('gram.settings.permissionMode') || 'autonomous';
        personaTone = localStorage.getItem('gram.settings.personaTone') || 'programmer';
        systemPrompt = localStorage.getItem('gram.settings.systemPrompt') || '';
        rules = localStorage.getItem('gram.settings.rules') || '';
      } catch {}

      const promptPrefixes: string[] = [];
      if (personaTone && personaTone !== 'programmer') {
        promptPrefixes.push(`[Style: ${personaTone}]`);
      }
      if (systemPrompt.trim()) {
        promptPrefixes.push(`[System Instructions]\n${systemPrompt.trim()}`);
      }
      if (rules.trim()) {
        promptPrefixes.push(`[Workspace Rules]\n${rules.trim()}`);
      }
      const promptWithSettings =
        promptPrefixes.length > 0 ? `${promptPrefixes.join('\n\n')}\n\n${effectivePrompt}` : effectivePrompt;

      const selectedDevice = devices.find((d) => d.deviceId === activeDeviceId);
      const isCloudDevice = selectedDevice?.kind === 'cloud';
      const useLyra = authState.connected && isCloudDevice;
      const historyPayload = nextWithUser
        .slice(0, -1)
        .filter((m) => (m.role === 'user' || m.role === 'assistant') && m.content && !m.isError && !m.id.startsWith('e-'))
        .slice(-16)
        .map((m) => ({ role: m.role, content: m.content }));

      const runStartMs = Date.now();
      let finalResult: any = null;
      let streamedThinking = '';
      let streamedText = '';
      let streamedTools: TraceStep[] = [];
      let streamedApproval: ApprovalItem | undefined;
      let streamedThinkingSec: number | undefined;

      const runController = new AbortController();
      activeRequestRef.current = runController;
      const clientRunId = useLyra ? undefined : `run-${Date.now()}`;
      if (clientRunId) setActiveRunId(clientRunId);
      const streamEndpoint = useLyra ? '/api/lyra/chat/stream' : '/api/run/stream';
      const streamBody = useLyra
        ? {
            prompt: promptWithSettings,
            sessionId: targetTab.sessionId,
            mode: runMode,
            pinnedModelId: runMode === 'chat' || targetTab.modelId === 'auto' ? undefined : targetTab.modelId,
            projectId,
          }
        : {
            prompt: effectivePrompt,
            modelId: runMode === 'chat' ? 'auto' : targetTab.modelId,
            mode: runMode,
            runId: clientRunId,
            sessionId: targetTab.sessionId,
            projectId,
            projectLabel,
            deviceId: activeDeviceId,
            history: historyPayload,
            systemPrompt: promptPrefixes.join('\n\n') || undefined,
            effort,
            requireApproval: permissionMode === 'ask',
          };
      const res = await fetch(streamEndpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(streamBody),
        signal: runController.signal,
      });

      if (!res.ok || !res.body) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.error || `Request failed (${res.status})`);
      }

      let liveThinking = '';
      let liveText = '';
      let shownThinking = '';
      let shownText = '';
      let rafId: number | null = null;
      let liveTools: TraceStep[] = [];
      let liveApproval: ApprovalItem | undefined;
      let thinkingStartedAt: number | null = null;
      let thinkingDurationSeconds: number | undefined;

        const updateLiveMsg = (done = false) => {
          streamedThinking = liveThinking;
          streamedText = liveText;
          streamedTools = liveTools;
          streamedApproval = liveApproval;
          streamedThinkingSec = thinkingDurationSeconds;

          const renderThinking = done ? liveThinking : shownThinking;
          const renderText = done ? liveText : shownText;

          const liveSteps: TraceStep[] = [];
          if (renderThinking.trim() || (!done && thinkingStartedAt !== null)) {
            liveSteps.push({
              type: 'reasoning',
              text: renderThinking,
              state: done || thinkingDurationSeconds !== undefined ? 'done' : 'streaming',
              durationSeconds: thinkingDurationSeconds,
            });
          }
          liveSteps.push(...liveTools);

          const liveMsg: ChatEntry = {
            id: pendingAssistantId,
            role: 'assistant',
            content: liveApproval ? '' : renderText,
            steps: liveSteps.length > 0 ? liveSteps : undefined,
            approval: liveApproval,
            originalPrompt: trimmed,
          };
          setMessagesByTab((prev) => ({ ...prev, [tabId]: [...nextWithUser, liveMsg] }));
        };

        const flushSmoothRender = (done = false) => {
          if (rafId !== null) {
            cancelAnimationFrame(rafId);
            rafId = null;
          }
          shownThinking = liveThinking;
          shownText = liveText;
          updateLiveMsg(done);
        };

        const stepSmoothRender = () => {
          rafId = null;
          shownThinking = liveThinking;
          shownText = liveText;
          updateLiveMsg(false);
        };

        const scheduleSmoothRender = () => {
          if (rafId === null) {
            rafId = requestAnimationFrame(stepSmoothRender);
          }
        };

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let liveDiagnostics: Record<string, unknown> | undefined;

        while (true) {
          const { value, done } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });

          let nl: number;
          while ((nl = buffer.indexOf('\n')) >= 0) {
            const line = buffer.slice(0, nl).trim();
            buffer = buffer.slice(nl + 1);
            if (!line.startsWith('data:')) continue;
            const raw = line.slice(5).trim();
            if (!raw) continue;

            let evt: any;
            try {
              evt = JSON.parse(raw);
            } catch {
              continue;
            }

            if (evt.type === 'start') {
              setActiveRunId(evt.runId || null);
            } else if (evt.type === 'task_started') {
              setActivePromptTitle(evt.title || trimmed.slice(0, 64));
              loadRecentTasks();
            } else if (evt.type === 'thinking_delta') {
              if (thinkingStartedAt === null) thinkingStartedAt = Date.now();
              liveThinking += evt.delta || '';
              scheduleSmoothRender();
            } else if (evt.type === 'text_delta') {
              if (thinkingStartedAt !== null && thinkingDurationSeconds === undefined) {
                thinkingDurationSeconds = Math.max(1, Math.round((Date.now() - thinkingStartedAt) / 1000));
              }
              liveText += evt.delta || '';
              scheduleSmoothRender();
            } else if (evt.type === 'tool_start') {
              setActivePromptTitle((prev) => prev || trimmed.slice(0, 64));
              liveTools = [
                ...liveTools,
                {
                  type: 'tool-runCommand',
                  command: evt.command || evt.name || 'tool',
                  state: 'running',
                },
              ];
              flushSmoothRender(false);
            } else if (evt.type === 'tool_result') {
              const idx = liveTools.findIndex(
                (t) => t.type === 'tool-runCommand' && t.command === (evt.command || evt.name) && t.state === 'running'
              );
              const updatedTool: TraceStep = {
                type: 'tool-runCommand',
                command: evt.command || evt.name || 'tool',
                state: evt.error ? 'output-error' : 'output-available',
                output: evt.result,
                errorText: evt.error,
              };
              if (idx >= 0) {
                liveTools = [...liveTools.slice(0, idx), updatedTool, ...liveTools.slice(idx + 1)];
              } else {
                liveTools = [...liveTools, updatedTool];
              }
              flushSmoothRender(false);
            } else if (evt.type === 'approval_required') {
              liveApproval = {
                id: evt.approval?.id || `apr-${Date.now()}`,
                runId: evt.runId,
                sessionId: evt.sessionId || targetTab.sessionId,
                command: evt.approval?.command || trimmed,
                description: evt.approval?.description || 'This operation runs in this project.',
                state: 'approval-requested',
              };
              flushSmoothRender(false);
            } else if (evt.type === 'plan_updated' && typeof evt.markdown === 'string') {
              setPlanBySession((prev) => ({
                ...prev,
                [targetTab.sessionId]: evt.markdown,
              }));
            } else if (evt.type === 'context_update' && evt.usage) {
              setContextUsageBySession((prev) => ({
                ...prev,
                [targetTab.sessionId]: evt.usage,
              }));
            } else if (evt.type === 'context_compacted') {
              const fmtTok = (n: number) => (n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n));
              liveTools = [
                ...liveTools,
                {
                  type: 'tool-runCommand',
                  command: `Compact context (${fmtTok(evt.beforeTokens || 0)} -> ${fmtTok(evt.afterTokens || 0)} tokens, ${evt.summarizedTurns || 0} turns)`,
                  state: 'output-available',
                },
              ];
              flushSmoothRender(false);
            } else if (evt.type === 'error') {
              liveText = evt.error || 'Execution failed.';
              if (evt.diagnostics && typeof evt.diagnostics === 'object') {
                liveDiagnostics = evt.diagnostics as Record<string, unknown>;
              }
              flushSmoothRender(true);
            } else if (evt.type === 'done') {
              finalResult = evt.result;
              if (evt.result?.usage) {
                setContextUsageBySession((prev) => ({
                  ...prev,
                  [targetTab.sessionId]: evt.result.usage,
                }));
              }
            }
          }
        }
        if (rafId !== null) {
          cancelAnimationFrame(rafId);
          rafId = null;
        }

      const result = finalResult || {};
      const elapsedSec = Math.max(1, Math.round((Date.now() - runStartMs) / 1000));
      setActiveRunId(result.runId || result.messageId || null);

      const finalThinking =
        typeof result.thinking === 'string' && result.thinking.trim().length > 0
          ? result.thinking.trim()
          : streamedThinking.trim();
      const steps: TraceStep[] = [];
      if (finalThinking) {
        steps.push({
          type: 'reasoning',
          text: finalThinking,
          state: 'done',
          durationSeconds: streamedThinkingSec ?? elapsedSec,
        });
      }
      if (Array.isArray(result.toolCalls) && result.toolCalls.length > 0) {
        for (const tc of result.toolCalls) {
          steps.push({
            type: 'tool-runCommand',
            command: tc.command || tc.name || 'tool',
            state: tc.error ? 'output-error' : 'output-available',
            output: typeof tc.result === 'string' ? tc.result : JSON.stringify(tc.result ?? '', null, 2),
            errorText: tc.error,
          });
        }
      } else if (streamedTools.length > 0) {
        steps.push(...streamedTools);
      }

      const pendingApproval = result.checkpoint?.pendingApproval;
      const approval: ApprovalItem | undefined = pendingApproval
        ? {
            id: pendingApproval.id,
            runId: result.runId,
            sessionId: targetTab.sessionId,
            command: pendingApproval.command || trimmed,
            description: pendingApproval.description || 'This operation runs in this project.',
            state: 'approval-requested',
          }
        : streamedApproval;

      const assistantMsg: ChatEntry = {
        id: pendingAssistantId,
        role: 'assistant',
        runId: result.runId,
        remoteSessionId: useLyra ? result.sessionId : undefined,
        lane: result.lane,
        content: approval ? '' : result.output || result.content || streamedText || '',
        steps: steps.length > 0 ? steps : undefined,
        tasks: Array.isArray(result.tasks) && result.tasks.length > 0 ? result.tasks : undefined,
        approval,
        workers: Array.isArray(result.workers) ? result.workers : undefined,
        previewUrl: result.previewUrl,
        originalPrompt: trimmed,
        codingHandoffPrompt: runMode === 'chat' && isCodingWorkPrompt(trimmed) ? trimmed : undefined,
        isError: result.completed === false || result.checkpoint?.stage === 'failed' || undefined,
        diagnostics: result.diagnostics || result.checkpoint?.diagnostics || liveDiagnostics,
      };
      const fullMessages = [...nextWithUser, assistantMsg];
      latestMessagesForTitle = fullMessages;
      setMessagesByTab((prev) => ({ ...prev, [tabId]: fullMessages }));
      await saveChatSession({
        sessionId: targetTab.sessionId,
        title: resolvedTitle,
        projectId,
        projectLabel,
        modelId: targetTab.modelId,
        mode: runMode,
        messages: fullMessages,
      });
      loadRecentTasks();
      void loadManagedProcesses();
      if (speakFinalResponse && assistantMsg.content) void speakSummary(assistantMsg.content);
    } catch (err: any) {
      if (err?.name === 'AbortError') {
        setMessagesByTab((prev) => ({ ...prev, [tabId]: nextWithUser }));
        return;
      }
      const errMsg: ChatEntry = {
        id: createChatMessageId('e'),
        role: 'assistant',
        content: err?.message || 'Execution failed.',
        isError: true,
      };
      const fullMessages = [...nextWithUser, errMsg];
      latestMessagesForTitle = fullMessages;
      setMessagesByTab((prev) => ({ ...prev, [tabId]: fullMessages }));
      await saveChatSession({
        sessionId: targetTab.sessionId,
        title: resolvedTitle,
        projectId,
        projectLabel,
        modelId: targetTab.modelId,
        mode: runMode,
        messages: fullMessages,
      });
    } finally {
      activeRequestRef.current = null;
      setIsExecuting(false);
      setActivePromptTitle(null);
    }
  }

  async function handleStop(opts?: { fromSteer?: boolean }) {
    if (!opts?.fromSteer) {
      manualStopRef.current = true;
    }
    activeRequestRef.current?.abort();
    stopSpeechPlayback();
    if (activeRunId) await fetch('/api/cancel', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ runId: activeRunId }),
    }).catch(() => {});
    setIsExecuting(false);
  }

  shortcutActionsRef.current = {
    openNewChatTab: () => openNewChatTab(),
    switchAppMode,
    handleRetryMessage,
    handleStop: () => void handleStop(),
    navigateToPage,
    isExecuting,
    appMode,
    activeMessages,
    shortcutsOpen,
    projectHubId,
  };

  async function handleContinueInCode(sourceMsg: ChatEntry) {
    if (handoffBusyId) return;
    setHandoffBusyId(sourceMsg.id);
    try {
      const currentMessages = (messagesByTab[activeTab.id] || [])
        .filter((m) => (m.role === 'user' || m.role === 'assistant') && m.content)
        .map((m) => ({ role: m.role, content: m.content }));
      const goal = sourceMsg.codingHandoffPrompt || sourceMsg.originalPrompt || '';
      const res = await fetch('/api/chat/summarize-handoff', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messages: currentMessages, goal }),
      });
      const data = await res.json().catch(() => ({}));
      const summary = data.summary || (goal ? `Goal: ${goal}` : 'Continue from previous chat.');

      const newTabId = `tab-${Date.now()}`;
      const newSessionId = `session-${Date.now()}`;
      const defaultModel = (() => {
        try {
          return localStorage.getItem('gram.settings.defaultModel') || 'auto';
        } catch {
          return 'auto';
        }
      })();
      const newTitle = (goal || activeTab.title || 'Coding task').slice(0, 40);
      const handoffEntry: ChatEntry = {
        id: createChatMessageId('handoff'),
        role: 'assistant',
        content: `### Context from Chat\n\n${summary}`,
      };
      setTabs((prev) => [...prev, { id: newTabId, title: newTitle, sessionId: newSessionId, modelId: defaultModel, mode: 'code', projectId: activeProjectId === 'local' ? undefined : activeProjectId }]);
      setActiveTabId(newTabId);
      setMessagesByTab((prev) => ({ ...prev, [newTabId]: [handoffEntry] }));
      setAppMode('code');
      setPrompt(goal);
      await saveChatSession({
        sessionId: newSessionId,
        title: newTitle,
        projectId: activeProjectId,
        projectLabel: projects.find((p) => p.id === activeProjectId)?.label || 'Local workspace',
        modelId: defaultModel,
        mode: 'code',
        messages: [handoffEntry],
      });
    } finally {
      setHandoffBusyId(null);
    }
  }

  async function handleEscalate(promptText?: string) {
    if (!promptText) return;
    const res = await fetch('/api/lyra/escalate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prompt: promptText, sessionId: activeTab.sessionId }),
    });
    const data = await res.json();
    const msg: ChatEntry = {
      id: createChatMessageId('s'),
      role: 'assistant',
      runId: data.runId,
      remoteSessionId: data.sessionId,
      lane: data.lane,
      content: data.output || data.content || 'Escalated to Lyra swarm.',
      workers: Array.isArray(data.workers) ? data.workers : undefined,
      previewUrl: data.previewUrl,
      originalPrompt: promptText,
    };
    const tabId = activeTab.id;
    const prevMessages = messagesByTab[tabId] || [];
    const fullMessages = [...prevMessages, msg];
    setMessagesByTab((prev) => ({ ...prev, [tabId]: fullMessages }));
    const projectId = activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined);
    const projectLabel = projects.find((p) => p.id === projectId)?.label || 'Local workspace';
    saveChatSession({
      sessionId: activeTab.sessionId,
      title: activeTab.title || 'New Chat',
      projectId,
      projectLabel,
      modelId: activeTab.modelId,
      messages: fullMessages,
    });
    loadRecentTasks();
  }

  async function handlePullLyraPatch(runId: string, sessionId?: string) {
    try {
      const res = await fetch('/api/lyra/swarm/pull', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runId, sessionId: sessionId || activeTab.sessionId }),
      });
      const data = await res.json();
      if (!res.ok) throw new Error(data.error || `Patch pull failed (${res.status}).`);
      const diffSteps: TraceStep[] = (data.diffs || []).map((diff: any) => ({
        type: 'tool-runCommand',
        command: `Lyra patch: ${diff.path}`,
        state: 'output-available',
        output: diff.patch || '(no textual diff)',
      }));
      for (const diff of data.diffs || []) {
        if (diff.hostPatch && diff.hostPatch !== diff.patch) diffSteps.push({
          type: 'tool-runCommand',
          command: `Workspace diff: ${diff.path}`,
          state: 'output-available',
          output: diff.hostPatch,
        });
      }
      const patchMsg: ChatEntry = {
        id: createChatMessageId(`lyra-patch-${runId}`),
        role: 'assistant',
        runId,
        reviewLayerId: data.layerId,
        content: `Pulled Lyra swarm patch into GramFS layer \`${data.layerId}\`. Review the file diffs before committing.`,
        steps: diffSteps,
      };
      const fullMessages = [...(messagesByTab[activeTab.id] || []), patchMsg];
      setMessagesByTab((prev) => ({ ...prev, [activeTab.id]: fullMessages }));
      await saveChatSession({
        sessionId: activeTab.sessionId,
        title: activeTab.title,
        projectId: activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined),
        projectLabel: projects.find((project) => project.id === (activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined)))?.label || 'Local workspace',
        modelId: activeTab.modelId,
        messages: fullMessages,
      });
    } catch (error: any) {
      const errorMsg: ChatEntry = { id: createChatMessageId('lyra-patch-error'), role: 'assistant', content: error?.message || 'Could not pull the Lyra patch.' };
      setMessagesByTab((prev) => ({ ...prev, [activeTab.id]: [...(prev[activeTab.id] || []), errorMsg] }));
    }
  }

  async function handleCommitLyraLayer(message: ChatEntry) {
    if (!message.reviewLayerId) return;
    try {
      const response = await fetch('/api/lyra/swarm/layer/commit', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ layerId: message.reviewLayerId }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || `Overlay commit failed (${response.status}).`);
      const tabId = activeTab.id;
      const messages = messagesByTab[tabId] || [];
      const next = messages.map((item) => item.id === message.id
        ? { ...item, content: `${item.content}\n\nCommitted ${[...(data.committedFiles || []), ...(data.deletedFiles || [])].length} file change(s) to the workspace.`, reviewLayerId: undefined }
        : item);
      setMessagesByTab((prev) => ({ ...prev, [tabId]: next }));
      await saveChatSession({ sessionId: activeTab.sessionId, title: activeTab.title, projectId: activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined), projectLabel: projects.find((p) => p.id === (activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined)))?.label || 'Local workspace', modelId: activeTab.modelId, mode: activeTab.mode, messages: next });
      emitSystemToast({ title: 'Changes committed to workspace', variant: 'success' });
    } catch (error: any) {
      const msg = error?.message || 'Could not commit the Lyra overlay.';
      emitSystemToast({ title: 'Overlay commit failed', description: msg, variant: 'error' });
      const errorMsg: ChatEntry = { id: createChatMessageId('lyra-commit-error'), role: 'assistant', content: msg };
      setMessagesByTab((prev) => ({ ...prev, [activeTab.id]: [...(prev[activeTab.id] || []), errorMsg] }));
    }
  }

  async function handleRollbackLyraLayer(message: ChatEntry) {
    if (!message.reviewLayerId) return;
    try {
      const response = await fetch('/api/lyra/swarm/layer/rollback', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ layerId: message.reviewLayerId }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || `Overlay rollback failed (${response.status}).`);
      const tabId = activeTab.id;
      const messages = messagesByTab[tabId] || [];
      const next = messages.map((item) => item.id === message.id
        ? { ...item, content: `${item.content}\n\nDiscarded the Lyra overlay.`, reviewLayerId: undefined }
        : item);
      setMessagesByTab((prev) => ({ ...prev, [tabId]: next }));
      await saveChatSession({ sessionId: activeTab.sessionId, title: activeTab.title, projectId: activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined), projectLabel: projects.find((p) => p.id === (activeTab.projectId || (activeTab.mode === 'code' ? activeProjectId : undefined)))?.label || 'Local workspace', modelId: activeTab.modelId, mode: activeTab.mode, messages: next });
      emitSystemToast({ title: 'Overlay discarded', variant: 'info' });
    } catch (error: any) {
      const msg = error?.message || 'Could not discard the Lyra overlay.';
      emitSystemToast({ title: 'Overlay discard failed', description: msg, variant: 'error' });
      const errorMsg: ChatEntry = { id: createChatMessageId('lyra-rollback-error'), role: 'assistant', content: msg };
      setMessagesByTab((prev) => ({ ...prev, [activeTab.id]: [...(prev[activeTab.id] || []), errorMsg] }));
    }
  }

  function openInDefaultBrowser(targetUrl: string) {
    fetch('/api/open-external', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url: targetUrl }),
    })
      .then((res) => {
        if (!res.ok) window.open(targetUrl, '_blank', 'noopener,noreferrer');
      })
      .catch(() => {
        window.open(targetUrl, '_blank', 'noopener,noreferrer');
      });
  }

  useEffect(() => {
    const onDocumentClick = (e: MouseEvent) => {
      const target = e.target as HTMLElement | null;
      const anchor = target?.closest?.('a[href]') as HTMLAnchorElement | null;
      if (!anchor) return;
      const rawHref = anchor.getAttribute('href')?.trim();
      if (!rawHref || rawHref.startsWith('#')) return;
      let parsed: URL;
      try {
        parsed = new URL(rawHref, window.location.href);
      } catch {
        return;
      }
      if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:' && parsed.protocol !== 'file:') {
        return;
      }
      const isModifierClick = e.metaKey || e.ctrlKey || e.button === 1;
      const isExternal = parsed.origin !== window.location.origin;
      const isBlank = anchor.target === '_blank';
      if (isModifierClick || isExternal || isBlank) {
        e.preventDefault();
        e.stopPropagation();
        openInDefaultBrowser(parsed.toString());
      }
    };
    document.addEventListener('click', onDocumentClick, true);
    return () => document.removeEventListener('click', onDocumentClick, true);
  }, []);

  function handleOpenPreview(url: string) {
    const isDesktop = Boolean((window as any).__TAURI__ || window.navigator?.userAgent?.includes('Electron'));
    if (!isDesktop) {
      window.open(url, '_blank', 'noopener,noreferrer');
      return;
    }
    setPreviewModalUrl(url);
  }

  async function submitAuth(e: React.FormEvent) {
    e.preventDefault();
    setLoginError('');
    setAuthSubmitting(true);
    try {
      const endpoint = authMode === 'signup' ? '/api/auth/sign-up' : '/api/auth/sign-in';
      const payload =
        authMode === 'signup'
          ? { name: signupName, email: loginEmail, password: loginPassword }
          : { email: loginEmail, password: loginPassword };
      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        setLoginError(
          d.message || d.error || (authMode === 'signup' ? 'Sign up failed' : 'Sign in failed')
        );
        return;
      }
      setLoginPassword('');
      await Promise.allSettled([refreshAuth(), loadChatSessions(), loadRecentTasks()]);
      if (authMode === 'signup') {
        navigateToPage('onboarding', { direction: 'forward' });
      } else {
        navigateToPage('chat', { direction: 'forward' });
      }
    } catch (err: any) {
      setLoginError(err?.message || (authMode === 'signup' ? 'Sign up failed' : 'Sign in failed'));
    } finally {
      setAuthSubmitting(false);
    }
  }

  async function handleGoogleSignIn() {
    setLoginError('');
    setGoogleSubmitting(true);
    try {
      const isDesktopShell = Boolean(
        typeof window !== 'undefined' && ((window as any).__TAURI__ || (window as any).__TAURI_INTERNALS__)
      );
      const res = await fetch('/api/auth/google', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          returnTo: window.location.origin + '/',
          desktop: isDesktopShell,
        }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok || (!data?.url && !data?.handoffId)) {
        setLoginError(data?.error || 'Google sign-in is not configured yet.');
        return;
      }
      if (isDesktopShell && data?.handoffId) {
        const hid = String(data.handoffId);
        const deadline = Date.now() + 2 * 60 * 1000;
        while (Date.now() < deadline) {
          await new Promise((r) => setTimeout(r, 1200));
          const poll = await fetch(`/api/auth/handoff-status?hid=${encodeURIComponent(hid)}`).catch(() => null);
          const statusData = poll && poll.ok ? await poll.json().catch(() => null) : null;
          if (statusData?.status === 'completed' && statusData?.token) {
            const verify = await fetch('/api/auth/session-token', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ token: statusData.token }),
            });
            if (!verify.ok) {
              setLoginError('Could not complete Google session handoff.');
              return;
            }
            await refreshAuth();
            let onboardingDone = false;
            try {
              onboardingDone = localStorage.getItem('gram.onboarding.completed') === 'true';
            } catch {}
            navigateToPage(onboardingDone ? 'chat' : 'onboarding', { direction: 'forward' });
            return;
          }
          if (statusData?.status === 'unknown') {
            setLoginError('Google sign-in session expired. Please try again.');
            return;
          }
        }
        setLoginError('Google sign-in timed out. Please try again.');
        return;
      }
      window.location.href = data.url;
    } catch (err: any) {
      setLoginError(err?.message || 'Unable to start Google sign-in.');
    } finally {
      setGoogleSubmitting(false);
    }
  }

  const activeProjectLabel = projects.find((p) => p.id === activeProjectId)?.label || 'Local workspace';
  const islandActivities: IslandActivity[] = isExecuting && activePromptTitle && !recentTasks.some((t) => t.sessionId === activeTab.sessionId && (t.isActive || t.stage === 'executing'))
    ? [
        {
          runId: activeRunId || `live-${activeTab.sessionId}`,
          sessionId: activeTab.sessionId,
          title: activePromptTitle,
          prompt: activePromptTitle,
          stage: 'executing',
          projectId: activeProjectId,
          projectLabel: activeProjectLabel,
          kind: 'agent',
          isActive: true,
          updatedAt: new Date().toISOString(),
        },
        ...recentTasks,
      ]
    : recentTasks;

    if (bootError) {
      return (
        <AppErrorScreen
          title={bootError.title}
          message={bootError.message}
          details={bootError.details}
          retryLabel="Retry connection"
          onRetry={() => window.location.reload()}
        />
      );
    }

    if (isAppLoading) {
    return (
      <div className="gram-boot-screen" role="status" aria-label="Loading Gram">
        <GramMark size={64} className="gram-boot-logo" />
      </div>
    );
  }

  if (activePage === 'auth') {
    return (
      <AuthPage
        mode={authMode}
        name={signupName}
        email={loginEmail}
        password={loginPassword}
        error={loginError}
        submitting={authSubmitting}
        googleSubmitting={googleSubmitting}
        onModeChange={(nextMode) =>
          navigateToPage('auth', {
            authMode: nextMode,
            direction: nextMode === 'signup' ? 'forward' : 'backward',
          })
        }
        onNameChange={setSignupName}
        onEmailChange={setLoginEmail}
        onPasswordChange={setLoginPassword}
        onSubmit={submitAuth}
        onGoogleSignIn={handleGoogleSignIn}
        onBackToWorkspace={() => navigateToPage('chat', { direction: 'backward' })}
      />
    );
  }

  if (activePage === 'onboarding') {
    return (
      <OnboardingWizard
        userName={authState.user?.name || signupName}
        initialMode={appMode === 'code' ? 'code' : 'chat'}
        currentTheme={theme}
        onSelectTheme={handleThemeChange}
        onComplete={(preferredMode) => {
          try {
            localStorage.setItem('gram.onboarding.completed', 'true');
          } catch {}
          switchAppMode(preferredMode);
          navigateToPage('chat', { direction: 'forward' });
        }}
      />
    );
  }

  return (
    <MessageScrollerProvider>
      <div className="app-shell" data-theme={theme}>
        <header className="topbar">
          <div className="topbar-left">
            <a className="brand" href="/" aria-label="Gram home">
              <img
                src={theme === 'bone' ? '/icons/gram-logo-light.svg' : '/icons/gram-logo.svg'}
                alt="Gram"
                className="h-6 w-6 rounded-md"
              />
            </a>
            <nav className="topbar-nav">
              <button
                type="button"
                className={`nav-link ${appMode === 'chat' ? 'active' : ''}`}
                onClick={() => switchAppMode('chat')}
              >
                Chat
              </button>
              <button
                type="button"
                className={`nav-link ${appMode === 'code' ? 'active' : ''}`}
                onClick={() => switchAppMode('code')}
              >
                Code
              </button>
            </nav>
            <div className="topbar-divider" />
            <DynamicIslandBar
              activities={islandActivities}
              activeSessionId={activeTab.sessionId}
              onSelectActivity={handleSelectTask}
              onApproveActivity={handleApproveActivity}
              onContinueActivity={async (activity, feedback) => {
                await fetch('/api/tasks/continue', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({
                    sessionId: activity.sessionId,
                    runId: activity.runId,
                    feedback,
                  }),
                });
                await loadRecentTasks();
              }}
              onStopActivity={(runId) => {
                fetch('/api/cancel', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ runId }),
                })
                  .then(() => {
                    if (runId === activeRunId) setIsExecuting(false);
                    loadRecentTasks();
                  })
                  .catch(() => {});
              }}
            />
          </div>
          <div className="topbar-center" />
          <div className="topbar-right">
            <div style={{ position: 'relative' }} data-popover-root="user">
              <button
                className="user-pill"
                type="button"
                title={authState.user ? authState.user.name || authState.user.email : 'Account'}
                aria-label={authState.user ? authState.user.name || authState.user.email : 'Account'}
                style={{
                  padding: 0,
                  height: '32px',
                  width: '32px',
                  borderRadius: '999px',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
                onClick={() => setUserMenuOpen((v) => !v)}
              >
                <span className="shadcn-avatar shadcn-avatar-sm" style={{ width: '28px', height: '28px', fontSize: '12px' }}>
                  <span className="shadcn-avatar-fallback">
                    {authState.user ? (authState.user.name || authState.user.email).charAt(0).toUpperCase() : 'G'}
                  </span>
                </span>
              </button>
              {userMenuOpen && (
                <div
                  className="user-profile-popover"
                  style={{
                    display: 'flex',
                    position: 'absolute',
                    right: 0,
                    top: 'calc(100% + 6px)',
                    width: '220px',
                    background: 'var(--bg-panel)',
                    border: '1px solid var(--border-color)',
                    borderRadius: 'var(--radius-md)',
                    padding: '6px',
                    zIndex: 9999,
                    flexDirection: 'column',
                    gap: '2px',
                  }}
                >
                  <div style={{ padding: '4px 8px', fontSize: '11px', color: 'var(--text-muted)' }}>
                    {authState.user
                      ? `${authState.user.name ? `${authState.user.name} · ` : ''}${authState.user.email}`
                      : 'Local session · Not signed in'}
                  </div>
                  <div className="shadcn-context-menu-separator" />
                  <button
                    type="button"
                    className="shadcn-context-menu-item"
                    onClick={() => (activePage === 'settings' ? closeSettings() : openSettings())}
                  >
                    {activePage === 'settings' ? 'Back to Chat' : 'Settings'}
                  </button>
                  <button
                    type="button"
                    className="shadcn-context-menu-item"
                    onClick={() => navigateToPage('onboarding', { direction: 'forward' })}
                  >
                    Getting started
                  </button>
                  <div className="shadcn-context-menu-separator" />
                  {authState.authenticated ? (
                    <button
                      className="shadcn-context-menu-item"
                      type="button"
                      onClick={() => {
                        setUserMenuOpen(false);
                        handleSignOut();
                      }}
                    >
                      Sign out
                    </button>
                  ) : (
                    <>
                      <button
                        className="shadcn-context-menu-item"
                        type="button"
                        onClick={() =>
                          navigateToPage('auth', { authMode: 'signin', direction: 'forward' })
                        }
                      >
                        Sign in
                      </button>
                      <button
                        className="shadcn-context-menu-item"
                        type="button"
                        onClick={() =>
                          navigateToPage('auth', { authMode: 'signup', direction: 'forward' })
                        }
                      >
                        Create account
                      </button>
                    </>
                  )}
                </div>
              )}
            </div>
          </div>
        </header>

        {activePage === 'settings' ? (
          <SettingsView
            theme={theme}
            onThemeChange={handleThemeChange}
            models={models}
            hideBuiltinModels={hideBuiltinModels}
            onHideBuiltinModelsChange={handleHideBuiltinModelsChange}
            onModelsChanged={reloadModels}
            layoutPreset={layoutPreset}
            onLayoutPresetChange={handleLayoutPresetChange}
            authState={authState}
            onOpenSignIn={() => navigateToPage('auth', { authMode: 'signin', direction: 'forward' })}
            onSignOut={handleSignOut}
            onHistoryCleared={() => {
              setChatSessions([]);
              setRecentTasks([]);
              const id = `chat-${Date.now()}`;
              setTabs([{ id, title: 'New chat', sessionId: `session-${Date.now()}`, modelId: activeTab.modelId || 'auto' }]);
              setActiveTabId(id);
              setMessagesByTab({ [id]: [] });
            }}
            onSyncCompleted={() => {
              loadChatSessions();
              loadRecentTasks();
            }}
            onClose={closeSettings}
          />
        ) : (
        <main className="page-frame">
          <div className={`workspace-panes ${sidebarOpen ? 'left-open' : 'left-closed'}`}>
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
            {sidebarOpen && (
              <div
                role="separator"
                aria-orientation="vertical"
                aria-label="Resize sidebar"
                title="Drag to resize sidebar (double-click to expand/reset)"
                onMouseDown={startSidebarResize}
                onDoubleClick={toggleSidebarExpanded}
                style={{ touchAction: 'none' }}
                className={`relative z-30 -ml-1 h-full w-2 shrink-0 cursor-col-resize transition-colors ${
                  isResizingSidebar
                    ? 'bg-[var(--border-focus)]/50'
                    : 'bg-transparent hover:bg-[var(--border-focus)]/35'
                }`}
              />
            )}

            <div className="flex min-w-0 flex-1 overflow-hidden">
              {appMode === 'code' && (
                <WorkspaceDock
                  activeSurface={activeDockSurface}
                  onToggleSurface={(surface) => {
                    if (surface === 'plan') {
                      setActiveDockSurface((prev) => (prev === 'plan' ? null : 'plan'));
                    }
                  }}
                  hasPlan={Boolean(planBySession[activeTab.sessionId])}
                  processCount={managedProcesses.length}
                  devices={devices}
                  activeDeviceId={activeDeviceId}
                  onSelectDevice={setActiveDeviceId}
                />
              )}
            <main className="pane pane-center min-w-0 flex-1">
              <header className="chat-header" style={{ position: 'relative' }}>
                <div className="chat-header-left">
                  {!sidebarOpen && (
                    <button
                      className="btn-pane-toggle"
                      type="button"
                      title="Show chats"
                      onClick={() => setSidebarOpen(true)}
                    >
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" /><path d="M9 3v18" /></svg>
                    </button>
                  )}
                </div>
                {appMode === 'code' && activeMessages.length > 0 && activeProjectId !== 'local' && (
                  <div
                    className="pointer-events-none absolute left-1/2 -translate-x-1/2 flex items-center gap-1.5 text-xs font-medium text-[var(--text-secondary)]"
                    title={activeProjectLabel}
                  >
                    <FolderIcon size={13} />
                    <span className="max-w-[240px] truncate">{activeProjectLabel}</span>
                  </div>
                )}
                {appMode === 'code' && managedProcesses.length > 0 && (
                  <div className="relative flex items-center gap-2">
                    <button
                      type="button"
                      className="btn-pane-toggle flex items-center gap-1.5 px-2.5 text-xs"
                      onClick={() => {
                        setProcessesOpen((v) => !v);
                        void loadManagedProcesses();
                      }}
                      title="Managed background processes"
                    >
                      <span
                        className={`inline-block h-2 w-2 rounded-full ${
                          managedProcesses.some((p) => p.status === 'running')
                            ? 'bg-emerald-400'
                            : 'bg-[var(--text-muted)]'
                        }`}
                      />
                      <span>
                        Processes ({managedProcesses.filter((p) => p.status === 'running').length}/
                        {managedProcesses.length})
                      </span>
                    </button>
                    {processesOpen && (
                      <div className="absolute right-0 top-full z-50 mt-2 w-[380px] rounded-xl border border-[var(--border)] bg-[var(--bg-elevated)] p-3 shadow-xl">
                        <div className="mb-2 flex items-center justify-between text-xs font-medium text-[var(--text-secondary)]">
                          <span>Managed Processes</span>
                          <button
                            type="button"
                            className="text-[11px] hover:text-[var(--text-primary)]"
                            onClick={() => setProcessesOpen(false)}
                          >
                            Close
                          </button>
                        </div>
                        <div className="flex max-h-56 flex-col gap-2 overflow-y-auto">
                          {managedProcesses.map((proc) => (
                            <div
                              key={proc.id}
                              className="rounded-lg border border-[var(--border)] bg-[var(--bg-surface)] p-2 text-xs"
                            >
                              <div className="flex items-center justify-between gap-2">
                                <span className="truncate font-medium text-[var(--text-primary)]">
                                  {proc.label}
                                </span>
                                <span
                                  className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium ${
                                    proc.status === 'running'
                                      ? 'bg-emerald-500/15 text-emerald-400'
                                      : 'bg-[var(--bg-hover)] text-[var(--text-muted)]'
                                  }`}
                                >
                                  {proc.status}
                                </span>
                              </div>
                              <div className="mt-1 truncate font-mono text-[11px] text-[var(--text-muted)]">
                                {proc.command} (pid {proc.pid})
                              </div>
                              <div className="mt-2 flex items-center gap-2">
                                <button
                                  type="button"
                                  className="rounded border border-[var(--border)] px-2 py-0.5 text-[11px] text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
                                  onClick={() => void handleViewProcessLogs(proc.id)}
                                >
                                  Logs
                                </button>
                                {proc.status === 'running' && (
                                  <button
                                    type="button"
                                    className="rounded border border-red-500/30 bg-red-500/10 px-2 py-0.5 text-[11px] text-red-400 hover:bg-red-500/20"
                                    onClick={() => void handleStopProcess(proc.id)}
                                  >
                                    Stop
                                  </button>
                                )}
                              </div>
                            </div>
                          ))}
                        </div>
                        {selectedProcessLogs && (
                          <pre className="mt-2 max-h-40 overflow-auto rounded-lg border border-[var(--border)] bg-black/40 p-2 font-mono text-[11px] text-[var(--text-secondary)] whitespace-pre-wrap">
                            {selectedProcessLogs.logs}
                          </pre>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </header>

              <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                {activeMessages.length === 0 ? (
                  isPlanMode ? (
                    <div className="mx-auto flex w-full max-w-[900px] flex-1 flex-col justify-start gap-6 px-6 pt-10 pb-8 overflow-y-auto">
                      <div className="text-center">
                        <h1 className="text-2xl font-semibold tracking-tight text-[var(--text-primary)]">
                          Plan mode
                        </h1>
                        <p className="mt-1 text-xs text-[var(--text-muted)]">
                          Read-only codebase inspection, EARS requirements, architecture design, and task breakdown.
                        </p>
                      </div>
                      {planBySession[activeTab.sessionId] ? (
                        <PlanArtifactCard
                          markdown={planBySession[activeTab.sessionId] || ''}
                          onChange={(nextMd) =>
                            setPlanBySession((prev) => ({ ...prev, [activeTab.sessionId]: nextMd }))
                          }
                          onSave={(md) => saveSessionPlan(activeTab.sessionId, md)}
                          onApproveAndBuild={handleApprovePlanAndBuild}
                        />
                      ) : (
                        <div className="mx-auto flex w-full max-w-[860px] flex-col gap-5 py-2">
                          <div className="text-center text-sm font-medium text-[var(--text-secondary)]">
                            Explore an idea for {activeProjectId !== 'local' ? activeProjectLabel : 'this workspace'}
                          </div>
                          <div className="grid gap-3.5 sm:grid-cols-3">
                            {currentPlanIdeas.map((idea) => (
                              <button
                                key={idea}
                                type="button"
                                onClick={() => {}}
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
                              onClick={() =>
                                setPlanBySession((prev) => ({
                                  ...prev,
                                  [activeTab.sessionId]:
                                    '## Requirements\n- WHEN <trigger> THE SYSTEM SHALL <behavior>\n\n### Non-Goals (Deferred)\n- \n\n## Design\n- \n\n## Tasks\n1. [ ] ',
                                }))
                              }
                              className="rounded-lg border border-[var(--border-color)] px-3.5 py-1.5 text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
                            >
                              Create blank spec
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  ) : (
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
                  )
                ) : (
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
                                              <div className="flex w-full flex-col gap-3 text-sm leading-relaxed text-[var(--text-primary)]">
                                                {msg.tasks && msg.tasks.length > 0 && (
                                                  <Trace>
                                                    {msg.tasks.map((taskItem) => (
                                                      <TaskRow key={taskItem.id} task={taskItem} streaming={streaming} />
                                                    ))}
                                                  </Trace>
                                                )}
                                                {msg.steps && msg.steps.length > 0 && (
                                                  <Trace>
                                                    {msg.steps.map((step, idx) => (
                                                      <StepRow key={idx} step={step} streaming={streaming} busy={isExecuting} />
                                                    ))}
                                                  </Trace>
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
                                                 ) : (
                                                   msg.content && (
                                                     <MessageResponse isStreaming={isExecuting && msg.id === activeMessages[activeMessages.length - 1]?.id}>
                                                       {msg.content}
                                                     </MessageResponse>
                                                   )
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
                        {isPlanMode && (
                          <MessageScrollerItem>
                            <div
                              ref={planSectionRef}
                              className="mx-auto flex min-h-[calc(100vh-210px)] w-full max-w-[900px] flex-col justify-start gap-6 pt-10 pb-8"
                            >
                              <div className="text-center">
                                <h2 className="text-2xl font-semibold tracking-tight text-[var(--text-primary)]">
                                  Plan mode
                                </h2>
                                <p className="mt-1 text-xs text-[var(--text-muted)]">
                                  Review, edit, or approve the spec below to switch into Code mode and build.
                                </p>
                              </div>
                              {planBySession[activeTab.sessionId] ? (
                                <PlanArtifactCard
                                  markdown={planBySession[activeTab.sessionId] || ''}
                                  onChange={(nextMd) =>
                                    setPlanBySession((prev) => ({ ...prev, [activeTab.sessionId]: nextMd }))
                                  }
                                  onSave={(md) => saveSessionPlan(activeTab.sessionId, md)}
                                  onApproveAndBuild={handleApprovePlanAndBuild}
                                />
                              ) : (
                                <div className="mx-auto flex w-full max-w-[860px] flex-col gap-5 py-2">
                                  <div className="text-center text-sm font-medium text-[var(--text-secondary)]">
                                    Explore an idea for {activeProjectId !== 'local' ? activeProjectLabel : 'this workspace'}
                                  </div>
                                  <div className="grid gap-3.5 sm:grid-cols-3">
                                    {currentPlanIdeas.map((idea) => (
                                      <button
                                        key={idea}
                                        type="button"
                                        onClick={() => {}}
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
                                      onClick={() =>
                                        setPlanBySession((prev) => ({
                                          ...prev,
                                          [activeTab.sessionId]:
                                            '## Requirements\n- WHEN <trigger> THE SYSTEM SHALL <behavior>\n\n### Non-Goals (Deferred)\n- \n\n## Design\n- \n\n## Tasks\n1. [ ] ',
                                        }))
                                      }
                                      className="rounded-lg border border-[var(--border-color)] px-3.5 py-1.5 text-xs font-medium text-[var(--text-secondary)] hover:bg-[var(--bg-hover)] hover:text-[var(--text-primary)] cursor-pointer"
                                    >
                                      Create blank spec
                                    </button>
                                  </div>
                                </div>
                              )}
                            </div>
                          </MessageScrollerItem>
                        )}
                      </MessageScrollerContent>
                    </MessageScrollerViewport>
                    <MessageScrollerButton />
                  </MessageScroller>
                )}
              </div>

              {systemWarnings.length > 0 && !systemWarningsDismissed && (
                <div className="mx-auto mb-2 w-full max-w-[836px] px-7">
                  <div
                    role="status"
                    className="flex items-start justify-between gap-3 rounded-xl border border-amber-500/35 bg-amber-500/12 px-3.5 py-2 text-xs text-[var(--text-primary)]"
                  >
                    <div className="flex min-w-0 flex-1 flex-col gap-1">
                      <div className="flex items-center gap-1.5 font-semibold text-amber-300 [[data-theme=bone]_&]:text-amber-800">
                        <CircleAlertIcon size={13} className="shrink-0" />
                        <span>
                          {systemWarnings.length === 1
                            ? systemWarnings[0]!.label
                            : `${systemWarnings.length} system warnings`}
                        </span>
                      </div>
                      <ul className="space-y-0.5 text-[11px] text-[var(--text-secondary)]">
                        {systemWarnings.map((w) => (
                          <li key={w.id} className="flex flex-wrap items-center gap-2">
                            <span>{w.message}</span>
                            {w.action === 'signin' && (
                              <button
                                type="button"
                                onClick={() => navigateToPage('auth', { authMode: 'signin', direction: 'forward' })}
                                className="font-medium text-[var(--text-primary)] underline underline-offset-2 hover:opacity-80 cursor-pointer"
                              >
                                Sign in
                              </button>
                            )}
                            {w.action === 'settings' && (
                              <button
                                type="button"
                                onClick={() => openSettings()}
                                className="font-medium text-[var(--text-primary)] underline underline-offset-2 hover:opacity-80 cursor-pointer"
                              >
                                Open Settings
                              </button>
                            )}
                          </li>
                        ))}
                      </ul>
                    </div>
                    <button
                      type="button"
                      onClick={() => setSystemWarningsDismissed(true)}
                      aria-label="Dismiss warnings"
                      className="shrink-0 rounded-md p-0.5 text-[var(--text-muted)] hover:bg-amber-500/20 hover:text-[var(--text-primary)] cursor-pointer"
                    >
                      <XIcon size={13} />
                    </button>
                  </div>
                </div>
              )}

              {actionError && (
                <div className="mx-auto mb-2 w-full max-w-[836px] px-7">
                  <div
                    role="alert"
                    className="flex items-start justify-between gap-3 rounded-xl border border-red-500/35 bg-red-500/12 px-3.5 py-2.5 text-xs text-[var(--text-primary)]"
                  >
                    <div className="flex min-w-0 items-start gap-2">
                      <CircleAlertIcon size={14} className="mt-0.5 shrink-0 text-red-400" />
                      <span className="break-words leading-relaxed">{actionError.message}</span>
                    </div>
                    <button
                      type="button"
                      onClick={() => setActionError(null)}
                      aria-label="Dismiss error"
                      className="shrink-0 rounded-md p-0.5 text-red-300/80 hover:bg-red-500/20 hover:text-red-100 cursor-pointer"
                    >
                      <XIcon size={13} />
                    </button>
                  </div>
                </div>
              )}

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
                    placeholder={voiceRecording ? 'Listening... speak naturally' : composerPlaceholder}
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
                          aria-label={`Effort: ${effort}`}
                          onClick={() => {
                            setEffortMenuOpen((v) => !v);
                            setComposerModelOpen(false);
                            setContextMenuOpen(false);
                          }}
                        >
                          <span className="text-[var(--text-muted)]">Effort</span>
                          <span className="capitalize">{effort}</span>
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
                              width: '130px',
                              background: 'var(--bg-panel)',
                              border: '1px solid var(--border-color)',
                              borderRadius: 'var(--radius-md)',
                              padding: '4px',
                              zIndex: 9999,
                              flexDirection: 'column',
                              gap: '2px',
                            }}
                          >
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
                            variant="default"
                            size="icon-sm"
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
              <p className="mx-auto -mt-2 mb-2 w-full max-w-[836px] px-7 text-center text-[11px] text-[var(--text-muted)]">
                Models can make mistakes. Check important info.
              </p>
            </main>
            </div>
          </div>
        </main>
        )}

        <ShortcutsDialog open={shortcutsOpen} onOpenChange={setShortcutsOpen} />

        <ProjectDialog
          open={Boolean(projectHubId)}
          onOpenChange={(open) => {
            if (!open) setProjectHubId(null);
          }}
          activeProj={
            projectHubId && projectHubId !== '__new__'
              ? projects.find((p) => p.id === projectHubId)
              : undefined
          }
          projectDraft={projectDraft}
          setProjectDraft={setProjectDraft}
          projectHubTab={projectHubTab}
          setProjectHubTab={setProjectHubTab}
          projectGitInfo={projectGitInfo}
          pickingFolder={pickingFolder}
          setPickingFolder={setPickingFolder}
          confirmDeleteProject={confirmDeleteProject}
          setConfirmDeleteProject={setConfirmDeleteProject}
          onSave={saveProjectHub}
          onLoadGitInfo={(id) => void loadProjectGitInfo(id)}
          onArchive={(id, archived) => void handleArchiveProject(id, archived)}
          onDelete={(id) => void handleDeleteProject(id)}
        />

        <Dialog
          open={Boolean(previewModalUrl)}
          onOpenChange={(open) => {
            if (!open) setPreviewModalUrl(null);
          }}
        >
          <DialogContent className="sm:max-w-sm">
            <DialogHeader>
              <DialogTitle>Open preview</DialogTitle>
              <DialogDescription>
                Choose how to open this preview.
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <DialogClose render={<Button type="button" variant="outline">Cancel</Button>} />
              <Button
                type="button"
                onClick={() => {
                  if (previewModalUrl) {
                    openInDefaultBrowser(previewModalUrl);
                  }
                  setPreviewModalUrl(null);
                }}
              >
                Open in browser
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
        {openChatMenu && (() => {
          const targetChat = chatSessions.find((item) => item.sessionId === openChatMenu.runId);
          if (!targetChat) return null;
          const displayTitle =
            chatMeta.titles[targetChat.sessionId] || targetChat.title || targetChat.sessionId;
          return (
            <ChatContextMenu
              menu={openChatMenu}
              chat={targetChat}
              displayTitle={displayTitle}
              isPinned={Boolean(chatMeta.pinned[targetChat.sessionId])}
              isUnread={Boolean(chatMeta.unread[targetChat.sessionId])}
              onClose={() => setOpenChatMenu(null)}
              onRename={(sessionId, currentTitle) => {
                setRenamingRunId(sessionId);
                setRenameDraft(currentTitle);
              }}
              onTogglePin={handleTogglePin}
              onToggleUnread={handleToggleUnread}
              onSplit={handleSplitChat}
              onArchive={(sessionId) => void handleArchiveTask(sessionId)}
            />
          );
        })()}
        <SystemToastViewport />
      </div>
    </MessageScrollerProvider>
  );
}
