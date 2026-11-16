# Technical Audit Prompt Specification

The following prompt specifies the scope, ground rules, and deliverables for the comprehensive technical audit of Gram.

---

```markdown
You are a senior staff engineer doing a rigorous technical audit of this codebase. 
It is intended to be a model-agnostic alternative to OpenAI Codex: a tool that lets 
ANY LLM write code, chat with the user, and run automations. It currently exists as 
multiple loosely connected parts. Your job is to tell me honestly whether this is a 
coherent product or a collection of experiments, and what to do about it.

## Ground rules
- Read the actual code before judging. Explore the repo structure, entry points, 
  configs, and docs first. Do not assume from filenames.
- Every finding must cite evidence: file path, function/component name, and a 
  line range or snippet. No evidence, no finding.
- Be blunt. Do not soften problems or pad with praise. If something is good, say 
  so briefly and move on.
- Distinguish between: (a) broken, (b) works but fragile, (c) works but 
  inconsistent/unscalable, (d) fine.
- Flag anything that looks "vibe coded": duplicated logic, dead code, 
  copy-pasted prompts, magic strings, hardcoded model names/paths/keys, 
  inconsistent naming, missing error handling, unused abstractions, 
  and features that exist in the UI but not in the backend (or vice versa).
- If you cannot determine something, say what you'd need to see.

## Phase 0: Product definition & inventory
1. Map every component/part of the project: what it is, what language/framework, 
   how it runs, and how it connects to the others.
2. State in 3 sentences what this product actually does today (not what it aspires to).
3. Identify the core user loop (e.g., user gives task -> agent plans -> edits 
   code -> runs/tests -> reports). Is that loop implemented end to end, 
   or only in pieces?
4. List the overlaps and gaps between parts. Which parts duplicate each other? 
   Which required pieces are missing?
5. Rate overall coherence 1-10 and justify it.

## Phase 1: System design & architecture
- Draw the architecture (ASCII or Mermaid): components, data flow, process 
  boundaries, where state lives.
- Evaluate separation of concerns: UI, orchestration/agent loop, model 
  provider layer, tool execution, persistence, automation/scheduling.
- Model-agnosticism: is there a real provider abstraction, or are 
  provider-specific assumptions leaking everywhere (message formats, tool-call 
  schemas, streaming, token limits, system-prompt handling)? What breaks if I 
  swap in a different model?
- Agent loop design: planning, tool calling, retries, stop conditions, 
  loop/runaway protection, cancellation, context management, 
  and recovery from failed steps.
- Tool/sandbox layer: how are file edits, shell commands, and network access 
  executed? Is there sandboxing, permissioning, or approval flow? Evaluate the 
  security risks (arbitrary command execution, path traversal, secret exposure, 
  prompt injection via file contents or web content).
- State & persistence: sessions, history, project context, config, and 
  secrets. What happens on crash or restart?
- Automation: how are triggers, schedules, and background tasks 
  modeled? Are they reliable, observable, and idempotent?
- Observability: logging, tracing of model calls and tool calls, cost/token 
  tracking, debuggability of a bad agent run.
- Scalability and extensibility: how hard is it to add a new model, tool, 
  or automation? Show the exact files I would touch.
- Output: top architectural risks, ranked, with a proposed target 
  architecture.

## Phase 2: Prompt structuring
- Locate every prompt in the system (system prompts, tool descriptions, 
  planner/executor prompts, summarizers, automation prompts). Inventory them in 
  a table: location, purpose, size, model assumptions.
- Evaluate: Are prompts versioned, templated, and centralized, or scattered 
  inline strings? Is there a clear separation of role, task, context, 
  constraints, and output format?
- Check for: conflicting instructions, vague success criteria, missing 
  output schemas, over-long prompts, prompt-level hacks compensating for code 
  bugs, and model-specific phrasing that won't transfer.
- Tool definitions: are names, descriptions, and parameter schemas precise 
  enough for different models to use correctly? 
- Context engineering: what goes into the context window per step, how it is 
  selected/truncated/summarized, and what happens at the limit.
- Is there any prompt evaluation or regression testing? If not, propose a 
  minimal harness.
- Output: rewrite the 2 weakest prompts as examples of the standard you'd hold.

## Phase 3: Modeling / model layer
- How are models selected, configured, and routed? Is there a capability 
  registry (context size, tool support, streaming, vision, cost)?
- Handling of differences across providers and local models: tool-calling 
  formats, structured output reliability, failures and malformed responses.
- Fallbacks, retries, rate limits, timeouts, streaming error handling.
- Output validation: how are model outputs parsed and verified before 
  being acted on? What happens when the model returns garbage?
- Evaluation: is there any way to measure how well a given model performs 
  in this tool? Propose a small benchmark (task set + pass/fail criteria).
- Cost and latency awareness.

## Phase 4: Code generation & editing quality
- How does the system produce code changes: whole-file rewrites, diffs, 
  patches, AST edits? Evaluate reliability and failure modes of the chosen approach.
- Repo understanding: how does the agent find relevant files (search, 
  indexing, embeddings, manual selection)? How does it handle large repos?
- Verification loop: does it run tests, linters, or type checks and feed 
  results back? Does it detect when its own change broke something?
- Safety of edits: diff review, undo/rollback, git integration, protection 
  against clobbering user changes.
- Compare against what Codex/Claude Code-style tools do well and identify 
  the largest gaps.

## Phase 5: UI / UX
- Evaluate the UI as a product, not a demo: information architecture, 
  core flows (start a task, watch progress, review changes, approve/reject, 
  interrupt), and error states.
- Does the UI honestly reflect the agent's state (streaming, tool calls, 
  diffs, failures), or does it hide or fake things?
- Chat vs. code vs. automation: is there a coherent mental model for the 
  three modes, or three apps stapled together?
- Frontend code quality: component structure, state management, 
  duplicated logic, API contract with the backend, accessibility basics, 
  responsiveness.
- Output: the 5 UX problems most likely to make a new user quit.

## Phase 6: Code quality & engineering hygiene
- Structure, naming, consistency, dead code, duplication, dependency health.
- Error handling and typing discipline.
- Tests: what exists, what coverage is meaningful, what critical paths 
  have none.
- Config, secrets handling, environment setup, README accuracy, 
  and whether a new developer could run it in under 15 minutes.
- CI, linting, formatting, versioning.

## Deliverable format
1. Executive verdict (max 10 lines): is this a product or a pile of experiments, 
   and why.
2. Scorecard table: each phase scored 1-10 with one-line justification.
3. Findings per phase, each tagged [CRITICAL / HIGH / MEDIUM / LOW], with 
   evidence and a concrete fix.
4. "Vibe-code smell" list: the specific places that read as unplanned or 
   generated without design.
5. Recommended target architecture and a consolidated repo/module layout.
6. A prioritized roadmap: 
   - Next 3 days (stabilize and unify)
   - Next 2 weeks (core loop reliability + model abstraction)
   - Next 6 weeks (evals, UX, automation hardening)
   Each item should state what to do, why it matters, and how I'll know it's done.
7. A "cut list": features, files, or components I should delete or defer 
   because they dilute the product.
8. The 5 questions you'd ask me as the author before trusting this design.

Do not make any code changes yet. Audit and report only. Start with Phase 0 
and show me the inventory before proceeding to the later phases.
```
