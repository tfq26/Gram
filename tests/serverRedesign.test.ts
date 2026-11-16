import { describe, it, expect, afterAll, spyOn } from 'bun:test';
import { server } from '../src/server/index';

describe('Gram v2 Web Interface Redesign (3-Step Developer Environment)', () => {
  afterAll(() => {
    server.stop();
  });

  it('serves the redesigned 3-step web interface at /', async () => {
    const res = await server.fetch(new Request('http://localhost/'));
    expect(res.status).toBe(200);
    const html = await res.text();

    // When the studio production build exists, the server serves the compiled React entry.
    if (html.includes('<div id="root"></div>')) {
      expect(html).toContain('/assets/index-');
      return;
    }

    // 1. Step 1: Choose a project (Compact Header & Collapsible Sidebar)
    expect(html).toContain('class="app-header"');
    expect(html).toContain('id="project-pill"');
    expect(html).toContain('id="vcs-badge"');
    expect(html).toContain('id="app-sidebar"');
    expect(html).toContain('class="new-task-btn"');
    expect(html).toContain('id="recent-tasks-list"');

    // 2. Step 2: Give Gram a task (Conversation as Main View)
    expect(html).toContain('class="app-conversation"');
    expect(html).toContain('class="conversation-timeline"');
    expect(html).toContain('class="fresh-task-hero"');
    expect(html).toContain('class="composer-card"');
    expect(html).toContain('id="composer-input"');
    expect(html).toContain('id="model-select"');
    expect(html).toContain('id="submit-btn"');
    expect(html).toContain('id="stop-btn"');

    // 3. Step 3: Review what it did (Collapsible Review Work Area)
    expect(html).toContain('id="app-work-area"');
    expect(html).toContain('id="tab-btn-changes"');
    expect(html).toContain('id="tab-btn-preview"');
    expect(html).toContain('id="tab-btn-files"');
    expect(html).toContain('id="changes-list"');
    expect(html).toContain('id="files-tree-container"');

    // 4. Details moved out of default view (Compact Activity Strip & Expandable Logs Drawer)
    expect(html).toContain('class="app-activity-strip"');
    expect(html).toContain('id="app-logs-drawer"');
    expect(html).toContain('class="logs-drawer-toggle"');

    // 5. Linear-inspired design.md layout elements
    expect(html).toContain('class="workspace-switcher"');
    expect(html).toContain('id="starter-section"');
    expect(html).toContain('class="starter-grid"');
    expect(html).toContain('class="skills-pill-btn"');

    // 6. Responsive narrow screen support
    expect(html).toContain('@media (max-width: 900px)');

    // 7. Anti-Vibe-Coding Constitution compliance & no blue focus outline box
    expect(html).not.toContain('outline: 2px solid var(--accent)');
    expect(html).not.toContain('alert(');
    expect(html).not.toContain('confirm(');
    expect(html).not.toContain('scale(1.04)');
    expect(/[\u{1F300}-\u{1F9FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/u.test(html)).toBe(false);
  });

  it('provides workspace metadata via /api/workspace', async () => {
    const res = await server.fetch(new Request('http://localhost/api/workspace'));
    expect(res.status).toBe(200);
    const data = await res.json();
    expect(data.name).toBe('Gram');
    expect(data.path).toContain('Gram');
    expect(data.vcs).toBeDefined();
  });

  it('submits a fresh task via /api/run and retrieves checkpoint', async () => {
    const prompt = 'Audit project configuration and verify file integrity';
    const runRes = await server.fetch(
      new Request('http://localhost/api/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ prompt, modelId: 'us.amazon.nova-2-pro-v1:0' }),
      })
    );
    expect(runRes.status).toBe(200);
    const runData = await runRes.json();
    expect(runData.runId).toBeDefined();
    expect(runData.checkpoint).toBeDefined();
    expect(runData.completed).toBe(true);

    // Verify recent tasks list
    const tasksRes = await server.fetch(new Request('http://localhost/api/tasks'));
    const tasks = await tasksRes.json();
    expect(Array.isArray(tasks)).toBe(true);
    expect(tasks.length).toBeGreaterThan(0);
    expect(tasks[0].runId).toBe(runData.runId);

    // Verify individual task checkpoint
    const taskRes = await server.fetch(new Request(`http://localhost/api/task?runId=${runData.runId}`));
    const taskCheckpoint = await taskRes.json();
    expect(taskCheckpoint.runId).toBe(runData.runId);
    expect(taskCheckpoint.stage).toBe('completed');
  });

  it('supports the review work area tools (Changes, Diff, Tree, Read)', async () => {
    // 1. Changes
    const changesRes = await server.fetch(new Request('http://localhost/api/files/changes'));
    expect(changesRes.status).toBe(200);
    const changes = await changesRes.json();
    expect(Array.isArray(changes)).toBe(true);

    // 2. Diff
    const diffRes = await server.fetch(new Request('http://localhost/api/diff?path=package.json'));
    expect(diffRes.status).toBe(200);
    const diff = await diffRes.json();
    expect(diff.path).toBe('package.json');
    expect(diff.original).toContain('gram');

    // 3. File Tree
    const treeRes = await server.fetch(new Request('http://localhost/api/files/tree'));
    expect(treeRes.status).toBe(200);
    const tree = await treeRes.json();
    expect(Array.isArray(tree)).toBe(true);
    expect(tree.some((n: any) => n.name === 'package.json')).toBe(true);

    // 4. File Read
    const readRes = await server.fetch(new Request('http://localhost/api/files/read?path=package.json'));
    expect(readRes.status).toBe(200);
    const file = await readRes.json();
    expect(file.content).toContain('gram');
  });

  it('supports task cancellation endpoint /api/cancel', async () => {
    const cancelRes = await server.fetch(
      new Request('http://localhost/api/cancel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runId: 'non-existent-run' }),
      })
    );
    expect(cancelRes.status).toBe(200);
    const resData = await cancelRes.json();
    expect(resData.cancelled).toBe(false);
  });

  it('transcribes uploaded audio through the AssemblyAI endpoint', async () => {
    const originalKey = process.env.ASSEMBLYAI_API_KEY;
    process.env.ASSEMBLYAI_API_KEY = 'test-assembly-key';
    const fetchSpy = spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ upload_url: 'https://uploads.example/audio' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'transcript-1', status: 'queued' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'transcript-1', status: 'completed', text: 'Hello Gram.' }), { status: 200 }));
    try {
      const response = await server.fetch(new Request('http://localhost/api/voice/transcribe', {
        method: 'POST', headers: { 'Content-Type': 'audio/webm' }, body: new Uint8Array([1, 2, 3]),
      }));
      expect(response.status).toBe(200);
      expect(await response.json()).toEqual({ text: 'Hello Gram.' });
      expect(fetchSpy).toHaveBeenCalledTimes(3);
    } finally {
      fetchSpy.mockRestore();
      if (originalKey === undefined) delete process.env.ASSEMBLYAI_API_KEY;
      else process.env.ASSEMBLYAI_API_KEY = originalKey;
    }
  });

  it('streams ElevenLabs audio through the speech endpoint', async () => {
    const oldKey = process.env.ELEVENLABS_API_KEY;
    const oldVoice = process.env.ELEVENLABS_VOICE_ID;
    const oldModel = process.env.ELEVENLABS_MODEL_ID;
    process.env.ELEVENLABS_API_KEY = 'test-eleven-key';
    process.env.ELEVENLABS_VOICE_ID = 'test-voice';
    process.env.ELEVENLABS_MODEL_ID = 'eleven_flash_v2_5';
    const fetchSpy = spyOn(globalThis, 'fetch').mockResolvedValue(new Response(new Uint8Array([7, 8, 9]), {
      status: 200, headers: { 'Content-Type': 'audio/mpeg' },
    }));
    try {
      const response = await server.fetch(new Request('http://localhost/api/voice/speak', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text: 'Hello there.' }),
      }));
      expect(response.status).toBe(200);
      expect(response.headers.get('Content-Type')).toBe('audio/mpeg');
      expect([...new Uint8Array(await response.arrayBuffer())]).toEqual([7, 8, 9]);
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    } finally {
      fetchSpy.mockRestore();
      if (oldKey === undefined) delete process.env.ELEVENLABS_API_KEY; else process.env.ELEVENLABS_API_KEY = oldKey;
      if (oldVoice === undefined) delete process.env.ELEVENLABS_VOICE_ID; else process.env.ELEVENLABS_VOICE_ID = oldVoice;
      if (oldModel === undefined) delete process.env.ELEVENLABS_MODEL_ID; else process.env.ELEVENLABS_MODEL_ID = oldModel;
    }
  });

  it('normalizes Lyra AHP stream events into Gram SSE events', async () => {
    const oldHubUrl = process.env.LYRA_HUB_URL;
    process.env.LYRA_HUB_URL = 'https://lyra.example';
    const body = [
      'event: chunk\ndata: {"chunk":"Lyra says hi."}\n\n',
      'event: done\ndata: {"sessionId":"cloud-session","content":"Lyra says hi."}\n\n',
    ].join('');
    const fetchSpy = spyOn(globalThis, 'fetch').mockResolvedValue(new Response(body, {
      status: 200, headers: { 'Content-Type': 'text/event-stream' },
    }));
    try {
      const response = await server.fetch(new Request('http://localhost/api/lyra/chat/stream', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ prompt: 'Hello', sessionId: 'local-session' }),
      }));
      expect(response.status).toBe(200);
      const streamText = await response.text();
      expect(streamText).toContain('"type":"start"');
      expect(streamText).toContain('"type":"text_delta","delta":"Lyra says hi."');
      expect(streamText).toContain('"type":"done"');
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    } finally {
      fetchSpy.mockRestore();
      if (oldHubUrl === undefined) delete process.env.LYRA_HUB_URL; else process.env.LYRA_HUB_URL = oldHubUrl;
    }
  });

  it('pulls a completed Lyra diff into a dedicated review overlay', async () => {
    const oldHubUrl = process.env.LYRA_HUB_URL;
    process.env.LYRA_HUB_URL = 'https://lyra.example';
    const diff = [
      'diff --git a/lyra_import_test.txt b/lyra_import_test.txt',
      'new file mode 100644',
      '--- /dev/null',
      '+++ b/lyra_import_test.txt',
      '@@ -0,0 +1 @@',
      '+pulled from Lyra',
    ].join('\n');
    const fetchSpy = spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      runId: 'run-harness-test1', stage: 'completed', state: 'completed',
      diffs: [{ filename: 'lyra_import_test.txt', patch: diff }],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    try {
      const response = await server.fetch(new Request('http://localhost/api/lyra/swarm/pull', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runId: 'run-harness-test1', sessionId: 'cloud-session' }),
      }));
      expect(response.status).toBe(200);
      const result = await response.json();
      expect(result.layerId).toBe('lyra-run-harness-test1');
      expect(result.diffs[0].path).toBe('lyra_import_test.txt');
      expect(result.diffs[0].patch).toContain('+pulled from Lyra');
      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const rollback = await server.fetch(new Request('http://localhost/api/lyra/swarm/layer/rollback', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ layerId: result.layerId }),
      }));
      expect(rollback.status).toBe(200);
    } finally {
      fetchSpy.mockRestore();
      if (oldHubUrl === undefined) delete process.env.LYRA_HUB_URL; else process.env.LYRA_HUB_URL = oldHubUrl;
    }
  });

  it('validates and accepts safe URLs on /api/open-external while rejecting unsafe protocols', async () => {
    const okRes = await server.fetch(new Request('http://localhost/api/open-external', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url: 'https://example.com/docs' }),
    }));
    expect(okRes.status).toBe(200);
    const okBody = await okRes.json();
    expect(okBody.opened).toBe(true);
    expect(okBody.url).toBe('https://example.com/docs');

    const badRes = await server.fetch(new Request('http://localhost/api/open-external', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ url: 'javascript:alert(1)' }),
    }));
    expect(badRes.status).toBe(400);
  });
});
