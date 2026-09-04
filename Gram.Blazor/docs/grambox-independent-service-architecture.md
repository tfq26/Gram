# GramBox Independent Sandbox Service Architecture

## Purpose

GramBox is a proprietary sandbox execution platform shared by Gram, Lyra, and future products. GramBox owns sandbox creation, agent execution, resource enforcement, repository integrity, scheduling, cleanup, and result packaging.

Gram and Lyra are clients. They submit intent and consume status, events, diffs, artifacts, and execution results. They do not implement their own sandbox lifecycle or directly manage worker processes.

## Target topology

```text
Gram Web / Desktop / Mobile       Lyra and other clients
             \                         /
              \     GramBox Client   /
               +--------+------------+
                        |
                 GramBox API
                        |
        +---------------+----------------+
        |                                |
  Control-plane data                 Dispatch plane
  PostgreSQL                         Redis Streams
        |                                |
        +---------------+----------------+
                        |
                GramBox workers
             /          |           \\
     Native driver   OCI driver   Agent runner
```

The API, queue, and worker processes may initially run on one server PC. They must still communicate through explicit interfaces so they can be separated across multiple PCs later.

## Ownership boundary

### GramBox owns

- Sandbox creation and disposal.
- Native and OCI execution drivers.
- Resource budgets and enforcement reporting.
- Network and environment policy.
- Repository checkout and source verification.
- Agent process lifecycle.
- Scheduling, queues, leases, and worker health.
- Checkpoints, diffs, patches, logs, and result manifests.
- Cleanup and abandoned-run recovery.
- Audit events.

### Gram and Lyra own

- User intent and prompts.
- Product-specific workflows.
- Client-side presentation of progress and results.
- Approval or finalization of changes.
- Conversation and project context.
- Which result to accept, merge, publish, or discard.

## Source integrity model

Jobs must execute against immutable source snapshots.

1. The client prepares a commit, temporary ref, or uploaded archive.
2. The client records the exact commit SHA or content digest.
3. The job includes that immutable source identifier and an allowed-path manifest.
4. GramBox fetches only that source snapshot.
5. The worker verifies the source digest and file hashes before execution.
6. The sandbox receives a private working copy or mounted snapshot.
7. The agent may modify only files beneath the assigned sandbox root and allowed paths.
8. The worker verifies changed-file paths and hashes after execution.
9. Results are published as a patch, artifact, or isolated result branch.
10. The original branch is never changed automatically.

Jobs must never pull an unpinned `main` or default branch. Repository credentials are server-side connections or short-lived scoped credentials; they are not included in job payloads.

For uncommitted local files, the client creates a temporary upload ref or archive. The original local repository is not modified silently.

## Job contract

A job contains:

- Repository provider and repository identity.
- Immutable commit SHA or content digest.
- Allowed file paths.
- Agent profile, not an arbitrary executable.
- Model or provider reference.
- Task prompt and validation checks.
- Sandbox engine and resource class.
- Network mode and approved hosts.
- Idempotency key.
- Priority.
- Maximum runtime.
- Owning user, project, and client.

Secrets are resolved by GramBox from server-side configuration or a secrets manager. They are never echoed into logs or returned to clients.

## Job lifecycle

```text
Submitted
  -> Validating
  -> SourcePrepared
  -> Queued
  -> Assigned
  -> SandboxStarting
  -> AgentRunning
  -> ValidatingResult
  -> ResultReady
  -> Published
  -> CleanedUp
```

Failure states include `Rejected`, `Failed`, `Cancelled`, `TimedOut`, `WorkerLost`, and `IntegrityViolation`.

Every state transition records a timestamp, reason, worker identity, and job attempt. Clients can reconnect using a sequence number without duplicating events.

## Control plane

The control plane validates ownership, source references, agent profiles, resource requests, quotas, and policies. It creates durable job records and an outbox message in one database transaction.

Suggested API:

```text
POST   /v1/jobs
GET    /v1/jobs/{id}
POST   /v1/jobs/{id}/cancel
GET    /v1/jobs/{id}/events
GET    /v1/jobs/{id}/result
GET    /v1/jobs/{id}/diff
POST   /v1/jobs/{id}/publish
GET    /v1/workers
GET    /v1/capabilities
```

The API creates jobs; it does not execute agent processes directly.

## Queue and outbox

PostgreSQL is the source of truth. Redis Streams is the distributed dispatch transport. The transactional outbox prevents a job from being committed without a dispatch message.

```text
Database transaction:
  save job + save outbox message
          |
          v
Outbox processor publishes to Redis Stream
          |
          v
Worker claims message and reports status
```

The outbox processor may publish a message more than once, so workers must be idempotent. A worker acknowledges work only after it has durably recorded its assignment. Unacknowledged messages become available for recovery.

The development implementation may use the in-process queue. It must implement the same queue interface as Redis so tests do not depend on a particular transport.

## Workers

Workers register with:

- Worker ID and version.
- Host and platform.
- Available engines.
- CPU, memory, disk, and concurrency capacity.
- Current assignments.
- Last heartbeat.
- Health and drain state.

Workers heartbeat continuously. A worker that stops heartbeating is removed from scheduling and its jobs are marked `WorkerLost` or safely requeued according to their execution stage.

Dispatch must consider resource capacity, engine availability, network policy, priority, user fairness, and project quotas.

## Execution drivers

### Native driver

Native worktrees are the low-resource mode for trusted tasks. They provide Git and filesystem-change isolation, path guards, process limits, monitoring, and cleanup. They must not be described as a complete operating-system security boundary.

### OCI driver

OCI/Docker execution is the stronger isolation mode for untrusted tasks. Containers must:

- Mount only the assigned workspace.
- Never mount the host home directory or Docker socket.
- Use a non-root user where practical.
- Enforce CPU, memory, PID, disk, and output limits.
- Use `--network none` for disabled networking.
- Receive only approved environment variables.
- Be removed after result collection.

An explicit OCI request must fail clearly when no OCI runtime is available. It must not silently fall back to native execution.

## Caching

Caching is an optimization layer, never the source of truth.

Allowed cache data includes:

- Worker health and capabilities.
- Short-lived job status reads.
- Rate-limit counters.
- Queue-depth summaries.
- Deduplication and idempotency lookups.
- Bounded dependency caches.

Durable job state, ownership, quotas, audit records, and result references remain in PostgreSQL.

## File and result handling

Results should contain:

- Source commit SHA or input digest.
- Changed-file manifest.
- Before and after hashes.
- Diff or patch reference.
- Output artifact references.
- Validation results.
- Resource usage.
- Worker and engine identity.
- Failure or integrity reason.

GramBox should retain compact metadata after cleanup while deleting temporary worktrees, containers, and run-owned files according to the retention policy.

## Security rules

- Every job belongs to exactly one authenticated owner and project.
- Clients cannot select another user’s job, sandbox, artifact, or result.
- Repository access uses registered connections or short-lived scoped credentials.
- Paths are validated after resolution and symlink checks.
- Agent profiles are allowlisted.
- Network access is disabled by default.
- Sensitive environment variables are stripped by default.
- No generic host shell or unrestricted filesystem API is exposed.
- Publishing or merging a result is separate from execution.
- Integrity violations fail closed and preserve diagnostic metadata.

## Migration path

1. Keep the current GramBox runtime working in Gram.
2. Keep `GramBox.Contracts` and `GramBox.Client` frontend-neutral.
3. Complete the database-backed job and outbox implementation.
4. Add worker registration and heartbeats.
5. Add a standalone GramBox API host.
6. Move the existing native runtime behind a GramBox worker adapter.
7. Add Redis worker consumption and acknowledgements.
8. Add the OCI driver.
9. Change Gram and Lyra to submit jobs through the client package.
10. Retire direct Gram/Lyra references to sandbox internals.

## Acceptance criteria

- Gram and Lyra can submit jobs without knowing how sandboxes are implemented.
- Jobs survive API restarts.
- A database commit and dispatch message cannot become inconsistent.
- Duplicate submissions do not create duplicate sandboxes.
- Workers can be added or removed without client changes.
- Worker failures do not leave jobs permanently stuck.
- Source commits and changed-file hashes are verifiable.
- Results never write directly to a user’s primary branch.
- Native and OCI isolation levels are reported accurately.
- Temporary sandbox resources are cleaned up safely.
- The same contract works with in-process development transport and distributed Redis transport.
