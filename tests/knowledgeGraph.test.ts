import { describe, expect, test } from 'bun:test';
import { Database } from 'bun:sqlite';
import { KnowledgeGraphStore } from '../src/core/knowledgeGraph';
import { selectOptimalModel } from '../src/providers/router';

describe('KnowledgeGraphStore (Lean YAGNI Knowledge Store)', () => {
  test('inherits global learnings across projects and models while scoping project facts', () => {
    const db = new Database(':memory:');
    const store = new KnowledgeGraphStore(db);

    const { node: savedGlobal, created } = store.recordInsight({
      scope: 'global',
      projectId: 'proj-alpha',
      kind: 'preference',
      title: 'Prefer Vite + React + Tailwind for new web apps',
      content: 'When scaffolding a new web UI, default to Vite, React, TypeScript, and Tailwind unless another stack is requested.',
      tags: ['website', 'vite', 'react', 'tailwind', 'frontend'],
      sourceModel: 'claude-sonnet-4-5',
    });
    expect(created).toBe(true);

    store.recordInsight({
      scope: 'project',
      projectId: 'proj-alpha',
      kind: 'architecture',
      title: 'Alpha uses Stripe webhook secret',
      content: 'Stripe webhooks verify signatures in src/billing.ts.',
      tags: ['billing', 'stripe', 'website'],
      sourceModel: 'claude-sonnet-4-5',
    });

    const retrieval = store.retrieveRelevantKnowledge({
      query: 'Build a small website with React and Tailwind',
      projectId: 'proj-beta',
      limit: 6,
    });

    const titles = retrieval.items.map((item) => item.title);
    expect(titles).toContain(savedGlobal.title);
    expect(titles).not.toContain('Alpha uses Stripe webhook secret');
    expect(retrieval.formattedPromptBlock).toContain('<INHERITED_KNOWLEDGE>');
    db.close();
  });

  test('hides superseded insights from retrieval', () => {
    const db = new Database(':memory:');
    const store = new KnowledgeGraphStore(db);

    const { node: oldRule } = store.recordInsight({
      scope: 'global',
      kind: 'pattern',
      title: 'Old deployment target Heroku',
      content: 'Deploy web apps to Heroku.',
      tags: ['deploy', 'hosting'],
    });
    const { node: newRule } = store.recordInsight({
      scope: 'global',
      kind: 'preference',
      title: 'Deploy web apps on Cloudflare',
      content: 'User deploys web apps on Cloudflare Tunnel or Pages.',
      tags: ['deploy', 'cloudflare', 'hosting'],
      supersedesId: oldRule.id,
    });

    const result = store.retrieveRelevantKnowledge({
      query: 'How should we deploy on Cloudflare?',
      projectId: 'any',
      limit: 5,
    });

    const ids = result.items.map((item) => item.id);
    expect(ids).toContain(newRule.id);
    expect(ids).not.toContain(oldRule.id);
    db.close();
  });

  test('captures explicit user directives while ignoring casual debugging chatter', () => {
    const db = new Database(':memory:');
    const store = new KnowledgeGraphStore(db);

    const ignored = store.captureExplicitDirective({
      userPrompt: 'The issue is my port 5173 is already in use right now.',
      projectId: 'gram',
      modelId: 'gemini-4',
    });
    expect(ignored.length).toBe(0);

    const captured = store.captureExplicitDirective({
      userPrompt: 'Remember that I prefer using Google sign-in via Better Auth for all projects.',
      projectId: 'gram',
      modelId: 'gemini-4',
    });
    expect(captured.length).toBe(1);
    expect(captured[0]?.kind).toBe('preference');
    db.close();
  });

  test('balanced Auto model routing prioritizes coding quality over raw price penalty', () => {
    const selected = selectOptimalModel({
      taskComplexity: 'moderate',
      priority: 'balanced',
    });
    expect(selected.capabilities.coding).toBeGreaterThanOrEqual(9.0);
  });
});
