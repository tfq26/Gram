import React from 'react';
import { FolderIcon, FolderPlusIcon, XIcon } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '../../components/ui/dialog';
import { Field, FieldGroup } from '../../components/ui/field';
import { Label } from '../../components/ui/label';

export interface ProjectGitInfo {
  loading: boolean;
  isGitRepo: boolean;
  branch?: string;
  lastCommit?: string;
  hasGramGuide?: boolean;
  remotes: Array<{ name: string; rawUrl: string; webUrl: string | null }>;
}

export interface ProjectDraft {
  id?: string;
  label: string;
  workspacePath: string;
  instructions: string;
}

export interface ProjectDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  activeProj?: {
    id: string;
    label: string;
    workspacePath?: string;
    archived?: boolean;
  };
  projectDraft: ProjectDraft;
  setProjectDraft: React.Dispatch<React.SetStateAction<ProjectDraft>>;
  projectHubTab: 'general' | 'git' | 'settings';
  setProjectHubTab: (tab: 'general' | 'git' | 'settings') => void;
  projectGitInfo: ProjectGitInfo;
  pickingFolder: boolean;
  setPickingFolder: (v: boolean) => void;
  confirmDeleteProject: boolean;
  setConfirmDeleteProject: (v: boolean) => void;
  onSave: (e: React.FormEvent) => void;
  onLoadGitInfo: (projectId: string) => void;
  onArchive: (projectId: string, archived: boolean) => void;
  onDelete: (projectId: string) => void;
}

export function ProjectDialog({
  open,
  onOpenChange,
  activeProj,
  projectDraft,
  setProjectDraft,
  projectHubTab,
  setProjectHubTab,
  projectGitInfo,
  pickingFolder,
  setPickingFolder,
  confirmDeleteProject,
  setConfirmDeleteProject,
  onSave,
  onLoadGitInfo,
  onArchive,
  onDelete,
}: ProjectDialogProps) {
  const handlePickFolder = async () => {
    setPickingFolder(true);
    try {
      const res = await fetch('/api/projects/pick-folder', { method: 'POST' });
      const data = await res.json().catch(() => ({}));
      if (res.ok && !data.cancelled && data.path) {
        setProjectDraft((prev) => ({
          ...prev,
          workspacePath: data.path,
          label: prev.label.trim() ? prev.label : data.name || '',
        }));
      }
    } finally {
      setPickingFolder(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[660px] p-7 overflow-hidden">
        <div className="flex min-w-0 w-full flex-col gap-6">
          <DialogHeader className="flex min-w-0 flex-row items-center justify-between gap-3 space-y-0 pr-10">
            <DialogTitle
              className="min-w-0 flex-1 truncate text-[21px] font-semibold"
              title={activeProj ? `Project: ${activeProj.label}` : 'Create project'}
            >
              {activeProj ? `Project: ${activeProj.label}` : 'Create project'}
            </DialogTitle>
            {activeProj && (
              <div className="inline-flex shrink-0 items-center gap-1 rounded-xl border border-border bg-secondary/30 p-1">
                {(
                  [
                    { id: 'general', label: 'General' },
                    { id: 'git', label: 'Git & Links' },
                    { id: 'settings', label: 'Settings' },
                  ] as const
                ).map((tab) => (
                  <button
                    key={tab.id}
                    type="button"
                    onClick={() => {
                      setProjectHubTab(tab.id);
                      if (tab.id === 'git') onLoadGitInfo(activeProj.id);
                    }}
                    className={`rounded-lg px-3 py-1.5 text-[13px] font-medium transition-colors cursor-pointer ${
                      projectHubTab === tab.id
                        ? 'bg-background text-foreground shadow-sm'
                        : 'text-muted-foreground hover:text-foreground'
                    }`}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            )}
          </DialogHeader>

          {(!activeProj || projectHubTab === 'general') && (
            <form onSubmit={onSave} className="flex flex-col gap-6">
              <FieldGroup className="gap-5">
                <Field>
                  <Label htmlFor="proj-label" className="sr-only">
                    Project name
                  </Label>
                  <div className="flex h-12 w-full items-center overflow-hidden rounded-xl border border-border bg-secondary/20 transition-all focus-within:border-[#8b5cf6] focus-within:ring-2 focus-within:ring-[#8b5cf6]/25">
                    <div className="flex h-full w-12 shrink-0 items-center justify-center border-r border-border text-muted-foreground">
                      <FolderIcon className="size-4.5" />
                    </div>
                    <input
                      id="proj-label"
                      name="label"
                      type="text"
                      required
                      placeholder="Project name"
                      value={projectDraft.label}
                      onChange={(e) => setProjectDraft((prev) => ({ ...prev, label: e.target.value }))}
                      className="h-full flex-1 bg-transparent px-4 text-[15px] text-foreground outline-none placeholder:text-muted-foreground"
                    />
                  </div>
                </Field>

                <Field className="gap-2.5">
                  <Label htmlFor="proj-workspace-btn" className="text-sm font-medium text-foreground">
                    Source folders
                  </Label>
                  <div className="flex flex-col items-center justify-center rounded-xl border border-border bg-secondary/15 px-6 py-7">
                    {projectDraft.workspacePath ? (
                      <div className="flex w-full items-center justify-between gap-3">
                        <div className="flex min-w-0 items-center gap-3">
                          <div className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-secondary text-foreground">
                            <FolderIcon className="size-4.5" />
                          </div>
                          <div className="min-w-0">
                            <div className="truncate text-sm font-medium text-foreground">
                              {projectDraft.workspacePath.split('/').filter(Boolean).pop() || projectDraft.workspacePath}
                            </div>
                            <div
                              className="truncate text-[13px] text-muted-foreground"
                              title={projectDraft.workspacePath}
                            >
                              {projectDraft.workspacePath}
                            </div>
                          </div>
                        </div>
                        <div className="flex shrink-0 items-center gap-2">
                          <Button
                            id="proj-workspace-btn"
                            type="button"
                            variant="secondary"
                            size="sm"
                            className="rounded-full px-3.5 text-[13px]"
                            disabled={pickingFolder}
                            onClick={() => void handlePickFolder()}
                          >
                            {pickingFolder ? 'Opening…' : 'Change'}
                          </Button>
                          <button
                            type="button"
                            aria-label="Remove folder"
                            onClick={() => setProjectDraft((prev) => ({ ...prev, workspacePath: '' }))}
                            className="rounded-full p-1.5 text-muted-foreground hover:bg-accent hover:text-foreground cursor-pointer"
                          >
                            <XIcon className="size-4" />
                          </button>
                        </div>
                      </div>
                    ) : (
                      <div className="flex flex-col items-center gap-3 text-center">
                        <div className="text-sm text-muted-foreground">
                          Add a folder on <span className="font-medium text-foreground">this computer</span>
                        </div>
                        <Button
                          id="proj-workspace-btn"
                          type="button"
                          variant="secondary"
                          size="sm"
                          className="h-9 rounded-full px-4 text-[13px] font-medium"
                          disabled={pickingFolder}
                          onClick={() => void handlePickFolder()}
                        >
                          <FolderPlusIcon className="size-4" />
                          <span>{pickingFolder ? 'Opening…' : 'Add'}</span>
                        </Button>
                      </div>
                    )}
                  </div>
                </Field>
              </FieldGroup>

              <DialogFooter className="pt-1 sm:gap-2.5">
                <DialogClose
                  render={
                    <Button type="button" variant="ghost" className="rounded-xl text-sm text-muted-foreground hover:text-foreground">
                      Cancel
                    </Button>
                  }
                />
                <Button type="submit" className="rounded-xl px-5 text-sm font-medium">
                  {activeProj ? 'Save project settings' : 'Create project'}
                </Button>
              </DialogFooter>
            </form>
          )}

          {activeProj && projectHubTab === 'git' && (
            <div className="flex min-w-0 flex-col gap-5">
              <div className="min-w-0 overflow-hidden rounded-xl border border-border bg-secondary/15 p-5 text-[13px]">
                {projectGitInfo.loading ? (
                  <div className="text-muted-foreground">Inspecting Git remotes…</div>
                ) : !projectGitInfo.isGitRepo ? (
                  <div className="break-words text-muted-foreground">
                    No Git repository detected in <span className="font-mono text-foreground">{activeProj.workspacePath || '(unset)'}</span>.
                  </div>
                ) : (
                  <div className="flex min-w-0 flex-col gap-3.5">
                    <div className="flex min-w-0 items-center justify-between gap-3">
                      <span className="shrink-0 text-muted-foreground">Current branch</span>
                      <span
                        className="max-w-[65%] truncate rounded-md bg-secondary px-2.5 py-0.5 font-mono text-[13px] font-medium text-foreground"
                        title={projectGitInfo.branch || 'HEAD'}
                      >
                        {projectGitInfo.branch || 'HEAD'}
                      </span>
                    </div>
                    {projectGitInfo.lastCommit && (
                      <div className="flex min-w-0 items-center justify-between gap-3">
                        <span className="shrink-0 text-muted-foreground">Latest commit</span>
                        <span
                          className="min-w-0 flex-1 truncate text-right font-mono text-[13px] text-foreground"
                          title={projectGitInfo.lastCommit}
                        >
                          {projectGitInfo.lastCommit}
                        </span>
                      </div>
                    )}
                    <div className="flex min-w-0 items-center justify-between gap-2">
                      <span className="shrink-0 text-muted-foreground">.gram/gram.md</span>
                      <span className="text-foreground">
                        {projectGitInfo.hasGramGuide ? 'Initialized' : 'Not created yet'}
                      </span>
                    </div>
                  </div>
                )}
              </div>

              <div className="flex min-w-0 flex-col gap-2.5">
                <div className="text-sm font-medium text-foreground">Linked remotes</div>
                {projectGitInfo.remotes.length === 0 ? (
                  <div className="rounded-xl border border-border bg-secondary/10 px-4 py-3.5 text-[13px] text-muted-foreground">
                    No Git remotes configured.
                  </div>
                ) : (
                  <div className="flex min-w-0 flex-col gap-2.5">
                    {projectGitInfo.remotes.map((r) => (
                      <div
                        key={`${r.name}-${r.rawUrl}`}
                        className="flex min-w-0 items-center justify-between gap-3 rounded-xl border border-border bg-secondary/15 px-4 py-3 text-[13px]"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="font-medium text-foreground">{r.name}</div>
                          <div className="truncate font-mono text-[13px] text-muted-foreground" title={r.rawUrl}>
                            {r.rawUrl}
                          </div>
                        </div>
                        {r.webUrl && (
                          <a
                            href={r.webUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="shrink-0 rounded-full border border-border px-3 py-1 text-[13px] font-medium text-foreground hover:bg-accent"
                          >
                            Open
                          </a>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          )}

          {activeProj && projectHubTab === 'settings' && (
            <div className="flex flex-col gap-4">
              {activeProj.id === 'local' ? (
                <div className="rounded-xl border border-border bg-secondary/15 p-5 text-sm text-muted-foreground">
                  The default Local workspace cannot be archived or deleted.
                </div>
              ) : (
                <>
                  <div className="flex items-center justify-between gap-4 rounded-xl border border-border bg-secondary/15 p-5">
                    <div>
                      <div className="text-sm font-medium text-foreground">
                        {activeProj.archived ? 'Unarchive project' : 'Archive project'}
                      </div>
                      <div className="mt-1 text-[13px] text-muted-foreground">
                        {activeProj.archived
                          ? 'Restore this project to your active sidebar list.'
                          : 'Hide this project from the sidebar without deleting any data.'}
                      </div>
                    </div>
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      className="text-[13px]"
                      onClick={() => onArchive(activeProj.id, !activeProj.archived)}
                    >
                      {activeProj.archived ? 'Unarchive' : 'Archive'}
                    </Button>
                  </div>

                  <div className="flex items-center justify-between gap-4 rounded-xl border border-destructive/30 bg-destructive/5 p-5">
                    <div>
                      <div className="text-sm font-medium text-foreground">Delete project</div>
                      <div className="mt-1 text-[13px] text-muted-foreground">
                        Removes this project from Gram. Your files on disk are not deleted.
                      </div>
                    </div>
                    {confirmDeleteProject ? (
                      <div className="flex shrink-0 items-center gap-2">
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          className="text-[13px]"
                          onClick={() => setConfirmDeleteProject(false)}
                        >
                          Cancel
                        </Button>
                        <Button
                          type="button"
                          variant="destructive"
                          size="sm"
                          className="text-[13px]"
                          onClick={() => onDelete(activeProj.id)}
                        >
                          Confirm
                        </Button>
                      </div>
                    ) : (
                      <Button
                        type="button"
                        variant="destructive"
                        size="sm"
                        className="text-[13px]"
                        onClick={() => setConfirmDeleteProject(true)}
                      >
                        Delete
                      </Button>
                    )}
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
