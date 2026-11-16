import { describe, it, expect, beforeEach } from 'bun:test';
import { SemanticGraph } from '../src/core/semanticGraph';

describe('Gram Semantic Graph & Reflexes', () => {
  let graph: SemanticGraph;

  const authFileContent = `
export interface AuthToken {
  userId: string;
  expiresAt: number;
}

export function verifyToken(token: string): AuthToken {
  if (!token) throw new Error("Missing token");
  return { userId: "user-1", expiresAt: Date.now() + 3600 };
}
`;

  const billingFileContent = `
import { verifyToken } from './auth';

export function handleInvoice(rawToken: string, amount: number) {
  const auth = verifyToken(rawToken);
  return { status: "paid", user: auth.userId, amount };
}
`;

  beforeEach(() => {
    graph = new SemanticGraph();
    graph.indexFile('src/auth.ts', authFileContent);
    graph.indexFile('src/billing.ts', billingFileContent);
  });

  it('indexes semantic symbol URIs correctly', () => {
    const symbols = graph.listSymbols();
    const uris = symbols.map((s) => s.uri);

    expect(uris).toContain('gram://src/auth.ts#interface:AuthToken');
    expect(uris).toContain('gram://src/auth.ts#function:verifyToken');
    expect(uris).toContain('gram://src/billing.ts#function:handleInvoice');
  });

  it('runs blast_radius_simulate reflex on proposed changes', () => {
    const targetUri = 'gram://src/auth.ts#function:verifyToken';
    const blast = graph.blastRadiusSimulate(targetUri);

    expect(blast.directCallers.length).toBe(1);
    expect(blast.directCallers[0]!.name).toBe('handleInvoice');
    expect(blast.impactedFiles).toContain('src/billing.ts');
  });

  it('extracts architectural intent via semantic_blame_why reflex', () => {
    const targetUri = 'gram://src/auth.ts#function:verifyToken';
    const blame = graph.semanticBlameWhy(targetUri);

    expect(blame.symbolUri).toBe(targetUri);
    expect(blame.intentSummary).toContain("Defines function 'verifyToken'");
    expect(blame.cautionNotes.some((n) => n.includes('authentication'))).toBe(true);
  });

  it('surgically replaces a symbol in a line-independent manner', () => {
    const targetUri = 'gram://src/auth.ts#function:verifyToken';
    const newImplementation = `export function verifyToken(token: string): AuthToken {
  // Upgraded with strict Bearer check
  if (!token.startsWith("Bearer ")) throw new Error("Invalid Bearer");
  return { userId: "user-upgraded", expiresAt: Date.now() + 7200 };
}`;

    const updatedFile = graph.replaceSymbol(authFileContent, targetUri, newImplementation);

    expect(updatedFile).toContain('Invalid Bearer');
    expect(updatedFile).toContain('export interface AuthToken'); // Sibling interface preserved
    expect(updatedFile).not.toContain('Missing token'); // Old implementation replaced
  });
});
