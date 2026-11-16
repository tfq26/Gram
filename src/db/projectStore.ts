/**
 * SQLite-backed project definitions, uploaded reference files, and workspace rules.
 */

import type { Database } from 'bun:sqlite';
import crypto from 'node:crypto';
import path from 'node:path';

export class ProjectStore {
  constructor(
    private readonly db: Database,
    private readonly workspaceRoot: string
  ) {}

  public listProjects(): Array<{
    id: string;
    label: string;
    workspacePath: string;
    instructions: string;
    archived: boolean;
    updatedAt: string;
    files: Array<{ fileId: string; filename: string; mimeType: string; sizeBytes: number; createdAt: string }>;
  }> {
    const rows = this.db.query<any, []>(
      `SELECT project_id, label, workspace_path, instructions, archived, updated_at FROM projects ORDER BY CASE WHEN project_id = 'local' THEN 0 ELSE 1 END, updated_at DESC`
    ).all();
    const fileRows = this.db.query<any, []>(
      `SELECT file_id, project_id, filename, mime_type, size_bytes, created_at FROM project_files ORDER BY created_at DESC`
    ).all();
    const filesByProject = new Map<
      string,
      Array<{ fileId: string; filename: string; mimeType: string; sizeBytes: number; createdAt: string }>
    >();
    for (const f of fileRows) {
      const list = filesByProject.get(f.project_id) || [];
      list.push({
        fileId: f.file_id,
        filename: f.filename,
        mimeType: f.mime_type,
        sizeBytes: Number(f.size_bytes || 0),
        createdAt: f.created_at,
      });
      filesByProject.set(f.project_id, list);
    }

    const mapped = rows.map((r) => ({
      id: r.project_id,
      label: r.label,
      workspacePath: r.workspace_path || '',
      instructions: r.instructions || '',
      archived: Boolean(r.archived),
      updatedAt: r.updated_at,
      files: filesByProject.get(r.project_id) || [],
    }));
    if (!mapped.some((p) => p.id === 'local')) {
      mapped.unshift({
        id: 'local',
        label: 'Local workspace',
        workspacePath: this.workspaceRoot,
        instructions: '',
        archived: false,
        updatedAt: new Date(0).toISOString(),
        files: filesByProject.get('local') || [],
      });
    }
    return mapped;
  }

  public getProjectRow(projectId: string): {
    project_id: string;
    label: string;
    workspace_path: string;
    instructions: string;
  } | null {
    return this.db.query<any, [string]>(
      `SELECT project_id, label, workspace_path, instructions FROM projects WHERE project_id = ? LIMIT 1`
    ).get(projectId);
  }

  public listProjectFileContents(projectId: string, limit = 8): Array<{ filename: string; content_text: string }> {
    return this.db.query<any, [string, number]>(
      `SELECT filename, content_text FROM project_files WHERE project_id = ? ORDER BY created_at DESC LIMIT ?`
    ).all(projectId, limit);
  }

  public archiveProject(projectId: string, archived: boolean): boolean {
    const id = (projectId || '').trim();
    if (!id || id === 'local') return false;
    const res = this.db.run(
      `UPDATE projects SET archived = ?, updated_at = ? WHERE project_id = ?`,
      [archived ? 1 : 0, new Date().toISOString(), id]
    );
    return res.changes > 0;
  }

  public deleteProject(projectId: string): boolean {
    const id = (projectId || '').trim();
    if (!id || id === 'local') return false;
    this.db.run(`DELETE FROM project_files WHERE project_id = ?`, [id]);
    const res = this.db.run(`DELETE FROM projects WHERE project_id = ?`, [id]);
    return res.changes > 0;
  }

  public saveProject(input: {
    id?: string;
    label: string;
    workspacePath?: string;
    instructions?: string;
  }): { id: string; label: string; workspacePath: string; instructions: string; updatedAt: string } {
    const id = (input.id || `proj-${crypto.randomUUID().slice(0, 8)}`).trim();
    const label = input.label.trim().slice(0, 120) || 'Untitled project';
    const workspacePath = (input.workspacePath ?? '').trim();
    const instructions = (input.instructions ?? '').trim().slice(0, 12_000);
    const updatedAt = new Date().toISOString();
    this.db.run(
      `INSERT INTO projects (project_id, label, workspace_path, instructions, updated_at)
       VALUES (?, ?, ?, ?, ?)
       ON CONFLICT(project_id) DO UPDATE SET
         label = excluded.label,
         workspace_path = excluded.workspace_path,
         instructions = excluded.instructions,
         updated_at = excluded.updated_at`,
      [id, label, workspacePath, instructions, updatedAt]
    );
    return { id, label, workspacePath, instructions, updatedAt };
  }

  public addProjectFile(input: {
    projectId: string;
    filename: string;
    mimeType?: string;
    contentText: string;
  }): { fileId: string; projectId: string; filename: string; mimeType: string; sizeBytes: number; createdAt: string } {
    const fileId = `pfile-${crypto.randomUUID().slice(0, 10)}`;
    const projectId = (input.projectId || 'local').trim();
    const filename = path.basename(input.filename || 'untitled.txt').slice(0, 160);
    const mimeType = (input.mimeType || 'text/plain').slice(0, 120);
    const contentText = (input.contentText || '').slice(0, 200_000);
    const sizeBytes = Buffer.byteLength(contentText, 'utf8');
    const createdAt = new Date().toISOString();
    this.db.run(
      `INSERT INTO project_files (file_id, project_id, filename, mime_type, size_bytes, content_text, created_at)
       VALUES (?, ?, ?, ?, ?, ?, ?)`,
      [fileId, projectId, filename, mimeType, sizeBytes, contentText, createdAt]
    );
    return { fileId, projectId, filename, mimeType, sizeBytes, createdAt };
  }

  public deleteProjectFile(fileId: string): void {
    this.db.run(`DELETE FROM project_files WHERE file_id = ?`, [fileId]);
  }

  public updateProjectInstructions(projectId: string, instructions: string): void {
    this.db.run(
      `UPDATE projects SET instructions = ?, updated_at = ? WHERE project_id = ?`,
      [instructions, new Date().toISOString(), projectId]
    );
  }

  public getProjectPromptContext(projectId?: string): string {
    const id = (projectId || 'local').trim();
    const proj = this.getProjectRow(id);
    const files = this.listProjectFileContents(id, 8);

    const parts: string[] = [];
    if (proj?.instructions?.trim()) {
      parts.push(`## Project Instructions (${proj.label || id})\n${proj.instructions.trim()}`);
    }
    if (files.length > 0) {
      let remaining = 10_000;
      const fileBlocks: string[] = [];
      for (const f of files) {
        if (remaining <= 0) break;
        const snippet = String(f.content_text || '').slice(0, remaining);
        remaining -= snippet.length;
        fileBlocks.push(`### ${f.filename}\n${snippet}`);
      }
      if (fileBlocks.length > 0) {
        parts.push(`## Uploaded Project Reference Files\n${fileBlocks.join('\n\n')}`);
      }
    }
    return parts.join('\n\n');
  }

  public rememberWorkspaceRule(ruleText: string): { ruleId: string; rule: string; created: boolean } {
    const rule = ruleText.trim().replace(/\s+/g, ' ');
    if (!rule || rule.length > 1000) throw new Error('Rule must contain 1 to 1000 characters.');
    const existing = this.db.query<any, [string]>(
      `SELECT rule_id, rule_text FROM workspace_rules WHERE rule_text = ? COLLATE NOCASE LIMIT 1`
    ).get(rule);
    const now = new Date().toISOString();
    if (existing) {
      this.db.run(`UPDATE workspace_rules SET active = 1, updated_at = ? WHERE rule_id = ?`, [now, existing.rule_id]);
      return { ruleId: existing.rule_id, rule: existing.rule_text, created: false };
    }
    const ruleId = `rule-${crypto.randomUUID()}`;
    this.db.run(
      `INSERT INTO workspace_rules (rule_id, rule_text, active, created_at, updated_at) VALUES (?, ?, 1, ?, ?)`,
      [ruleId, rule, now, now]
    );
    return { ruleId, rule, created: true };
  }

  public listWorkspaceRules(): string[] {
    return this.db.query<any, []>(
      `SELECT rule_text FROM workspace_rules WHERE active = 1 ORDER BY created_at ASC LIMIT 50`
    ).all().map((row) => String(row.rule_text));
  }
}
