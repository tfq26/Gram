import { useState } from 'react';
import {
  CircleAlertIcon,
  CircleCheckIcon,
  LoaderCircleIcon,
  TerminalIcon,
} from 'lucide-react';
import {
  Trace,
  TraceCollapsible,
  TraceCollapsibleContent,
  TraceCollapsibleTrigger,
  TraceIcon,
  TraceLine,
  TraceTitle,
} from '../../components/ui/trace';
import { CodeBlock, CodeBlockContent } from '../../components/ui/code-block';
import type { TaskItem, TaskStepItem, TraceStep } from '../../lib/types';

function getTaskStepStatus(
  status: TaskStepItem['status'],
  streaming: boolean
): 'running' | 'error' | undefined {
  if (status === 'done') return undefined;
  return status === 'running' && streaming ? 'running' : 'error';
}

function TaskStatusIcon({ status }: { status?: 'running' | 'error' }) {
  if (status === 'running') {
    return <LoaderCircleIcon size={13} className="animate-spin" />;
  }
  return status === 'error' ? <CircleAlertIcon size={13} /> : <CircleCheckIcon size={13} />;
}

export function TaskRow({ task, streaming }: { task: TaskItem; streaming: boolean }) {
  const [open, setOpen] = useState<boolean | undefined>(undefined);
  const steps = task.steps.map((step) => ({
    title: step.title,
    status: getTaskStepStatus(step.status, streaming),
  }));
  const status = steps.some((step) => step.status === 'error')
    ? 'error'
    : steps.some((step) => step.status === 'running')
      ? 'running'
      : undefined;

  return (
    <TraceCollapsible open={open ?? status === 'running'} onOpenChange={setOpen}>
      <TraceCollapsibleTrigger>
        <TraceLine status={status}>
          <TraceIcon>
            <TaskStatusIcon status={status} />
          </TraceIcon>
          <TraceTitle>{task.title}</TraceTitle>
        </TraceLine>
      </TraceCollapsibleTrigger>
      <TraceCollapsibleContent keepMounted>
        <Trace>
          {steps.map((step, idx) => (
            <TraceLine key={`${step.title}-${idx}`} status={step.status}>
              <TraceTitle>{step.title}</TraceTitle>
            </TraceLine>
          ))}
        </Trace>
      </TraceCollapsibleContent>
    </TraceCollapsible>
  );
}

function ReasoningRow({
  text,
  running,
  durationSeconds,
  defaultOpen,
}: {
  text: string;
  running: boolean;
  durationSeconds?: number;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState<boolean | undefined>(defaultOpen);
  const label = running
    ? 'Thinking'
    : typeof durationSeconds === 'number' && durationSeconds > 0
      ? `Thought for ${durationSeconds}s`
      : 'Thought';

  const line = (
    <TraceLine status={running ? 'running' : undefined}>
      <TraceIcon>
        {running ? <LoaderCircleIcon size={13} className="animate-spin" /> : <CircleCheckIcon size={13} />}
      </TraceIcon>
      <TraceTitle>{label}</TraceTitle>
    </TraceLine>
  );

  if (text.length === 0) {
    return line;
  }

  return (
    <TraceCollapsible open={open ?? (running || Boolean(defaultOpen))} onOpenChange={setOpen}>
      <TraceCollapsibleTrigger>{line}</TraceCollapsibleTrigger>
      <TraceCollapsibleContent className="whitespace-pre-wrap">{text}</TraceCollapsibleContent>
    </TraceCollapsible>
  );
}

function ToolRow({
  command,
  state,
  output,
  errorText,
  streaming,
  defaultOpen,
}: {
  command: string;
  state: 'running' | 'output-available' | 'output-error';
  output?: string;
  errorText?: string;
  streaming: boolean;
  defaultOpen?: boolean;
}) {
  const rawLabel = (command || '').trim();
  const displayLabel = rawLabel ? (rawLabel.length > 68 ? `${rawLabel.slice(0, 65)}...` : rawLabel) : 'Ran command';

  if (state === 'output-available' || state === 'output-error') {
    const failed = state === 'output-error';
    return (
      <TraceCollapsible defaultOpen={defaultOpen}>
        <TraceCollapsibleTrigger>
          <TraceLine status={failed ? 'error' : undefined}>
            <TraceIcon>
              <TerminalIcon size={13} />
            </TraceIcon>
            <TraceTitle>
              <span title={rawLabel || 'Ran command'}>
                {failed ? `Failed: ${displayLabel}` : displayLabel}
              </span>
            </TraceTitle>
          </TraceLine>
        </TraceCollapsibleTrigger>
        <TraceCollapsibleContent>
          <CodeBlock size="sm">
            <CodeBlockContent wrap className="max-h-64">
              {failed ? errorText : output}
            </CodeBlockContent>
          </CodeBlock>
        </TraceCollapsibleContent>
      </TraceCollapsible>
    );
  }

  return (
    <TraceLine status={streaming ? 'running' : 'error'}>
      <TraceIcon>
        <TerminalIcon size={13} />
      </TraceIcon>
      <TraceTitle>
        <span title={rawLabel || 'Running command'}>
          {streaming ? (rawLabel ? displayLabel : 'Running command') : `Failed: ${displayLabel}`}
        </span>
      </TraceTitle>
    </TraceLine>
  );
}

export function StepRow({
  step,
  streaming,
  busy,
}: {
  step: TraceStep;
  streaming: boolean;
  busy?: boolean;
}) {
  if (step.type === 'reasoning') {
    return (
      <ReasoningRow
        text={step.text}
        running={streaming && step.state === 'streaming'}
        durationSeconds={step.durationSeconds}
        defaultOpen={busy ? undefined : true}
      />
    );
  }

  return (
    <ToolRow
      command={step.command}
      state={step.state}
      output={step.output}
      errorText={step.errorText}
      streaming={streaming}
      defaultOpen={!streaming && step.state === 'output-error'}
    />
  );
}

export function checkpointInspectionSteps(checkpoint: any): TraceStep[] {
  return [
    ...(Array.isArray(checkpoint?.toolCalls)
      ? checkpoint.toolCalls.map((tool: any) => ({
          type: 'tool-runCommand' as const,
          command: tool.command || tool.name || 'tool',
          state: tool.error ? ('output-error' as const) : ('output-available' as const),
          output: tool.result,
          errorText: tool.error,
        }))
      : []),
    ...(Array.isArray(checkpoint?.diffs)
      ? checkpoint.diffs.map((diff: any) => ({
          type: 'tool-runCommand' as const,
          command: `GramFS diff: ${diff.path}`,
          state: 'output-available' as const,
          output: diff.patch || (diff.isDeleted ? '(deleted)' : '(no text diff)'),
        }))
      : []),
  ];
}
