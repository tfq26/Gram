# Gram Autonomous Coding Engine: 5 Multi-Worktree Scenarios Evaluation

This evaluation tests Gram's end-to-end coding capabilities (codebase inspection, diagnosis, surgical patching, build/test execution, and error recovery) against 5 distinct scenarios in isolated Git worktrees.

## Scenarios Specification

| Scenario | Title | Type | Files Touched | Verification Target |
| :--- | :--- | :--- | :--- | :--- |
| **1** | String Edge Cases & Bound Handling | Bug Fix | `TextTransformer.cs`, `TextTransformerTests.cs` | `Slugify` consecutive special characters + `TruncateWithEllipsis` zero/negative bounds |
| **2** | Shipping Calculation Engine | Feature Addition | `OrderPricingService.cs`, `OrderPricingServiceTests.cs` | Implement `CalculateShipping` business rules with comprehensive tests |
| **3** | Structured Pricing Breakdown | Contract Refactoring | `OrderPricingService.cs`, `OrderPricingServiceTests.cs` | Refactor `CalculateTotal` return type to `PricingBreakdown` record |
| **4** | Extensible Tax Provider | Dependency Abstraction | `OrderPricingService.cs`, `ITaxCalculator.cs`, `OrderPricingServiceTests.cs` | Interface extraction, default implementation, polymorphism tests |
| **5** | Multi-File Discrepancy Diagnostics | Multi-Failure Repair | `OrderPricingService.cs`, `TextTransformer.cs` | Diagnose 2 concurrent failing tests in different files and fix both |

## Protocol per Scenario
1. **Worktree Isolation**: Create a dedicated git worktree from `main` (e.g. `/Users/taufeeqali/Projects/GramLab_wt<N>`).
2. **Setup**: Introduce the broken test or feature contract.
3. **Execution**: Trigger Gram via API against the worktree with `allowLocalCapabilities: true`.
4. **Observation**: Monitor Gram's thoughts, tool calls (`view_file`, `edit_file`, `write_file`, `run_command`), and responses.
5. **Evaluation & Fix**:
   - If Gram encounters an issue due to tool handling, argument parsing, diff matching, or platform handling, analyze Gram's core code, apply a **general, multiplatform fix to Gram's processing**, restart Gram, and re-run.
   - If Gram succeeds, run `dotnet test` independently to verify.
6. **Cleanup**: Delete the worktree and branch.

---

## Scenario Execution Log

| Scenario | Initial Result | Root Cause (if failed) | Gram Processing Hardening | Final Result |
| :--- | :--- | :--- | :--- | :--- |
| **Scenario 1** | Passed | None (Executed run_command, view_file x2, edit_file x2, verify run_command) | None required (Surgical edits and test verification passed cleanly) | Passed (24/24 tests) |
| **Scenario 2** | Passed | None (Executed view_file x2, edit_file x2, verify run_command) | None required (Added business logic and 6 unit tests with 100% pass rate) | Passed (38/38 tests) |
| **Scenario 3** | Passed | None (Executed view_file x2, edit_file x4, verify run_command) | None required (Refactored core contract + updated all consuming tests) | Passed (20/20 tests) |
| **Scenario 4** | Failed (Premature termination) | Turn budget exhausted at 8 iterations; excessive turns spent on shallow directory exploration; cross-platform path check bug in grep | 1. Raised `MaxIterations` to 25 for local workflows in `AgentWorkflows.cs` & `AgentOrchestrator.cs`.<br>2. Made `list_directory` recursive (depth 2) with noise filtering.<br>3. Fixed cross-platform path separator normalization in `grep_search`. | Passed (21/21 tests) |
| **Scenario 5** | Passed | None (Executed run_command x2, view_file x3, edit_file x2) | None required (Accurately isolated multi-file failures, patched both bugs, and verified test suite) | Passed (20/20 tests) |

## Hardening Summary Applied to Gram Processing

1. **Autonomous Turn Budget Elasticity**:
   - Increased default agent iteration limit from 8 to 25 for Coding, Chat, and Research workflows with local capabilities enabled (`AgentWorkflows.cs`, `AgentOrchestrator.cs`).
   - Added explicit notification `[Agent reached maximum loop iterations (25). Stopping.]` instead of silent termination.
2. **Subtree Exploration Optimization (`list_directory`)**:
   - Augmented `list_directory` to traverse directory subtrees up to depth 2 with automatic noise suppression (`.git`, `bin`, `obj`, `node_modules`, `.vs`), allowing the model to understand repository structure in 1 call instead of 4–6 calls (`AgentTools.cs`).
3. **Cross-Platform Path Sanitization (`grep_search`)**:
   - Replaced Windows-only path filtering (`\\bin\\`, `\\obj\\`) with cross-platform normalized path separators (`p.Replace('\\', '/')`), ensuring proper ignore filtering across macOS, Linux, and Windows (`AgentTools.cs`).

