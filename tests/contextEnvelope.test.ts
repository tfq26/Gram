import { describe, it, expect } from 'bun:test';
import { ContextEnvelope } from '../src/core/contextEnvelope';
import { getModelProfile, selectOptimalModel, isAllowedModel, listAllowedModels } from '../src/providers/router';

describe('Context Envelope & Multi-Provider Router', () => {
  it('detects model family and projects appropriate tools for Claude vs Codex', () => {
    const claudeEnvelope = new ContextEnvelope({
      sessionId: 'sess-1',
      workspacePath: '/app',
      activeModel: 'claude-3-7-sonnet',
      activeFamily: ContextEnvelope.detectFamily('claude-3-7-sonnet'),
    });

    expect(claudeEnvelope.state.activeFamily).toBe('anthropic');
    const claudeTools = claudeEnvelope.getProjectedTools();
    expect(claudeTools.some((t) => t.name === 'edit_file')).toBe(true);
    expect(claudeTools.some((t) => t.name === 'apply_patch')).toBe(false);

    const codexEnvelope = new ContextEnvelope({
      sessionId: 'sess-2',
      workspacePath: '/app',
      activeModel: 'openai/gpt-4o',
      activeFamily: ContextEnvelope.detectFamily('openai/gpt-4o'),
    });

    expect(codexEnvelope.state.activeFamily).toBe('openai');
    const codexTools = codexEnvelope.getProjectedTools();
    expect(codexTools.some((t) => t.name === 'apply_patch')).toBe(true);
    expect(codexTools.some((t) => t.name === 'edit_file')).toBe(false);
  });

  it('compiles provider-neutral system prompt with pinned facts and plan nodes', () => {
    const envelope = new ContextEnvelope({
      sessionId: 'sess-facts',
      workspacePath: '/test/repo',
    });

    envelope.pinFact('db_schema', 'Users table has uuid primary key');
    envelope.updatePlan([
      { id: '1', title: 'Setup auth', status: 'completed', dependencies: [] },
      { id: '2', title: 'Add billing', status: 'in_progress', dependencies: ['1'] },
    ]);

    const systemPrompt = envelope.compileSystemPrompt();
    expect(systemPrompt).toContain('Users table has uuid primary key');
    expect(systemPrompt).toContain('[COMPLETED] (1) Setup auth');
    expect(systemPrompt).toContain('[IN_PROGRESS] (2) Add billing');
  });

  it('routes across the Bedrock, Foundry, and OpenRouter allowlist', () => {
    expect(listAllowedModels().length).toBeGreaterThanOrEqual(46);
    expect(isAllowedModel('not-a-real-model')).toBe(false);
    expect(isAllowedModel('google/gemini-2.5-flash')).toBe(false);
    expect(isAllowedModel('google/gemini-4-hydrogen')).toBe(true);
    expect(isAllowedModel('us.openai.gpt-6-luna')).toBe(true);

    // The catalog is owned by Lyra, so assert on properties rather than a hard-coded winner.
    const frontier = selectOptimalModel({ taskComplexity: 'architecture', priority: 'frontier' });
    const frontierScore = (m: { capabilities: { coding: number; reasoning: number } }) =>
      m.capabilities.coding + m.capabilities.reasoning;
    for (const m of listAllowedModels().filter((x) => x.provider === 'bedrock' && x.autoEligible !== false)) {
      expect(frontierScore(frontier)).toBeGreaterThanOrEqual(frontierScore(m));
    }

    const frugal = selectOptimalModel({ taskComplexity: 'simple', priority: 'cost' });
    expect(frugal.id).toBe('nova-2-lite');
    // Legacy Bedrock ids still resolve to the canonical profile.
    expect(getModelProfile('us.amazon.nova-2-lite-v1:0')?.id).toBe('nova-2-lite');

    const balanced = selectOptimalModel({ taskComplexity: 'moderate', priority: 'balanced' });
    expect(['bedrock', 'openrouter']).toContain(balanced.provider);
  });
});
