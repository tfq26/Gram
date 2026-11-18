# Oberon sync architecture

## Decision

Oberon owns cross-application sync. Gram and Lyra keep local SQLite caches and durable outboxes, then exchange changes through the Oberon protocol in `src/sync/oberonProtocol.ts`. Oberon owns account/device authorization, canonical revisions, ordering, tombstones, conflict responses, and shared blob storage.

Git remains the source of truth for committed project code. Oberon stores the portable project identity and resumable work state between commits. Each execution device stores its own checkout path and derived search index locally.

## Change envelope

Each mutation has a stable `changeId`, application and entity IDs, operation, entity revision, base cloud revision, timestamp, and typed payload. The client writes the change to its local outbox before sending it. Repeated delivery of the same `changeId` is idempotent. Oberon acknowledges accepted IDs and returns a monotonic cursor plus explicit conflicts. Clients remove only acknowledged outbox entries.

The authentication token identifies the account and authorized device. Clients do not choose an account ID in the request body. Oberon checks app and device ownership for each entity. Deletes are tombstones until every authorized client has advanced past them or the retention policy expires.

## Entity policies

- **Chat messages:** immutable message records with stable IDs. Oberon merges independent additions by ID and retains divergent edits as conflicts. Session title, mode, and project binding use revision checks because those fields are mutable.
- **Personal context:** account-scoped records with provenance, category, revision, and tombstones. A device edit to the same record returns a conflict; deletions cannot be undone by a stale offline copy.
- **Projects:** sync stable project ID, repository identity, display metadata, and project instructions. Do not sync an absolute workspace path as portable identity. Each device maintains a local `projectId -> checkoutPath` binding.
- **Work checkpoints:** identify the project, work session, base commit SHA, branch, revision, chat session, plan/progress summary, verification state, and patch/blob references. Checkpoints remain separate from Git commits.
- **Blobs:** large checkpoint deltas and attachments are uploaded by content hash. Oberon publishes a checkpoint manifest only after all referenced blobs are durable and verified.

## Device handoff

When a user pauses work or switches devices, Gram records an incremental checkpoint against the current Git base SHA. The next device syncs chat, project metadata, and the checkpoint, resolves its local checkout binding, creates an isolated worktree at that base, and applies the delta. If the remote branch has advanced, Gram attempts a three-way merge in the isolated worktree and reports any conflicts before changing the user's checkout.

Git handles coherent source-control milestones. Gram stages only files the user selected, previews the staged diff, then commits and pushes after the user requests those actions. The checkpoint lets work move between devices without creating a Git commit for every edit.

## Search and local execution

Chat messages, personal context, project notes, and work checkpoints sync as source records. Each execution host builds its own derived FTS and vector indexes from records it has received. The index is versioned by source revision and embedding model; it can be deleted and rebuilt without data loss. Search reports when sync or indexing is incomplete. Embeddings stay on the execution host unless a future setting explicitly permits cloud embedding.

## Current Gram integration status

The current local AHP adapter still sends chat/task snapshots to Lyra. `projects`, personal-context records, GramFS overlays, and local worktree state are not part of that payload. The local client now has a first message-merge and conflict-review layer, but the authoritative merge must live in Oberon. Gram should migrate to `OberonSyncTransport` after the Oberon server implements the shared envelope; Lyra should use the same client contract. Keep the existing AHP path as a migration adapter until both clients and Oberon have compatible rollout versions.

## Rollout order

1. Implement Oberon's authenticated account/device model, idempotent change log, cursor pull, tombstones, and conflict responses.
2. Sync project identity, personal-context records, and chat messages with per-entity revisions.
3. Add content-addressed work-checkpoint blobs and device-local checkout bindings.
4. Move Gram and Lyra behind the shared Oberon transport while retaining the legacy adapter during migration.
5. Build local derived search indexes and exercise offline reconnect, duplicate delivery, concurrent edits, deletes, and base-commit divergence.
