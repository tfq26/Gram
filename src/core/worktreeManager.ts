import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

export interface WorktreeHandle {
  worktreePath: string;
  branchOrHead: string;
  baseCommit: string;
}

/**
 * Manages lightweight per-task Git worktrees under `.gram/worktrees/<runId>`
 * so concurrent or experimental tasks never collide with the user's checkout.
 */
export class WorktreeManager {
  public static isGitRepo(workspaceRoot: string): boolean {
    try {
      const out = execFileSync('git', ['rev-parse', '--is-inside-work-tree'], {
        cwd: workspaceRoot,
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
      }).trim();
      return out === 'true';
    } catch {
      return false;
    }
  }

  public static ensureGitExclude(workspaceRoot: string): void {
    try {
      const gitDir = execFileSync('git', ['rev-parse', '--git-dir'], {
        cwd: workspaceRoot,
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
      }).trim();
      const absGitDir = path.isAbsolute(gitDir) ? gitDir : path.resolve(workspaceRoot, gitDir);
      const excludePath = path.join(absGitDir, 'info', 'exclude');
      if (!fs.existsSync(path.dirname(excludePath))) {
        fs.mkdirSync(path.dirname(excludePath), { recursive: true });
      }
      const existing = fs.existsSync(excludePath) ? fs.readFileSync(excludePath, 'utf8') : '';
      if (!existing.includes('.gram/worktrees')) {
        const suffix = existing.endsWith('\n') || existing.length === 0 ? '' : '\n';
        fs.writeFileSync(excludePath, `${existing}${suffix}.gram/worktrees/\n`, 'utf8');
      }
    } catch {}
  }

  public static createTaskWorktree(workspaceRoot: string, runId: string): WorktreeHandle | null {
    if (!this.isGitRepo(workspaceRoot)) return null;
    try {
      this.ensureGitExclude(workspaceRoot);
      const safeRunId = runId.replace(/[^a-zA-Z0-9_-]/g, '_');
      const worktreeRoot = path.join(workspaceRoot, '.gram', 'worktrees', safeRunId);
      if (fs.existsSync(worktreeRoot)) {
        const head = execFileSync('git', ['rev-parse', 'HEAD'], {
          cwd: worktreeRoot,
          encoding: 'utf8',
          stdio: ['ignore', 'pipe', 'ignore'],
        }).trim();
        return { worktreePath: worktreeRoot, branchOrHead: 'detached', baseCommit: head };
      }

      fs.mkdirSync(path.dirname(worktreeRoot), { recursive: true });
      const baseCommit = execFileSync('git', ['rev-parse', 'HEAD'], {
        cwd: workspaceRoot,
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
      }).trim();

      execFileSync('git', ['worktree', 'add', '--detach', worktreeRoot, baseCommit], {
        cwd: workspaceRoot,
        stdio: ['ignore', 'pipe', 'pipe'],
      });

      return {
        worktreePath: worktreeRoot,
        branchOrHead: 'detached',
        baseCommit,
      };
    } catch {
      return null;
    }
  }

  public static getWorktreeDiff(worktreePath: string): string {
    try {
      return execFileSync('git', ['diff', 'HEAD'], {
        cwd: worktreePath,
        encoding: 'utf8',
        stdio: ['ignore', 'pipe', 'ignore'],
      });
    } catch {
      return '';
    }
  }

  public static removeTaskWorktree(workspaceRoot: string, worktreePath: string): boolean {
    try {
      if (!fs.existsSync(worktreePath)) return true;
      execFileSync('git', ['worktree', 'remove', '--force', worktreePath], {
        cwd: workspaceRoot,
        stdio: ['ignore', 'pipe', 'ignore'],
      });
      return true;
    } catch {
      return false;
    }
  }
}
