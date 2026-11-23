import { describe, expect, test } from 'bun:test';
import { Database } from 'bun:sqlite';
import { GramCodeMode } from './codeMode';

describe('GramCodeMode (Isolated Worker Sandbox)', () => {
  const mockGramFs = {
    files: new Map<string, string>([['src/app.ts', 'export const x = 1;']]),
    readFile(p: string) {
      const val = this.files.get(p);
      if (val === undefined) throw new Error(`Missing ${p}`);
      return val;
    },
    writeFile(p: string, c: string) {
      this.files.set(p, c);
      return p;
    },
    fileExists(p: string) {
      return this.files.has(p);
    },
    deleteFile(p: string) {
      this.files.delete(p);
      return p;
    },
    listModifiedFiles() {
      return Array.from(this.files.keys()).map((path) => ({ path, isDeleted: false }));
    },
  } as any;

  const mockGraph = {
    getNode: () => null,
    replaceSymbol: (_c: string, _u: string, next: string) => next,
    indexFile: () => {},
    blastRadiusSimulate: () => ({ impactedFiles: [], riskScore: 0 }),
    listSymbols: () => [],
  } as any;

  test('search() returns filtered TypeScript SDK declarations', () => {
    const cm = new GramCodeMode(mockGramFs, mockGraph);
    const dbDocs = cm.search('db');
    expect(dbDocs.namespaces).toContain('gram.db');
    expect(dbDocs.sdkDeclarations).toContain('queryReadOnly');
  });

  test('execute() runs in isolated Worker, blocks ambient globals, and persists memory/db', async () => {
    const db = new Database(':memory:');
    const cm = new GramCodeMode(mockGramFs, mockGraph, { db });

    try {
      const res = await cm.execute(`
        const hasProcess = typeof process !== 'undefined';
        const hasFetch = typeof fetch !== 'undefined';
        const hasBun = typeof Bun !== 'undefined';

        const content = await gram.fs.read('src/app.ts');
        await gram.memory.pinFact('arch', 'isolated-worker');
        const fact = await gram.memory.getFact('arch');
        const tables = await gram.db.listTables();

        return { hasProcess, hasFetch, hasBun, content, fact, hasMemoryTable: tables.includes('gram_codemode_memory') };
      `);

      expect(res.success).toBe(true);
      expect(res.returnValue).toEqual({
        hasProcess: false,
        hasFetch: false,
        hasBun: false,
        content: 'export const x = 1;',
        fact: 'isolated-worker',
        hasMemoryTable: true,
      });
    } finally {
      db.close();
    }
  });
});
