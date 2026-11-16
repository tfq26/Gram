/**
 * Managed Background Process Runner for Gram
 *
 * Lets Code mode launch long-running services/servers (API hosts, workers, dev servers)
 * without being killed by run_command's 15s timeout, while exposing status, logs, and stop controls.
 */

export type ManagedProcessStatus = 'running' | 'exited' | 'stopped';

export interface ManagedProcessSnapshot {
  id: string;
  label: string;
  command: string;
  cwd: string;
  pid: number;
  status: ManagedProcessStatus;
  exitCode: number | null;
  startedAt: string;
  stoppedAt?: string;
}

interface ManagedProcessRecord extends ManagedProcessSnapshot {
  proc: ReturnType<typeof Bun.spawn>;
  logLines: string[];
  totalBytes: number;
}

const MAX_LOG_LINES = 500;
const MAX_LOG_BYTES = 64 * 1024;

export class ManagedProcessManager {
  private readonly processes = new Map<string, ManagedProcessRecord>();
  private counter = 0;

  private appendLog(record: ManagedProcessRecord, chunk: string, streamName: 'stdout' | 'stderr'): void {
    if (!chunk) return;
    const prefix = streamName === 'stderr' ? '[stderr] ' : '';
    const lines = chunk.replace(/\r\n/g, '\n').split('\n');
    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      if (i === lines.length - 1 && line === '') continue;
      const entry = `${prefix}${line}`;
      record.logLines.push(entry);
      record.totalBytes += entry.length + 1;
    }
    while (record.logLines.length > MAX_LOG_LINES || record.totalBytes > MAX_LOG_BYTES) {
      const removed = record.logLines.shift();
      if (removed === undefined) break;
      record.totalBytes -= removed.length + 1;
    }
  }

  private pumpStream(
    stream: ReadableStream<Uint8Array> | number | null | undefined,
    record: ManagedProcessRecord,
    streamName: 'stdout' | 'stderr'
  ): void {
    if (!stream || typeof stream === 'number') return;
    const reader = stream.getReader();
    const decoder = new TextDecoder();
    void (async () => {
      try {
        while (true) {
          const { done, value } = await reader.read();
          if (done) break;
          if (value) {
            this.appendLog(record, decoder.decode(value, { stream: true }), streamName);
          }
        }
        const tail = decoder.decode();
        if (tail) this.appendLog(record, tail, streamName);
      } catch {
        // Stream closed
      }
    })();
  }

  private toSnapshot(record: ManagedProcessRecord): ManagedProcessSnapshot {
    return {
      id: record.id,
      label: record.label,
      command: record.command,
      cwd: record.cwd,
      pid: record.pid,
      status: record.status,
      exitCode: record.exitCode,
      startedAt: record.startedAt,
      stoppedAt: record.stoppedAt,
    };
  }

  public async start(params: {
    command: string;
    cwd: string;
    label?: string;
    waitMs?: number;
  }): Promise<{ process: ManagedProcessSnapshot; initialOutput: string }> {
    const command = params.command.trim();
    if (!command) {
      throw new Error('Missing required "command" argument.');
    }
    this.counter += 1;
    const id = `proc-${Date.now()}-${this.counter}`;
    const proc = Bun.spawn(['zsh', '-lc', command], {
      cwd: params.cwd,
      stdout: 'pipe',
      stderr: 'pipe',
    });

    const record: ManagedProcessRecord = {
      id,
      label: params.label?.trim() || command.slice(0, 64),
      command,
      cwd: params.cwd,
      pid: proc.pid,
      status: 'running',
      exitCode: null,
      startedAt: new Date().toISOString(),
      proc,
      logLines: [],
      totalBytes: 0,
    };

    this.processes.set(id, record);
    this.pumpStream(proc.stdout, record, 'stdout');
    this.pumpStream(proc.stderr, record, 'stderr');

    void proc.exited.then((code) => {
      if (record.status === 'running') {
        record.status = 'exited';
      }
      record.exitCode = code;
      if (!record.stoppedAt) {
        record.stoppedAt = new Date().toISOString();
      }
    });

    const waitMs = Math.max(200, Math.min(params.waitMs ?? 1500, 3000));
    await Promise.race([
      proc.exited,
      new Promise<void>((resolve) => setTimeout(resolve, waitMs)),
    ]);

    return {
      process: this.toSnapshot(record),
      initialOutput: record.logLines.join('\n').trim(),
    };
  }

  public list(cwd?: string): ManagedProcessSnapshot[] {
    const all = Array.from(this.processes.values());
    const filtered = cwd ? all.filter((r) => r.cwd === cwd) : all;
    return filtered
      .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
      .map((r) => this.toSnapshot(r));
  }

  public getLogs(id: string, tailLines = 120): { process: ManagedProcessSnapshot; logs: string } | null {
    const record = this.processes.get(id);
    if (!record) return null;
    const count = Math.max(1, Math.min(tailLines, MAX_LOG_LINES));
    return {
      process: this.toSnapshot(record),
      logs: record.logLines.slice(-count).join('\n'),
    };
  }

  public async stop(id: string): Promise<ManagedProcessSnapshot | null> {
    const record = this.processes.get(id);
    if (!record) return null;
    if (record.status === 'running') {
      record.status = 'stopped';
      record.stoppedAt = new Date().toISOString();
      try {
        record.proc.kill('SIGTERM');
      } catch {}
      await Promise.race([
        record.proc.exited,
        new Promise<void>((resolve) => setTimeout(resolve, 1500)),
      ]);
      if (record.exitCode === null) {
        try {
          record.proc.kill('SIGKILL');
        } catch {}
      }
    }
    return this.toSnapshot(record);
  }

  public async stopAll(): Promise<void> {
    const running = Array.from(this.processes.values()).filter((r) => r.status === 'running');
    await Promise.all(running.map((r) => this.stop(r.id)));
  }
}

export const sharedProcessManager = new ManagedProcessManager();
