import React, {
  useState,
} from 'react';
import {
  emitSystemToast,
} from '../components/ui/toast';
import {
  type ProjectItem,
} from '../lib/types';



export interface UseProjectHubParams {
  setProjects: React.Dispatch<React.SetStateAction<ProjectItem[]>>;
  activeProjectId: string;
  setActiveProjectId: React.Dispatch<React.SetStateAction<string>>;
  setActionError: React.Dispatch<React.SetStateAction<{ message: string; details?: string; } | null>>;
}

export function useProjectHub({ setProjects, activeProjectId, setActiveProjectId, setActionError }: UseProjectHubParams) {
const [expandedProjects, setExpandedProjects] = useState<Record<string, boolean>>({});
  const [projectHubId, setProjectHubId] = useState<string | null>(null);
  const [projectHubTab, setProjectHubTab] = useState<'general' | 'git' | 'settings'>('general');
  const [projectGitInfo, setProjectGitInfo] = useState<{
    loading: boolean;
    isGitRepo: boolean;
    branch?: string;
    lastCommit?: string;
    hasGramGuide?: boolean;
    remotes: Array<{ name: string; rawUrl: string; webUrl: string | null }>;
  }>({ loading: false, isGitRepo: false, remotes: [] });
  const [confirmDeleteProject, setConfirmDeleteProject] = useState(false);
  const [showArchivedProjects, setShowArchivedProjects] = useState(false);
  const [projectDraft, setProjectDraft] = useState<{ id?: string; label: string; workspacePath: string; instructions: string }>({
    label: '',
    workspacePath: '',
    instructions: '',
  });
  const [pickingFolder, setPickingFolder] = useState(false);

  async function loadProjectGitInfo(projectId: string) {
    setProjectGitInfo({ loading: true, isGitRepo: false, remotes: [] });
    try {
      const res = await fetch(`/api/projects/${encodeURIComponent(projectId)}/git`);
      const data = await res.json().catch(() => ({}));
      setProjectGitInfo({
        loading: false,
        isGitRepo: Boolean(data?.isGitRepo),
        branch: data?.branch,
        lastCommit: data?.lastCommit,
        hasGramGuide: Boolean(data?.hasGramGuide),
        remotes: Array.isArray(data?.remotes) ? data.remotes : [],
      });
    } catch {
      setProjectGitInfo({ loading: false, isGitRepo: false, remotes: [] });
    }
  }

  async function handleArchiveProject(projectId: string, archived: boolean) {
    try {
      const res = await fetch('/api/projects/archive', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: projectId, archived }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not update project (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      if (archived && activeProjectId === projectId) {
        setActiveProjectId('local');
      }
      emitSystemToast({
        title: archived ? 'Project archived' : 'Project restored',
        variant: 'info',
      });
    } catch (err: any) {
      const msg = err?.message || 'Could not archive project.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Project update failed', description: msg, variant: 'error' });
    }
  }

  async function handleDeleteProject(projectId: string) {
    try {
      const res = await fetch('/api/projects/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ id: projectId }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not delete project (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      if (activeProjectId === projectId) {
        setActiveProjectId('local');
      }
      setConfirmDeleteProject(false);
      setProjectHubId(null);
      emitSystemToast({ title: 'Project deleted', variant: 'info' });
    } catch (err: any) {
      const msg = err?.message || 'Could not delete project.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Could not delete project', description: msg, variant: 'error' });
    }
  }

  function openProjectHub(project?: ProjectItem) {
    setProjectHubTab('general');
    setConfirmDeleteProject(false);
    if (project) {
      setProjectHubId(project.id);
      setProjectDraft({
        id: project.id,
        label: project.label,
        workspacePath: project.workspacePath || '',
        instructions: project.instructions || '',
      });
      void loadProjectGitInfo(project.id);
    } else {
      setProjectHubId('__new__');
      setProjectDraft({ label: '', workspacePath: '', instructions: '' });
      setProjectGitInfo({ loading: false, isGitRepo: false, remotes: [] });
    }
  }

  async function saveProjectHub(e: React.FormEvent) {
    e.preventDefault();
    if (!projectDraft.label.trim()) {
      setActionError({ message: 'Project name is required.' });
      return;
    }
    const isCreating = !projectDraft.id || projectHubId === '__new__';
    try {
      const res = await fetch('/api/projects', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(projectDraft),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not save project (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      if (data?.project?.id) {
        setActiveProjectId(data.project.id);
        setProjectHubId(isCreating ? null : data.project.id);
      }
      emitSystemToast({
        title: isCreating ? 'Project created' : 'Project settings saved',
        description: data?.project?.label || projectDraft.label.trim(),
        variant: 'success',
      });
    } catch (err: any) {
      const msg = err?.message || 'Could not save project.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Could not save project', description: msg, variant: 'error' });
    }
  }

  async function handleUploadProjectFiles(projectId: string, fileList: FileList | null) {
    if (!fileList || fileList.length === 0) return;
    try {
      for (const file of Array.from(fileList)) {
        const contentText = await file.text().catch(() => '');
        const res = await fetch('/api/projects/files', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            projectId,
            filename: file.name,
            mimeType: file.type || 'text/plain',
            contentText,
          }),
        });
        const data = await res.json().catch(() => null);
        if (!res.ok) throw new Error(data?.error || `Could not upload ${file.name} (${res.status}).`);
        if (data?.projects) setProjects(data.projects);
      }
      emitSystemToast({
        title: `Uploaded ${fileList.length} project file${fileList.length === 1 ? '' : 's'}`,
        variant: 'success',
      });
    } catch (err: any) {
      const msg = err?.message || 'Could not upload project file.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Upload failed', description: msg, variant: 'error' });
    }
  }

  async function handleDeleteProjectFile(fileId: string) {
    try {
      const res = await fetch('/api/projects/files/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileId }),
      });
      const data = await res.json().catch(() => null);
      if (!res.ok) throw new Error(data?.error || `Could not delete project file (${res.status}).`);
      if (data?.projects) setProjects(data.projects);
      emitSystemToast({ title: 'Project file removed', variant: 'info' });
    } catch (err: any) {
      const msg = err?.message || 'Could not delete project file.';
      setActionError({ message: msg });
      emitSystemToast({ title: 'Could not remove file', description: msg, variant: 'error' });
    }
  }

  return { projectHubId, openProjectHub, showArchivedProjects, expandedProjects, setExpandedProjects, setShowArchivedProjects, setProjectHubId, projectDraft, setProjectDraft, projectHubTab, setProjectHubTab, projectGitInfo, pickingFolder, setPickingFolder, confirmDeleteProject, setConfirmDeleteProject, saveProjectHub, loadProjectGitInfo, handleArchiveProject, handleDeleteProject };
}
