import { describe, expect, test } from 'bun:test';
import {
  formatEnvironmentLabel,
  resolveActivityEnvironment,
  splitAttentionActivities,
} from '../src/ui/features/workspace/MissionControlDrawer';
import type { IslandActivity } from '../src/ui/components/ui/dynamic-island';

describe('Mission Control grouping & environment helpers', () => {
  test('separates attention items and groups activities by project', () => {
    const activities: IslandActivity[] = [
      {
        runId: 'run-1',
        sessionId: 's-1',
        title: 'Run tests in Aegis',
        stage: 'paused',
        projectId: 'proj-aegis',
        projectLabel: 'Aegis',
        pendingApproval: { id: 'apr-1', title: 'Confirm', command: 'dotnet test' },
      },
      {
        runId: 'run-2',
        sessionId: 's-2',
        title: 'Refactor router',
        stage: 'executing',
        projectId: 'proj-gram',
        projectLabel: 'Gram',
        kind: 'swarm',
        workers: [
          { id: 'w-1', name: 'Worker 1', role: 'Refactor', model: 'haiku', status: 'running', currentStep: 'Editing router.ts' },
        ],
      },
      {
        runId: 'run-3',
        sessionId: 's-3',
        title: 'Swarm patch ready',
        stage: 'completed',
        projectId: 'proj-aegis',
        projectLabel: 'Aegis',
        reviewLayerId: 'layer-9',
        stagedFileCount: 3,
      },
    ];

    const { attention, grouped } = splitAttentionActivities(activities);
    expect(attention.map((a) => a.runId)).toEqual(['run-1', 'run-3']);
    expect(grouped.length).toBe(2);
    expect(grouped[0]?.projectLabel).toBe('Aegis');
    expect(grouped[0]?.items.length).toBe(2);
    expect(grouped[1]?.projectLabel).toBe('Gram');
  });

  test('resolves and formats execution environment labels', () => {
    expect(
      resolveActivityEnvironment({
        runId: 'r1',
        sessionId: 's1',
        title: 't',
        stage: 'executing',
        kind: 'swarm',
      })
    ).toBe('cloud-lyra');
    expect(
      formatEnvironmentLabel({
        runId: 'r2',
        sessionId: 's2',
        title: 't',
        stage: 'executing',
        worktreePath: '/tmp/wt-1',
      })
    ).toBe('Worktree');
    expect(
      formatEnvironmentLabel({
        runId: 'r3',
        sessionId: 's3',
        title: 't',
        stage: 'executing',
      })
    ).toBe('Local');
  });
});
