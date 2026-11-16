import { describe, expect, it } from 'bun:test';
import { ThinkingTagStreamSplitter } from '../src/providers/llmClient';

describe('ThinkingTagStreamSplitter', () => {
  it('keeps split thinking markers out of the user-visible text stream', () => {
    const visible: string[] = [];
    const thinking: string[] = [];
    const splitter = new ThinkingTagStreamSplitter((chunk) => thinking.push(chunk), (chunk) => visible.push(chunk));
    splitter.push('Answer <think');
    splitter.push('ing>private plan</thinking> finished.');
    splitter.flush();
    expect(visible.join('')).toBe('Answer finished.');
    expect(thinking.join('')).toBe('private plan');
  });

  it('passes through incomplete opening markers at end of stream', () => {
    const visible: string[] = [];
    const splitter = new ThinkingTagStreamSplitter(() => {}, (chunk) => visible.push(chunk));
    splitter.push('literal <think');
    splitter.flush();
    expect(visible.join('')).toBe('literal <think');
  });
});
