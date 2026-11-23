import React, {
  useState,
} from 'react';
import {
  type AuthState,
} from '../lib/types';



export interface UseSystemChecksParams {
  setAuthState: React.Dispatch<React.SetStateAction<AuthState>>;
}

export function useSystemChecks({ setAuthState }: UseSystemChecksParams) {
const [bootError, setBootError] = useState<{ title: string; message: string; details?: string } | null>(null);
  const [actionError, setActionError] = useState<{ message: string; details?: string } | null>(null);
  const [systemWarnings, setSystemWarnings] = useState<
    Array<{
      id: string;
      label: string;
      severity: 'critical' | 'warning';
      ok: boolean;
      message: string;
      action?: 'retry' | 'signin' | 'settings';
    }>
  >([]);
  const [systemWarningsDismissed, setSystemWarningsDismissed] = useState(false);

  function applySystemChecks(rawChecks: any[]) {
    const checks = rawChecks.map((c: any) =>
      c.id === 'internet_connection' && typeof navigator !== 'undefined' && navigator.onLine === false
        ? { ...c, ok: false, message: 'Browser reports no internet connection. Cloud models and sync may not work.' }
        : c
    );
    const criticalFailures = checks.filter((c: any) => c.severity === 'critical' && !c.ok);
    const warningFailures = checks.filter((c: any) => c.severity === 'warning' && !c.ok);
    if (criticalFailures.length > 0) {
      setBootError({
        title: criticalFailures[0].label || 'System check failed',
        message: criticalFailures.map((c: any) => c.message).join(' '),
        details: criticalFailures.map((c: any) => `[${c.id}] ${c.message}`).join('\n'),
      });
    } else {
      setBootError(null);
    }
    setSystemWarnings((prev) => {
      const prevIds = new Set(prev.map((w) => w.id));
      const hasNewWarning = warningFailures.some((w: any) => !prevIds.has(w.id));
      if (hasNewWarning) setSystemWarningsDismissed(false);
      return warningFailures;
    });
  }

  async function refreshSystemChecks() {
    try {
      const res = await fetch('/api/system/checks');
      if (!res.ok) return;
      const data = await res.json().catch(() => null);
      if (data?.auth) setAuthState(data.auth as AuthState);
      if (Array.isArray(data?.systemChecks)) {
        applySystemChecks(data.systemChecks);
      }
    } catch {}
  }

  return { setBootError, applySystemChecks, setSystemWarnings, actionError, setActionError, systemWarnings, bootError, refreshSystemChecks, setSystemWarningsDismissed, systemWarningsDismissed };
}
