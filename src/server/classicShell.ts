/**
 * Classic 3-pane HTML fallback shell served when dist/studio/index.html is not present.
 */

export function getAppHtml(): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Gram</title>
  <link rel="icon" type="image/svg+xml" href="/icons/gram-logo.svg">
  <link rel="stylesheet" href="/shadcn.css">
  <link rel="stylesheet" href="/app.css">
  <link rel="stylesheet" href="/v2-adapter.css">

</head>
<body>
<div class="app-shell" data-theme="obsidian">
  <header class="topbar">
    <div class="topbar-left">
      <a class="brand" href="/" aria-label="Gram home">
        <span>Gram</span>
      </a>
      <nav class="topbar-nav">
        <a class="nav-link active" href="/" onclick="return false;">Chat</a>
        <a class="nav-link" href="/settings">Settings</a>
      </nav>
      <div class="topbar-tabs-container">
        <div class="topbar-divider"></div>
        <div class="topbar-tabs-list" id="topbar-chat-tabs"></div>
          <button class="btn-new-tab" id="btn-new-chat-tab" type="button" title="Open new chat tab" aria-label="Open new chat tab" onclick="openNewChatTab()">
            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>
          </button>
      </div>
    </div>
    <div class="topbar-center"></div>
    <div class="topbar-right">
      <select id="project-select" class="topbar-project-select" aria-label="Active workspace" onchange="selectActiveProject(this.value)">
        <option value="local">Local workspace</option>
      </select>
    </div>
  </header>

  <main class="page-frame">
    <div class="workspace-panes left-open" id="workspace-panes">
      <aside class="pane pane-left open" id="app-sidebar">
        <div class="pane-content">
          <div class="rail-header">
            <span class="rail-title">Previous chats</span>
            <button class="btn-pane-toggle" type="button" title="Close chats" aria-label="Close chats" onclick="toggleSidebar()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 3v18"/></svg>
            </button>
          </div>
          <button class="btn-new-chat" type="button" onclick="openNewChatTab()">+ New chat</button>
          <div class="conversation-list" id="recent-tasks-list">
            <div class="sidebar-empty-state">Loading chats...</div>
          </div>
          <div class="rail-footer">
            <div style="position:relative;width:100%">
              <button class="user-pill" type="button" onclick="toggleUserMenu()">
                <span class="shadcn-avatar shadcn-avatar-sm"><span class="shadcn-avatar-fallback" id="user-avatar-initial">G</span></span>
                <span style="display:flex;flex-direction:column;align-items:flex-start;min-width:0;flex:1">
                  <span class="user-name" id="user-display-name">Local session</span>
                  <span class="user-email" id="user-display-email">Sign in to sync with Lyra</span>
                </span>
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"/></svg>
              </button>
            <div class="shadcn-context-menu-backdrop" id="user-menu-backdrop" style="display:none" onclick="toggleUserMenu()"></div>
              <div class="user-profile-popover" id="user-profile-popover" style="display:none;position:absolute;left:10px;bottom:calc(100% + 6px);width:210px;background:var(--bg-panel);border:1px solid var(--border-color);border-radius:var(--radius-md);padding:6px;box-shadow:var(--shadow-md);z-index:9999;flex-direction:column;gap:2px">
                <div class="user-profile-popover-header" style="padding:8px 10px;border-bottom:1px solid var(--border-color);margin-bottom:4px;display:flex;flex-direction:column"><strong id="popover-user-name">Local session</strong><span id="popover-user-email">Not signed in</span></div>
                <a class="shadcn-context-menu-item" href="/settings" onclick="toggleUserMenu()">Settings</a>
                <div class="shadcn-context-menu-separator"></div>
                <button class="shadcn-context-menu-item" id="auth-action-btn" type="button" onclick="handleAuthAction()">Sign in</button>
              </div>
            </div>
          </div>
        </div>
      </aside>

      <main class="pane pane-center" id="app-conversation">
        <header class="chat-header">
          <div class="chat-header-left">
            <button class="btn-pane-toggle show-chats-toggle" type="button" title="Show chats" aria-label="Show chats" onclick="toggleSidebar()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M9 3v18"/></svg>
            </button>
            <div class="model-dropdown-wrapper">
              <button type="button" class="model-dropdown-trigger" id="model-select-trigger" title="Switch AI model" onclick="toggleModelMenu()">
                <span class="model-icon-wrap"><img id="model-select-icon" src="/logo/openrouter-color.svg" alt="Auto" width="14" height="14"></span>
                <span class="model-trigger-name" id="model-select-label">Auto</span>
                <svg class="model-dropdown-chevron" width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>
              </button>
              <select id="model-select" style="display:none" aria-hidden="true" onchange="syncModelSelection(this.value)">
                <option value="auto">Auto</option>
              </select>
              <div class="model-dropdown-backdrop" id="model-menu-backdrop" style="display:none" onclick="closeModelMenu()"></div>
              <div class="model-dropdown-menu" id="model-menu" style="display:none"></div>
            </div>
          </div>
        </header>

        <div class="messages-container" id="conversation-timeline">
          <div class="harness-empty-state" id="fresh-hero">
            <h2>How can I help?</h2>
            <p>Type a message or tap the microphone to speak.</p>
          </div>
          <div id="dynamic-messages" class="v2-message-list"></div>
        </div>

        <div class="composer-wrapper" id="bottom-composer-dock">
          <div class="composer-dock-row">
            <div class="composer-box" id="hero-composer-card">
              <input id="attachment-input" type="file" multiple hidden onchange="handleAttachments(this.files)">
              <div id="attachment-tray" class="attachment-tray" style="display:none"></div>
              <div class="voice-banner user-voice" id="voice-banner" style="display:none">
                <div class="voice-info">
                  <div class="visualizer-bars" id="visualizerBars">
                    <span class="v-bar"></span><span class="v-bar"></span><span class="v-bar"></span><span class="v-bar"></span>
                    <span class="v-bar"></span><span class="v-bar"></span><span class="v-bar"></span><span class="v-bar"></span>
                    <span class="v-bar"></span><span class="v-bar"></span><span class="v-bar"></span><span class="v-bar"></span>
                  </div>
                  <span class="voice-label" id="voice-label">Listening to your voice...</span>
                </div>
                <button class="btn-stop-voice" type="button" onclick="stopVoice()">Stop</button>
              </div>
              <div class="composer-inner">
                <div class="composer-actions-left">
                  <button class="btn-composer-icon" type="button" title="Attach files" aria-label="Attach files" onclick="document.getElementById('attachment-input').click()">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 5v14M5 12h14"/></svg>
                  </button>
                </div>
                <textarea class="composer-textarea" id="composer-input" rows="1" placeholder="Message Gram (type @ for context mentions)..." onkeydown="handleComposerKeyDown(event)" oninput="autoResizeComposer(this)"></textarea>
                <div class="composer-actions-right">
                  <button class="btn-composer-icon voice-btn" id="voice-input-btn" type="button" title="Voice input" aria-label="Voice input" onclick="toggleVoice()">
                    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="2" width="6" height="13" rx="3"/><path d="M5 10v2a7 7 0 0 0 14 0v-2M12 19v3m-4 0h8"/></svg>
                  </button>
                  <button class="btn-composer-send" id="submit-btn" type="button" title="Send" aria-label="Send" onclick="submitTask()">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5m-7 7 7-7 7 7"/></svg>
                  </button>
                  <button class="btn-composer-send stop-btn" id="stop-btn" type="button" title="Stop generation" aria-label="Stop generation" style="display:none" onclick="stopActiveTask()">
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </main>

      <!-- V2 right-side panels are intentionally omitted on the chat page. See docs/v2-feature-review.md. -->
    </div>
  </main>

  <div id="common-modal" class="modal-overlay" onclick="if(event.target===this) closeModal()">
    <div class="modal-card">
      <div class="modal-header"><span id="modal-title">Details</span><button type="button" class="btn-pane-toggle" onclick="closeModal()">Close</button></div>
      <div id="modal-body" class="modal-body"></div>
    </div>
  </div>
</div>
  <script src="/audio.js"></script>
  <script>
    let activeRunId = null;
    let activeSessionId = 'session-' + Date.now();
    let activeChatTabId = 'chat-' + Date.now();
    let openChatTabs = [{ id: activeChatTabId, title: 'New chat', runId: null, sessionId: activeSessionId, modelId: 'auto' }];
    let isExecuting = false;
    let recentTasks = [];
    let currentChatTitle = 'New chat';
    let pendingRun = null;
    let pendingDeleteRunId = null;
    let lastRunLatencyMs = 0;
    let attachedFiles = [];
    let voiceDraftBase = '';
    let voiceSilenceTimer = null;
    let activeExecutionMode = 'auto';
    let activeProjectId = 'local';
    let lyraAuthState = { connected: false, authenticated: false, user: null };
    let availableModels = [];

    async function initWorkspace() {
      try {
        const res = await fetch('/api/workspace');
        const data = await res.json();
        const workspaceName = document.getElementById('workspace-name');
        if (workspaceName) workspaceName.textContent = data.name;
      } catch {
        const workspaceName = document.getElementById('workspace-name');
        if (workspaceName) workspaceName.textContent = 'Gram';
      }
      await loadModels();
      await initAuthState();
      renderTopbarTabs();
      loadRecentTasks();
      const savedModel = localStorage.getItem('gram.settings.defaultModel');
      syncHeaderModel(savedModel || 'auto');
      document.querySelector('.app-shell').dataset.theme = localStorage.getItem('gram.settings.theme') || 'carbon';
      updateTelemetry();
    }

    async function loadModels() {
      try {
        const res = await fetch('/api/models');
        const data = await res.json();
        availableModels = Array.isArray(data.models) ? data.models : [];
      } catch {
        availableModels = [];
      }
      const selectEl = document.getElementById('model-select');
      const menuEl = document.getElementById('model-menu');
      if (!selectEl || !menuEl) return;

      const allItems = [
        { id: 'auto', name: 'Auto', provider: 'bedrock + openrouter', family: 'openrouter', inputPricePerM: null, outputPricePerM: null },
        ...availableModels,
      ];

      selectEl.innerHTML = allItems.map(m => \`<option value="\${escapeHtml(m.id)}">\${escapeHtml(m.name)}</option>\`).join('');
      menuEl.innerHTML = allItems.map(m => {
        const subtitle = m.id === 'auto'
          ? 'Automatic routing across Bedrock and OpenRouter'
          : \`\${m.provider} · \${m.id}\`;
        const warningHtml = m.contributorWarning
          ? \`<span class="model-warning-wrap" onclick="event.stopPropagation()" aria-label="\${escapeHtml(m.contributorWarning)}"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg><span class="model-warning-tooltip">\${escapeHtml(m.contributorWarning)}</span></span>\`
          : '';
        return \`<button type="button" class="model-dropdown-item" data-model-id="\${escapeHtml(m.id)}" onclick="selectModel('\${escapeHtml(m.id)}')"><div class="model-item-info"><img src="\${getModelLogoPath(m.id)}" alt="" width="14" height="14"><div class="model-item-text"><span class="model-item-label">\${escapeHtml(m.name)}</span><span class="model-item-vendor">\${escapeHtml(subtitle)}</span></div>\${warningHtml}</div></button>\`;
      }).join('');
    }

    async function initAuthState() {
      try {
        const res = await fetch('/api/lyra/auth/state');
        lyraAuthState = await res.json();
      } catch {
        lyraAuthState = { connected: false, authenticated: false, user: null };
      }
      const nameEl = document.getElementById('user-display-name');
      const emailEl = document.getElementById('user-display-email');
      const popNameEl = document.getElementById('popover-user-name');
      const popEmailEl = document.getElementById('popover-user-email');
      const avatarEl = document.getElementById('user-avatar-initial');
      const authBtn = document.getElementById('auth-action-btn');

      if (lyraAuthState.authenticated && lyraAuthState.user) {
        const name = lyraAuthState.user.name || lyraAuthState.user.email;
        if (nameEl) nameEl.textContent = name;
        if (emailEl) emailEl.textContent = lyraAuthState.user.email;
        if (popNameEl) popNameEl.textContent = name;
        if (popEmailEl) popEmailEl.textContent = lyraAuthState.user.email;
        if (avatarEl) avatarEl.textContent = name.charAt(0).toUpperCase();
        if (authBtn) authBtn.textContent = 'Sign out';
        loadLyraProjects();
      } else {
        const statusText = lyraAuthState.connected ? 'Sign in to sync with Lyra' : 'Local session (Lyra offline)';
        if (nameEl) nameEl.textContent = 'Local session';
        if (emailEl) emailEl.textContent = statusText;
        if (popNameEl) popNameEl.textContent = 'Local session';
        if (popEmailEl) popEmailEl.textContent = statusText;
        if (avatarEl) avatarEl.textContent = 'G';
        if (authBtn) authBtn.textContent = 'Sign in';
      }
    }

    async function loadLyraProjects() {
      const sel = document.getElementById('project-select');
      if (!sel) return;
      try {
        const res = await fetch('/api/lyra/sessions');
        if (!res.ok) return;
        const sessions = await res.json();
        if (Array.isArray(sessions) && sessions.length > 0) {
          const seen = new Set(['local']);
          const opts = ['<option value="local">Local workspace</option>'];
          for (const s of sessions.slice(0, 15)) {
            const label = s.title || s.workspacePath || s.sessionId;
            if (!seen.has(s.sessionId)) {
              seen.add(s.sessionId);
              opts.push(\`<option value="\${escapeHtml(s.sessionId)}">\${escapeHtml(label)}</option>\`);
            }
          }
          sel.innerHTML = opts.join('');
        }
      } catch {}
    }

    function selectActiveProject(val) {
      activeProjectId = val || 'local';
    }

    function handleAuthAction() {
      toggleUserMenu();
      if (lyraAuthState.authenticated) {
        fetch('/api/lyra/auth/sign-out', { method: 'POST' }).then(() => initAuthState());
        return;
      }
      openModal('Sign in to Lyra', \`
        <form onsubmit="submitLyraSignIn(event)">
          <div class="auth-form-row">
            <label for="lyra-login-email">Email</label>
            <input id="lyra-login-email" type="email" required placeholder="you@company.com" />
          </div>
          <div class="auth-form-row">
            <label for="lyra-login-password">Password</label>
            <input id="lyra-login-password" type="password" required placeholder="Password" />
          </div>
          <div id="lyra-login-error" style="color:var(--danger);font-size:12px;margin-bottom:8px;display:none"></div>
          <div class="result-actions">
            <button class="result-action-btn" type="submit">Sign in</button>
          </div>
        </form>
      \`);
    }

    async function submitLyraSignIn(e) {
      e.preventDefault();
      const email = document.getElementById('lyra-login-email').value;
      const password = document.getElementById('lyra-login-password').value;
      const errEl = document.getElementById('lyra-login-error');
      try {
        const res = await fetch('/api/lyra/auth/sign-in', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email, password })
        });
        if (!res.ok) {
          const data = await res.json().catch(() => ({}));
          errEl.textContent = data.message || data.error || 'Sign in failed';
          errEl.style.display = 'block';
          return;
        }
        closeModal();
        await initAuthState();
      } catch (err) {
        errEl.textContent = err.message || 'Sign in failed';
        errEl.style.display = 'block';
      }
    }

    function toggleSidebar() {
      const sidebar = document.getElementById('app-sidebar');
      const panes = document.getElementById('workspace-panes');
      const isOpen = sidebar.classList.toggle('open');
      sidebar.classList.toggle('closed', !isOpen);
      panes.classList.toggle('left-open', isOpen);
      panes.classList.toggle('left-closed', !isOpen);
    }

    function toggleUserMenu() {
      const menu = document.getElementById('user-profile-popover');
      const backdrop = document.getElementById('user-menu-backdrop');
      const open = menu?.style.display !== 'flex';
      if (menu) menu.style.display = open ? 'flex' : 'none';
      if (backdrop) backdrop.style.display = open ? 'block' : 'none';
    }

    function syncModelSelection(modelId) {
      const headerSelect = document.getElementById('model-select');
      if (headerSelect) headerSelect.value = modelId;
      const selectedOption = headerSelect?.querySelector('option:checked');
      const selectedName = selectedOption?.textContent || modelId;
      const selectedLabel = document.getElementById('model-select-label');
      const selectedIcon = document.getElementById('model-select-icon');
      if (selectedLabel) selectedLabel.textContent = selectedName;
      if (selectedIcon) {
        selectedIcon.src = getModelLogoPath(modelId);
        selectedIcon.alt = selectedName;
      }
      document.querySelectorAll('.model-dropdown-item').forEach(item => {
        item.classList.toggle('active', item.getAttribute('data-model-id') === modelId);
      });
      const activeTab = openChatTabs.find(tab => tab.id === activeChatTabId);
      if (activeTab) activeTab.modelId = modelId;
      updateTelemetry();
    }

    function syncHeaderModel(modelId) {
      const headerSelect = document.getElementById('model-select');
      if (headerSelect) headerSelect.value = modelId;
      syncModelSelection(modelId);
    }

    function getModelLogoPath(modelId) {
      const key = (modelId || '').toLowerCase();
      if (key.includes('anthropic') || key.includes('claude')) return '/logo/anthropic.svg';
      if (key.includes('openai') || key.includes('gpt-')) return '/logo/openai-light.svg';
      if (key.includes('deepseek')) return '/logo/Deepseek-logo-icon.svg.webp';
      if (key.includes('google') || key.includes('gemini')) return '/logo/gemini.svg';
      if (key.includes('meta') || key.includes('llama') || key.includes('muse')) return '/logo/meta-12361.svg';
      if (key.includes('x-ai') || key.includes('xai') || key.includes('grok')) return '/logo/XAI_Logo.svg.webp';
      return '/logo/openrouter-color.svg';
    }

    function toggleModelMenu() {
      const menu = document.getElementById('model-menu');
      const backdrop = document.getElementById('model-menu-backdrop');
      const open = menu.style.display !== 'flex';
      menu.style.display = open ? 'flex' : 'none';
      backdrop.style.display = open ? 'block' : 'none';
      document.querySelector('.model-dropdown-chevron')?.classList.toggle('open', open);
    }

    function closeModelMenu() {
      const menu = document.getElementById('model-menu');
      const backdrop = document.getElementById('model-menu-backdrop');
      if (menu) menu.style.display = 'none';
      if (backdrop) backdrop.style.display = 'none';
      document.querySelector('.model-dropdown-chevron')?.classList.remove('open');
    }

    function selectModel(modelId) {
      syncModelSelection(modelId);
      closeModelMenu();
    }

    function updateChatTitle(value) {
      currentChatTitle = value.trim() || 'New chat';
      const activeTab = openChatTabs.find(tab => tab.id === activeChatTabId);
      if (activeTab) activeTab.title = currentChatTitle;
      renderTopbarTabs();
    }

    function getAssistantName() {
      return localStorage.getItem('gram.settings.assistantName') || 'Gram';
    }

    function renderTopbarTabs() {
      const container = document.getElementById('topbar-chat-tabs');
      if (!container) return;
      container.innerHTML = '';
      openChatTabs.forEach(tab => {
        const item = document.createElement('div');
        item.className = 'topbar-tab' + (tab.id === activeChatTabId ? ' active' : '');
        item.title = tab.title;
        item.onclick = () => activateChatTab(tab.id);
        const title = document.createElement('span');
        title.className = 'topbar-tab-title';
        title.textContent = tab.title;
        item.appendChild(title);
        if (openChatTabs.length > 1) {
          const close = document.createElement('button');
          close.className = 'topbar-tab-close';
          close.type = 'button';
          close.title = 'Close tab';
          close.textContent = '×';
          close.onclick = event => { event.stopPropagation(); closeChatTab(tab.id); };
          item.appendChild(close);
        }
        container.appendChild(item);
      });
    }

    function openNewChatTab() {
      const tab = { id: 'chat-' + Date.now(), title: 'New chat', runId: null, sessionId: 'session-' + Date.now(), modelId: document.getElementById('model-select').value };
      openChatTabs.push(tab);
      activeChatTabId = tab.id;
      activeSessionId = tab.sessionId;
      if (tab.modelId) syncModelSelection(tab.modelId);
      renderTopbarTabs();
      startNewTask();
    }

    function activateChatTab(tabId) {
      const tab = openChatTabs.find(item => item.id === tabId);
      if (!tab) return;
      activeChatTabId = tab.id;
      activeSessionId = tab.sessionId;
      if (tab.modelId) syncModelSelection(tab.modelId);
      renderTopbarTabs();
      if (tab.runId) {
        selectTask(tab.runId);
        return;
      }
      activeRunId = null;
      document.getElementById('fresh-hero').style.display = 'flex';
      document.getElementById('dynamic-messages').innerHTML = '';
      document.getElementById('composer-input').value = '';
      attachedFiles = tab.attachedFiles || [];
      renderAttachments();
      updateChatTitle(tab.title);
      updateTelemetry();
    }

    function closeChatTab(tabId) {
      if (openChatTabs.length <= 1) return;
      const wasActive = activeChatTabId === tabId;
      const index = openChatTabs.findIndex(tab => tab.id === tabId);
      openChatTabs = openChatTabs.filter(tab => tab.id !== tabId);
      if (wasActive) {
        const next = openChatTabs[Math.min(index, openChatTabs.length - 1)];
        activateChatTab(next.id);
      } else {
        renderTopbarTabs();
      }
    }

    function openDeferredFeature(feature) {
      openModal(feature, '<p>This V2 feature is parked while the original conversation interface is restored. See <code>docs/v2-feature-review.md</code> for its current status.</p>');
    }

    function updateTelemetry(outputText = '', promptText = null) {
      const userText = promptText ?? document.getElementById('composer-input')?.value ?? '';
      const attachmentLength = attachedFiles.reduce((total, file) => total + file.content.length, 0);
      const promptTokens = Math.max(0, Math.ceil((userText.length + attachmentLength + (localStorage.getItem('gram.settings.systemPrompt') || '').length + (localStorage.getItem('gram.settings.rules') || '').length) / 4));
      const completionTokens = Math.ceil(outputText.length / 4);
      const totalTokens = promptTokens + completionTokens;
      const modelId = document.getElementById('model-select')?.value;
      const contextWindow = ({
        'us.anthropic.claude-sonnet-5': 200000,
        'us.amazon.nova-pro-v1:0': 300000,
        'us.amazon.nova-lite-v1:0': 300000,
        'gpt-4o': 128000,
        'gemini-2.5-flash': 1000000,
        'ollama/qwen3-coder': 64000,
      })[modelId] || 128000;
      const promptElement = document.getElementById('prompt-token-count');
      const completionElement = document.getElementById('completion-token-count');
      const totalElement = document.getElementById('total-token-count');
      const latencyElement = document.getElementById('latency-value');
      if (promptElement) promptElement.textContent = '~' + promptTokens.toLocaleString();
      if (completionElement) completionElement.textContent = completionTokens.toLocaleString();
      if (totalElement) totalElement.textContent = '~' + totalTokens.toLocaleString();
      if (latencyElement) latencyElement.textContent = lastRunLatencyMs ? lastRunLatencyMs + 'ms' : 'Instant';
      const percent = Math.min(100, Math.max(1, Math.round(totalTokens / contextWindow * 100)));
      const meter = document.getElementById('context-meter-fill');
      const label = document.getElementById('context-meter-label');
      if (meter) meter.style.width = percent + '%';
      const contextLabel = contextWindow >= 1000000 ? (contextWindow / 1000000) + 'M' : (contextWindow / 1000) + 'k';
      if (label) label.textContent = percent + '% (' + totalTokens.toLocaleString() + ' / ' + contextLabel + ')';
    }

    async function loadRecentTasks() {
      try {
        const res = await fetch('/api/tasks');
        const tasks = await res.json();
        recentTasks = tasks;
        renderSidebarTasks(tasks);
      } catch {
        document.getElementById('recent-tasks-list').innerHTML = '<div class="sidebar-empty-state">Unable to load tasks.</div>';
      }
    }

    function renderSidebarTasks(tasks) {
      const container = document.getElementById('recent-tasks-list');
      if (!tasks || tasks.length === 0) {
        container.innerHTML = '<div class="sidebar-empty-state">Your previous chats will appear here.</div>';
        return;
      }
      container.innerHTML = tasks.map(t => {
        const dateLabel = formatChatDate(t.updatedAt);
        return \`
          <div class="conversation-item \${activeRunId === t.runId ? 'active' : ''}" onclick="selectTask('\${t.runId}')">
            <div class="conv-meta">
              <span class="conv-title">\${escapeHtml(t.title || t.runId)}</span>
              <span class="conv-time">\${dateLabel}</span>
            </div>
            \${tasks.length > 1 ? \`<button class="conv-delete" type="button" title="Delete" aria-label="Delete conversation" onclick="event.stopPropagation(); deleteConversation('\${t.runId}')">×</button>\` : ''}
          </div>
        \`;
      }).join('');
    }

    function formatChatDate(value) {
      if (!value) return '';
      return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
    }

    function deleteConversation(runId) {
      if (recentTasks.length <= 1) return;
      const task = recentTasks.find(item => item.runId === runId);
      pendingDeleteRunId = runId;
      openModal('Delete conversation?', \`
        <p>Remove <strong>\${escapeHtml(task?.title || runId)}</strong> from previous chats? Any staged workspace changes remain in the virtual overlay.</p>
        <div class="result-actions">
          <button class="btn-secondary" type="button" onclick="pendingDeleteRunId=null; closeModal()">Cancel</button>
          <button class="btn-primary" type="button" onclick="confirmDeleteConversation()">Delete</button>
        </div>
      \`);
    }

    async function confirmDeleteConversation() {
      const runId = pendingDeleteRunId;
      const task = recentTasks.find(item => item.runId === runId);
      if (!runId || !task) return closeModal();
      try {
        await fetch('/api/task/delete', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: task.sessionId, runId }),
        });
        recentTasks = recentTasks.filter(item => item.runId !== runId);
        const wasActive = activeRunId === runId;
        openChatTabs = openChatTabs.filter(tab => tab.runId !== runId);
        if (!openChatTabs.length) {
          const newTab = { id: 'chat-' + Date.now(), title: 'New chat', runId: null, sessionId: 'session-' + Date.now() };
          openChatTabs = [newTab];
          activeChatTabId = newTab.id;
          activeSessionId = newTab.sessionId;
          activeRunId = null;
          document.getElementById('dynamic-messages').innerHTML = '';
          document.getElementById('fresh-hero').style.display = 'flex';
          updateChatTitle('New chat');
        } else if (wasActive) {
          activateChatTab(openChatTabs[0].id);
        } else {
          renderTopbarTabs();
        }
        renderSidebarTasks(recentTasks);
      } catch (error) {
        openModal('Delete failed', escapeHtml(error.message));
        return;
      }
      pendingDeleteRunId = null;
      closeModal();
    }

    function filterSidebarTasks(query) {
      if (!query.trim()) {
        renderSidebarTasks(recentTasks);
        return;
      }
      const q = query.toLowerCase();
      const filtered = recentTasks.filter(t => t.title.toLowerCase().includes(q) || t.runId.toLowerCase().includes(q));
      renderSidebarTasks(filtered);
    }

    function startNewTask() {
      activeRunId = null;
      activeSessionId = 'session-' + Date.now();
      const clearChatButton = document.getElementById('clear-chat-btn');
      if (clearChatButton) clearChatButton.disabled = true;
      const activeTab = openChatTabs.find(tab => tab.id === activeChatTabId);
      if (activeTab) {
        activeTab.runId = null;
        activeTab.sessionId = activeSessionId;
        activeTab.modelId = document.getElementById('model-select').value;
        activeTab.attachedFiles = [];
      }
      attachedFiles = [];
      renderAttachments();
      document.getElementById('fresh-hero').style.display = 'flex';
      document.getElementById('dynamic-messages').innerHTML = '';
      moveComposerToHero();
      const input = document.getElementById('composer-input');
      input.value = '';
      updateChatTitle('New chat');
      const titleInput = document.getElementById('chat-title-input');
      if (titleInput) titleInput.value = 'New chat';
      updateTelemetry();
      input.focus();
      renderSidebarTasks(recentTasks);
      renderTopbarTabs();
    }

    function moveComposerToDock() {
      const dock = document.getElementById('bottom-composer-dock');
      if (dock) dock.style.display = 'block';
    }

    function moveComposerToHero() {
      const dock = document.getElementById('bottom-composer-dock');
      if (dock) dock.style.display = 'block';
    }

    function dismissStarterGrid() {
      const s = document.getElementById('starter-section');
      if (s) s.style.display = 'none';
    }

    function insertWorkspaceContext() {
      const input = document.getElementById('composer-input');
      const prefix = input.value && !input.value.endsWith(' ') ? ' ' : '';
      input.value = input.value + prefix + '@workspace ';
      input.focus();
      autoResizeComposer(input);
    }

    async function handleAttachments(fileList) {
      for (const file of Array.from(fileList || [])) {
        try {
          attachedFiles.push({ name: file.name, content: await file.text(), size: file.size });
        } catch {
          openModal('Attachment unavailable', 'Gram could not read this file as text.');
        }
      }
      document.getElementById('attachment-input').value = '';
      renderAttachments();
      updateTelemetry();
    }

    function removeAttachment(index) {
      attachedFiles.splice(index, 1);
      renderAttachments();
      updateTelemetry();
    }

    function renderAttachments() {
      const activeTab = openChatTabs.find(tab => tab.id === activeChatTabId);
      if (activeTab) activeTab.attachedFiles = attachedFiles;
      const tray = document.getElementById('attachment-tray');
      const fileList = document.getElementById('workspace-files-list');
      const count = document.getElementById('workspace-file-count');
      if (count) count.textContent = attachedFiles.length;
      if (tray) {
        tray.style.display = attachedFiles.length ? 'flex' : 'none';
        tray.innerHTML = attachedFiles.map((file, index) => \`
          <div class="attachment-pill">
            <span class="attachment-name" title="\${escapeHtml(file.name)}">\${escapeHtml(file.name)}</span>
            <button class="btn-remove-attachment" type="button" title="Remove file" onclick="removeAttachment(\${index})">×</button>
          </div>
        \`).join('');
      }
      if (fileList) {
        fileList.innerHTML = attachedFiles.length ? attachedFiles.map(file => \`
          <div class="sheet-item-row"><span>\${escapeHtml(file.name)}</span><span>\${Math.ceil(file.size / 1024)}KB</span></div>
        \`).join('') : '<span class="v2-capability-note">No files attached.</span>';
      }
    }

    async function toggleVoice() {
      if (window.gramAudio?.isListening) {
        stopVoice();
        return;
      }
      if (!window.gramAudio) {
        openModal('Voice input', 'The audio helper is not available.');
        return;
      }
      const input = document.getElementById('composer-input');
      voiceDraftBase = input.value.trim();
      const callback = {
        invokeMethodAsync: async (method, transcript) => {
          if (method === 'OnTranscriptReceived') {
            input.value = [voiceDraftBase, transcript].filter(Boolean).join(' ').trim();
            autoResizeComposer(input);
            updateTelemetry();
            if (localStorage.getItem('gram.settings.autoSendVoice') === 'true') {
              if (voiceSilenceTimer) clearTimeout(voiceSilenceTimer);
              voiceSilenceTimer = setTimeout(() => { stopVoice(); submitTask(); }, 1200);
            }
          }
        },
      };
      const started = await window.gramAudio.startVoice(callback, 'visualizerBars');
      if (!started) {
        openModal('Voice input', 'Gram could not access the microphone. Check browser microphone access and try again.');
        return;
      }
      document.getElementById('voice-input-btn')?.classList.add('active');
      document.getElementById('voice-banner').style.display = 'flex';
      document.getElementById('voice-label').textContent = 'Listening to your voice...';
      input.placeholder = 'Listening...';
      playVoiceChime();
    }

    function stopVoice() {
      if (voiceSilenceTimer) clearTimeout(voiceSilenceTimer);
      voiceSilenceTimer = null;
      window.gramAudio?.stopVoice('visualizerBars');
      document.getElementById('voice-input-btn')?.classList.remove('active');
      const banner = document.getElementById('voice-banner');
      if (banner) banner.style.display = 'none';
      const input = document.getElementById('composer-input');
      if (input) input.placeholder = 'Message Gram (type @ for context mentions)...';
      playVoiceChime();
    }

    function playVoiceChime() {
      if (localStorage.getItem('gram.settings.playVoiceChime') !== 'true') return;
      try {
        const AudioContext = window.AudioContext || window.webkitAudioContext;
        const context = new AudioContext();
        const oscillator = context.createOscillator();
        const gain = context.createGain();
        oscillator.frequency.value = 660;
        gain.gain.value = 0.035;
        oscillator.connect(gain);
        gain.connect(context.destination);
        oscillator.start();
        oscillator.stop(context.currentTime + 0.07);
        oscillator.onended = () => context.close();
      } catch {}
    }

    async function selectTask(runId) {
      activeRunId = runId;
      let chatTab = openChatTabs.find(tab => tab.runId === runId);
      if (!chatTab) {
        const recent = recentTasks.find(task => task.runId === runId);
        chatTab = {
          id: 'task-' + runId,
          title: recent?.title || runId,
          runId,
          sessionId: recent?.sessionId || activeSessionId,
          modelId: document.getElementById('model-select').value,
        };
        openChatTabs.push(chatTab);
      }
      activeChatTabId = chatTab.id;
      activeSessionId = chatTab.sessionId;
      if (chatTab.modelId) syncModelSelection(chatTab.modelId);
      attachedFiles = chatTab.attachedFiles || [];
      renderAttachments();
      renderTopbarTabs();
      renderSidebarTasks(recentTasks);
      try {
        const res = await fetch(\`/api/task?runId=\${runId}\`);
        const task = await res.json();
        if (task) {
          activeSessionId = task.sessionId || activeSessionId;
          chatTab.sessionId = activeSessionId;
              const taskTitle = task.activePlan?.[0]?.title || task.runId || 'Task';
          updateChatTitle(taskTitle);
          const titleInput = document.getElementById('chat-title-input');
          if (titleInput) titleInput.value = taskTitle;
          renderTaskCheckpoint(task);
        }
      } catch (err) {
        appendLog('Failed to load task: ' + err.message);
      }
    }

    function renderTaskCheckpoint(checkpoint) {
      document.getElementById('fresh-hero').style.display = 'none';
      moveComposerToDock();
      const clearChatButton = document.getElementById('clear-chat-btn');
      if (clearChatButton) clearChatButton.disabled = false;

      const container = document.getElementById('dynamic-messages');
      container.innerHTML = '';

      const userCard = document.createElement('div');
      userCard.className = 'message-card user-card';
      userCard.innerHTML = \`
        <div class="card-header">
          <div class="card-author">
            
            <span>You</span>
          </div>
          <span>\${checkpoint.timestamp ? new Date(checkpoint.timestamp).toLocaleTimeString() : ''}</span>
        </div>
        <div class="card-body">\${escapeHtml(checkpoint.activePlan?.[0]?.title || 'Task Run')}</div>
      \`;
      container.appendChild(userCard);

      // V2 execution-plan inspector is deferred; see docs/v2-feature-review.md.

      const assistantCard = document.createElement('div');
      assistantCard.className = 'message-card';
      assistantCard.innerHTML = \`
        <div class="card-header">
          <div class="card-author">
            
            <span>\${escapeHtml(getAssistantName())}</span>
          </div>
          <span>\${checkpoint.stage}</span>
        </div>
        <div class="card-body">
          Run status: <code>\${escapeHtml(checkpoint.stage || 'saved')}</code>.
          \${checkpoint.completedTaskIds?.length ? \`<p style="margin-top: 6px; font-size: 12px; color: var(--text-secondary);">This chat is ready to continue.</p>\` : ''}
        </div>
      \`;
      container.appendChild(assistantCard);
      scrollToBottom();
    }

    async function submitTask(approved = false) {
      const input = document.getElementById('composer-input');
      const prompt = input.value.trim();
      if (!prompt || isExecuting) return;

      const modelId = document.getElementById('model-select').value;
      const permissionMode = localStorage.getItem('gram.settings.permissionMode') || 'autonomous';
      if (permissionMode === 'ask' && !approved) {
        pendingRun = { prompt, modelId };
        openModal('Approve this run', \`
          <p>Gram is ready to run this task in the virtual workspace.</p>
          <div class="permission-preview">\${escapeHtml(prompt)}</div>
          <div class="result-actions">
            <button class="btn-secondary" type="button" onclick="closeModal()">Cancel</button>
            <button class="btn-primary" type="button" onclick="confirmPendingRun()">Run task</button>
          </div>
        \`);
        return;
      }
      const systemPrompt = localStorage.getItem('gram.settings.systemPrompt') || '';
      const rules = localStorage.getItem('gram.settings.rules') || '';
      const personaTone = localStorage.getItem('gram.settings.personaTone') || 'programmer';
      const personaText = {
        concise: 'Use concise and pragmatic language.',
        programmer: 'Act as a technical pair programmer.',
        minimalist: 'Be a strict minimalist and answer directly.',
        creative: 'Be creative and analytical.',
      }[personaTone] || '';
      const safePrefix = permissionMode === 'safe' ? 'Keep all file changes in the virtual workspace overlay. Do not commit changes to host files.\\n\\n' : '';
      const attachmentContext = attachedFiles.map(file => \`Attached file: \${file.name}\\n\${file.content}\`).join('\\n\\n');
      const executionPrompt = [safePrefix + systemPrompt, personaText, rules ? 'Additional rules:\\n' + rules : '', attachmentContext, prompt].filter(Boolean).join('\\n\\n');

      if (currentChatTitle === 'New chat') {
        const title = prompt.replace(/\\s+/g, ' ').slice(0, 42) || 'New chat';
        updateChatTitle(title);
        const titleInput = document.getElementById('chat-title-input');
        if (titleInput) titleInput.value = title;
      }

      document.getElementById('fresh-hero').style.display = 'none';
      const clearChatButton = document.getElementById('clear-chat-btn');
      if (clearChatButton) clearChatButton.disabled = false;
      moveComposerToDock();
      setExecuting(true);
      const startedAt = performance.now();
      updateTelemetry('', prompt);

      const container = document.getElementById('dynamic-messages');
      const userCard = document.createElement('div');
      userCard.className = 'message-card user-card';
      userCard.innerHTML = \`
        <div class="card-header">
          <div class="card-author">
            
            <span>You</span>
          </div>
          <span>\${new Date().toLocaleTimeString()}</span>
        </div>
        <div class="card-body">\${escapeHtml(prompt)}</div>
      \`;
      container.appendChild(userCard);

      const progressCard = document.createElement('div');
      progressCard.className = 'message-card';
      progressCard.id = 'active-progress-card';
      progressCard.innerHTML = \`
        <div class="card-header">
          <div class="card-author">
            
            <span>\${escapeHtml(getAssistantName())}</span>
          </div>
          <span>Executing...</span>
        </div>
        <div class="card-body" id="active-progress-body">Gram is working on your request...</div>
      \`;
      container.appendChild(progressCard);
      scrollToBottom();

      input.value = '';
      autoResizeComposer(input);

      updateStripStatus('Running task...');
      appendLog(\`[Run] Dispatching task: "\${escapeHtml(prompt)}" with model \${escapeHtml(modelId)}\`);

      try {
        const useLyraCloud = lyraAuthState.connected && activeExecutionMode !== 'local';
        const endpoint = useLyraCloud ? '/api/lyra/chat' : '/api/run';
        const payload = useLyraCloud
          ? {
              prompt: executionPrompt,
              sessionId: activeSessionId,
              modePreference: activeExecutionMode,
              pinnedModelId: modelId === 'auto' ? undefined : modelId,
              projectId: activeProjectId === 'local' ? undefined : activeProjectId,
            }
          : { prompt: executionPrompt, modelId, sessionId: activeSessionId };

        const res = await fetch(endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        const result = await res.json();

        lastRunLatencyMs = Math.round(performance.now() - startedAt);
        activeRunId = result.runId || result.messageId || ('run-' + Date.now());
        const activeTab = openChatTabs.find(tab => tab.id === activeChatTabId);
        if (activeTab) {
          activeTab.runId = activeRunId;
          activeTab.sessionId = result.sessionId || activeSessionId;
          activeTab.title = currentChatTitle;
          activeTab.modelId = modelId;
          activeTab.attachedFiles = attachedFiles;
        }
        renderTopbarTabs();
        renderRunResult(result, executionPrompt);
        updateTelemetry(result.output || result.content || '', prompt);
        loadRecentTasks();
      } catch (err) {
        appendLog('Task failed: ' + err.message);
        renderRunError(err.message);
      } finally {
        setExecuting(false);
      }
    }

    function confirmPendingRun() {
      if (!pendingRun) return;
      const input = document.getElementById('composer-input');
      input.value = pendingRun.prompt;
      pendingRun = null;
      closeModal();
      submitTask(true);
    }

    function renderRunResult(result, originalPrompt) {
      const progressCard = document.getElementById('active-progress-card');
      if (progressCard) progressCard.remove();

      const container = document.getElementById('dynamic-messages');
      const outputText = result.output || result.content || 'Task execution completed.';
      const statusText = result.lane ? \`Completed · \${result.lane}\` : 'Completed';
      const workers = Array.isArray(result.workers) ? result.workers : [];
      const workerRowsHtml = workers.length > 0
        ? \`<div class="cloud-progress-list">\${workers.map(w => \`<div class="cloud-progress-row"><span>\${escapeHtml(w.name || w.id)}</span><span class="status-text">\${escapeHtml(w.status || 'running')}</span></div>\`).join('')}</div>\`
        : '';

      const previewUrl = result.previewUrl || '';
      const encodedPrompt = encodeURIComponent(originalPrompt || '');

      const assistantCard = document.createElement('div');
      assistantCard.className = 'message-card';
      assistantCard.innerHTML = \`
        <div class="card-header">
          <div class="card-author">
            <span>\${escapeHtml(getAssistantName())}</span>
          </div>
          <span style="color: var(--success);">\${escapeHtml(statusText)}</span>
        </div>
        <div class="card-body">
          <div style="white-space: pre-wrap; font-size: 12.5px; line-height: 1.6;">\${escapeHtml(outputText)}</div>
          \${workerRowsHtml}
          <div class="result-actions">
            <button class="result-action-btn" type="button" onclick="escalatePromptToSwarm(decodeURIComponent('\${encodedPrompt}'))">Escalate to swarm</button>
            \${previewUrl ? \`<button class="result-action-btn" type="button" onclick="openPreview('\${escapeHtml(previewUrl)}')">Open preview</button>\` : ''}
          </div>
        </div>
      \`;
      container.appendChild(assistantCard);
      scrollToBottom();
      updateStripStatus('Ready');
    }

    async function escalatePromptToSwarm(promptText) {
      if (!promptText) return;
      updateStripStatus('Escalating to Lyra swarm...');
      try {
        const res = await fetch('/api/lyra/escalate', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ prompt: promptText, sessionId: activeSessionId })
        });
        const data = await res.json();
        renderRunResult(data, promptText);
      } catch (err) {
        renderRunError(err.message || 'Escalation failed');
      }
    }

    function openPreview(previewUrl) {
      if (!previewUrl) return;
      const isDesktop = Boolean(window.__TAURI__ || window.navigator?.userAgent?.includes('Electron'));
      if (!isDesktop) {
        window.open(previewUrl, '_blank', 'noopener,noreferrer');
        return;
      }
      openModal('Open preview', \`
        <p style="font-size:12.5px;color:var(--text-secondary);margin-bottom:12px;">Choose how to open this preview.</p>
        <div class="result-actions">
          <button class="result-action-btn" type="button" onclick="closeModal(); window.open('\${escapeHtml(previewUrl)}', '_blank', 'noopener,noreferrer')">Open in browser</button>
          <button class="result-action-btn" type="button" onclick="openModal('Preview', '<iframe src=\\'\${escapeHtml(previewUrl)}\\' style=\\'width:100%;height:65vh;border:0;\\'></iframe>')">Open in app</button>
        </div>
      \`);
    }

    function renderRunError(errorMsg) {
      const progressCard = document.getElementById('active-progress-card');
      if (progressCard) progressCard.remove();

      const container = document.getElementById('dynamic-messages');
      const errCard = document.createElement('div');
      errCard.className = 'message-card';
      errCard.innerHTML = \`
        <div class="card-header">
          <div class="card-author">
            
            <span>\${escapeHtml(getAssistantName())}</span>
          </div>
          <span style="color: var(--danger);">Failed</span>
        </div>
        <div class="card-body" style="color: var(--danger);">
          Error: \${escapeHtml(errorMsg)}
        </div>
      \`;
      container.appendChild(errCard);
      scrollToBottom();
      updateStripStatus('Task failed');
    }

    async function stopActiveTask() {
      if (!activeRunId) return;
      try {
        await fetch('/api/cancel', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ runId: activeRunId })
        });
        appendLog(\`[Cancel] Requested cancellation for run \${activeRunId}\`);
        setExecuting(false);
      } catch (err) {
        appendLog('Failed to stop: ' + err.message);
      }
    }

    function setExecuting(val) {
      isExecuting = val;
      const submitBtn = document.getElementById('submit-btn');
      const stopBtn = document.getElementById('stop-btn');
      if (val) {
        submitBtn.style.display = 'none';
        stopBtn.style.display = 'inline-flex';
      } else {
        submitBtn.style.display = 'inline-flex';
        stopBtn.style.display = 'none';
      }
    }

    function handleComposerKeyDown(e) {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        submitTask();
      }
    }

    function autoResizeComposer(textarea) {
      textarea.style.height = 'auto';
      textarea.style.height = Math.min(textarea.scrollHeight, 160) + 'px';
    }

    function setPromptAndFocus(text) {
      const input = document.getElementById('composer-input');
      input.value = text;
      autoResizeComposer(input);
      input.focus();
    }

    function scrollToBottom() {
      const timeline = document.getElementById('conversation-timeline');
      timeline.scrollTop = timeline.scrollHeight;
    }

    // V2 right-pane handlers are intentionally omitted from this classic chat shell; see docs/v2-feature-review.md.

    function updateStripStatus(text) {
      const status = document.getElementById('run-status-label');
      if (status) status.textContent = text;
    }

    function appendLog(msg) {
      const box = document.getElementById('logs-content-box');
      if (!box) return;
      const time = new Date().toLocaleTimeString();
      const div = document.createElement('div');
      div.textContent = \`[\${time}] \${msg}\`;
      box.appendChild(div);
      box.scrollTop = box.scrollHeight;
    }

    function openModal(title, bodyHtml) {
      document.getElementById('modal-title').textContent = title;
      document.getElementById('modal-body').innerHTML = bodyHtml;
      document.getElementById('common-modal').classList.add('open');
    }

    function closeModal() {
      document.getElementById('common-modal').classList.remove('open');
    }

    async function openProjectModal() {
      try {
        const res = await fetch('/api/workspace');
        const data = await res.json();
        openModal('Workspace Overview', \`
          <p><strong>Name:</strong> \${escapeHtml(data.name)}</p>
          <p><strong>Path:</strong> <code>\${escapeHtml(data.path)}</code></p>
          <p><strong>VCS:</strong> \${data.vcs ? \`\${data.vcs.type} on branch <code>\${data.vcs.branch || 'main'}</code>\` : 'None'}</p>
          <p style="margin-top: 10px; font-size: 12px; color: var(--text-muted);">
            Virtual CoW overlay layer is mounted at <code>main</code> with SQLite rollback logging.
          </p>
        \`);
      } catch {
        openModal('Workspace Overview', 'Unable to fetch workspace metadata.');
      }
    }

    // Deferred V2 surfaces: Gram Connect and sandbox diagnostics. See feature review.
    // V2 connection and mesh UI handler intentionally omitted from chat.

    // V2 sandbox diagnostics UI handler intentionally omitted from chat.

    function formatTimeAgo(isoString) {
      if (!isoString) return '';
      const ms = Date.now() - new Date(isoString).getTime();
      const mins = Math.floor(ms / 60000);
      if (mins < 1) return 'now';
      if (mins < 60) return \`\${mins}m\`;
      const hrs = Math.floor(mins / 60);
      if (hrs < 24) return \`\${hrs}h\`;
      return \`\${Math.floor(hrs / 24)}d\`;
    }

    function escapeHtml(str) {
      if (!str) return '';
      return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
    }

    window.addEventListener('keydown', (e) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        const searchInput = document.getElementById('task-filter-input');
        if (searchInput) searchInput.focus();
      } else if (e.key === 'Escape') {
        closeModal();
        closeModelMenu();
        const userMenu = document.getElementById('user-profile-popover');
        if (userMenu?.style.display === 'flex') toggleUserMenu();
      }
    });

    window.addEventListener('DOMContentLoaded', () => {
      initWorkspace();
    });
  </script>
</body>
</html>`;
}
