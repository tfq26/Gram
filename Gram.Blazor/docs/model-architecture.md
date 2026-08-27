# Gram Model Architecture

## Goal

Make model access provider-neutral so workflows can choose models by capability, latency, privacy, quota, and cost without embedding provider logic in the agent runtime.

## Layering

```text
Workflow
  → ModelGateway
  → ModelPolicy and quota service
  → provider adapter
  → normalized stream/result
```

## Model gateway

The gateway owns model resolution, credentials, rate limiting, timeout classification, retries, usage telemetry, and normalized errors. Workflows request a model capability; they do not construct provider HTTP payloads.

```csharp
public interface IModelGateway
{
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);
}
```

## Model policy

- Gram Q uses the Groq system key.
- Gram Q+ uses the Google system key.
- Platform models never use user BYOK keys.
- Presentation generation uses Q+ and queues when Q+ is unavailable.
- Local Ollama models are available for privacy, testing, and quota-independent workloads.

Routing considers task capability, context size, tool support, latency, cost, privacy, and current quota. Fallbacks must be explicit and must not silently reduce quality for workflows where quality is a requirement.

## Workflow-specific use

- `ChatWorkflow`: normal model response and optional tools.
- `ResearchWorkflow`: receives an evidence packet and produces cited answers.
- `PresentationWorkflow`: plans and delegates sections, then assembles artifacts.
- `CodingWorkflow`: uses the sandbox and validation services.
- `VoiceWorkflow`: uses concise conversational prompting and TTS.

## Streaming contract

Normalize provider output into text deltas, completion, usage, tool requests, and typed errors. A provider timeout, rate limit, quota exhaustion, and invalid request must remain distinguishable after normalization.

## Context policy

The gateway receives already-assembled context from the workflow. It should not scan the workspace, perform search, or invent additional context. Prompt construction remains explicit and observable.

## Evaluation

Every model can be evaluated against the same controlled corpus and task suite. Record completion rate, latency, token usage, cost, citation coverage, expected-answer coverage, timeout rate, and error class.
