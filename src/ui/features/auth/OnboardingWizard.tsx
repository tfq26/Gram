import React, { useState } from 'react';
import {
  ArrowLeftIcon,
  ArrowRightIcon,
  CheckIcon,
  Code2Icon,
  MessageSquareIcon,
  PaletteIcon,
  SparklesIcon,
  TerminalIcon,
} from 'lucide-react';
import { GramMark } from './AuthPage';

export interface OnboardingWizardProps {
  userName?: string | null;
  initialMode: 'chat' | 'code';
  currentTheme: string;
  onSelectTheme: (theme: string) => void;
  onComplete: (preferredMode: 'chat' | 'code') => void;
}

type WorkflowChoice = 'chat' | 'code' | 'both';

const THEMES: Array<{
  id: string;
  label: string;
  subtitle: string;
  bg: string;
  surface: string;
  accent: string;
}> = [
  {
    id: 'carbon',
    label: 'Carbon',
    subtitle: 'Balanced dark graphite',
    bg: '#0b0c0f',
    surface: '#16181d',
    accent: '#3b82f6',
  },
  {
    id: 'obsidian',
    label: 'Obsidian',
    subtitle: 'Deep true-black contrast',
    bg: '#040405',
    surface: '#111114',
    accent: '#a855f7',
  },
  {
    id: 'midnight',
    label: 'Midnight',
    subtitle: 'Cool slate & cobalt',
    bg: '#071124',
    surface: '#10203d',
    accent: '#245bff',
  },
  {
    id: 'paper',
    label: 'Paper',
    subtitle: 'Crisp daylight surface',
    bg: '#f8fafc',
    surface: '#ffffff',
    accent: '#245bff',
  },
];

export function OnboardingWizard({
  userName,
  initialMode,
  currentTheme,
  onSelectTheme,
  onComplete,
}: OnboardingWizardProps) {
  const [step, setStep] = useState(0);
  const [direction, setDirection] = useState<'forward' | 'backward'>('forward');
  const [workflow, setWorkflow] = useState<WorkflowChoice>(
    initialMode === 'code' ? 'code' : 'both'
  );

  const totalSteps = 4;
  const progressPct = ((step + 1) / totalSteps) * 100;

  function goToStep(next: number) {
    if (next < 0 || next >= totalSteps) return;
    setDirection(next > step ? 'forward' : 'backward');
    setStep(next);
  }

  function finish() {
    const resolvedMode: 'chat' | 'code' = workflow === 'code' ? 'code' : 'chat';
    onComplete(resolvedMode);
  }

  const animClass = direction === 'forward' ? 'gram-step-forward' : 'gram-step-backward';
  const greetingName = userName?.trim() ? `, ${userName.trim().split(/\s+/)[0]}` : '';

  return (
    <div
      className="min-h-screen w-full flex flex-col justify-between px-6 py-8 sm:px-10"
      style={{
        background: 'var(--bg-body, #050505)',
        color: 'var(--text-primary, #f4f4f5)',
        fontFamily: 'var(--font-sans, Inter, system-ui, sans-serif)',
      }}
    >
      {/* Top Progress Header */}
      <header className="w-full max-w-[680px] mx-auto flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div
              className="w-8 h-8 rounded-full flex items-center justify-center"
              style={{ background: 'rgba(36, 91, 255, 0.14)' }}
            >
              <GramMark size={18} color="#245bff" />
            </div>
            <span className="text-[13px] font-medium" style={{ color: 'var(--text-muted, #9ca3af)' }}>
              Step {step + 1} of {totalSteps}
            </span>
          </div>

          <button
            type="button"
            onClick={finish}
            className="text-[13px] font-medium transition-opacity opacity-75 hover:opacity-100 cursor-pointer"
            style={{ color: 'var(--text-muted, #9ca3af)' }}
          >
            Skip setup
          </button>
        </div>

        <div
          className="w-full h-1.5 rounded-full overflow-hidden"
          style={{ background: 'var(--bg-panel-subtle, #18181b)' }}
        >
          <div
            className="h-full rounded-full transition-all duration-300 ease-out"
            style={{
              width: `${progressPct}%`,
              background: '#245bff',
            }}
          />
        </div>
      </header>

      {/* Center Step Stage */}
      <main className="flex-1 flex items-center justify-center py-8">
        <div key={step} className={`w-full max-w-[640px] ${animClass}`}>
          {step === 0 && (
            <div className="flex flex-col items-center text-center">
              <div
                className="w-16 h-16 rounded-2xl flex items-center justify-center mb-6 shadow-sm"
                style={{
                  background: 'rgba(36, 91, 255, 0.12)',
                  border: '1px solid rgba(36, 91, 255, 0.25)',
                }}
              >
                <GramMark size={34} color="#245bff" />
              </div>

              <h1 className="text-[30px] sm:text-[34px] font-semibold tracking-tight leading-tight">
                Welcome to Gram{greetingName}
              </h1>
              <p
                className="mt-3 text-[15px] max-w-[460px] leading-relaxed"
                style={{ color: 'var(--text-muted, #a1a1aa)' }}
              >
                One fast desktop workspace for everyday chat, deep repository coding, and built-in Lyra agent workflows.
              </p>

              <div className="w-full grid grid-cols-1 sm:grid-cols-3 gap-3 mt-8 text-left">
                {[
                  {
                    icon: <MessageSquareIcon className="w-4 h-4 text-[#245bff]" />,
                    title: 'Auto-routed Chat',
                    desc: 'Defaults to the fastest, lowest-cost model with effort control.',
                  },
                  {
                    icon: <Code2Icon className="w-4 h-4 text-[#245bff]" />,
                    title: 'Projects & Code',
                    desc: 'Organize repos, files, and diffs with one-click Chat → Code handoff.',
                  },
                  {
                    icon: <TerminalIcon className="w-4 h-4 text-[#245bff]" />,
                    title: 'Built-in Lyra Tools',
                    desc: 'Run local edits, terminal checks, and multi-agent tools natively.',
                  },
                ].map((item) => (
                  <div
                    key={item.title}
                    className="p-4 rounded-2xl border"
                    style={{
                      background: 'var(--bg-panel, #0f1014)',
                      borderColor: 'var(--border-color, #242429)',
                    }}
                  >
                    <div
                      className="w-8 h-8 rounded-lg flex items-center justify-center mb-3"
                      style={{ background: 'rgba(36, 91, 255, 0.12)' }}
                    >
                      {item.icon}
                    </div>
                    <div className="text-[13.5px] font-semibold">{item.title}</div>
                    <div
                      className="mt-1 text-[12.5px] leading-relaxed"
                      style={{ color: 'var(--text-muted, #9ca3af)' }}
                    >
                      {item.desc}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {step === 1 && (
            <div className="flex flex-col">
              <div className="text-center mb-7">
                <h2 className="text-[26px] font-semibold tracking-tight">
                  How do you plan to use Gram?
                </h2>
                <p className="mt-2 text-[14px]" style={{ color: 'var(--text-muted, #9ca3af)' }}>
                  We will set your starting workspace mode. You can switch anytime from the sidebar.
                </p>
              </div>

              <div className="flex flex-col gap-3">
                {[
                  {
                    id: 'both' as const,
                    title: 'Both Chat & Code',
                    badge: 'Recommended',
                    desc: 'Start in Chat with seamless handoff to Code whenever a coding task comes up.',
                    icon: <SparklesIcon className="w-5 h-5" />,
                  },
                  {
                    id: 'chat' as const,
                    title: 'Everyday Chat & Research',
                    desc: 'Clean conversational workspace routed automatically to the cheapest fast model.',
                    icon: <MessageSquareIcon className="w-5 h-5" />,
                  },
                  {
                    id: 'code' as const,
                    title: 'Coding & Repositories',
                    desc: 'Open straight into Projects, pinned coding sessions, file uploads, and diffs.',
                    icon: <Code2Icon className="w-5 h-5" />,
                  },
                ].map((opt) => {
                  const selected = workflow === opt.id;
                  return (
                    <button
                      key={opt.id}
                      type="button"
                      onClick={() => setWorkflow(opt.id)}
                      className="w-full p-4 rounded-2xl border text-left flex items-center gap-4 transition-all cursor-pointer"
                      style={{
                        background: selected ? 'rgba(36, 91, 255, 0.10)' : 'var(--bg-panel, #0f1014)',
                        borderColor: selected ? '#245bff' : 'var(--border-color, #242429)',
                      }}
                    >
                      <div
                        className="w-10 h-10 rounded-xl flex items-center justify-center shrink-0"
                        style={{
                          background: selected ? '#245bff' : 'var(--bg-panel-subtle, #18181b)',
                          color: selected ? '#ffffff' : 'var(--text-muted, #9ca3af)',
                        }}
                      >
                        {opt.icon}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-[14.5px] font-semibold">{opt.title}</span>
                          {opt.badge && (
                            <span
                              className="text-[11px] font-medium px-2 py-0.5 rounded-full"
                              style={{ background: 'rgba(36, 91, 255, 0.18)', color: '#60a5fa' }}
                            >
                              {opt.badge}
                            </span>
                          )}
                        </div>
                        <p
                          className="mt-0.5 text-[13px]"
                          style={{ color: 'var(--text-muted, #9ca3af)' }}
                        >
                          {opt.desc}
                        </p>
                      </div>
                      <div
                        className="w-5 h-5 rounded-full border flex items-center justify-center shrink-0"
                        style={{
                          borderColor: selected ? '#245bff' : 'var(--border-color, #3f3f46)',
                          background: selected ? '#245bff' : 'transparent',
                        }}
                      >
                        {selected && <CheckIcon className="w-3.5 h-3.5 text-white" />}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {step === 2 && (
            <div className="flex flex-col">
              <div className="text-center mb-7">
                <div className="inline-flex items-center justify-center w-10 h-10 rounded-xl mb-3" style={{ background: 'rgba(36, 91, 255, 0.12)' }}>
                  <PaletteIcon className="w-5 h-5 text-[#245bff]" />
                </div>
                <h2 className="text-[26px] font-semibold tracking-tight">
                  Choose your look
                </h2>
                <p className="mt-2 text-[14px]" style={{ color: 'var(--text-muted, #9ca3af)' }}>
                  Click a theme to preview it live across Gram.
                </p>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3.5">
                {THEMES.map((t) => {
                  const selected = currentTheme === t.id;
                  return (
                    <button
                      key={t.id}
                      type="button"
                      onClick={() => onSelectTheme(t.id)}
                      className="p-4 rounded-2xl border text-left flex items-center gap-3.5 transition-all cursor-pointer"
                      style={{
                        background: selected ? 'rgba(36, 91, 255, 0.10)' : 'var(--bg-panel, #0f1014)',
                        borderColor: selected ? '#245bff' : 'var(--border-color, #242429)',
                      }}
                    >
                      <div
                        className="w-12 h-12 rounded-xl border flex items-end justify-end p-1.5 shrink-0"
                        style={{
                          background: t.bg,
                          borderColor: 'rgba(148, 163, 184, 0.25)',
                        }}
                      >
                        <div className="w-4 h-4 rounded-full" style={{ background: t.accent }} />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="text-[14.5px] font-semibold">{t.label}</div>
                        <div className="text-[12.5px]" style={{ color: 'var(--text-muted, #9ca3af)' }}>
                          {t.subtitle}
                        </div>
                      </div>
                      {selected && (
                        <div className="w-5 h-5 rounded-full bg-[#245bff] flex items-center justify-center">
                          <CheckIcon className="w-3.5 h-3.5 text-white" />
                        </div>
                      )}
                    </button>
                  );
                })}
              </div>
            </div>
          )}

          {step === 3 && (
            <div className="flex flex-col items-center text-center">
              <div
                className="w-14 h-14 rounded-full flex items-center justify-center mb-5"
                style={{ background: 'rgba(34, 197, 94, 0.14)', color: '#22c55e' }}
              >
                <CheckIcon className="w-7 h-7" />
              </div>

              <h2 className="text-[28px] font-semibold tracking-tight">
                You are all set
              </h2>
              <p
                className="mt-2 text-[14.5px] max-w-[440px]"
                style={{ color: 'var(--text-muted, #9ca3af)' }}
              >
                Your workspace is configured and ready. You can adjust API keys, models, or appearance anytime in Settings.
              </p>

              <div
                className="w-full max-w-[420px] mt-7 p-4 rounded-2xl border text-left flex flex-col gap-2.5"
                style={{
                  background: 'var(--bg-panel, #0f1014)',
                  borderColor: 'var(--border-color, #242429)',
                }}
              >
                <div className="flex items-center justify-between text-[13px]">
                  <span style={{ color: 'var(--text-muted, #9ca3af)' }}>Starting workspace</span>
                  <span className="font-medium capitalize">
                    {workflow === 'both' ? 'Chat + Code' : workflow}
                  </span>
                </div>
                <div className="h-px" style={{ background: 'var(--border-color, #242429)' }} />
                <div className="flex items-center justify-between text-[13px]">
                  <span style={{ color: 'var(--text-muted, #9ca3af)' }}>Theme</span>
                  <span className="font-medium capitalize">{currentTheme}</span>
                </div>
                <div className="h-px" style={{ background: 'var(--border-color, #242429)' }} />
                <div className="flex items-center justify-between text-[13px]">
                  <span style={{ color: 'var(--text-muted, #9ca3af)' }}>Chat routing</span>
                  <span className="font-medium">Auto (lowest cost)</span>
                </div>
              </div>
            </div>
          )}
        </div>
      </main>

      {/* Bottom Step Controls */}
      <footer className="w-full max-w-[680px] mx-auto flex items-center justify-between pt-4">
        <div>
          {step > 0 && (
            <button
              type="button"
              onClick={() => goToStep(step - 1)}
              className="h-10 px-4 rounded-full border text-[13.5px] font-medium inline-flex items-center gap-1.5 transition-colors cursor-pointer"
              style={{
                borderColor: 'var(--border-color, #27272a)',
                color: 'var(--text-primary, #f4f4f5)',
              }}
            >
              <ArrowLeftIcon className="w-4 h-4" />
              Back
            </button>
          )}
        </div>

        <div className="flex items-center gap-2">
          {Array.from({ length: totalSteps }, (_, idx) => (
            <button
              key={idx}
              type="button"
              aria-label={`Go to step ${idx + 1}`}
              onClick={() => goToStep(idx)}
              className="h-2 rounded-full transition-all cursor-pointer"
              style={{
                width: idx === step ? '20px' : '8px',
                background: idx === step ? '#245bff' : 'var(--border-color, #3f3f46)',
              }}
            />
          ))}
        </div>

        <div>
          {step < totalSteps - 1 ? (
            <button
              type="button"
              onClick={() => goToStep(step + 1)}
              className="h-10 px-6 rounded-full bg-[#245bff] hover:bg-[#1d4ed8] text-white text-[13.5px] font-medium inline-flex items-center gap-1.5 transition-all cursor-pointer"
            >
              Continue
              <ArrowRightIcon className="w-4 h-4" />
            </button>
          ) : (
            <button
              type="button"
              onClick={finish}
              className="h-10 px-6 rounded-full bg-[#245bff] hover:bg-[#1d4ed8] text-white text-[13.5px] font-medium inline-flex items-center gap-1.5 transition-all cursor-pointer"
            >
              Launch Gram
              <ArrowRightIcon className="w-4 h-4" />
            </button>
          )}
        </div>
      </footer>
    </div>
  );
}
