import React, { useEffect, useMemo, useState } from 'react';
import {
  ArrowLeftIcon,
  AlertTriangleIcon,
  CheckIcon,
  DatabaseIcon,
  RefreshCwIcon,
  SearchIcon,
  Trash2Icon,
  UserIcon,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Item,
  ItemActions,
  ItemContent,
  ItemDescription,
  ItemGroup,
  ItemMedia,
  ItemTitle,
} from '@/components/ui/item';
import { Separator } from '@/components/ui/separator';
import { getModelLogoPath } from '../../lib/modelLogos';

type PersonalContextCategory =
  | 'communication' | 'coding' | 'workflow' | 'career' | 'family' | 'finances'
  | 'relationships' | 'health' | 'education' | 'background' | 'goals' | 'other';
type PersonalContextItem = {
  id: string;
  category: PersonalContextCategory;
  value: string;
  source: 'explicit' | 'manual' | 'imported';
  evidence: string | null;
  updatedAt: string;
};
type PersonalContextCandidate = {
  category: PersonalContextCategory;
  key: string;
  value: string;
  evidence: string;
  sessionId: string;
  chatTitle: string;
};
type PersonalContextProfile = {
  schemaVersion: 1;
  enabled: boolean;
  autoCapture: boolean;
  items: PersonalContextItem[];
};
import {
  CATEGORIES,
  NOVEL_TOOLS,
  THEMES,
  readLocalSetting,
  writeLocalSetting,
  type LayoutPreset,
  type SettingsCategory,
  type SettingsViewProps,
} from './settingsConfig';

export type { LayoutPreset };

export function SettingsView({
  theme,
  onThemeChange,
  models,
  hideBuiltinModels = false,
  onHideBuiltinModelsChange,
  onModelsChanged,
  layoutPreset = 'beginner',
  onLayoutPresetChange,
  authState,
  onOpenSignIn,
  onSignOut,
  onHistoryCleared,
  onSyncCompleted,
  onClose,
}: SettingsViewProps) {
  const [category, setCategory] = useState<SettingsCategory>('general');
  const [search, setSearch] = useState('');
  const [byokProvider, setByokProvider] = useState<'openai' | 'anthropic' | 'openrouter' | 'ollama' | 'custom'>('openai');
  const [byokName, setByokName] = useState('');
  const [byokModelId, setByokModelId] = useState('');
  const [byokBaseUrl, setByokBaseUrl] = useState('');
  const [byokApiKey, setByokApiKey] = useState('');
  const [byokSaving, setByokSaving] = useState(false);
  const [byokError, setByokError] = useState('');

  const [personaTone, setPersonaTone] = useState(() => readLocalSetting('personaTone', 'programmer'));
  const [permissionMode, setPermissionMode] = useState(() => readLocalSetting('permissionMode', 'autonomous'));
  const [defaultModel, setDefaultModel] = useState(() => readLocalSetting('defaultModel', 'auto'));
  const [defaultEffort, setDefaultEffort] = useState(() => readLocalSetting('defaultEffort', 'medium'));
  const [systemPrompt, setSystemPrompt] = useState(() => readLocalSetting('systemPrompt', ''));
  const [rules, setRules] = useState(() => readLocalSetting('rules', ''));
  const [disabledSkills, setDisabledSkills] = useState<Record<string, boolean>>(() => {
    try {
      const raw = localStorage.getItem('gram.settings.disabledSkills');
      return raw ? JSON.parse(raw) : {};
    } catch {
      return {};
    }
  });

  const [modelFilter, setModelFilter] = useState('');
  const [skills, setSkills] = useState<Array<{ name: string; displayName: string; description: string; whenToUse: string }>>([]);
  const [workspaceInfo, setWorkspaceInfo] = useState<{ name?: string; path?: string; vcs?: { type?: string; branch?: string } | null }>({});
  const [overlayChanges, setOverlayChanges] = useState<Array<{ path: string; status?: string }>>([]);
  const [devices, setDevices] = useState<Array<{ deviceId: string; deviceName: string; deviceType: string; isExecutionHost?: boolean }>>([]);
  const [healthInfo, setHealthInfo] = useState<{ status?: string; version?: string; workspace?: string }>({});
  const [doctorResult, setDoctorResult] = useState<Record<string, unknown> | null>(null);
  const [personalContext, setPersonalContext] = useState<PersonalContextProfile>({
    schemaVersion: 1,
    enabled: true,
    autoCapture: true,
    items: [],
  });
  const [contextDrafts, setContextDrafts] = useState<Record<string, string>>({});
  const [contextCategory, setContextCategory] = useState<PersonalContextCategory>('other');
  const [newContextValue, setNewContextValue] = useState('');
  const [contextError, setContextError] = useState('');
  const [contextBusy, setContextBusy] = useState<string | null>(null);
  const [clearContextConfirming, setClearContextConfirming] = useState(false);
  const [contextCandidates, setContextCandidates] = useState<PersonalContextCandidate[]>([]);

  const [syncStatus, setSyncStatus] = useState<{ synced?: boolean; latestSeq?: number; loading?: boolean; message?: string }>({});
  const [syncConflicts, setSyncConflicts] = useState<Array<{
    conflictId: string;
    sessionId: string;
    field: string;
    localValue: string;
    remoteValue: string;
    detectedAt: string;
  }>>([]);
  const [clearConfirming, setClearConfirming] = useState(false);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [ttsProvider, setTtsProvider] = useState(() => readLocalSetting('ttsProvider', 'auto'));
  const [ttsVoiceId, setTtsVoiceId] = useState(() =>
    readLocalSetting('ttsVoiceId', readLocalSetting('elevenLabsVoiceId', ''))
  );
  const [voiceCatalog, setVoiceCatalog] = useState<{
    providers: Array<{ id: string; label: string; available: boolean }>;
    voices: Array<{ id: string; name: string; provider: string }>;
  }>({ providers: [], voices: [] });
  const [voiceTestStatus, setVoiceTestStatus] = useState<string>('');

  async function refreshVoiceCatalog(providerOverride = ttsProvider) {
    try {
      const query = providerOverride && providerOverride !== 'auto' ? `?provider=${encodeURIComponent(providerOverride)}` : '';
      const res = await fetch(`/api/voice/voices${query}`);
      if (!res.ok) return;
      const data = await res.json();
      setVoiceCatalog({
        providers: Array.isArray(data.providers) ? data.providers : [],
        voices: Array.isArray(data.voices) ? data.voices : [],
      });
    } catch {}
  }

  async function handleTestVoice() {
    setVoiceTestStatus('Playing...');
    try {
      const res = await fetch('/api/voice/speak', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          text: 'Hello! This is Gram testing your selected voice.',
          provider: ttsProvider,
          voiceId: ttsVoiceId || undefined,
        }),
      });
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        setVoiceTestStatus(err?.error || `Failed (${res.status})`);
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      audio.onended = () => {
        URL.revokeObjectURL(url);
        setVoiceTestStatus('');
      };
      audio.onerror = () => {
        URL.revokeObjectURL(url);
        setVoiceTestStatus('Playback failed');
      };
      await audio.play();
    } catch (error: any) {
      setVoiceTestStatus(error?.message || 'Test failed');
    }
  }

  async function refreshLiveData() {
    fetch('/api/skills')
      .then((r) => r.json())
      .then((d) => setSkills(Array.isArray(d.skills) ? d.skills : []))
      .catch(() => {});
    fetch('/api/workspace')
      .then((r) => r.json())
      .then((d) => setWorkspaceInfo(d || {}))
      .catch(() => {});
    fetch('/api/files/changes')
      .then((r) => r.json())
      .then((d) => setOverlayChanges(Array.isArray(d) ? d : []))
      .catch(() => {});
    fetch('/api/connect')
      .then((r) => r.json())
      .then((d) => setDevices(Array.isArray(d.devices) ? d.devices : []))
      .catch(() => {});
    fetch('/api/health')
      .then((r) => r.json())
      .then((d) => setHealthInfo(d || {}))
      .catch(() => {});
    fetch('/api/sync/conflicts')
      .then((r) => r.json())
      .then((d) => setSyncConflicts(Array.isArray(d) ? d : []))
      .catch(() => {});
    void refreshVoiceCatalog();
  }

  async function refreshPersonalContext() {
    try {
      const response = await fetch('/api/user-context');
      if (!response.ok) throw new Error(`Could not load personal context (${response.status}).`);
      const profile = await response.json();
      if (profile && Array.isArray(profile.items)) setPersonalContext(profile);
    } catch (error: any) {
      setContextError(error?.message || 'Could not load personal context.');
    }
  }

  useEffect(() => {
    refreshLiveData();
    refreshPersonalContext();
  }, []);

  async function updatePersonalContextSettings(patch: { enabled?: boolean; autoCapture?: boolean }) {
    setContextBusy('settings');
    setContextError('');
    try {
      const response = await fetch('/api/user-context/settings', {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(patch),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not update personal context (${response.status}).`);
      setPersonalContext(data);
    } catch (error: any) {
      setContextError(error?.message || 'Could not update personal context.');
    } finally {
      setContextBusy(null);
    }
  }

  async function addPersonalContextItem() {
    if (!newContextValue.trim()) return;
    setContextBusy('add');
    setContextError('');
    try {
      const response = await fetch('/api/user-context/items', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ category: contextCategory, value: newContextValue }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not add personal context (${response.status}).`);
      setPersonalContext(data.profile);
      setNewContextValue('');
    } catch (error: any) {
      setContextError(error?.message || 'Could not add personal context.');
    } finally {
      setContextBusy(null);
    }
  }

  async function savePersonalContextItem(item: PersonalContextItem) {
    const value = contextDrafts[item.id] ?? item.value;
    if (!value.trim() || value === item.value) return;
    setContextBusy(item.id);
    setContextError('');
    try {
      const response = await fetch(`/api/user-context/items/${encodeURIComponent(item.id)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ value }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not update personal context (${response.status}).`);
      setPersonalContext(data.profile);
      setContextDrafts((prev) => {
        const next = { ...prev };
        delete next[item.id];
        return next;
      });
    } catch (error: any) {
      setContextError(error?.message || 'Could not update personal context.');
    } finally {
      setContextBusy(null);
    }
  }

  async function deletePersonalContextItem(id: string) {
    setContextBusy(id);
    setContextError('');
    try {
      const response = await fetch(`/api/user-context/items/${encodeURIComponent(id)}`, { method: 'DELETE' });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not delete personal context (${response.status}).`);
      setPersonalContext((prev) => ({ ...prev, items: prev.items.filter((item) => item.id !== id) }));
      setContextDrafts((prev) => {
        const next = { ...prev };
        delete next[id];
        return next;
      });
    } catch (error: any) {
      setContextError(error?.message || 'Could not delete personal context.');
    } finally {
      setContextBusy(null);
    }
  }

  async function clearPersonalContext() {
    setContextBusy('clear');
    setContextError('');
    try {
      const response = await fetch('/api/user-context', { method: 'DELETE' });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not clear personal context (${response.status}).`);
      setPersonalContext(data.profile);
      setContextDrafts({});
      setContextCandidates([]);
      setClearContextConfirming(false);
    } catch (error: any) {
      setContextError(error?.message || 'Could not clear personal context.');
    } finally {
      setContextBusy(null);
    }
  }

  async function scanPastChatsForContext() {
    setContextBusy('scan');
    setContextError('');
    try {
      const response = await fetch('/api/user-context/scan-history', { method: 'POST' });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not scan saved chats (${response.status}).`);
      setContextCandidates(Array.isArray(data?.candidates) ? data.candidates : []);
    } catch (error: any) {
      setContextError(error?.message || 'Could not scan saved chats.');
    } finally {
      setContextBusy(null);
    }
  }

  async function acceptPastChatContext(candidate: PersonalContextCandidate) {
    setContextBusy(candidate.key + candidate.sessionId);
    setContextError('');
    try {
      const response = await fetch('/api/user-context/import-candidate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(candidate),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not save memory (${response.status}).`);
      setPersonalContext(data.profile);
      setContextCandidates((prev) => prev.filter((item) => item !== candidate));
    } catch (error: any) {
      setContextError(error?.message || 'Could not save memory candidate.');
    } finally {
      setContextBusy(null);
    }
  }

  function exportPersonalContext() {
    const blob = new Blob([JSON.stringify({ ...personalContext, exportedAt: new Date().toISOString() }, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'gram-personal-context.json';
    link.click();
    URL.revokeObjectURL(url);
  }

  async function handleSyncNow() {
    setSyncStatus((prev) => ({ ...prev, loading: true, message: 'Syncing...' }));
    try {
      const res = await fetch('/api/sync', { method: 'POST' });
      const data = await res.json();
      setSyncStatus({
        synced: Boolean(data.synced),
        latestSeq: data.latestSeq,
        loading: false,
        message: data.synced ? `Synced (seq ${data.latestSeq ?? 0})` : 'Lyra Hub offline — saved locally in SQLite',
      });
      onSyncCompleted();
      const conflictsRes = await fetch('/api/sync/conflicts');
      const conflictsData = await conflictsRes.json().catch(() => []);
      setSyncConflicts(Array.isArray(conflictsData) ? conflictsData : []);
    } catch {
      setSyncStatus({ synced: false, loading: false, message: 'Sync failed' });
    }
  }

  async function handleResolveSyncConflict(conflictId: string, choice: 'local' | 'remote') {
    setBusyAction(`sync-conflict:${conflictId}`);
    try {
      const response = await fetch('/api/sync/conflicts/resolve', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ conflictId, choice }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error || `Could not resolve sync conflict (${response.status}).`);
      setSyncConflicts(Array.isArray(data.conflicts) ? data.conflicts : []);
      onSyncCompleted();
    } catch (error: any) {
      setSyncStatus((prev) => ({ ...prev, message: error?.message || 'Could not resolve sync conflict.' }));
    } finally {
      setBusyAction(null);
    }
  }

  async function handleClearAllHistory() {
    if (!clearConfirming) {
      setClearConfirming(true);
      return;
    }
    setBusyAction('clear');
    try {
      await fetch('/api/history/clear', { method: 'POST' });
      setClearConfirming(false);
      onHistoryCleared();
      setSyncStatus((prev) => ({ ...prev, message: 'All local chats and tasks cleared.' }));
    } finally {
      setBusyAction(null);
    }
  }

  async function handleCommitOverlay() {
    setBusyAction('commit');
    try {
      await fetch('/api/files/commit', { method: 'POST' });
      await refreshLiveData();
    } finally {
      setBusyAction(null);
    }
  }

  async function handleResetOverlay() {
    setBusyAction('reset');
    try {
      await fetch('/api/files/reset', { method: 'POST' });
      await refreshLiveData();
    } finally {
      setBusyAction(null);
    }
  }

  async function handleRunDoctor() {
    setBusyAction('doctor');
    try {
      const res = await fetch('/api/doctor');
      const data = await res.json();
      setDoctorResult(data);
    } finally {
      setBusyAction(null);
    }
  }

  function toggleSkill(name: string) {
    setDisabledSkills((prev) => {
      const next = { ...prev, [name]: !prev[name] };
      writeLocalSetting('disabledSkills', JSON.stringify(next));
      return next;
    });
  }

  const filteredCategories = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return CATEGORIES;
    return CATEGORIES.filter(
      (c) => c.label.toLowerCase().includes(q) || c.description.toLowerCase().includes(q)
    );
  }, [search]);

  const activeCategoryMeta = CATEGORIES.find((c) => c.id === category) || CATEGORIES[0]!;

  const filteredModels = useMemo(() => {
    const q = modelFilter.trim().toLowerCase();
    if (!q) return models;
    return models.filter(
      (m) =>
        m.name.toLowerCase().includes(q) ||
        m.id.toLowerCase().includes(q) ||
        m.provider.toLowerCase().includes(q)
    );
  }, [models, modelFilter]);

  return (
    <div className="flex flex-1 min-h-0 overflow-hidden bg-[var(--bg-app)] text-[var(--text-primary)]">
      <aside className="flex w-64 shrink-0 flex-col border-r border-[var(--border-color)] bg-[var(--bg-panel)]">
        <div className="flex flex-col gap-3 p-3">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="justify-start gap-2 text-[var(--text-secondary)] hover:text-[var(--text-primary)]"
            onClick={onClose}
          >
            <ArrowLeftIcon size={14} />
            <span>Back to Chat</span>
          </Button>
          <div className="relative flex items-center">
            <SearchIcon size={13} className="pointer-events-none absolute left-2.5 text-[var(--text-muted)]" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search settings..."
              aria-label="Search settings"
              className="w-full rounded-md border border-[var(--border-color)] bg-[var(--bg-app)] py-1.5 pl-8 pr-2.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:border-[var(--accent)]"
            />
          </div>
        </div>
        <Separator />
        <nav className="flex flex-1 flex-col gap-1 overflow-y-auto p-2">
          {filteredCategories.map((cat) => {
            const Icon = cat.icon;
            const active = category === cat.id;
            return (
              <button
                key={cat.id}
                type="button"
                onClick={() => setCategory(cat.id)}
                className={`flex items-center gap-2.5 rounded-md px-2.5 py-2 text-left text-[13px] transition-colors ${
                  active
                    ? 'bg-[var(--bg-panel-subtle)] font-medium text-[var(--text-primary)]'
                    : 'text-[var(--text-secondary)] hover:bg-[var(--bg-panel-subtle)] hover:text-[var(--text-primary)]'
                }`}
              >
                <Icon size={14} />
                <span className="truncate">{cat.label}</span>
              </button>
            );
          })}
        </nav>
      </aside>

      <main className="flex-1 overflow-y-auto px-8 py-6">
        <div className="mx-auto flex w-full max-w-2xl flex-col gap-5">
          <header className="flex flex-col gap-1">
            <h1 className="text-lg font-semibold text-[var(--text-primary)]">{activeCategoryMeta.label}</h1>
            <p className="text-xs text-[var(--text-muted)]">{activeCategoryMeta.description}</p>
          </header>
          <Separator />

          {category === 'general' && (
            <ItemGroup className="divide-y divide-[var(--border-color)]">
              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemContent>
                  <ItemTitle>Persona &amp; tone</ItemTitle>
                  <ItemDescription>Default behavioral style and communication format for agent replies.</ItemDescription>
                </ItemContent>
                <ItemActions>
                  <select
                    aria-label="Persona and tone"
                    value={personaTone}
                    onChange={(e) => {
                      setPersonaTone(e.target.value);
                      writeLocalSetting('personaTone', e.target.value);
                    }}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                  >
                    <option value="concise">Concise &amp; Pragmatic</option>
                    <option value="programmer">Technical Pair Programmer</option>
                    <option value="minimalist">Strict Minimalist</option>
                    <option value="creative">Creative &amp; Analytical</option>
                  </select>
                </ItemActions>
              </Item>

              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemContent>
                  <ItemTitle>Permissions</ItemTitle>
                  <ItemDescription>
                    Choose when Gram pauses to show an inline confirmation prompt before executing runs.
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  <select
                    aria-label="Permission mode"
                    value={permissionMode}
                    onChange={(e) => {
                      setPermissionMode(e.target.value);
                      writeLocalSetting('permissionMode', e.target.value);
                    }}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                  >
                    <option value="autonomous">Autonomous</option>
                    <option value="safe">Safe (pause risky commands)</option>
                    <option value="ask">Ask before running</option>
                  </select>
                </ItemActions>
              </Item>

              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemMedia className="text-[var(--text-muted)]">
                  <DatabaseIcon size={15} />
                </ItemMedia>
                <ItemContent>
                  <ItemTitle>Local storage &amp; cross-device sync</ItemTitle>
                  <ItemDescription>
                    {syncStatus.message || 'Stored locally in .gram/checkpoints.db (SQLite WAL mode).'}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={syncStatus.loading}
                    onClick={handleSyncNow}
                  >
                    <RefreshCwIcon size={12} className={syncStatus.loading ? 'animate-spin' : ''} />
                    <span>Sync Now</span>
                  </Button>
                </ItemActions>
              </Item>

              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemContent>
                  <ItemTitle>Voice &amp; text-to-speech</ItemTitle>
                  <ItemDescription>
                    Choose between macOS system speech and cloud TTS (ElevenLabs), then preview a voice.
                    {voiceTestStatus ? ` (${voiceTestStatus})` : ''}
                  </ItemDescription>
                </ItemContent>
                <ItemActions className="flex flex-wrap items-center gap-2">
                  <select
                    aria-label="TTS provider"
                    value={ttsProvider}
                    onChange={(e) => {
                      const next = e.target.value;
                      setTtsProvider(next);
                      writeLocalSetting('ttsProvider', next);
                      setTtsVoiceId('');
                      writeLocalSetting('ttsVoiceId', '');
                      void refreshVoiceCatalog(next);
                    }}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                  >
                    <option value="auto">Auto</option>
                    {voiceCatalog.providers.map((p) => (
                      <option key={p.id} value={p.id} disabled={!p.available}>
                        {p.label}{p.available ? '' : ' (unavailable)'}
                      </option>
                    ))}
                  </select>
                  <select
                    aria-label="TTS voice"
                    value={ttsVoiceId}
                    onChange={(e) => {
                      const next = e.target.value;
                      setTtsVoiceId(next);
                      writeLocalSetting('ttsVoiceId', next);
                    }}
                    className="max-w-[180px] rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                  >
                    <option value="">Default voice</option>
                    {voiceCatalog.voices.map((v) => (
                      <option key={`${v.provider}:${v.id}`} value={v.id}>
                        {v.name}
                      </option>
                    ))}
                  </select>
                  <Button type="button" size="sm" variant="outline" onClick={() => void handleTestVoice()}>
                    Test voice
                  </Button>
                </ItemActions>
              </Item>

              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemMedia className="text-[var(--text-muted)]">
                  <Trash2Icon size={15} />
                </ItemMedia>
                <ItemContent>
                  <ItemTitle>Clear all chats &amp; tasks</ItemTitle>
                  <ItemDescription>
                    Permanently remove all saved chat sessions and task checkpoints from this workspace.
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  {clearConfirming && (
                    <Button type="button" size="sm" variant="ghost" onClick={() => setClearConfirming(false)}>
                      Cancel
                    </Button>
                  )}
                  <Button
                    type="button"
                    size="sm"
                    variant={clearConfirming ? 'destructive' : 'outline'}
                    disabled={busyAction === 'clear'}
                    onClick={handleClearAllHistory}
                  >
                    {clearConfirming ? 'Confirm Delete All' : 'Clear History'}
                  </Button>
                </ItemActions>
              </Item>

              {onLayoutPresetChange && (
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Workspace layout preset</ItemTitle>
                    <ItemDescription>
                      Beginner keeps the left project list open. Mid &amp; Power hide the permanent left sheet in favor of top navigation + ⌘K quick switching.
                    </ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <div className="inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-0.5">
                      {(
                        [
                          { id: 'beginner', label: 'Beginner' },
                          { id: 'mid', label: 'Mid' },
                          { id: 'power', label: 'Power' },
                        ] as const
                      ).map((preset) => (
                        <button
                          key={preset.id}
                          type="button"
                          onClick={() => onLayoutPresetChange(preset.id)}
                          className={`rounded-md px-2.5 py-1 text-xs font-medium transition-colors cursor-pointer ${
                            layoutPreset === preset.id
                              ? 'bg-[var(--bg-hover)] text-[var(--text-primary)]'
                              : 'text-[var(--text-muted)] hover:text-[var(--text-primary)]'
                          }`}
                        >
                          {preset.label}
                        </button>
                      ))}
                    </div>
                  </ItemActions>
                </Item>
              )}
            </ItemGroup>
          )}

          {category === 'personal-context' && (
            <div className="flex flex-col gap-5">
              <div className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-4 text-xs leading-relaxed text-[var(--text-secondary)]">
                Gram can remember durable facts and goals you explicitly share, such as your role, career plans, family context, or preferences. Saved context is stored locally in this Gram installation and may be included in prompts sent to the model you select. Gram requires a quote from your message before it can save a fact.
              </div>

              <ItemGroup className="divide-y divide-[var(--border-color)]">
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Use personal context</ItemTitle>
                    <ItemDescription>Include saved details in new Chat, Code, and Plan conversations.</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <Button
                      type="button"
                      size="sm"
                      variant={personalContext.enabled ? 'default' : 'outline'}
                      disabled={contextBusy === 'settings'}
                      onClick={() => void updatePersonalContextSettings({ enabled: !personalContext.enabled })}
                    >
                      {personalContext.enabled ? 'On' : 'Off'}
                    </Button>
                  </ItemActions>
                </Item>
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Automatically remember explicit details</ItemTitle>
                    <ItemDescription>Only facts and preferences directly stated by you are saved; inferred details are not stored.</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <Button
                      type="button"
                      size="sm"
                      variant={personalContext.autoCapture ? 'default' : 'outline'}
                      disabled={contextBusy === 'settings'}
                      onClick={() => void updatePersonalContextSettings({ autoCapture: !personalContext.autoCapture })}
                    >
                      {personalContext.autoCapture ? 'On' : 'Off'}
                    </Button>
                  </ItemActions>
                </Item>
              </ItemGroup>

              <div className="flex flex-col gap-3">
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <h2 className="text-sm font-semibold text-[var(--text-primary)]">Saved context</h2>
                    <p className="mt-1 text-xs text-[var(--text-muted)]">Review, edit, or remove anything Gram has saved about you.</p>
                  </div>
                  <div className="flex gap-2">
                    <Button type="button" size="sm" variant="outline" disabled={contextBusy === 'scan'} onClick={() => void scanPastChatsForContext()}>
                      {contextBusy === 'scan' ? 'Scanning…' : 'Find memories in recent chats'}
                    </Button>
                    <Button type="button" size="sm" variant="outline" onClick={exportPersonalContext}>
                      Export JSON
                    </Button>
                  </div>
                </div>

                {contextCandidates.length > 0 && (
                  <div className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-3">
                    <div className="mb-2">
                      <h3 className="text-xs font-semibold text-[var(--text-primary)]">Review possible memories</h3>
                      <p className="mt-1 text-[11px] text-[var(--text-muted)]">Found locally in up to 80 recent saved chats, using your messages only. Nothing is saved until you choose Add.</p>
                    </div>
                    <div className="flex flex-col gap-2">
                      {contextCandidates.map((candidate, index) => (
                        <div key={`${candidate.sessionId}-${candidate.key}-${index}`} className="rounded-md border border-[var(--border-color)] bg-[var(--bg-app)] p-3">
                          <div className="mb-1 flex items-center justify-between gap-2">
                            <span className="text-[10px] font-medium uppercase tracking-wide text-[var(--text-muted)]">{candidate.category}</span>
                            <span className="max-w-[65%] truncate text-[10px] text-[var(--text-muted)]">{candidate.chatTitle}</span>
                          </div>
                          <p className="text-xs leading-relaxed text-[var(--text-primary)]">{candidate.value}</p>
                          <div className="mt-2 flex justify-end gap-2">
                            <Button type="button" size="sm" variant="ghost" onClick={() => setContextCandidates((prev) => prev.filter((item) => item !== candidate))}>Ignore</Button>
                            <Button type="button" size="sm" disabled={contextBusy === candidate.key + candidate.sessionId} onClick={() => void acceptPastChatContext(candidate)}>Add</Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {personalContext.items.length === 0 ? (
                  <div className="rounded-lg border border-dashed border-[var(--border-color)] p-5 text-center text-xs text-[var(--text-muted)]">
                    Nothing saved yet. Add an item below or share a durable fact in chat. Gram will offer to remember personal facts when automatic capture is enabled.
                  </div>
                ) : (
                  <div className="flex flex-col gap-2">
                    {personalContext.items.map((item) => (
                      <div key={item.id} className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-3">
                        <div className="mb-2 flex items-center justify-between gap-3">
                          <span className="rounded bg-[var(--bg-panel-subtle)] px-2 py-1 text-[10px] font-medium uppercase tracking-wide text-[var(--text-muted)]">
                            {item.category}
                          </span>
                          <span className="text-[10px] text-[var(--text-muted)]">
                            {item.source === 'imported' ? 'Imported from saved chat' : item.source === 'explicit' ? 'Saved from chat' : 'Added manually'}
                          </span>
                        </div>
                        <textarea
                          value={contextDrafts[item.id] ?? item.value}
                          onChange={(event) => setContextDrafts((prev) => ({ ...prev, [item.id]: event.target.value }))}
                          rows={2}
                          aria-label={`Personal context: ${item.category}`}
                          className="w-full resize-y rounded-md border border-[var(--border-color)] bg-[var(--bg-app)] p-2.5 text-xs leading-relaxed text-[var(--text-primary)] focus:outline-none focus:border-[var(--accent)]"
                        />
                        {item.evidence && (
                          <p className="mt-2 border-l-2 border-[var(--border-color)] pl-2 text-[10px] leading-relaxed text-[var(--text-muted)]">
                            Source: “{item.evidence}”
                          </p>
                        )}
                        <div className="mt-2 flex justify-end gap-2">
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            disabled={contextBusy === item.id}
                            onClick={() => void deletePersonalContextItem(item.id)}
                          >
                            <Trash2Icon size={12} />
                            Forget
                          </Button>
                          <Button
                            type="button"
                            size="sm"
                            variant="outline"
                            disabled={contextBusy === item.id || !(contextDrafts[item.id] ?? item.value).trim() || (contextDrafts[item.id] ?? item.value) === item.value}
                            onClick={() => void savePersonalContextItem(item)}
                          >
                            <CheckIcon size={12} />
                            Save edit
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-4">
                <h2 className="text-sm font-semibold text-[var(--text-primary)]">Add context</h2>
                <div className="mt-3 flex flex-col gap-2">
                  <select
                    aria-label="Personal context category"
                    value={contextCategory}
                    onChange={(event) => setContextCategory(event.target.value as PersonalContextCategory)}
                    className="w-full rounded-md border border-[var(--border-color)] bg-[var(--bg-app)] px-2.5 py-2 text-xs text-[var(--text-primary)]"
                  >
                    {['communication', 'coding', 'workflow', 'career', 'family', 'finances', 'relationships', 'health', 'education', 'background', 'goals', 'other'].map((value) => (
                      <option key={value} value={value}>{value[0]!.toUpperCase() + value.slice(1)}</option>
                    ))}
                  </select>
                  <textarea
                    value={newContextValue}
                    onChange={(event) => setNewContextValue(event.target.value)}
                    rows={3}
                    placeholder="For example: I’m preparing for senior software engineering interviews."
                    className="w-full resize-y rounded-md border border-[var(--border-color)] bg-[var(--bg-app)] p-2.5 text-xs leading-relaxed text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:border-[var(--accent)]"
                  />
                  <div className="flex justify-end">
                    <Button type="button" size="sm" disabled={contextBusy === 'add' || !newContextValue.trim()} onClick={() => void addPersonalContextItem()}>
                      Add to profile
                    </Button>
                  </div>
                </div>
              </div>

              {contextError && (
                <div role="alert" className="rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-400">
                  {contextError}
                </div>
              )}

              <div className="flex items-center justify-between border-t border-[var(--border-color)] pt-4">
                <span className="text-xs text-[var(--text-muted)]">{personalContext.items.length} saved item(s)</span>
                <div className="flex items-center gap-2">
                  {clearContextConfirming && (
                    <Button type="button" size="sm" variant="ghost" onClick={() => setClearContextConfirming(false)}>Cancel</Button>
                  )}
                  <Button
                    type="button"
                    size="sm"
                    variant={clearContextConfirming ? 'destructive' : 'outline'}
                    disabled={contextBusy === 'clear' || personalContext.items.length === 0}
                    onClick={() => clearContextConfirming ? void clearPersonalContext() : setClearContextConfirming(true)}
                  >
                    {clearContextConfirming ? 'Confirm forget all' : 'Forget all'}
                  </Button>
                </div>
              </div>
            </div>
          )}

          {category === 'models' && (
            <div className="flex flex-col gap-4">
              <ItemGroup className="divide-y divide-[var(--border-color)]">
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Default model</ItemTitle>
                    <ItemDescription>Model selected automatically for new chat sessions.</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <select
                      aria-label="Default model"
                      value={defaultModel}
                      onChange={(e) => {
                        setDefaultModel(e.target.value);
                        writeLocalSetting('defaultModel', e.target.value);
                      }}
                      className="max-w-[240px] rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                    >
                      <option value="auto">Auto (Optimal Router)</option>
                      {models.map((m) => (
                        <option key={m.id} value={m.id}>
                          {m.name} ({m.provider})
                        </option>
                      ))}
                    </select>
                  </ItemActions>
                </Item>

                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Default reasoning effort</ItemTitle>
                    <ItemDescription>Controls how deeply models reason before responding.</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <select
                      aria-label="Default reasoning effort"
                      value={defaultEffort}
                      onChange={(e) => {
                        setDefaultEffort(e.target.value);
                        writeLocalSetting('defaultEffort', e.target.value);
                      }}
                      className="rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                    >
                      <option value="low">Low</option>
                      <option value="medium">Medium</option>
                      <option value="high">High</option>
                    </select>
                  </ItemActions>
                </Item>
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Hide Gram / Lyra built-in models</ItemTitle>
                    <ItemDescription>Show only your BYOK and OpenRouter models in the model switcher.</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <Button
                      type="button"
                      size="sm"
                      variant={hideBuiltinModels ? 'default' : 'outline'}
                      onClick={() => onHideBuiltinModelsChange?.(!hideBuiltinModels)}
                    >
                      {hideBuiltinModels ? 'Hidden' : 'Visible'}
                    </Button>
                  </ItemActions>
                </Item>
              </ItemGroup>

              <Separator />

              <div className="flex flex-col gap-3 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-3.5">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="text-xs font-medium text-[var(--text-primary)]">Add BYOK / Local Model</div>
                    <div className="text-[11px] text-[var(--text-muted)]">
                      Connect OpenAI, Anthropic, OpenRouter, Ollama, or any OpenAI-compatible endpoint.
                    </div>
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  <select
                    aria-label="BYOK provider"
                    value={byokProvider}
                    onChange={(e) => setByokProvider(e.target.value as any)}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                  >
                    <option value="openai">OpenAI</option>
                    <option value="anthropic">Anthropic</option>
                    <option value="openrouter">OpenRouter</option>
                    <option value="ollama">Ollama (Local)</option>
                    <option value="custom">Custom OpenAI-Compatible</option>
                  </select>
                  <input
                    type="text"
                    value={byokModelId}
                    onChange={(e) => setByokModelId(e.target.value)}
                    placeholder="Model ID (e.g. gpt-5.2, qwen2.5-coder:32b)"
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                  <input
                    type="text"
                    value={byokName}
                    onChange={(e) => setByokName(e.target.value)}
                    placeholder="Display name (optional)"
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                  <input
                    type="password"
                    value={byokApiKey}
                    onChange={(e) => setByokApiKey(e.target.value)}
                    placeholder={byokProvider === 'ollama' ? 'API key (optional for Ollama)' : 'API key'}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                </div>
                {(byokProvider === 'custom' || byokProvider === 'ollama') && (
                  <input
                    type="text"
                    value={byokBaseUrl}
                    onChange={(e) => setByokBaseUrl(e.target.value)}
                    placeholder={byokProvider === 'ollama' ? 'http://127.0.0.1:11434/v1' : 'https://your-endpoint/v1'}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                )}
                {byokError && <div className="text-[11px] text-[var(--danger)]">{byokError}</div>}
                <div className="flex justify-end">
                  <Button
                    type="button"
                    size="sm"
                    disabled={byokSaving || !byokModelId.trim()}
                    onClick={async () => {
                      setByokError('');
                      setByokSaving(true);
                      try {
                        const res = await fetch('/api/models/byok', {
                          method: 'POST',
                          headers: { 'Content-Type': 'application/json' },
                          body: JSON.stringify({
                            provider: byokProvider,
                            modelId: byokModelId.trim(),
                            name: byokName.trim() || byokModelId.trim(),
                            baseUrl: byokBaseUrl.trim() || undefined,
                            apiKey: byokApiKey.trim() || undefined,
                          }),
                        });
                        const d = await res.json().catch(() => ({}));
                        if (!res.ok) {
                          setByokError(d.error || 'Failed to save model.');
                          return;
                        }
                        setByokModelId('');
                        setByokName('');
                        setByokApiKey('');
                        onModelsChanged?.();
                      } finally {
                        setByokSaving(false);
                      }
                    }}
                  >
                    {byokSaving ? 'Saving...' : 'Add BYOK Model'}
                  </Button>
                </div>
              </div>

              <Separator />

              <div className="flex items-center justify-between">
                <span className="text-xs font-medium text-[var(--text-secondary)]">
                  Verified Catalog ({filteredModels.length} models)
                </span>
                <input
                  type="text"
                  value={modelFilter}
                  onChange={(e) => setModelFilter(e.target.value)}
                  placeholder="Filter models..."
                  className="w-48 rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] px-2.5 py-1 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none"
                />
              </div>

              <ItemGroup className="max-h-80 divide-y divide-[var(--border-color)] overflow-y-auto pr-1">
                {filteredModels.map((m) => (
                  <Item key={m.id} size="sm" className="rounded-none border-0 px-0 py-2.5">
                    <ItemMedia>
                      <img src={getModelLogoPath(m.id, m.provider)} alt="" width={15} height={15} />
                    </ItemMedia>
                    <ItemContent>
                      <ItemTitle>{m.name}</ItemTitle>
                      <ItemDescription>{m.id}</ItemDescription>
                    </ItemContent>
                    <ItemActions>
                      <span className="text-[11px] text-[var(--text-muted)]">{m.provider}</span>
                      {m.source === 'byok' && (
                        <Button
                          type="button"
                          size="xs"
                          variant="ghost"
                          onClick={async () => {
                            await fetch(`/api/models/byok/${encodeURIComponent(m.id)}`, { method: 'DELETE' });
                            onModelsChanged?.();
                          }}
                        >
                          Remove
                        </Button>
                      )}
                      {defaultModel === m.id ? (
                        <span className="inline-flex items-center gap-1 text-[11px] font-medium text-[var(--accent)]">
                          <CheckIcon size={12} /> Default
                        </span>
                      ) : (
                        <Button
                          type="button"
                          size="xs"
                          variant="ghost"
                          onClick={() => {
                            setDefaultModel(m.id);
                            writeLocalSetting('defaultModel', m.id);
                          }}
                        >
                          Set Default
                        </Button>
                      )}
                    </ItemActions>
                  </Item>
                ))}
              </ItemGroup>
            </div>
          )}

          {category === 'prompts' && (
            <div className="flex flex-col divide-y divide-[var(--border-color)]">
              <div className="flex flex-col gap-1.5 py-3.5">
                <label htmlFor="settings-system-prompt" className="text-xs font-medium text-[var(--text-primary)]">
                  System prompt
                </label>
                <span className="text-xs text-[var(--text-muted)]">
                  Default instructions included with runs in this workspace.
                </span>
                <textarea
                  id="settings-system-prompt"
                  rows={5}
                  value={systemPrompt}
                  onChange={(e) => {
                    setSystemPrompt(e.target.value);
                    writeLocalSetting('systemPrompt', e.target.value);
                  }}
                  placeholder="Optional custom system prompt for this project..."
                  className="mt-1.5 w-full rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] p-2.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:border-[var(--accent)]"
                />
              </div>

              <div className="flex flex-col gap-1.5 py-3.5">
                <label htmlFor="settings-rules" className="text-xs font-medium text-[var(--text-primary)]">
                  Workspace rules
                </label>
                <span className="text-xs text-[var(--text-muted)]">
                  Coding conventions, boundaries, or review constraints appended to prompts.
                </span>
                <textarea
                  id="settings-rules"
                  rows={5}
                  value={rules}
                  onChange={(e) => {
                    setRules(e.target.value);
                    writeLocalSetting('rules', e.target.value);
                  }}
                  placeholder="Example: Prefer TypeScript strict types and run bun test before finishing..."
                  className="mt-1.5 w-full rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] p-2.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)] focus:outline-none focus:border-[var(--accent)]"
                />
              </div>
            </div>
          )}

          {category === 'skills' && (
            <div className="flex flex-col gap-5">
              <div className="flex flex-col gap-1">
                <span className="text-xs font-medium text-[var(--text-secondary)]">
                  Autonomous Agent Skills ({skills.length})
                </span>
                <ItemGroup className="divide-y divide-[var(--border-color)]">
                  {skills.map((skill) => {
                    const enabled = !disabledSkills[skill.name];
                    return (
                      <Item key={skill.name} size="sm" className="rounded-none border-0 px-0 py-3">
                        <ItemContent>
                          <ItemTitle>{skill.displayName}</ItemTitle>
                          <ItemDescription>{skill.description}</ItemDescription>
                        </ItemContent>
                        <ItemActions>
                          <Button
                            type="button"
                            size="xs"
                            variant={enabled ? 'secondary' : 'outline'}
                            onClick={() => toggleSkill(skill.name)}
                          >
                            {enabled ? 'Enabled' : 'Disabled'}
                          </Button>
                        </ItemActions>
                      </Item>
                    );
                  })}
                </ItemGroup>
              </div>

              <Separator />

              <div className="flex flex-col gap-1">
                <span className="text-xs font-medium text-[var(--text-secondary)]">
                  Novel Agent-First Tools ({NOVEL_TOOLS.length})
                </span>
                <ItemGroup className="divide-y divide-[var(--border-color)]">
                  {NOVEL_TOOLS.map((tool) => (
                    <Item key={tool.name} size="sm" className="rounded-none border-0 px-0 py-2.5">
                      <ItemContent>
                        <ItemTitle>{tool.name}</ItemTitle>
                        <ItemDescription>{tool.desc}</ItemDescription>
                      </ItemContent>
                      <ItemActions>
                        <span className="text-[11px] text-[var(--text-muted)]">Built-in</span>
                      </ItemActions>
                    </Item>
                  ))}
                </ItemGroup>
              </div>
            </div>
          )}

          {category === 'themes' && (
            <ItemGroup className="divide-y divide-[var(--border-color)]">
              {THEMES.map((t) => {
                const selected = theme === t.id;
                return (
                  <Item
                    key={t.id}
                    className={`cursor-pointer rounded-none border-0 px-2 py-3 transition-colors hover:bg-[var(--bg-panel-subtle)] ${
                      selected ? 'bg-[var(--accent-light)]' : ''
                    }`}
                    onClick={() => onThemeChange(t.id)}
                  >
                    <ItemContent>
                      <ItemTitle>{t.name}</ItemTitle>
                      <ItemDescription>{t.desc}</ItemDescription>
                    </ItemContent>
                    <ItemActions>
                      <div className="flex items-center gap-1.5">
                        {t.swatches.map((hex, i) => (
                          <span
                            key={i}
                            className="inline-block h-3.5 w-3.5 rounded-full border border-[var(--border-color)]"
                            style={{ backgroundColor: hex }}
                          />
                        ))}
                      </div>
                      <Button
                        type="button"
                        size="xs"
                        variant={selected ? 'default' : 'outline'}
                        onClick={(e) => {
                          e.stopPropagation();
                          onThemeChange(t.id);
                        }}
                      >
                        {selected ? 'Active' : 'Apply'}
                      </Button>
                    </ItemActions>
                  </Item>
                );
              })}
            </ItemGroup>
          )}

          {category === 'workspace' && (
            <div className="flex flex-col gap-5">
              <ItemGroup className="divide-y divide-[var(--border-color)]">
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Git &amp; repository</ItemTitle>
                    <ItemDescription>{workspaceInfo.path || 'Current workspace'}</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <span className="text-xs font-medium text-[var(--text-primary)]">
                      {workspaceInfo.vcs ? `${workspaceInfo.vcs.type || 'git'} (${workspaceInfo.vcs.branch || 'main'})` : 'No VCS'}
                    </span>
                  </ItemActions>
                </Item>

                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>GramFS Copy-on-Write overlay</ItemTitle>
                    <ItemDescription>
                      {overlayChanges.length === 0
                        ? 'Main overlay matches host disk (0 uncommitted overlay files).'
                        : `${overlayChanges.length} modified file(s) staged in GramFS overlay.`}
                    </ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={overlayChanges.length === 0 || busyAction === 'reset'}
                      onClick={handleResetOverlay}
                    >
                      Reset Overlay
                    </Button>
                    <Button
                      type="button"
                      size="sm"
                      disabled={overlayChanges.length === 0 || busyAction === 'commit'}
                      onClick={handleCommitOverlay}
                    >
                      Commit to Host
                    </Button>
                  </ItemActions>
                </Item>
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Hide Gram / Lyra built-in models</ItemTitle>
                    <ItemDescription>Show only your BYOK and OpenRouter models in the model switcher.</ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <Button
                      type="button"
                      size="sm"
                      variant={hideBuiltinModels ? 'default' : 'outline'}
                      onClick={() => onHideBuiltinModelsChange?.(!hideBuiltinModels)}
                    >
                      {hideBuiltinModels ? 'Hidden' : 'Visible'}
                    </Button>
                  </ItemActions>
                </Item>
              </ItemGroup>

              <Separator />

              <div className="flex flex-col gap-3 rounded-xl border border-[var(--border-color)] bg-[var(--bg-panel)] p-3.5">
                <div className="flex items-center justify-between">
                  <div>
                    <div className="text-xs font-medium text-[var(--text-primary)]">Add BYOK / Local Model</div>
                    <div className="text-[11px] text-[var(--text-muted)]">
                      Connect OpenAI, Anthropic, OpenRouter, Ollama, or any OpenAI-compatible endpoint.
                    </div>
                  </div>
                </div>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  <select
                    aria-label="BYOK provider"
                    value={byokProvider}
                    onChange={(e) => setByokProvider(e.target.value as any)}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)]"
                  >
                    <option value="openai">OpenAI</option>
                    <option value="anthropic">Anthropic</option>
                    <option value="openrouter">OpenRouter</option>
                    <option value="ollama">Ollama (Local)</option>
                    <option value="custom">Custom OpenAI-Compatible</option>
                  </select>
                  <input
                    type="text"
                    value={byokModelId}
                    onChange={(e) => setByokModelId(e.target.value)}
                    placeholder="Model ID (e.g. gpt-5.2, qwen2.5-coder:32b)"
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                  <input
                    type="text"
                    value={byokName}
                    onChange={(e) => setByokName(e.target.value)}
                    placeholder="Display name (optional)"
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                  <input
                    type="password"
                    value={byokApiKey}
                    onChange={(e) => setByokApiKey(e.target.value)}
                    placeholder={byokProvider === 'ollama' ? 'API key (optional for Ollama)' : 'API key'}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                </div>
                {(byokProvider === 'custom' || byokProvider === 'ollama') && (
                  <input
                    type="text"
                    value={byokBaseUrl}
                    onChange={(e) => setByokBaseUrl(e.target.value)}
                    placeholder={byokProvider === 'ollama' ? 'http://127.0.0.1:11434/v1' : 'https://your-endpoint/v1'}
                    className="rounded-md border border-[var(--border-color)] bg-[var(--bg-body)] px-2.5 py-1.5 text-xs text-[var(--text-primary)] placeholder:text-[var(--text-muted)]"
                  />
                )}
                {byokError && <div className="text-[11px] text-[var(--danger)]">{byokError}</div>}
                <div className="flex justify-end">
                  <Button
                    type="button"
                    size="sm"
                    disabled={byokSaving || !byokModelId.trim()}
                    onClick={async () => {
                      setByokError('');
                      setByokSaving(true);
                      try {
                        const res = await fetch('/api/models/byok', {
                          method: 'POST',
                          headers: { 'Content-Type': 'application/json' },
                          body: JSON.stringify({
                            provider: byokProvider,
                            modelId: byokModelId.trim(),
                            name: byokName.trim() || byokModelId.trim(),
                            baseUrl: byokBaseUrl.trim() || undefined,
                            apiKey: byokApiKey.trim() || undefined,
                          }),
                        });
                        const d = await res.json().catch(() => ({}));
                        if (!res.ok) {
                          setByokError(d.error || 'Failed to save model.');
                          return;
                        }
                        setByokModelId('');
                        setByokName('');
                        setByokApiKey('');
                        onModelsChanged?.();
                      } finally {
                        setByokSaving(false);
                      }
                    }}
                  >
                    {byokSaving ? 'Saving...' : 'Add BYOK Model'}
                  </Button>
                </div>
              </div>

              <Separator />

              <div className="flex flex-col gap-1">
                <span className="text-xs font-medium text-[var(--text-secondary)]">
                  Gram Connect Devices ({devices.length})
                </span>
                <ItemGroup className="divide-y divide-[var(--border-color)]">
                  {devices.map((d) => (
                    <Item key={d.deviceId} size="sm" className="rounded-none border-0 px-0 py-2.5">
                      <ItemContent>
                        <ItemTitle>{d.deviceName}</ItemTitle>
                        <ItemDescription>
                          {d.deviceType} · {d.deviceId}
                        </ItemDescription>
                      </ItemContent>
                      <ItemActions>
                        <span className="text-[11px] text-[var(--text-muted)]">
                          {d.isExecutionHost ? 'Execution Host' : 'Companion'}
                        </span>
                      </ItemActions>
                    </Item>
                  ))}
                </ItemGroup>
              </div>
            </div>
          )}

          {category === 'account' && (
            <div className="flex flex-col gap-4">
            {syncConflicts.length > 0 && (
              <div className="rounded-lg border border-amber-500/30 bg-amber-500/[0.06] p-4">
                <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-[var(--text-primary)]">
                  <AlertTriangleIcon size={15} className="text-amber-500" />
                  <span>Changes from another device need review</span>
                </div>
                <p className="mb-3 text-xs leading-relaxed text-[var(--text-secondary)]">
                  Gram kept both versions. Choose which session detail should become the shared version.
                </p>
                <div className="flex flex-col gap-2">
                  {syncConflicts.map((conflict) => (
                    <div key={conflict.conflictId} className="rounded-md border border-[var(--border-color)] bg-[var(--bg-panel)] p-3">
                      <div className="mb-2 flex items-center justify-between gap-2">
                        <span className="text-xs font-medium text-[var(--text-primary)]">{conflict.field.replace(/_/g, ' ')}</span>
                        <span className="max-w-[60%] truncate text-[10px] text-[var(--text-muted)]">{conflict.sessionId}</span>
                      </div>
                      <div className="grid gap-2 sm:grid-cols-2">
                        <div className="rounded bg-[var(--bg-app)] p-2">
                          <div className="mb-1 text-[10px] font-medium uppercase text-[var(--text-muted)]">This device</div>
                          <div className="break-words text-xs text-[var(--text-secondary)]">{conflict.localValue}</div>
                        </div>
                        <div className="rounded bg-[var(--bg-app)] p-2">
                          <div className="mb-1 text-[10px] font-medium uppercase text-[var(--text-muted)]">Synced version</div>
                          <div className="break-words text-xs text-[var(--text-secondary)]">{conflict.remoteValue}</div>
                        </div>
                      </div>
                      <div className="mt-2 flex justify-end gap-2">
                        <Button type="button" size="sm" variant="outline" disabled={busyAction === `sync-conflict:${conflict.conflictId}`} onClick={() => void handleResolveSyncConflict(conflict.conflictId, 'local')}>
                          Keep this device
                        </Button>
                        <Button type="button" size="sm" disabled={busyAction === `sync-conflict:${conflict.conflictId}`} onClick={() => void handleResolveSyncConflict(conflict.conflictId, 'remote')}>
                          Use synced version
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            <ItemGroup className="divide-y divide-[var(--border-color)]">
              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemMedia className="text-[var(--text-muted)]">
                  <UserIcon size={15} />
                </ItemMedia>
                <ItemContent>
                  <ItemTitle>
                    {authState.authenticated && authState.user
                      ? authState.user.name || authState.user.email
                      : 'Local session'}
                  </ItemTitle>
                  <ItemDescription>
                    {authState.authenticated && authState.user
                      ? `${authState.user.email} · Connected to Lyra Hub`
                      : authState.connected
                        ? 'Lyra Hub reachable — sign in to sync across devices.'
                        : 'Lyra Hub offline — changes are queued in local SQLite until connected.'}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  {authState.authenticated ? (
                    <Button type="button" size="sm" variant="outline" onClick={() => onSignOut()}>
                      Sign out
                    </Button>
                  ) : (
                    <Button type="button" size="sm" onClick={onOpenSignIn}>
                      Sign in
                    </Button>
                  )}
                </ItemActions>
              </Item>

              <Item className="rounded-none border-0 px-0 py-3.5">
                <ItemContent>
                  <ItemTitle>Cross-device delta sync</ItemTitle>
                  <ItemDescription>
                    {syncStatus.message ||
                      (syncStatus.latestSeq !== undefined
                        ? `Last synced sequence: ${syncStatus.latestSeq}`
                        : 'Push dirty local chats/tasks and pull remote changes via AHP.')}
                  </ItemDescription>
                </ItemContent>
                <ItemActions>
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={syncStatus.loading}
                    onClick={handleSyncNow}
                  >
                    <RefreshCwIcon size={12} className={syncStatus.loading ? 'animate-spin' : ''} />
                    <span>Sync Now</span>
                  </Button>
                </ItemActions>
              </Item>

              {onLayoutPresetChange && (
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Workspace layout preset</ItemTitle>
                    <ItemDescription>
                      Beginner keeps the left project list open. Mid &amp; Power hide the permanent left sheet in favor of top navigation + ⌘K quick switching.
                    </ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <div className="inline-flex items-center gap-1 rounded-lg border border-[var(--border-color)] bg-[var(--bg-panel)] p-0.5">
                      {(
                        [
                          { id: 'beginner', label: 'Beginner' },
                          { id: 'mid', label: 'Mid' },
                          { id: 'power', label: 'Power' },
                        ] as const
                      ).map((preset) => (
                        <button
                          key={preset.id}
                          type="button"
                          onClick={() => onLayoutPresetChange(preset.id)}
                          className={`rounded-md px-2.5 py-1 text-xs font-medium transition-colors cursor-pointer ${
                            layoutPreset === preset.id
                              ? 'bg-[var(--bg-hover)] text-[var(--text-primary)]'
                              : 'text-[var(--text-muted)] hover:text-[var(--text-primary)]'
                          }`}
                        >
                          {preset.label}
                        </button>
                      ))}
                    </div>
                  </ItemActions>
                </Item>
              )}
            </ItemGroup>
            </div>
          )}

          {category === 'diagnostics' && (
            <div className="flex flex-col gap-3">
              <ItemGroup className="divide-y divide-[var(--border-color)]">
                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Gram Studio Runtime</ItemTitle>
                    <ItemDescription>
                      Workspace: {healthInfo.workspace || workspaceInfo.path || '—'}
                    </ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <span className="text-xs text-[var(--text-muted)]">
                      v{healthInfo.version || '2.0.0'} · {healthInfo.status || 'ok'}
                    </span>
                  </ItemActions>
                </Item>

                <Item className="rounded-none border-0 px-0 py-3.5">
                  <ItemContent>
                    <ItemTitle>Environment Sandbox Doctor</ItemTitle>
                    <ItemDescription>
                      Inspect ports, runtime readiness, and environment configuration.
                    </ItemDescription>
                  </ItemContent>
                  <ItemActions>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={busyAction === 'doctor'}
                      onClick={handleRunDoctor}
                    >
                      {busyAction === 'doctor' ? 'Checking...' : 'Run Check'}
                    </Button>
                  </ItemActions>
                </Item>
              </ItemGroup>

              {doctorResult && (
                <pre className="max-h-72 overflow-auto border-t border-[var(--border-color)] pt-3 text-xs text-[var(--text-secondary)]">
                  {JSON.stringify(doctorResult, null, 2)}
                </pre>
              )}
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
