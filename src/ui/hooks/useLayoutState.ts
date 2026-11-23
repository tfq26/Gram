import React, {
  useState,
} from 'react';
import {
  type LayoutPreset,
} from '../features/settings/SettingsView';



export interface UseLayoutStateParams {

}

export function useLayoutState() {
const [layoutPreset, setLayoutPreset] = useState<LayoutPreset>(() => {
    try {
      const saved = localStorage.getItem('gram.settings.layoutPreset');
      return saved === 'mid' || saved === 'power' ? saved : 'beginner';
    } catch {
      return 'beginner';
    }
  });
  const [hideBuiltinModels, setHideBuiltinModels] = useState<boolean>(() => {
    try {
      return localStorage.getItem('gram.settings.hideBuiltinModels') === 'true';
    } catch {
      return false;
    }
  });
  const [sidebarOpen, setSidebarOpen] = useState(() => layoutPreset === 'beginner');
  const [sidebarWidth, setSidebarWidth] = useState<number>(() => {
    try {
      const saved = Number(localStorage.getItem('gram.sidebarWidth'));
      if (Number.isFinite(saved) && saved >= 220 && saved <= 520) return saved;
    } catch {}
    return 260;
  });
  const [isResizingSidebar, setIsResizingSidebar] = useState(false);

  const startSidebarResize = (e: React.MouseEvent<HTMLDivElement>) => {
    if (!sidebarOpen) return;
    e.preventDefault();
    const startX = e.clientX;
    const startWidth = sidebarWidth;
    setIsResizingSidebar(true);
    let latestWidth = startWidth;
    const prevCursor = document.body.style.cursor;
    const prevSelect = document.body.style.userSelect;
    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';

    const onMouseMove = (moveEvent: MouseEvent) => {
      const next = Math.min(520, Math.max(220, Math.round(startWidth + (moveEvent.clientX - startX))));
      latestWidth = next;
      setSidebarWidth(next);
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
      document.body.style.cursor = prevCursor;
      document.body.style.userSelect = prevSelect;
      setIsResizingSidebar(false);
      try {
        localStorage.setItem('gram.sidebarWidth', String(latestWidth));
      } catch {}
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  };

  const toggleSidebarExpanded = () => {
    const next = sidebarWidth >= 340 ? 260 : 380;
    setSidebarWidth(next);
    try {
      localStorage.setItem('gram.sidebarWidth', String(next));
    } catch {}
  };

  return { hideBuiltinModels, setHideBuiltinModels, setLayoutPreset, setSidebarOpen, layoutPreset, sidebarOpen, isResizingSidebar, sidebarWidth, toggleSidebarExpanded, startSidebarResize };
}
