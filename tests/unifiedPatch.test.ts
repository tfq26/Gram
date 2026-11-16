import { describe, expect, it } from 'bun:test';
import { applyUnifiedFilePatch } from '../src/runtime/unifiedPatch';

describe('Lyra unified patch importer', () => {
  it('applies an exact multi-hunk patch to the local overlay base', () => {
    const patch = [
      'diff --git a/file.txt b/file.txt',
      'index 1111111..2222222 100644',
      '--- a/file.txt',
      '+++ b/file.txt',
      '@@ -1,3 +1,3 @@',
      ' one',
      '-two',
      '+TWO',
      ' three',
    ].join('\n');
    expect(applyUnifiedFilePatch(patch, 'one\ntwo\nthree')).toEqual({ path: 'file.txt', content: 'one\nTWO\nthree' });
  });

  it('rejects context mismatches and binary patches', () => {
    const patch = ['--- a/file.txt', '+++ b/file.txt', '@@ -1 +1 @@', '-old', '+new'].join('\n');
    expect(() => applyUnifiedFilePatch(patch, 'different')).toThrow('did not match');
    expect(() => applyUnifiedFilePatch('GIT binary patch', '')).toThrow('Binary');
  });
});
