export interface EvalToolCall {
  name: string;
  command: string;
  result?: string;
  error?: string;
}

export interface HarnessEvalCase {
  id: string;
  title: string;
  prompt: string;
  requireInspectFirst?: boolean;
  requireVerifyAfterEdit?: boolean;
  forbidFullFileDump?: boolean;
  expectedTargetPattern?: RegExp;
}

export interface HarnessEvalScore {
  caseId: string;
  passed: boolean;
  score: number;
  checks: Array<{ name: string; passed: boolean; detail: string }>;
}

const INSPECT_TOOLS = new Set(['list_files', 'read_file', 'search_code', 'find_symbol_definition']);
const EDIT_TOOLS = new Set(['write_file', 'edit_file', 'apply_changeset', 'delete_file']);
const VERIFY_TOOLS = new Set(['validate_project', 'run_tests']);

function isVerificationTool(tc: EvalToolCall): boolean {
  if (VERIFY_TOOLS.has(tc.name)) return true;
  if (tc.name === 'run_command') {
    return /\b(test|build|typecheck|tsc|lint|check|pytest|cargo test|go test|dotnet test)\b/i.test(tc.command);
  }
  return false;
}

/**
 * Grades an agent run trajectory and output against senior-engineering harness standards.
 */
export function evaluateHarnessRun(
  evalCase: HarnessEvalCase,
  run: { output: string; toolCalls: EvalToolCall[] }
): HarnessEvalScore {
  const checks: Array<{ name: string; passed: boolean; detail: string }> = [];
  const toolCalls = run.toolCalls || [];

  const firstInspectIdx = toolCalls.findIndex((t) => INSPECT_TOOLS.has(t.name));
  const firstEditIdx = toolCalls.findIndex((t) => EDIT_TOOLS.has(t.name));

  if (evalCase.requireInspectFirst !== false && firstEditIdx !== -1) {
    const passed = firstInspectIdx !== -1 && firstInspectIdx < firstEditIdx;
    checks.push({
      name: 'inspect_first',
      passed,
      detail: passed
        ? `Inspected via ${toolCalls[firstInspectIdx]?.name} before editing`
        : 'Edited files without inspecting workspace first',
    });
  }

  let lastEditIdx = -1;
  let lastVerifyIdx = -1;
  toolCalls.forEach((t, idx) => {
    if (EDIT_TOOLS.has(t.name) && !t.error) lastEditIdx = idx;
    if (isVerificationTool(t) && !t.error) lastVerifyIdx = idx;
  });

  if (evalCase.requireVerifyAfterEdit !== false && lastEditIdx !== -1) {
    const passed = lastVerifyIdx > lastEditIdx;
    checks.push({
      name: 'verify_after_edit',
      passed,
      detail: passed
        ? `Verified via ${toolCalls[lastVerifyIdx]?.name} after last edit`
        : 'Modified files without running verification afterward',
    });
  }

  if (evalCase.forbidFullFileDump !== false) {
    const largeCodeBlock = /```[\w-]*\n([\s\S]*?)```/g;
    let hasDump = false;
    let match: RegExpExecArray | null;
    while ((match = largeCodeBlock.exec(run.output || '')) !== null) {
      if ((match[1] || '').split('\n').length > 18) {
        hasDump = true;
        break;
      }
    }
    checks.push({
      name: 'no_full_file_dump',
      passed: !hasDump,
      detail: hasDump ? 'Response dumped a large (>18 line) code block into chat' : 'Response stayed concise',
    });
  }

  if (evalCase.expectedTargetPattern) {
    const matched = toolCalls.some((t) => evalCase.expectedTargetPattern!.test(t.command));
    checks.push({
      name: 'target_accuracy',
      passed: matched,
      detail: matched ? 'Targeted expected path/command' : 'Did not target expected path/command',
    });
  }

  const passedCount = checks.filter((c) => c.passed).length;
  const score = checks.length > 0 ? Math.round((passedCount / checks.length) * 100) : 100;
  return {
    caseId: evalCase.id,
    passed: checks.every((c) => c.passed),
    score,
    checks,
  };
}

export const DEFAULT_HARNESS_EVAL_CASES: HarnessEvalCase[] = [
  {
    id: 'greenfield-website-clarify',
    title: 'Inspect empty target folder and clarify greenfield stack',
    prompt: 'I want to build a small website for Gram. Can you inspect the folder first and ask before picking a framework?',
    requireInspectFirst: true,
    requireVerifyAfterEdit: false,
    forbidFullFileDump: true,
  },
  {
    id: 'edit-verify-bugfix',
    title: 'Inspect, patch a bug, and verify tests pass',
    prompt: 'Fix the failing unit test and verify the test suite passes.',
    requireInspectFirst: true,
    requireVerifyAfterEdit: true,
    forbidFullFileDump: true,
  },
];
