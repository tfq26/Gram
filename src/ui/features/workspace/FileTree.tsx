/**
 * shadcn/ui File Tree Component
 * 
 * Renders folders and files as plain text rows without pictograms.
 * Supports file selection, recursive nesting, and changed-file badges.
 */

import React, { useState } from 'react';

export interface FileTreeNodeData {
  name: string;
  path: string;
  type: 'folder' | 'file';
  extension?: string;
  status?: 'modified' | 'added' | 'deleted';
  children?: FileTreeNodeData[];
}

export interface FileTreeProps {
  data: FileTreeNodeData[];
  selectedPath?: string;
  onSelect?: (path: string) => void;
  className?: string;
}

export interface FileTreeFolderProps {
  node: FileTreeNodeData;
  selectedPath?: string;
  onSelect?: (path: string) => void;
  defaultOpen?: boolean;
}

export interface FileTreeFileProps {
  node: FileTreeNodeData;
  selectedPath?: string;
  onSelect?: (path: string) => void;
}

export function FileTreeFile({ node, selectedPath, onSelect }: FileTreeFileProps) {
  const isSelected = selectedPath === node.path;
  return (
    <button
      type="button"
      className={`group flex w-full items-center gap-2 px-2 py-1 text-left text-xs transition-colors hover:bg-[#e9ebee] hover:text-[#20242b] ${
        isSelected ? 'bg-[#e5e8eb] text-[#20242b] font-medium' : 'text-[#626a75]'
      }`}
      onClick={() => onSelect?.(node.path)}
    >
      <span className="truncate flex-1">{node.name}</span>
      {node.status && (
        <span className="text-[10px] text-[#626a75]">{node.status}</span>
      )}
    </button>
  );
}

export function FileTreeFolder({
  node,
  selectedPath,
  onSelect,
  defaultOpen = true,
}: FileTreeFolderProps) {
  const [isOpen, setIsOpen] = useState(defaultOpen);

  return (
    <div className="flex flex-col">
      <button
        type="button"
        className="group flex w-full items-center gap-2 px-2 py-1 text-left text-xs text-[#626a75] transition-colors hover:bg-[#e9ebee] hover:text-[#20242b]"
        aria-expanded={isOpen}
        onClick={() => setIsOpen(!isOpen)}
      >
        <span className="truncate font-medium">{node.name}</span>
        <span className="ml-auto text-[10px] text-[#858d98]">{isOpen ? 'Collapse' : 'Expand'}</span>
      </button>

      {isOpen && node.children && (
        <div className="ml-3.5 flex flex-col pl-1.5">
          {node.children.map((child) => (
            <FileTreeNode
              key={child.path}
              node={child}
              selectedPath={selectedPath}
              onSelect={onSelect}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export function FileTreeNode({
  node,
  selectedPath,
  onSelect,
}: {
  node: FileTreeNodeData;
  selectedPath?: string;
  onSelect?: (path: string) => void;
}) {
  if (node.type === 'folder') {
    return (
      <FileTreeFolder
        node={node}
        selectedPath={selectedPath}
        onSelect={onSelect}
      />
    );
  }

  return (
    <FileTreeFile
      node={node}
      selectedPath={selectedPath}
      onSelect={onSelect}
    />
  );
}

export function FileTree({
  data,
  selectedPath,
  onSelect,
  className = '',
}: FileTreeProps) {
  return (
    <div className={`flex flex-col gap-0.5 select-none ${className}`}>
      {data.map((node) => (
        <FileTreeNode
          key={node.path}
          node={node}
          selectedPath={selectedPath}
          onSelect={onSelect}
        />
      ))}
    </div>
  );
}
