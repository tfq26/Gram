import { describe, it, expect } from 'bun:test';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { FileTree, type FileTreeNodeData } from '../src/ui/features/workspace/FileTree';

describe('shadcn/ui FileTree Component', () => {
  it('renders nested folders and files recursively with collapsible structure', () => {
    const mockData: FileTreeNodeData[] = [
      {
        name: 'src',
        path: 'src',
        type: 'folder',
        children: [
          {
            name: 'core',
            path: 'src/core',
            type: 'folder',
            children: [
              {
                name: 'gramfs.ts',
                path: 'src/core/gramfs.ts',
                type: 'file',
                extension: 'ts',
                status: 'modified',
              },
            ],
          },
          {
            name: 'index.ts',
            path: 'src/index.ts',
            type: 'file',
            extension: 'ts',
          },
        ],
      },
    ];

    const html = renderToString(<FileTree data={mockData} selectedPath="src/core/gramfs.ts" />);

    expect(html).toContain('src');
    expect(html).toContain('core');
    expect(html).toContain('gramfs.ts');
    expect(html).toContain('index.ts');
    // Modified status badge
    expect(html).toContain('modified');
  });
});
