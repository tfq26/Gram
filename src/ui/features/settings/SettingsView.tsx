import React, { useEffect, useMemo, useState } from 'react';
import {
  ArrowLeftIcon,
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

  const [syncStatus, setSyncStatus] = useState<{ synced?: boolean; latestSeq?: number; loading?: boolean; message?: string }>({});
  const [clearConfirming, setClearConfirming] = useState(false);
  const [busyAction, setBusyAction] = useState<string | null>(null);

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
  }

  useEffect(() => {
    refreshLiveData();
  }, []);

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
    } catch {
      setSyncStatus({ synced: false, loading: false, message: 'Sync failed' });
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
