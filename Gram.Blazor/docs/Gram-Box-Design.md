# Gram Box Sandbox Design

## Purpose

Gram Box gives autonomous models a disposable project workspace. The model should be able to create, edit, delete, build, and test files freely inside that workspace while the user’s real workspace remains unchanged.

## Current implementation

```text
Agent tool
   ↓
GramBoxService
   ↓
GramBoxWorktreeManager ── git worktree ──> ~/.gram/box/worktrees/sb-...
   ↓
NativeScratchpadDriver
   ↓
host shell process
```

The current sandbox is primarily a Git isolation mechanism:

- A new branch and worktree are created for a run.
- File changes are isolated from the base checkout.
- Commands execute in the worktree.
- Shared dependency caches reduce duplicate storage.
- Environment variables containing common secrets are removed.
- Commands have timeouts and can be cancelled.
- The result can be diffed, committed, or discarded.

## Important boundary

The native worktree is not an operating-system security boundary. It protects the base Git checkout, but the host process can still potentially access other files, start child processes, use the network, consume resources, or follow filesystem links unless those behaviors are explicitly constrained.

```text
Protected today: Git branch and normal worktree changes
Not fully protected today: host filesystem, network, memory, CPU, disk, processes
```

## Target architecture

```text
AgentRun
   ↓ owns
SandboxLease
   ↓ owns
WorktreeSession
   ↓ launches
ExecutionProcess
   ↓ produces
Checkpoint + logs + diff + artifacts
```

For parallel prototypes:

```text
Project run
├── Prototype A → Lease A → Worktree A → Process group A
├── Prototype B → Lease B → Worktree B → Process group B
└── Prototype C → Lease C → Worktree C → Process group C
```

No prototype should use a global active session. A run always passes its sandbox handle explicitly to every file, command, and artifact operation.

## Execution tiers

### Native Worktree

Use for low-risk, trusted, local development tasks:

- Fast startup.
- Low memory overhead.
- Shared package caches.
- Builds, tests, and ordinary prototypes.

### Hardened Runner

Use when commands are untrusted or need stronger isolation:

- Restricted filesystem root.
- Controlled network access.
- Resource limits enforced by the operating system.
- Separate process/user namespace or VM-backed boundary.

The hardened runner does not need to run every task. It is the safety tier for risky workloads.

## Sandbox lease lifecycle

```text
Allocate
  ↓
Prepare worktree and limits
  ↓
Running
  ↓
Checkpoint / validate
  ├── continue
  ├── rollback
  └── finalize result
  ↓
Released and cleaned
```

Every lease should persist:

- Run ID and user/session owner.
- Sandbox ID and worktree path.
- Base repository path.
- Process group IDs.
- Start and last-heartbeat timestamps.
- CPU, memory, disk, network, and timeout budgets.
- Current lifecycle state.

On startup, Gram should recover abandoned leases, terminate surviving processes, prune worktrees, and preserve logs before cleanup.

## Resource controls

Resource settings must be enforced rather than only stored in configuration:

- Wall-clock timeout.
- CPU priority or CPU-time budget.
- Memory limit.
- Disk and artifact size limit.
- Maximum child-process count.
- Maximum output size.
- Maximum concurrent processes per user and per project.

## Filesystem rules

All paths must resolve beneath the assigned worktree. The executor should reject:

- `..` traversal.
- Absolute paths outside the worktree.
- Symlink escapes.
- Work directories outside the lease root.
- Artifact paths that bypass the project artifact directory.

Commands should use the same controlled execution path as file tools so security checks are not bypassed by a separate code path.

## Autonomous completion

The model does not need approval for normal work inside the sandbox. Gram determines completion through validation:

```text
implementation complete
→ build passes
→ tests pass
→ secrets scan passes
→ scope/diff check passes
→ artifact exists
→ result packaged
```

Only finalization actions cross the sandbox boundary:

- Apply diff to the user workspace.
- Push to a remote repository.
- Publish or send an artifact externally.

## Recommended build order

1. Replace active-session fallback with explicit per-run sandbox handles.
2. Add a bounded sandbox scheduler for parallel prototypes.
3. Add leases, heartbeats, startup recovery, and automatic cleanup.
4. Enforce path and process-tree confinement.
5. Enforce resource budgets.
6. Add checkpoints and rollback.
7. Add the hardened runner for untrusted commands.
8. Add validation-driven finalization and artifact packaging.

## Design conclusion

Gram Box is currently a lightweight and efficient Git workspace isolator. It should remain the fast default, but it should not yet be treated as a complete secure sandbox. The target is an autonomy-first execution system where isolation, scheduling, validation, checkpoints, and rollback provide safety without interrupting the model during normal work.
