# GramBox SDK Boundary

GramBox is a reusable proprietary sandbox platform. Gram, Lyra, and future products consume it through stable contracts and a client rather than referencing Gram-specific runtime classes.

## Assemblies

- `clients/GramBox.Contracts` contains public request, result, lifecycle, resource, and event types.
- `clients/GramBox.Client` contains `IGramBoxClient` and the REST/SSE implementation.
- Gram’s existing `Shared/` classes are the current runtime and remain private implementation details.

The runtime will be exposed through `/api/v1/sandboxes` in the next slice. Until that endpoint is connected, the SDK establishes the compatibility boundary without changing the existing in-process GramBox execution path.

## Consumer rule

Consumers should depend only on `GramBox.Contracts` and `IGramBoxClient`. They should not reference `GramBoxService`, `NativeScratchpadDriver`, Git worktree managers, or platform controllers.

```csharp
var box = new GramBoxClient(httpClient);
var sandbox = await box.CreateAsync(new CreateSandboxRequest(repositoryPath, RunId: runId));
var result = await box.ExecuteAsync(sandbox.Id, new ExecuteCommandRequest("dotnet test"));
var diff = await box.GetDiffAsync(sandbox.Id);
await box.CloseAsync(sandbox.Id);
```

The client exposes named capabilities only: create, execute, read, write, diff, events, and close. It does not expose a generic host shell, arbitrary host filesystem access, Docker socket access, or provider credentials.
