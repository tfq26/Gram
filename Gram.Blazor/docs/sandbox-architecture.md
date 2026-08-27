# Gram Sandbox Architecture

## Purpose

Gram Box is the autonomy boundary for work that may execute code, modify many files, install dependencies, or run unknown scripts. It is not required for ordinary chat, search, voice, summarization, or narrow deterministic operations.

## Current state

The current implementation creates native Git worktrees and runs host processes in them. This is efficient and protects the base Git branch, but it is not a complete operating-system security boundary. Host filesystem access, network access, CPU, memory, disk, and child processes still require stronger enforcement.

## Target flow

```text
AgentRun
  → SandboxScheduler
  → SandboxLease
  → StorageLayer
  → ProcessExecutor
  → Checkpoints and logs
  → Validation
  → Diff/artifact/finalization
```

Each run owns its sandbox explicitly. No agent uses a global active session.

## Storage model

Use a read-only project base plus a per-run writable layer. Select the implementation according to platform, repository size, available disk, and concurrency:

- Native Git worktree for the initial lightweight path.
- Sparse worktree for partial repositories.
- Copy-on-write filesystem clone where supported.
- Overlay filesystem on Linux for large concurrent projects.
- Hardened runner for untrusted commands.

Shared caches are read-only from the agent’s perspective. Build output, temporary files, and artifacts are run-owned and quota-controlled.

## Scheduler and leases

The scheduler reserves bounded resource classes:

```text
Tiny    1 CPU / 1 GB RAM
Small   2 CPU / 2 GB RAM
Medium  4 CPU / 8 GB RAM
Large   8 CPU / 16 GB RAM
```

It reserves capacity for the operating system and Gram, queues work when capacity is unavailable, and records CPU, memory, disk, process, timeout, and network budgets in the lease.

## Isolation rules

- All paths resolve beneath the run’s storage root.
- Reject traversal, absolute-path escapes, and symlink escapes.
- Every command runs through one controlled executor.
- Every child process belongs to the run’s process group.
- Cancellation terminates the complete process tree.
- Network access is disabled, allowlisted, or unrestricted by explicit run policy.
- Secrets are not inherited by default.

## Parallel agents

Agents receive separate writable workspaces and may share only immutable bases and caches. Changes merge through an integration queue:

```text
agent workspace → validate → rebase/merge → integration tests → package
```

Conflicts are explicit and recoverable; no workspace is silently overwritten.

## Completion and recovery

Checkpoints occur after setup, implementation, tests, review, and artifact creation. Failed runs resume from the last valid checkpoint where possible. Startup recovery finds abandoned leases, terminates surviving processes, prunes storage, and preserves logs.

## Platform strategy

- Linux: cgroups, namespaces, and overlayfs.
- Windows: Job Objects and process-tree controls.
- macOS: path/access controls, APFS optimizations, and a hardened helper when native processes need stronger isolation.

The native runner is the fast default; hardened execution is introduced only when the task’s risk or policy requires it.
