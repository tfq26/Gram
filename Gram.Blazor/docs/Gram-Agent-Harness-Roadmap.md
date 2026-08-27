# Gram Agent Harness and Sandbox Roadmap

## Goal

Gram should be able to one-shot useful projects with minimal user oversight. Models should work autonomously, while Gram protects the user’s real workspace, controls resource usage, validates the result, and returns a recoverable artifact or diff.

## Current assessment

The native Gram Box implementation is efficient for Git isolation, but it is not yet a complete security sandbox. Git worktrees separate branches and file changes; they do not isolate host processes, networking, memory, CPU, disk, or filesystem access by themselves.

Current strengths:

- Native worktrees have substantially lower overhead than starting a container for every task.
- Multiple worktrees can be created in parallel.
- Shared dependency caches reduce duplicated package storage.
- Commands have timeouts and low-priority execution support.
- Sensitive environment variables are stripped.
- Diffs and commits remain separate from the base branch.

Current gaps:

- Native commands still execute on the host.
- Resource quota settings are not yet enforced by the process driver.
- Network access is not independently controlled.
- The active-session fallback is unsafe for concurrent runs.
- Sandbox state and leases are not fully recoverable after a crash.
- Cleanup is primarily explicit rather than automatic.
- All execution paths need to use the same path and command security checks.

## Target execution model

```text
Project run
├── Prototype A → Sandbox A → Worktree A
├── Prototype B → Sandbox B → Worktree B
└── Prototype C → Sandbox C → Worktree C

Each run owns:
- a sandbox lease
- a worktree
- a cancellation token
- resource limits
- checkpoints and logs
- a final diff/artifact manifest
```

## Autonomy policy

The default experience is autonomous execution inside a sandbox. User approval should not interrupt ordinary implementation, builds, tests, or iteration.

The boundary is the safety mechanism:

```text
User workspace → protected boundary → autonomous sandbox → validated result
```

The agent may freely create, modify, delete, build, and test files inside its sandbox. Applying changes to the user workspace, publishing an artifact, or pushing externally should be a separate finalization action.

## Required harness capabilities

### 1. Per-run sandbox ownership

Every agent run must receive an explicit sandbox handle. Agent tools must never depend on a global active session. This prevents concurrent prototypes and users from operating on the wrong worktree.

### 2. Bounded parallelism

Add a scheduler that limits the number of active sandboxes, processes, model calls, searches, and downloads. Parallel prototypes should be supported, but bounded by CPU, memory, disk, and provider quotas.

### 3. Filesystem confinement

All read, write, working-directory, and output paths must resolve beneath the sandbox worktree. Reject traversal, symlink escapes, and paths outside the assigned root.

### 4. Process and resource controls

Enforce, rather than merely configure:

- maximum wall-clock duration
- CPU priority or CPU time
- memory limit
- disk/output limit
- maximum child-process count
- maximum stdout/stderr size
- cancellation of the complete process tree

### 5. Network policy

Support task-level network modes:

- disabled
- allowlisted package and provider hosts
- unrestricted network for explicitly trusted tasks

### 6. Leases and recovery

Persist sandbox ownership, process IDs, timestamps, and status. On startup, detect abandoned leases, terminate surviving processes, prune worktrees, and preserve failed-run logs for diagnosis.

### 7. Durable checkpoints

Persist checkpoints after meaningful stages:

```text
created → dependencies ready → implementation complete
→ tests passed → review passed → artifact ready
```

A failed run should resume from the last successful checkpoint rather than restart from zero.

### 8. Validation-driven completion

A project is complete only when its declared checks pass. Typical checks include build, unit tests, artifact existence, source/license validation, secret scanning, and diff scope validation.

### 9. Standard run protocol

```text
plan → allocate sandbox → execute → validate → repair failures
→ package diff/artifacts → report result
```

The same protocol should serve coding, search, presentations, documents, and spreadsheets.

## Initial validation targets

Before expanding the feature set, the harness should reliably produce results for:

1. Standard live-search questions, with sources and freshness information.
2. Current-events questions that cannot be answered from training data alone.
3. A presentation request that produces section outputs, licensed visual assets, source metadata, and a final usable presentation artifact.
4. A coding request that creates a prototype in an isolated worktree, runs tests, and returns a diff.
5. Multiple concurrent prototypes without session or filesystem collisions.

## Implementation order

1. Refactor Gram Box to use per-run sandbox handles.
2. Add the bounded sandbox scheduler.
3. Enforce path and process resource limits.
4. Add leases, startup recovery, and automatic cleanup.
5. Add checkpoint-aware project execution.
6. Build the standard search and presentation evaluation harness.
7. Add artifact packaging, versioning, and finalization.
8. Add a minimal run inspector showing progress, validation, resources, and failures.

## Design principle

Gram should be autonomous within a controlled boundary. Safety should come from isolation, resource limits, validation, rollback, and recoverability—not from repeatedly asking the user for permission during normal work.
