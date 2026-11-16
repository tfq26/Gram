import { describe, it, expect } from 'bun:test';
import { AgentSkillsRegistry } from '../src/tools/agentSkills';
import { GramAgentRuntime } from '../src/runtime/agentRuntime';
import { ContextEnvelope } from '../src/core/contextEnvelope';

describe('Gram Agentic Skills (Autonomous Cognitive Tools)', () => {
  const cwd = process.cwd();
  const registry = new AgentSkillsRegistry();

  it('registers all 5 primary agent skills with schemas and whenToUse guidelines', () => {
    const skills = registry.listSkills();
    expect(skills.length).toBe(5);

    const names = skills.map((s) => s.name);
    expect(names).toContain('env_doctor');
    expect(names).toContain('semantic_graph');
    expect(names).toContain('device_mesh_connect');
    expect(names).toContain('code_mode');
    expect(names).toContain('hypothesis_race');

    for (const skill of skills) {
      expect(skill.displayName).toBeDefined();
      expect(skill.description.length).toBeGreaterThan(10);
      expect(skill.whenToUse.length).toBeGreaterThan(20);
      expect(skill.inputSchema).toBeDefined();
    }
  });

  it('autonomously infers the appropriate skill from natural language prompts', () => {
    const doctorInference = registry.inferBestSkill('verify my project setup and check ports');
    expect(doctorInference).not.toBeNull();
    expect(doctorInference?.skillName).toBe('env_doctor');
    expect(doctorInference?.reason).toContain('environment health');

    const graphInference = registry.inferBestSkill('Check blast radius for our updated Symbol class');
    expect(graphInference).not.toBeNull();
    expect(graphInference?.skillName).toBe('semantic_graph');

    const meshInference = registry.inferBestSkill('List active connect mesh companion devices');
    expect(meshInference).not.toBeNull();
    expect(meshInference?.skillName).toBe('device_mesh_connect');

    const codeModeInference = registry.inferBestSkill('Execute programmatic code mode batch transform');
    expect(codeModeInference).not.toBeNull();
    expect(codeModeInference?.skillName).toBe('code_mode');

    const raceInference = registry.inferBestSkill('Run a hypothesis race between two competing fixes');
    expect(raceInference).not.toBeNull();
    expect(raceInference?.skillName).toBe('hypothesis_race');
  });

  it('executes env_doctor skill and returns structured health status', async () => {
    const runtime = new GramAgentRuntime(cwd);
    const skill = registry.getSkill('env_doctor')!;
    const res = await skill.execute(
      { ports: [3000, 7337], checkEnv: true },
      {
        gramfs: runtime.gramfs,
        graph: runtime.graph,
        codeMode: runtime.codeMode,
        tools: runtime.tools,
        workspaceRoot: cwd,
      }
    );

    expect(res.success).toBe(true);
    expect(res.skillName).toBe('env_doctor');
    expect(res.summary).toContain('healthy');
    expect(res.pinnedFact).toBeDefined();
    expect(res.pinnedFact?.key).toBe('env.health');
    runtime.close();
  });

  it('executes semantic_graph skill and indexes workspace symbols', async () => {
    const runtime = new GramAgentRuntime(cwd);
    const skill = registry.getSkill('semantic_graph')!;
    const res = await skill.execute(
      { action: 'index' },
      {
        gramfs: runtime.gramfs,
        graph: runtime.graph,
        codeMode: runtime.codeMode,
        tools: runtime.tools,
        workspaceRoot: cwd,
      }
    );

    expect(res.success).toBe(true);
    expect(res.skillName).toBe('semantic_graph');
    expect(res.summary).toContain('Semantic Symbols');
    expect(res.pinnedFact?.key).toBe('ast.semantic_graph');
    runtime.close();
  });

  it('autonomously selects and executes skills in GramAgentRuntime.executeRun', async () => {
    const runtime = new GramAgentRuntime(cwd);
    const progressLogs: string[] = [];

    const result = await runtime.executeRun({
      sessionId: `skills-test-${Date.now()}`,
      prompt: 'verify my project setup',
      onProgress: (p) => progressLogs.push(p),
    });

    expect(result.completed).toBe(true);
    expect(result.checkpoint.stage).toBe('completed');
    expect(result.checkpoint.activePlan.length).toBe(3);

    // Verify skills were autonomously invoked and recorded
    expect(progressLogs.some((l) => l.includes('Autonomous Skill') || l.includes('Skill:'))).toBe(true);
    expect(result.output).toContain('Skill: Environment Sandbox Doctor');
    expect(result.checkpoint.pinnedFacts['env.health']).toBeDefined();

    runtime.close();
  });

  it('projects agent skills into ContextEnvelope tool catalog and system prompt', () => {
    const envelope = new ContextEnvelope({
      sessionId: 'test-session',
      workspacePath: cwd,
    });

    const tools = envelope.getProjectedTools('anthropic');
    const toolNames = tools.map((t: any) => t.name);

    expect(toolNames).toContain('env_doctor');
    expect(toolNames).toContain('semantic_graph');
    expect(toolNames).toContain('device_mesh_connect');
    expect(toolNames).toContain('code_mode');
    expect(toolNames).toContain('hypothesis_race');

    const prompt = envelope.compileSystemPrompt();
    expect(prompt).toContain('<AGENT_SKILLS_DIRECTORY>');
    expect(prompt).toContain('env_doctor');
    expect(prompt).toContain('semantic_graph');
  });
});
