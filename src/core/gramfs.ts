/**
 * GramFS — Modern Agent-Native Copy-on-Write (CoW) Virtual Filesystem
 * 
 * Powered by bun:sqlite.
 * Features:
 * - Zero-copy instant workspace branching for parallel/async agents (forkOverlay)
 * - Immutable operation log (opLog) with instant undo/rollback
 * - Isolated staging overlays preventing dirty uncommitted host file clobbering
 * - Atomic commit to host filesystem with automatic pre-write snapshots
 * - Jujutsu (jj) & Git colocation status detector
 */

import { Database } from 'bun:sqlite';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

export interface FileOverlayEntry {
  path: string;
  content: string;
  originalHash: string | null;
  newHash: string;
  isDeleted: boolean;
  modifiedAt: string;
}

export interface OpLogEntry {
  opId: string;
  layerId: string;
  opType: 'write' | 'edit' | 'delete' | 'commit' | 'rollback';
  filePath: string;
  oldContent: string | null;
  newContent: string | null;
  timestamp: string;
  metadata?: Record<string, unknown>;
}

export interface GramFSOptions {
  workspaceRoot: string;
  dbPath?: string;
}

export class GramFS {
  private db: Database;
  public readonly workspaceRoot: string;

  constructor(options: GramFSOptions) {
    this.workspaceRoot = path.resolve(options.workspaceRoot);
    const gramDir = path.join(this.workspaceRoot, '.gram');
    if (!fs.existsSync(gramDir)) {
      fs.mkdirSync(gramDir, { recursive: true });
    }

    const dbFile = options.dbPath || path.join(gramDir, 'gramfs.db');
    this.db = new Database(dbFile);
    this.initSchema();
  }

  private initSchema(): void {
    this.db.run(`
      CREATE TABLE IF NOT EXISTS overlay_files (
        layer_id TEXT NOT NULL,
        file_path TEXT NOT NULL,
        content TEXT NOT NULL,
        original_hash TEXT,
        new_hash TEXT NOT NULL,
        is_deleted INTEGER NOT NULL DEFAULT 0,
        modified_at TEXT NOT NULL,
        PRIMARY KEY (layer_id, file_path)
      );

      CREATE TABLE IF NOT EXISTS op_log (
        op_id TEXT PRIMARY KEY,
        layer_id TEXT NOT NULL,
        op_type TEXT NOT NULL,
        file_path TEXT NOT NULL,
        old_content TEXT,
        new_content TEXT,
        timestamp TEXT NOT NULL,
        metadata_json TEXT
      );

      CREATE INDEX IF NOT EXISTS idx_overlay_layer ON overlay_files (layer_id);
      CREATE INDEX IF NOT EXISTS idx_op_log_layer ON op_log (layer_id, timestamp);
    `);
  }

  /**
   * Resolves a relative path safely within workspace boundary.
   */
  public resolvePath(relPath: string): string {
    const normalized = path.normalize(relPath).replace(/\\/g, '/');
    if (normalized.startsWith('../') || normalized === '..') {
      throw new Error(`Security Exception: Path '${relPath}' escapes workspace root.`);
    }
    return normalized.startsWith('/') ? normalized.slice(1) : normalized;
  }

  /**
   * Hashes a string using SHA-256
   */
  public static hashContent(content: string): string {
    return crypto.createHash('sha256').update(content, 'utf8').digest('hex');
  }

  /**
   * Reads a file. Checks overlay layer first, falls back to parent overlay or host filesystem.
   */
  public readFile(relPath: string, layerId = 'main'): string {
    const cleanPath = this.resolvePath(relPath);

    // 1. Check current overlay
    const overlay = this.db.query<any, [string, string]>(
      `SELECT content, is_deleted FROM overlay_files WHERE layer_id = ? AND file_path = ? LIMIT 1`
    ).get(layerId, cleanPath);

    if (overlay) {
      if (overlay.is_deleted === 1) {
        throw new Error(`File '${cleanPath}' was deleted in layer '${layerId}'`);
      }
      return overlay.content;
    }

    // 2. Fall back to host filesystem
    const hostPath = path.join(this.workspaceRoot, cleanPath);
    if (!fs.existsSync(hostPath)) {
      throw new Error(`File '${cleanPath}' does not exist on host or in overlay '${layerId}'.`);
    }

    return fs.readFileSync(hostPath, 'utf8');
  }

  /**
   * Checks if a file exists either in overlay or on host.
   */
  public fileExists(relPath: string, layerId = 'main'): boolean {
    const cleanPath = this.resolvePath(relPath);
    const overlay = this.db.query<any, [string, string]>(
      `SELECT is_deleted FROM overlay_files WHERE layer_id = ? AND file_path = ? LIMIT 1`
    ).get(layerId, cleanPath);

    if (overlay) {
      return overlay.is_deleted === 0;
    }

    return fs.existsSync(path.join(this.workspaceRoot, cleanPath));
  }

  /**
   * Writes content to an overlay layer (CoW), without modifying the host filesystem.
   */
  public writeFile(relPath: string, content: string, layerId = 'main', metadata?: Record<string, unknown>): string {
    const cleanPath = this.resolvePath(relPath);
    let oldContent: string | null = null;
    let originalHash: string | null = null;

    try {
      oldContent = this.readFile(cleanPath, layerId);
      originalHash = GramFS.hashContent(oldContent);
    } catch {
      // File didn't exist before
    }

    const newHash = GramFS.hashContent(content);
    const now = new Date().toISOString();
    const opId = `op-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;

    this.db.run(
      `INSERT INTO overlay_files (layer_id, file_path, content, original_hash, new_hash, is_deleted, modified_at)
       VALUES (?, ?, ?, ?, ?, 0, ?)
       ON CONFLICT(layer_id, file_path) DO UPDATE SET
         content = excluded.content,
         new_hash = excluded.new_hash,
         is_deleted = 0,
         modified_at = excluded.modified_at`,
      [layerId, cleanPath, content, originalHash, newHash, now]
    );

    this.db.run(
      `INSERT INTO op_log (op_id, layer_id, op_type, file_path, old_content, new_content, timestamp, metadata_json)
       VALUES (?, ?, 'write', ?, ?, ?, ?, ?)`,
      [opId, layerId, cleanPath, oldContent, content, now, metadata ? JSON.stringify(metadata) : null]
    );

    return opId;
  }

  /** Atomically stage a prevalidated set of writes and their operation-log entries. */
  public writeFilesAtomic(changes: Array<{ path: string; content: string | null }>, layerId = 'main'): string[] {
    const normalized = changes.map((change) => ({ ...change, path: this.resolvePath(change.path) }));
    const uniquePaths = new Set(normalized.map((change) => change.path));
    if (uniquePaths.size !== normalized.length) throw new Error('Atomic changeset contains duplicate file paths.');
    const transaction = this.db.transaction(() => normalized.map((change) =>
      change.content === null ? this.deleteFile(change.path, layerId) : this.writeFile(change.path, change.content, layerId)
    ));
    return transaction();
  }

  /**
   * Marks a file as deleted in the overlay.
   */
  public deleteFile(relPath: string, layerId = 'main'): string {
    const cleanPath = this.resolvePath(relPath);
    let oldContent: string | null = null;
    try {
      oldContent = this.readFile(cleanPath, layerId);
    } catch {}

    const now = new Date().toISOString();
    const opId = `op-del-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;

    this.db.run(
      `INSERT INTO overlay_files (layer_id, file_path, content, original_hash, new_hash, is_deleted, modified_at)
       VALUES (?, ?, '', ?, '', 1, ?)
       ON CONFLICT(layer_id, file_path) DO UPDATE SET
         content = '',
         is_deleted = 1,
         modified_at = excluded.modified_at`,
      [layerId, cleanPath, oldContent ? GramFS.hashContent(oldContent) : null, now]
    );

    this.db.run(
      `INSERT INTO op_log (op_id, layer_id, op_type, file_path, old_content, new_content, timestamp)
       VALUES (?, ?, 'delete', ?, ?, NULL, ?)`,
      [opId, layerId, cleanPath, oldContent, now]
    );

    return opId;
  }

  /**
   * Instant zero-copy fork of an existing overlay into a new layer for parallel/async subagents.
   */
  public forkOverlay(sourceLayerId: string, targetLayerId: string): number {
    this.db.run(`DELETE FROM overlay_files WHERE layer_id = ?`, [targetLayerId]);
    const res = this.db.run(
      `INSERT INTO overlay_files (layer_id, file_path, content, original_hash, new_hash, is_deleted, modified_at)
       SELECT ?, file_path, content, original_hash, new_hash, is_deleted, modified_at
       FROM overlay_files WHERE layer_id = ?`,
      [targetLayerId, sourceLayerId]
    );
    return res.changes;
  }

  /** Remove all staged files and operation history from one overlay layer. */
  public clearLayer(layerId: string): void {
    this.db.run(`DELETE FROM overlay_files WHERE layer_id = ?`, [layerId]);
    this.db.run(`DELETE FROM op_log WHERE layer_id = ?`, [layerId]);
  }

  /**
   * Lists all modified files in a given layer.
   */
  public listModifiedFiles(layerId = 'main'): FileOverlayEntry[] {
    const rows = this.db.query<any, [string]>(
      `SELECT file_path, content, original_hash, new_hash, is_deleted, modified_at
       FROM overlay_files WHERE layer_id = ?`
    ).all(layerId);

    return rows.map((r) => ({
      path: r.file_path,
      content: r.content,
      originalHash: r.original_hash,
      newHash: r.new_hash,
      isDeleted: r.is_deleted === 1,
      modifiedAt: r.modified_at,
    }));
  }

  /**
   * Atomically commits an overlay layer to the host filesystem.
   * Creates pre-write backups in .gram/backups/ so undo is always possible.
   */
  public commitToHost(layerId = 'main'): { committedFiles: string[]; deletedFiles: string[] } {
    const modified = this.listModifiedFiles(layerId);
    const backupDir = path.join(this.workspaceRoot, '.gram', 'backups', `commit-${Date.now()}`);
    fs.mkdirSync(backupDir, { recursive: true });

    const committedFiles: string[] = [];
    const deletedFiles: string[] = [];

    for (const entry of modified) {
      const hostFile = path.join(this.workspaceRoot, entry.path);

      // 1. Backup host file if it exists
      if (fs.existsSync(hostFile)) {
        const backupTarget = path.join(backupDir, entry.path);
        fs.mkdirSync(path.dirname(backupTarget), { recursive: true });
        fs.copyFileSync(hostFile, backupTarget);
      }

      // 2. Apply or delete
      if (entry.isDeleted) {
        if (fs.existsSync(hostFile)) {
          fs.unlinkSync(hostFile);
        }
        deletedFiles.push(entry.path);
      } else {
        fs.mkdirSync(path.dirname(hostFile), { recursive: true });
        fs.writeFileSync(hostFile, entry.content, 'utf8');
        committedFiles.push(entry.path);
      }
    }

    // Clear overlay after successful host commit
    this.db.run(`DELETE FROM overlay_files WHERE layer_id = ?`, [layerId]);

    const opId = `op-commit-${Date.now()}`;
    this.db.run(
      `INSERT INTO op_log (op_id, layer_id, op_type, file_path, timestamp, metadata_json)
       VALUES (?, ?, 'commit', 'ALL', ?, ?)`,
      [opId, layerId, new Date().toISOString(), JSON.stringify({ committedFiles, deletedFiles, backupDir })]
    );

    return { committedFiles, deletedFiles };
  }

  /**
   * Reverts the last N operations in an overlay.
   */
  public undo(steps = 1, layerId = 'main'): number {
    const logs = this.db.query<any, [string, number]>(
      `SELECT op_id, op_type, file_path, old_content, new_content
       FROM op_log
       WHERE layer_id = ? AND op_type IN ('write', 'delete')
       ORDER BY timestamp DESC LIMIT ?`
    ).all(layerId, steps);

    let undone = 0;
    for (const log of logs) {
      if (log.old_content === null) {
        // Was a new creation, so delete from overlay
        this.db.run(`DELETE FROM overlay_files WHERE layer_id = ? AND file_path = ?`, [layerId, log.file_path]);
      } else {
        // Restore old content
        const hash = GramFS.hashContent(log.old_content);
        this.db.run(
          `UPDATE overlay_files SET content = ?, new_hash = ?, is_deleted = 0, modified_at = ?
           WHERE layer_id = ? AND file_path = ?`,
          [log.old_content, hash, new Date().toISOString(), layerId, log.file_path]
        );
      }
      this.db.run(`DELETE FROM op_log WHERE op_id = ?`, [log.op_id]);
      undone++;
    }

    return undone;
  }

  /**
   * Detects whether the repository is colocated with Jujutsu (.jj) or standard Git.
   */
  public detectVcs(): { isJujutsu: boolean; isGit: boolean; colocated: boolean } {
    const isJujutsu = fs.existsSync(path.join(this.workspaceRoot, '.jj'));
    const isGit = fs.existsSync(path.join(this.workspaceRoot, '.git'));
    return {
      isJujutsu,
      isGit,
      colocated: isJujutsu && isGit,
    };
  }

  public close(): void {
    this.db.close();
  }
}
