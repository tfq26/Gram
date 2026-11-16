/**
 * Project management, folder picker, file attachment, and Git metadata routes.
 */

import fs from 'node:fs';
import path from 'node:path';
import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { initializeProjectGramDir } from '../../core/projectInit';
import { errorResponse, jsonResponse } from '../http';

function toWebGitUrl(raw: string): string | null {
  const s = raw.trim();
  const sshMatch = s.match(/^git@([^:]+):(.+?)(?:\.git)?$/);
  if (sshMatch) return `https://${sshMatch[1]}/${sshMatch[2]}`;
  const sshProtoMatch = s.match(/^ssh:\/\/git@([^/]+)\/(.+?)(?:\.git)?$/);
  if (sshProtoMatch) return `https://${sshProtoMatch[1]}/${sshProtoMatch[2]}`;
  if (s.startsWith('http://') || s.startsWith('https://')) {
    return s.replace(/\.git$/, '');
  }
  return null;
}

export async function handleProjectRoutes(
  req: Request,
  url: URL,
  runtime: GramAgentRuntime
): Promise<Response | null> {
  if (url.pathname === '/api/projects' && req.method === 'GET') {
    return jsonResponse(runtime.listProjects());
  }

  if (url.pathname === '/api/projects' && req.method === 'POST') {
    const body = (await req.json()) as {
      id?: string;
      label?: string;
      workspacePath?: string;
      instructions?: string;
    };
    if (!body.label || !body.label.trim()) {
      return errorResponse('Project label is required.', 400);
    }
    const saved = runtime.saveProject({
      id: body.id,
      label: body.label,
      workspacePath: body.workspacePath,
      instructions: body.instructions,
    });
    if (saved.id !== 'local') {
      if (saved.workspacePath && fs.existsSync(saved.workspacePath)) {
        try {
          if (fs.statSync(saved.workspacePath).isDirectory()) {
            initializeProjectGramDir(saved.workspacePath, { label: saved.label });
          }
        } catch (err) {
          console.warn('[Gram] Failed to initialize .gram directory:', err);
        }
      }
      void runtime.summarizeProjectInBackground(saved.id);
    }
    return jsonResponse({ ok: true, project: saved, projects: runtime.listProjects() });
  }

  if (url.pathname === '/api/projects/pick-folder' && req.method === 'POST') {
    try {
      let cmd: string[];
      if (process.platform === 'darwin') {
        cmd = ['osascript', '-e', 'POSIX path of (choose folder with prompt "Select project folder")'];
      } else if (process.platform === 'win32') {
        cmd = [
          'powershell',
          '-NoProfile',
          '-Command',
          'Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; if ($f.ShowDialog() -eq "OK") { $f.SelectedPath }',
        ];
      } else {
        cmd = ['zenity', '--file-selection', '--directory', '--title=Select project folder'];
      }
      const proc = Bun.spawn(cmd, { stdout: 'pipe', stderr: 'pipe' });
      const exitCode = await proc.exited;
      const out = (await new Response(proc.stdout).text()).trim();
      if (exitCode !== 0 || !out) {
        return jsonResponse({ cancelled: true });
      }
      const normalized = out.length > 1 ? out.replace(/\/+$/, '') : out;
      return jsonResponse({
        cancelled: false,
        path: normalized,
        name: path.basename(normalized) || normalized,
      });
    } catch (err: any) {
      return errorResponse(err?.message || 'Folder picker unavailable', 500);
    }
  }

  if (url.pathname === '/api/projects/files' && req.method === 'POST') {
    const body = (await req.json()) as {
      projectId?: string;
      filename?: string;
      mimeType?: string;
      contentText?: string;
    };
    if (!body.projectId || !body.filename) {
      return errorResponse('projectId and filename are required.', 400);
    }
    const added = runtime.addProjectFile({
      projectId: body.projectId,
      filename: body.filename,
      mimeType: body.mimeType,
      contentText: body.contentText || '',
    });
    if (body.projectId !== 'local') {
      void runtime.summarizeProjectInBackground(body.projectId);
    }
    return jsonResponse({ ok: true, file: added, projects: runtime.listProjects() });
  }

  if (url.pathname === '/api/projects/files/delete' && req.method === 'POST') {
    const body = (await req.json()) as { fileId?: string };
    if (!body.fileId) {
      return errorResponse('fileId is required.', 400);
    }
    runtime.deleteProjectFile(body.fileId);
    return jsonResponse({ ok: true, projects: runtime.listProjects() });
  }

  if (url.pathname === '/api/projects/archive' && req.method === 'POST') {
    const body = (await req.json()) as { id?: string; archived?: boolean };
    if (!body.id || body.id === 'local') {
      return errorResponse('Cannot archive Local workspace.', 400);
    }
    const ok = runtime.archiveProject(body.id, Boolean(body.archived));
    return jsonResponse({ ok, projects: runtime.listProjects() }, ok ? 200 : 404);
  }

  if (url.pathname === '/api/projects/delete' && req.method === 'POST') {
    const body = (await req.json()) as { id?: string };
    if (!body.id || body.id === 'local') {
      return errorResponse('Cannot delete Local workspace.', 400);
    }
    const ok = runtime.deleteProject(body.id);
    return jsonResponse({ ok, projects: runtime.listProjects() }, ok ? 200 : 404);
  }

  const projGitMatch = url.pathname.match(/^\/api\/projects\/([^/]+)\/git$/);
  if (projGitMatch && req.method === 'GET') {
    const targetId = decodeURIComponent(projGitMatch[1]!);
    const proj = runtime.listProjects().find((p) => p.id === targetId);
    const ws = proj?.workspacePath;
    if (!proj || !ws || !fs.existsSync(ws)) {
      return jsonResponse({ isGitRepo: false, remotes: [] });
    }
    const runGit = (args: string[]) => {
      try {
        const out = Bun.spawnSync(['git', ...args], { cwd: ws, stdout: 'pipe', stderr: 'pipe' });
        return out.exitCode === 0 ? out.stdout.toString('utf8').trim() : '';
      } catch {
        return '';
      }
    };
    const isInside = runGit(['rev-parse', '--is-inside-work-tree']) === 'true';
    if (!isInside) {
      return jsonResponse({ isGitRepo: false, remotes: [] });
    }
    const branch = runGit(['branch', '--show-current']) || runGit(['rev-parse', '--short', 'HEAD']);
    const lastCommit = runGit(['log', '-1', '--format=%h · %s']);
    const remoteLines = runGit(['remote', '-v']).split('\n').filter(Boolean);
    const seenRemotes = new Set<string>();
    const remotes: Array<{ name: string; rawUrl: string; webUrl: string | null }> = [];
    for (const line of remoteLines) {
      const m = line.match(/^(\S+)\s+(\S+)\s+\((fetch|push)\)$/);
      if (!m) continue;
      const key = `${m[1]}:${m[2]}`;
      if (seenRemotes.has(key)) continue;
      seenRemotes.add(key);
      remotes.push({ name: m[1]!, rawUrl: m[2]!, webUrl: toWebGitUrl(m[2]!) });
    }
    return jsonResponse({
      isGitRepo: true,
      branch,
      lastCommit,
      hasGramGuide: fs.existsSync(path.join(ws, '.gram', 'gram.md')),
      remotes,
    });
  }

  return null;
}
