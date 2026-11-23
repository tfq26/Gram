import React, {
  lazy,
} from 'react';
import {
  type DockSurface,
} from '../workspace/WorkspaceDock';
import {
  type ChatTab,
  type ManagedProcessItem,
} from '../../lib/types';

const ContentStage = lazy(() =>
  import('../workspace/ContentStageAndMiniChat').then((m) => ({ default: m.ContentStage }))
);

export interface ContextPanelProps {
  activeDockSurface: DockSurface;
  setActiveDockSurface: React.Dispatch<React.SetStateAction<DockSurface | null>>;
  activeProjectId: string;
  planBySession: Record<string, string>;
  activeTab: ChatTab;
  setPlanBySession: React.Dispatch<React.SetStateAction<Record<string, string>>>;
  handleApprovePlanAndBuild: (opts: { markdown: string; isolateWorktree: boolean; }) => void;
  stagePreviewUrl: string;
  setStagePreviewUrl: React.Dispatch<React.SetStateAction<string>>;
  managedProcesses: ManagedProcessItem[];
  setManagedProcesses: React.Dispatch<React.SetStateAction<ManagedProcessItem[]>>;
}

export function ContextPanel({ activeDockSurface, setActiveDockSurface, activeProjectId, planBySession, activeTab, setPlanBySession, handleApprovePlanAndBuild, stagePreviewUrl, setStagePreviewUrl, managedProcesses, setManagedProcesses }: ContextPanelProps) {
  return (
    <aside className="hidden h-full w-[46%] min-w-[360px] max-w-[660px] shrink-0 border-l border-[var(--border-color)] lg:flex">
        <React.Suspense fallback={null}>
          <ContentStage
            surface={activeDockSurface}
            onSelectSurface={(next) => setActiveDockSurface(next)}
            onClose={() => setActiveDockSurface(null)}
            activeProjectId={activeProjectId}
            planMarkdown={planBySession[activeTab.sessionId] || ''}
            onPlanChange={(next) =>
              setPlanBySession((prev) => ({ ...prev, [activeTab.sessionId]: next }))
            }
            onSavePlan={async (markdown) => {
              await fetch('/api/plan/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                  sessionId: activeTab.sessionId,
                  projectId: activeProjectId,
                  markdown,
                }),
              });
            }}
            onApproveAndBuild={({ markdown, isolateWorktree }) => {
              setActiveDockSurface(null);
              void handleApprovePlanAndBuild({ markdown, isolateWorktree });
            }}
            previewUrl={stagePreviewUrl}
            onPreviewUrlChange={setStagePreviewUrl}
            managedProcesses={managedProcesses.map((p) => ({
              processId: p.id,
              label: p.label,
              command: p.command,
              status: p.status,
              pid: p.pid,
            }))}
            onRefreshProcesses={() => {
              fetch('/api/processes')
                .then((r) => r.json())
                .then((d) => {
                  if (Array.isArray(d?.processes)) setManagedProcesses(d.processes);
                })
                .catch(() => {});
            }}
            onStopProcess={async (processId) => {
              await fetch('/api/processes/stop', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ processId }),
              });
            }}
            onOpenExternal={(targetUrl) => {
              window.open(targetUrl, '_blank', 'noopener,noreferrer');
            }}
          />
        </React.Suspense>
      </aside>
  );
}
