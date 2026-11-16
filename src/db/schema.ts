/**
 * SQLite database initialization and schema migrations for Gram runtime state.
 */

import { Database } from 'bun:sqlite';
import fs from 'node:fs';
import path from 'node:path';

export function openRuntimeDatabase(workspaceRoot: string, isRepoTestRun: boolean): Database {
  const gramDir = path.join(workspaceRoot, '.gram');
  if (!fs.existsSync(gramDir)) {
    fs.mkdirSync(gramDir, { recursive: true });
  }
  const dbFileName = isRepoTestRun ? 'checkpoints.test.db' : 'checkpoints.db';
  const db = new Database(path.join(gramDir, dbFileName));
  initRuntimeSchema(db);
  return db;
}

export function initRuntimeSchema(db: Database): void {
  db.run(`PRAGMA journal_mode = WAL;`);
  db.run(`PRAGMA synchronous = NORMAL;`);
  db.run(`
    CREATE TABLE IF NOT EXISTS run_checkpoints (
      session_id TEXT NOT NULL,
      run_id TEXT NOT NULL,
      sequence INTEGER NOT NULL,
      stage TEXT NOT NULL,
      checkpoint_json TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      dirty INTEGER NOT NULL DEFAULT 1,
      sync_seq INTEGER NOT NULL DEFAULT 0,
      PRIMARY KEY (session_id, run_id)
    );
  `);
  db.run(`
    CREATE TABLE IF NOT EXISTS chat_sessions (
      session_id TEXT PRIMARY KEY,
      title TEXT NOT NULL,
      project_id TEXT NOT NULL DEFAULT 'local',
      project_label TEXT NOT NULL DEFAULT 'Local workspace',
      model_id TEXT NOT NULL DEFAULT 'auto',
      messages_json TEXT NOT NULL DEFAULT '[]',
      updated_at TEXT NOT NULL,
      dirty INTEGER NOT NULL DEFAULT 1,
      sync_seq INTEGER NOT NULL DEFAULT 0
    );
  `);
  for (const sql of [
    `ALTER TABLE run_checkpoints ADD COLUMN dirty INTEGER NOT NULL DEFAULT 1`,
    `ALTER TABLE run_checkpoints ADD COLUMN sync_seq INTEGER NOT NULL DEFAULT 0`,
    `ALTER TABLE chat_sessions ADD COLUMN dirty INTEGER NOT NULL DEFAULT 1`,
    `ALTER TABLE chat_sessions ADD COLUMN sync_seq INTEGER NOT NULL DEFAULT 0`,
    `ALTER TABLE chat_sessions ADD COLUMN mode TEXT NOT NULL DEFAULT 'chat'`,
    `ALTER TABLE chat_sessions ADD COLUMN revision INTEGER NOT NULL DEFAULT 1`,
  ]) {
    try {
      db.run(sql);
    } catch {}
  }
  db.run(`
    CREATE TABLE IF NOT EXISTS sync_state (
      key TEXT PRIMARY KEY,
      value TEXT NOT NULL
    );
  `);
  db.run(`
    CREATE TABLE IF NOT EXISTS workspace_rules (
      rule_id TEXT PRIMARY KEY,
      rule_text TEXT NOT NULL COLLATE NOCASE UNIQUE,
      active INTEGER NOT NULL DEFAULT 1,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL
    );
  `);
  db.run(`
    CREATE TABLE IF NOT EXISTS projects (
      project_id TEXT PRIMARY KEY,
      label TEXT NOT NULL,
      workspace_path TEXT NOT NULL DEFAULT '',
      instructions TEXT NOT NULL DEFAULT '',
      archived INTEGER NOT NULL DEFAULT 0,
      updated_at TEXT NOT NULL
    );
  `);
  try {
    db.run(`ALTER TABLE projects ADD COLUMN archived INTEGER NOT NULL DEFAULT 0`);
  } catch {}
  db.run(`
    CREATE TABLE IF NOT EXISTS project_files (
      file_id TEXT PRIMARY KEY,
      project_id TEXT NOT NULL,
      filename TEXT NOT NULL,
      mime_type TEXT NOT NULL DEFAULT 'text/plain',
      size_bytes INTEGER NOT NULL DEFAULT 0,
      content_text TEXT NOT NULL DEFAULT '',
      created_at TEXT NOT NULL
    );
  `);
  db.run(`
    CREATE TABLE IF NOT EXISTS users (
      user_id TEXT PRIMARY KEY,
      name TEXT NOT NULL,
      email TEXT NOT NULL UNIQUE COLLATE NOCASE,
      password_hash TEXT NOT NULL,
      created_at TEXT NOT NULL
    );
  `);
  db.run(`
    CREATE TABLE IF NOT EXISTS user_sessions (
      token TEXT PRIMARY KEY,
      user_id TEXT NOT NULL,
      created_at TEXT NOT NULL,
      expires_at TEXT NOT NULL
    );
  `);
}
