/**
 * Workspace file tree, file read, diff, and GramFS overlay commit/reset routes.
 */

import fs from 'node:fs';
import path from 'node:path';
import type { GramAgentRuntime } from '../../runtime/agentRuntime';
import { formatUnifiedDiff } from '../../runtime/agentTools';
import { errorResponse, jsonResponse } from '../http';

function getDirectoryTree(rootCwd: string, dirPath: string, depth = 0): any[] {
  if (depth > 4) return [];
  const entries: any[] = [];
  try {
    const items = fs.readdirSync(dirPath, { withFileTypes: true });
    items.sort((a, b) => {
      if (a.isDirectory() === b.isDirectory()) return a.name.localeCompare(b.name);
      return a.isDirectory() ? -1 : 1;
    });

    for (const item of items) {
      if (
        item.name === 'node_modules' ||
        item.name.startsWith('.') ||
        item.name === 'dist' ||
        item.name === 'target' ||
        item.name === 'coverage'
      ) {
        continue;
      }
      const full = path.join(dirPath, item.name);
      const rel = path.relative(rootCwd, full).replace(/\\/g, '/');
      if (item.isDirectory()) {
        entries.push({
          name: item.name,
          path: rel,
          type: 'dir',
          children: getDirectoryTree(rootCwd, full, depth + 1),
        });
      } else if (item.isFile()) {
        entries.push({
          name: item.name,
          path: rel,
          type: 'file',
          extension: path.extname(item.name).toLowerCase(),
        });
      }
    }
  } catch {}
  return entries;
}

export async function handleFileRoutes(
  req: Request,
  url: URL,
  runtime: GramAgentRuntime,
  cwd: string
): Promise<Response | null> {
  if (url.pathname === '/api/workspace/diff' && req.method === 'GET') {
    const projectId = url.searchParams.get('projectId') || 'local';
    let targetDir = cwd;
    if (projectId !== 'local') {
      const proj = runtime.listProjects().find((p) => p.id === projectId);
      if (proj?.workspacePath && fs.existsSync(proj.workspacePath)) {
        targetDir = proj.workspacePath;
      }
    }
    const statusProc = Bun.spawnSync(['git', 'status', '--short'], { cwd: targetDir });
    const diffProc = Bun.spawnSync(['git', 'diff'], { cwd: targetDir });
    const statusText = new TextDecoder().decode(statusProc.stdout).trim();
    const diffText = new TextDecoder().decode(diffProc.stdout);
    const overlayDiffs = runtime.gramfs.listModifiedFiles('main').map((entry) => {
      const hostPath = path.resolve(targetDir, entry.path);
      const before = entry.originalHash && fs.existsSync(hostPath) ? fs.readFileSync(hostPath, 'utf8') : '';
      return {
        path: entry.path,
        patch: formatUnifiedDiff(entry.path, before, entry.isDeleted ? '' : entry.content),
      };
    });
    return jsonResponse({
      workspacePath: targetDir,
      status: statusText,
      gitDiff: diffText.slice(0, 60000),
      overlayDiffs,
    });
  }

  if (url.pathname === '/api/files/changes') {
    return jsonResponse(runtime.gramfs.listModifiedFiles('main'));
  }

  if (url.pathname === '/api/diff') {
    const reqPath = url.searchParams.get('path');
    if (!reqPath) {
      return errorResponse('Missing path', 400);
    }
    try {
      const hostFile = path.resolve(cwd, reqPath);
      let original = '';
      if (fs.existsSync(hostFile)) {
        original = fs.readFileSync(hostFile, 'utf8');
      }
      let modified = '';
      let isDeleted = false;
      try {
        modified = runtime.gramfs.readFile(reqPath, 'main');
      } catch {
        isDeleted = true;
      }
      return jsonResponse({ path: reqPath, original, modified, isDeleted });
    } catch (err: any) {
      return errorResponse(err.message, 500);
    }
  }

  if (url.pathname === '/api/files/commit' && req.method === 'POST') {
    return jsonResponse(runtime.gramfs.commitToHost('main'));
  }

  if (url.pathname === '/api/files/reset' && req.method === 'POST') {
    runtime.gramfs.undo(100, 'main');
    return jsonResponse({ reset: true, modified: runtime.gramfs.listModifiedFiles('main') });
  }

  if (url.pathname === '/api/files/tree') {
    return jsonResponse(getDirectoryTree(cwd, cwd));
  }

  if (url.pathname === '/api/files/read') {
    const reqPath = url.searchParams.get('path') || 'package.json';
    try {
      let content = '';
      try {
        content = runtime.gramfs.readFile(reqPath, 'main');
      } catch {
        const fullPath = path.resolve(cwd, reqPath);
        content = fs.readFileSync(fullPath, 'utf8');
      }
      return jsonResponse({ path: reqPath, content });
    } catch (err: any) {
      return errorResponse(err.message, 404);
    }
  }

  return null;
}
