import React, {
  lazy,
  useEffect,
  useRef,
  useState,
} from 'react';
import {
  CircleAlertIcon,
  XIcon,
} from 'lucide-react';

import {
  AppErrorScreen,
} from './components/ui/ErrorBoundary';
import type {
  LayoutPreset,
} from './features/settings/SettingsView';
import type {
  DockSurface,
} from './features/workspace/WorkspaceDock';
import {
  MissionControlDrawer,
  splitAttentionActivities,
} from './features/workspace/MissionControlDrawer';
import {
  AuthPage,
  GramMark,
} from './features/auth/AuthPage';

const ProjectDialog = lazy(() =>
  import('./features/workspace/ProjectDialog').then((m) => ({ default: m.ProjectDialog }))
);
const SettingsView = lazy(() =>
  import('./features/settings/SettingsView').then((m) => ({ default: m.SettingsView }))
);
const WorkspaceDock = lazy(() =>
  import('./features/workspace/WorkspaceDock').then((m) => ({ default: m.WorkspaceDock }))
);
const MissionControlPip = lazy(() =>
  import('./features/workspace/MissionControlPip').then((m) => ({ default: m.MissionControlPip }))
);
const QuickCaptureBar = lazy(() =>
  import('./features/workspace/QuickCaptureBar').then((m) => ({ default: m.QuickCaptureBar }))
);
const OnboardingWizard = lazy(() =>
  import('./features/auth/OnboardingWizard').then((m) => ({ default: m.OnboardingWizard }))
);
const ShortcutsDialog = lazy(() =>
  import('./features/chat/ShortcutsDialog').then((m) => ({ default: m.ShortcutsDialog }))
);


import {
  Button,
} from './components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from './components/ui/dialog';



import {
  DynamicIslandBar,
  type IslandActivity,
} from './components/ui/dynamic-island';

import {
  SystemToastViewport,
  emitSystemToast,
} from './components/ui/toast';


import {
  MessageScrollerProvider,
} from './components/ui/message-scroller';


import {
  Trace,
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
  TraceStep,
} from './lib/types';
import {
  checkpointInspectionSteps,
} from './features/chat/TraceRows';
import {
  isCodingWorkPrompt,
} from './features/chat/chatHelpers';
import {
  ContextPanel,
} from './features/shell/ContextPanel';
import {
  Composer,
} from './features/shell/Composer';
import {
  MessageList,
} from './features/shell/MessageList';
import {
  ChatHeader,
} from './features/shell/ChatHeader';
import {
  Sidebar,
} from './features/shell/Sidebar';
import {
  ChatContextMenu,
  type ChatCopyMode,
} from './features/chat/ChatContextMenu';

import {
  useLayoutState,
} from './hooks/useLayoutState';

import {
  useVoice,
} from './hooks/useVoice';

import {
  useProjectHub,
} from './hooks/useProjectHub';

import {
  useSystemChecks,
} from './hooks/useSystemChecks';

function createChatMessageId(prefix: string): string {
  const unique = globalThis.crypto?.randomUUID?.() || Math.random().toString(36).slice(2, 12);
  return `${prefix}-${Date.now()}-${unique}`;
}

export function App() {
  const { hideBuiltinModels, setHideBuiltinModels, setLayoutPreset, setSidebarOpen, layoutPreset, sidebarOpen, isResizingSidebar, sidebarWidth, toggleSidebarExpanded, startSidebarResize } = useLayoutState();
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
  const [archivedChats, setArchivedChats] = useState<
    Array<{ sessionId: string; title: string; mode: 'chat' | 'code'; daysRemaining: number }>
  >([]);
  const [showArchivedChats, setShowArchivedChats] = useState(false);
  const [authState, setAuthState] = useState<AuthState>(() => {
    try {
      const raw = localStorage.getItem('gram.cache.authState');
      if (raw) {
        const parsed = JSON.parse(raw) as AuthState;
        if (parsed?.authenticated && parsed?.user) {
          return { ...parsed, connected: false, lyraConnected: false };
        }
      }
    } catch {}
    return { connected: false, authenticated: false, user: null };
  });

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
  const [permissionMode, setPermissionMode] = useState<'autonomous' | 'safe' | 'ask'>(() => {
    try {
      const saved = localStorage.getItem('gram.settings.permissionMode');
      if (saved === 'autonomous' || saved === 'safe' || saved === 'ask') return saved;
    } catch {}
    return 'autonomous';
  });
  const updatePermissionMode = (next: 'autonomous' | 'safe' | 'ask') => {
    setPermissionMode(next);
    try {
      localStorage.setItem('gram.settings.permissionMode', next);
    } catch {}
  };
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
  const [missionControlOpen, setMissionControlOpen] = useState(false);
  const [planBySession, setPlanBySession] = useState<Record<string, string>>({});
  const [planBrainDumpBySession, setPlanBrainDumpBySession] = useState<Record<string, boolean>>({});
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
  const [stagePreviewUrl, setStagePreviewUrl] = useState('http://localhost:5173');
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
  const [pipOpen, setPipOpen] = useState(false);
  const isPipMode =
    typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('pip') === '1';
  const isCaptureMode =
    typeof window !== 'undefined' && new URLSearchParams(window.location.search).get('capture') === '1';

  useEffect(() => {
    if (!isPipMode) return;
    const id = window.setInterval(() => {
      void loadRecentTasks();
    }, 1500);
    return () => window.clearInterval(id);
  }, [isPipMode]);

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

  const { voiceMode, speakSummary, stopSpeechPlayback, speakingMessageId, voiceError, voiceRecording, setVoiceMode, setVoiceError, toggleVoiceRecording } = useVoice({ activeRequestRef, activeRunId, setIsExecuting, prompt, setPrompt, textareaRef, formRef });

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

  const { setBootError, applySystemChecks, setSystemWarnings, actionError, setActionError, systemWarnings, bootError, refreshSystemChecks, setSystemWarningsDismissed, systemWarningsDismissed } = useSystemChecks({ setAuthState });

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

  async function handleCopyChat(chat: ChatSessionSummary, mode: ChatCopyMode) {
    if (mode === 'id') {
      await navigator.clipboard?.writeText(chat.sessionId).catch(() => {});
      emitSystemToast({ title: 'Copied chat ID', description: chat.sessionId, variant: 'success' });
      return;
    }

    const existingTab = tabs.find((t) => t.sessionId === chat.sessionId);
    let msgs: Array<{ role?: string; content?: string }> = existingTab
      ? (messagesByTab[existingTab.id] || [])
      : [];
    if (msgs.length === 0) {
      try {
        const res = await fetch(`/api/chats/${encodeURIComponent(chat.sessionId)}`);
        if (res.ok) {
          const data = (await res.json()) as { messages?: Array<{ role?: string; content?: string }> };
          if (Array.isArray(data?.messages)) msgs = data.messages;
        }
      } catch {}
    }

    const nonEmpty = msgs.filter((m) => typeof m.content === 'string' && m.content.trim());
    if (nonEmpty.length === 0) {
      emitSystemToast({ title: 'Nothing to copy yet', description: 'This chat has no messages.', variant: 'info' });
      return;
    }

    if (mode === 'last') {
      let lastUserIdx = -1;
      for (let i = nonEmpty.length - 1; i >= 0; i--) {
        if (nonEmpty[i]?.role === 'user') {
          lastUserIdx = i;
          break;
        }
      }
      const slice = lastUserIdx >= 0 ? nonEmpty.slice(lastUserIdx) : nonEmpty.slice(-2);
      const formatted = slice
        .map((m) => `${m.role === 'assistant' ? 'Assistant' : 'User'}:\n${m.content!.trim()}`)
        .join('\n\n');
      await navigator.clipboard?.writeText(formatted).catch(() => {});
      emitSystemToast({ title: 'Copied last interaction', variant: 'success' });
      return;
    }

    if (mode === 'full') {
      const formatted = nonEmpty
        .map((m) => `${m.role === 'assistant' ? 'Assistant' : 'User'}:\n${m.content!.trim()}`)
        .join('\n\n---\n\n');
      await navigator.clipboard?.writeText(formatted).catch(() => {});
      emitSystemToast({ title: 'Copied entire chat', variant: 'success' });
      return;
    }

    if (mode === 'summary') {
      emitSystemToast({ title: 'Summarizing chat...', variant: 'info', durationMs: 2200 });
      try {
        const res = await fetch('/api/chat/summarize', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: chat.sessionId, messages: nonEmpty }),
        });
        const data = (await res.json().catch(() => null)) as { summary?: string; error?: string } | null;
        if (!res.ok || !data?.summary) {
          throw new Error(data?.error || `Summarize failed (${res.status}).`);
        }
        await navigator.clipboard?.writeText(data.summary).catch(() => {});
        emitSystemToast({ title: 'Copied chat summary', variant: 'success' });
      } catch (err: any) {
        emitSystemToast({
          title: 'Could not summarize chat',
          description: err?.message || 'Try copying the full chat instead.',
          variant: 'error',
        });
      }
    }
  }

  async function handleSignOut() {
    try {
      localStorage.removeItem('gram.cache.authState');
    } catch {}
    setAuthState({ connected: false, authenticated: false, user: null });
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
      if (data.authenticated && data.user) {
        try {
          localStorage.setItem(
            'gram.cache.authState',
            JSON.stringify({
              authenticated: true,
              user: data.user,
              source: data.source,
              hubUrl: data.hubUrl,
              googleEnabled: data.googleEnabled,
            })
          );
        } catch {}
        setAuthState(data);
        return;
      }
      if (!data.connected && !data.lyraConnected) {
        setAuthState((prev) =>
          prev.authenticated && prev.user
            ? { ...prev, connected: false, lyraConnected: false, googleEnabled: data.googleEnabled, hubUrl: data.hubUrl }
            : data
        );
        return;
      }
      try {
        localStorage.removeItem('gram.cache.authState');
      } catch {}
      setAuthState(data);
    } catch {
      setAuthState((prev) =>
        prev.authenticated && prev.user
          ? { ...prev, connected: false, lyraConnected: false }
          : { connected: false, authenticated: false, user: null }
      );
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
    const firstAssistant = [...activeMessages]
      .reverse()
      .find((m) => m.role === 'assistant' && !m.isError && m.content.trim() && !/^Model execution error\b/i.test(m.content));
    if (!firstAssistant) return;
    if (/##\s+(?:Requirements|Design|Tasks)\b/i.test(firstAssistant.content)) {
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

  const { projectHubId, openProjectHub, showArchivedProjects, expandedProjects, setExpandedProjects, setShowArchivedProjects, setProjectHubId, projectDraft, setProjectDraft, projectHubTab, setProjectHubTab, projectGitInfo, pickingFolder, setPickingFolder, confirmDeleteProject, setConfirmDeleteProject, saveProjectHub, loadProjectGitInfo, handleArchiveProject, handleDeleteProject } = useProjectHub({ setProjects, activeProjectId, setActiveProjectId, setActionError });

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
      let activePermissionMode = permissionMode;
      let personaTone = 'programmer';
      let systemPrompt = '';
      let rules = '';
      try {
        activePermissionMode =
          (localStorage.getItem('gram.settings.permissionMode') as 'autonomous' | 'safe' | 'ask' | null) ||
          permissionMode;
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
            requireApproval: activePermissionMode === 'ask',
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
          const renderText = done ? liveText : '';

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
              setActiveDockSurface((prev) => prev || 'plan');
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

  if (isCaptureMode) {
    return (
      <div className="app-shell h-screen w-screen overflow-hidden" data-theme={theme}>
        <React.Suspense fallback={null}>
          <QuickCaptureBar
            onClose={() => {
              const tauriInvoke = (window as any)?.__TAURI_INTERNALS__?.invoke;
              if (typeof tauriInvoke === 'function') {
                void Promise.resolve(tauriInvoke('toggle_quick_capture', { open: false })).catch(() => {});
              } else {
                window.close();
              }
            }}
          />
        </React.Suspense>
      </div>
    );
  }

  if (isPipMode) {
    return (
      <div className="app-shell h-screen w-screen overflow-hidden" data-theme={theme}>
        <MissionControlPip
          activities={islandActivities}
          onApproveActivity={handleApproveActivity}
          onStopActivity={(runId) => {
            fetch('/api/cancel', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ runId }),
            })
              .then(() => void loadRecentTasks())
              .catch(() => {});
          }}
          onExpandToMain={() => {
            const tauriInvoke = (window as any)?.__TAURI_INTERNALS__?.invoke;
            if (typeof tauriInvoke === 'function') {
              void Promise.resolve(tauriInvoke('toggle_mission_pip', { open: false })).catch(() => {});
            } else {
              window.close();
            }
          }}
          onClose={() => {
            const tauriInvoke = (window as any)?.__TAURI_INTERNALS__?.invoke;
            if (typeof tauriInvoke === 'function') {
              void Promise.resolve(tauriInvoke('toggle_mission_pip', { open: false })).catch(() => {});
            } else {
              window.close();
            }
          }}
        />
      </div>
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
              onOpenMissionControl={() => setMissionControlOpen(true)}
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
                if (runId === activeRunId) {
                  activeRequestRef.current?.abort();
                  activeRequestRef.current = null;
                  setIsExecuting(false);
                }
                setRecentTasks((prev) =>
                  prev.map((t) =>
                    t.runId === runId ? { ...t, stage: 'failed', isActive: false, pendingApproval: undefined } : t
                  )
                );
                fetch('/api/cancel', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ runId }),
                })
                  .then(() => {
                    void loadRecentTasks();
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
            <Sidebar
              sidebarOpen={sidebarOpen}
              isResizingSidebar={isResizingSidebar}
              sidebarWidth={sidebarWidth}
              openNewChatTab={openNewChatTab}
              appMode={appMode}
              toggleSidebarExpanded={toggleSidebarExpanded}
              setSidebarOpen={setSidebarOpen}
              chatSessions={chatSessions}
              chatMeta={chatMeta}
              activeTab={activeTab}
              openChatMenu={openChatMenu}
              renamingRunId={renamingRunId}
              renameDraft={renameDraft}
              setRenameDraft={setRenameDraft}
              commitRename={commitRename}
              setRenamingRunId={setRenamingRunId}
              updateChatMeta={updateChatMeta}
              handleSelectChat={handleSelectChat}
              setOpenChatMenu={setOpenChatMenu}
              openProjectHub={openProjectHub}
              projects={projects}
              showArchivedProjects={showArchivedProjects}
              expandedProjects={expandedProjects}
              activeProjectId={activeProjectId}
              islandActivities={islandActivities}
              setActiveProjectId={setActiveProjectId}
              activeMessages={activeMessages}
              setTabs={setTabs}
              setExpandedProjects={setExpandedProjects}
              setShowArchivedProjects={setShowArchivedProjects}
              archivedChats={archivedChats}
              setShowArchivedChats={setShowArchivedChats}
              showArchivedChats={showArchivedChats}
              handleRestoreChat={handleRestoreChat}
            />
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
                    if (surface === 'agents') {
                      setMissionControlOpen((prev) => !prev);
                      return;
                    }
                    setActiveDockSurface((prev) => (prev === surface ? null : surface));
                  }}
                  hasPlan={Boolean(planBySession[activeTab.sessionId])}
                  attentionCount={splitAttentionActivities(islandActivities).attention.length}
                  missionControlOpen={missionControlOpen}
                  processCount={managedProcesses.length}
                  devices={devices}
                  activeDeviceId={activeDeviceId}
                  onSelectDevice={setActiveDeviceId}
                />
              )}
            <main className="pane pane-center min-w-0 flex-1">
              <ChatHeader
                sidebarOpen={sidebarOpen}
                setSidebarOpen={setSidebarOpen}
                appMode={appMode}
                isPlanMode={isPlanMode}
                activeMessages={activeMessages}
                activeProjectId={activeProjectId}
                activeProjectLabel={activeProjectLabel}
                devices={devices}
                activeDeviceId={activeDeviceId}
                setMissionControlOpen={setMissionControlOpen}
                setActiveDockSurface={setActiveDockSurface}
                managedProcesses={managedProcesses}
                setProcessesOpen={setProcessesOpen}
                loadManagedProcesses={loadManagedProcesses}
                processesOpen={processesOpen}
                handleViewProcessLogs={handleViewProcessLogs}
                handleStopProcess={handleStopProcess}
                selectedProcessLogs={selectedProcessLogs}
              />

              <MessageList
                isPlanMode={isPlanMode}
                activeMessages={activeMessages}
                planBySession={planBySession}
                activeTab={activeTab}
                planBrainDumpBySession={planBrainDumpBySession}
                setPlanBrainDumpBySession={setPlanBrainDumpBySession}
                activeProjectId={activeProjectId}
                activeProjectLabel={activeProjectLabel}
                currentPlanIdeas={currentPlanIdeas}
                setPrompt={setPrompt}
                textareaRef={textareaRef}
                appMode={appMode}
                chatEmptyCopy={chatEmptyCopy}
                isExecuting={isExecuting}
                copiedMessageId={copiedMessageId}
                handleApproveActivity={handleApproveActivity}
                handleRetryMessage={handleRetryMessage}
                setActiveDockSurface={setActiveDockSurface}
                handleApprovePlanAndBuild={handleApprovePlanAndBuild}
                handoffBusyId={handoffBusyId}
                handleContinueInCode={handleContinueInCode}
                handleCopyMessage={handleCopyMessage}
                handleReplyMessage={handleReplyMessage}
                speakingMessageId={speakingMessageId}
                stopSpeechPlayback={stopSpeechPlayback}
                speakSummary={speakSummary}
                handleEscalate={handleEscalate}
                handleOpenPreview={handleOpenPreview}
                handlePullLyraPatch={handlePullLyraPatch}
                handleCommitLyraLayer={handleCommitLyraLayer}
                handleRollbackLyraLayer={handleRollbackLyraLayer}
                planSectionRef={planSectionRef}
                setPlanBySession={setPlanBySession}
                saveSessionPlan={saveSessionPlan}
              />

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

              <Composer
                formRef={formRef}
                onSubmit={onSubmit}
                fileInputRef={fileInputRef}
                setAttachments={setAttachments}
                messageQueue={messageQueue}
                activeTab={activeTab}
                editingQueueId={editingQueueId}
                editingQueueText={editingQueueText}
                setEditingQueueText={setEditingQueueText}
                handleSaveQueuedEdit={handleSaveQueuedEdit}
                setEditingQueueId={setEditingQueueId}
                handleSteerQueuedMessage={handleSteerQueuedMessage}
                handleRemoveQueuedMessage={handleRemoveQueuedMessage}
                replyingTo={replyingTo}
                setReplyingTo={setReplyingTo}
                attachments={attachments}
                voiceError={voiceError}
                textareaRef={textareaRef}
                prompt={prompt}
                setPrompt={setPrompt}
                voiceRecording={voiceRecording}
                isPlanMode={isPlanMode}
                planBrainDumpBySession={planBrainDumpBySession}
                planBySession={planBySession}
                composerPlaceholder={composerPlaceholder}
                setContextMenuOpen={setContextMenuOpen}
                contextMenuOpen={contextMenuOpen}
                appMode={appMode}
                selectedModel={selectedModel}
                setComposerModelOpen={setComposerModelOpen}
                setEffortMenuOpen={setEffortMenuOpen}
                composerModelOpen={composerModelOpen}
                selectModel={selectModel}
                byokMenuModels={byokMenuModels}
                openRouterMenuModels={openRouterMenuModels}
                hideBuiltinModels={hideBuiltinModels}
                builtinMenuModels={builtinMenuModels}
                handleHideBuiltinModelsChange={handleHideBuiltinModelsChange}
                effort={effort}
                permissionMode={permissionMode}
                updatePermissionMode={updatePermissionMode}
                effortMenuOpen={effortMenuOpen}
                setEffort={setEffort}
                contextUsageBySession={contextUsageBySession}
                voiceMode={voiceMode}
                setVoiceMode={setVoiceMode}
                stopSpeechPlayback={stopSpeechPlayback}
                setVoiceError={setVoiceError}
                toggleVoiceRecording={toggleVoiceRecording}
                isExecuting={isExecuting}
                handleStop={handleStop}
              />
              <p className="mx-auto -mt-2 mb-2 w-full max-w-[836px] px-7 text-center text-[11px] text-[var(--text-muted)]">
                Models can make mistakes. Check important info.
              </p>
            </main>
            {appMode === 'code' && activeDockSurface && activeDockSurface !== 'agents' && (
              <ContextPanel
                activeDockSurface={activeDockSurface}
                setActiveDockSurface={setActiveDockSurface}
                activeProjectId={activeProjectId}
                planBySession={planBySession}
                activeTab={activeTab}
                setPlanBySession={setPlanBySession}
                handleApprovePlanAndBuild={handleApprovePlanAndBuild}
                stagePreviewUrl={stagePreviewUrl}
                setStagePreviewUrl={setStagePreviewUrl}
                managedProcesses={managedProcesses}
                setManagedProcesses={setManagedProcesses}
              />
            )}
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
              onFork={(sessionId) => {
                void (async () => {
                  try {
                    const res = await fetch('/api/chat/fork', {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: JSON.stringify({ sessionId }),
                    });
                    if (!res.ok) return;
                    const data = await res.json();
                    if (data?.session) {
                      await loadChatSessions();
                      void handleSelectChat(data.session.sessionId);
                      emitSystemToast({
                        title: 'Chat forked',
                        description: data.session.title,
                        variant: 'success',
                      });
                    }
                  } catch {}
                })();
              }}
              onCopyChat={(chat, mode) => void handleCopyChat(chat, mode)}
              onArchive={(sessionId) => void handleArchiveTask(sessionId)}
            />
          );
        })()}
        <MissionControlDrawer
          open={missionControlOpen}
          onClose={() => setMissionControlOpen(false)}
          activities={islandActivities}
          onSelectActivity={handleSelectTask}
          onApproveActivity={handleApproveActivity}
          onPopOutPip={() => {
            setMissionControlOpen(false);
            const tauriInvoke = (window as any)?.__TAURI_INTERNALS__?.invoke;
            if (typeof tauriInvoke === 'function') {
              void Promise.resolve(tauriInvoke('toggle_mission_pip', { open: true })).catch(() => {
                setPipOpen(true);
              });
            } else {
              setPipOpen(true);
            }
          }}
          onStopActivity={(runId) => {
            fetch('/api/cancel', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ runId }),
            })
              .then(() => {
                if (runId === activeRunId) setIsExecuting(false);
                void loadRecentTasks();
              })
              .catch(() => {});
          }}
        />
        {pipOpen && !isPipMode && (
          <div className="fixed bottom-4 right-4 z-[120]">
            <MissionControlPip
              embedded
              activities={islandActivities}
              onSelectActivity={handleSelectTask}
              onApproveActivity={handleApproveActivity}
              onStopActivity={(runId) => {
                fetch('/api/cancel', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ runId }),
                })
                  .then(() => {
                    if (runId === activeRunId) setIsExecuting(false);
                    void loadRecentTasks();
                  })
                  .catch(() => {});
              }}
              onExpandToMain={() => {
                setPipOpen(false);
                setMissionControlOpen(true);
              }}
              onClose={() => setPipOpen(false)}
            />
          </div>
        )}
        <SystemToastViewport />
      </div>
    </MessageScrollerProvider>
  );
}
