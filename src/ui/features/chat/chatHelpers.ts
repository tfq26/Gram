/**
 * Pure helper functions for the chat UI.
 */

export function isCodingWorkPrompt(text: string): boolean {
  const t = text.trim();
  if (!t) return false;
  return (
    /\b(edit|modify|update|fix|refactor|debug|implement|build|create|write|add|remove|delete|test|run\s+tests?|compile|typecheck|commit|pr|pull\s+request|repo|repository|codebase|file|files|function|class|component|endpoint|api|bug|error|stack\s*trace|css|html|react|typescript|rust|tauri|python|sql)\b/i.test(
      t
    ) &&
    /\b(in\s+(?:the\s+)?(?:repo|codebase|project|app|file|workspace)|fix\s+(?:this|the|my)|write\s+(?:a|the|some)|build\s+(?:a|the|me)|implement|refactor|run\s+(?:tests?|command|build)|debug|edit|modify|create\s+(?:a|the)\s+(?:file|component|endpoint|script|app))\b/i.test(
      t
    )
  );
}
