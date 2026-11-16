export interface ParsedUnifiedPatch {
  path: string;
  content: string | null;
}

/** Apply a single-file unified git diff to its exact base, rejecting fuzzy or binary patches. */
export function applyUnifiedFilePatch(patch: string, baseContent: string): ParsedUnifiedPatch {
  if (/GIT binary patch|Binary files .* differ/.test(patch)) throw new Error('Binary swarm patches are not supported.');
  const lines = patch.replace(/\r\n/g, '\n').split('\n');
  const oldHeader = lines.find((line) => line.startsWith('--- '));
  const newHeader = lines.find((line) => line.startsWith('+++ '));
  if (!oldHeader || !newHeader) throw new Error('Swarm diff is missing unified file headers.');
  const oldPath = oldHeader.slice(4).split('\t')[0]!.replace(/^a\//, '');
  const newPath = newHeader.slice(4).split('\t')[0]!.replace(/^b\//, '');
  if (oldPath !== '/dev/null' && newPath !== '/dev/null' && oldPath !== newPath) throw new Error(`File rename patch is not supported (${oldPath} → ${newPath}).`);
  const filePath = newPath === '/dev/null' ? oldPath : newPath;
  if (!filePath || filePath === '/dev/null') throw new Error('Swarm diff has no target file path.');

  const source = baseContent.replace(/\r\n/g, '\n').split('\n');
  if (baseContent.length === 0) source.length = 0;
  const output: string[] = [];
  let sourceIndex = 0;
  const hunkHeader = /^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/;
  for (let i = 0; i < lines.length; i++) {
    const header = lines[i]!.match(hunkHeader);
    if (!header) continue;
    const oldStart = Number(header[1]);
    const oldCount = header[2] === undefined ? 1 : Number(header[2]);
    const targetIndex = oldCount === 0 ? oldStart : oldStart - 1;
    while (sourceIndex < targetIndex) output.push(source[sourceIndex++]!);

    let consumed = 0;
    for (i += 1; i < lines.length && !lines[i]!.startsWith('@@ ') && !lines[i]!.startsWith('diff --git '); i++) {
      const line = lines[i]!;
      if (line.startsWith('\\ No newline')) continue;
      const prefix = line[0];
      const content = line.slice(1);
      if (prefix === ' ') {
        if (source[sourceIndex] !== content) throw new Error(`Swarm diff context did not match ${filePath} at line ${sourceIndex + 1}.`);
        output.push(content);
        sourceIndex++;
        consumed++;
      } else if (prefix === '-') {
        if (source[sourceIndex] !== content) throw new Error(`Swarm diff removal did not match ${filePath} at line ${sourceIndex + 1}.`);
        sourceIndex++;
        consumed++;
      } else if (prefix === '+') {
        output.push(content);
      } else {
        break;
      }
    }
    if (consumed !== oldCount) throw new Error(`Swarm diff hunk line count did not match ${filePath}.`);
    i--;
  }
  output.push(...source.slice(sourceIndex));
  const content = newPath === '/dev/null' ? null : output.join('\n');
  return { path: filePath, content };
}
