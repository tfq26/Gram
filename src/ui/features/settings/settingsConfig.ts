/**
 * Shared types, static configuration, and localStorage helpers for SettingsView.
 */

import type React from 'react';
import {
  ActivityIcon,
  CpuIcon,
  FileTextIcon,
  FolderGit2Icon,
  PaletteIcon,
  SlidersHorizontalIcon,
  SparklesIcon,
  UserIcon,
} from 'lucide-react';

export type SettingsCategory =
  | 'general'
  | 'models'
  | 'prompts'
  | 'skills'
  | 'themes'
  | 'workspace'
  | 'account'
  | 'diagnostics';

export interface SettingsModelItem {
  id: string;
  name: string;
  provider: string;
  source?: 'byok' | 'openrouter' | 'builtin';
  contributorWarning?: string;
}

export interface SettingsAuthState {
  connected: boolean;
  authenticated: boolean;
  user: { id: string; name: string; email: string } | null;
}

export type LayoutPreset = 'beginner' | 'mid' | 'power';

export interface SettingsViewProps {
  theme: string;
  onThemeChange: (nextTheme: string) => void;
  models: SettingsModelItem[];
  hideBuiltinModels?: boolean;
  onHideBuiltinModelsChange?: (hide: boolean) => void;
  onModelsChanged?: () => void;
  layoutPreset?: LayoutPreset;
  onLayoutPresetChange?: (preset: LayoutPreset) => void;
  authState: SettingsAuthState;
  onOpenSignIn: () => void;
  onSignOut: () => Promise<void> | void;
  onHistoryCleared: () => void;
  onSyncCompleted: () => void;
  onClose: () => void;
}

export const CATEGORIES: Array<{
  id: SettingsCategory;
  label: string;
  description: string;
  icon: React.ComponentType<{ size?: number }>;
}> = [
  { id: 'general', label: 'General', description: 'Behavior, permissions, and local storage', icon: SlidersHorizontalIcon },
  { id: 'models', label: 'Models', description: 'Default routing, reasoning effort, and verified catalog', icon: CpuIcon },
  { id: 'prompts', label: 'Prompts & Rules', description: 'Workspace system instructions and constraints', icon: FileTextIcon },
  { id: 'skills', label: 'Skills & Tools', description: 'Autonomous cognitive skills and runtime tools', icon: SparklesIcon },
  { id: 'themes', label: 'Themes', description: 'Color palettes applied across the studio shell', icon: PaletteIcon },
  { id: 'workspace', label: 'Workspace & Sandbox', description: 'GramFS overlay, Git branch, and mesh devices', icon: FolderGit2Icon },
  { id: 'account', label: 'Account & Sync', description: 'Lyra Hub session and SQLite delta sync', icon: UserIcon },
  { id: 'diagnostics', label: 'Diagnostics & About', description: 'Environment health, runtime checks, and version', icon: ActivityIcon },
];

export const THEMES = [
  { id: 'obsidian', name: 'Obsidian', desc: 'Default dark obsidian palette', swatches: ['#0e0f10', '#34d399', '#ececec'] },
  { id: 'bone', name: 'Bone', desc: 'Default warm light bone palette', swatches: ['#f7f5f0', '#0ea5e9', '#141311'] },
  { id: 'prism', name: 'Prism', desc: 'Three-color trio gradient on obsidian', swatches: ['#34d399', '#a78bfa', '#fb7185'] },
  { id: 'carbon', name: 'Carbon', desc: 'Original dark neutral palette', swatches: ['#0a0a0c', '#7c3aed', '#f4f4f5'] },
  { id: 'light', name: 'Light', desc: 'Clean daylight alabaster palette', swatches: ['#fafafa', '#18181b', '#e4e4e7'] },
  { id: 'purple', name: 'Purple', desc: 'Deep violet slate palette', swatches: ['#07050a', '#9333ea', '#f3f0fa'] },
  { id: 'amber', name: 'Amber', desc: 'Warm obsidian amber palette', swatches: ['#090806', '#d97706', '#fbf7ee'] },
  { id: 'emerald', name: 'Emerald', desc: 'Dark forest green palette', swatches: ['#040806', '#059669', '#ecfdf5'] },
  { id: 'blue', name: 'Blue', desc: 'Midnight cobalt palette', swatches: ['#06090f', '#2563eb', '#f0f6fc'] },
  { id: 'crimson', name: 'Crimson', desc: 'Dark ruby accent palette', swatches: ['#0a0406', '#e11d48', '#fff1f2'] },
];

export const NOVEL_TOOLS = [
  { name: 'blastRadiusSimulate', desc: 'Simulates direct callers and breakage risk before editing symbols.' },
  { name: 'runtimeProbe', desc: 'Executes fast isolated micro-evaluations against the active workspace.' },
  { name: 'hypothesisRace', desc: 'Forks parallel CoW overlays and keeps the highest-scoring fix.' },
  { name: 'semanticBlameWhy', desc: 'Extracts architectural intent and history for a semantic symbol URI.' },
  { name: 'uiGeometryAssert', desc: 'Detects bounding-box overlaps and z-index occlusions.' },
  { name: 'contextFold', desc: 'Compresses long conversation trajectories into pinned facts.' },
  { name: 'envSandboxDoctor', desc: 'Inspects open ports, runtime health, and environment drift.' },
  { name: 'uiVibeCheckAssert', desc: 'Audits generated UI against typography, layout, and palette rules.' },
];

export function readLocalSetting(key: string, fallback: string): string {
  try {
    return localStorage.getItem(`gram.settings.${key}`) ?? fallback;
  } catch {
    return fallback;
  }
}

export function writeLocalSetting(key: string, value: string): void {
  try {
    localStorage.setItem(`gram.settings.${key}`, value);
  } catch {}
}
