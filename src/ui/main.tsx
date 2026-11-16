import React from 'react';
import { createRoot } from 'react-dom/client';
import '@fontsource-variable/geist';
import '@fontsource-variable/geist-mono';
import './styles.css';
import { App } from './App';
import { AppErrorBoundary } from './components/ui/ErrorBoundary';

const rootEl = document.getElementById('root');
if (rootEl) {
  createRoot(rootEl).render(
    <React.StrictMode>
      <AppErrorBoundary>
        <App />
      </AppErrorBoundary>
    </React.StrictMode>
  );
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      document.getElementById('gram-preboot')?.remove();
    });
  });
}

const isDesktopShell = Boolean(
  (window as any).__TAURI__ || (window as any).__TAURI_INTERNALS__
);
if (!isDesktopShell && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {});
  });
}
