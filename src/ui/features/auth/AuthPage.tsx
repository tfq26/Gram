import React, { useEffect, useId, useRef, useState } from 'react';
import { ArrowLeftIcon, EyeIcon, EyeOffIcon, LoaderCircleIcon } from 'lucide-react';

export interface AuthPageProps {
  mode: 'signin' | 'signup';
  name: string;
  email: string;
  password: string;
  error: string;
  submitting?: boolean;
  googleSubmitting?: boolean;
  onModeChange: (mode: 'signin' | 'signup') => void;
  onNameChange: (value: string) => void;
  onEmailChange: (value: string) => void;
  onPasswordChange: (value: string) => void;
  onSubmit: (e: React.FormEvent) => void;
  onGoogleSignIn?: () => void;
  onBackToWorkspace: () => void;
}

const RIBBON_PALETTES = [
  ['#34D399', '#0EA5E9'],
  ['#A78BFA', '#38BDF8'],
  ['#FBBF24', '#FB7185'],
] as const;

function ObsidianAuroraCanvas() {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const reducedMotion =
      typeof window !== 'undefined' &&
      window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

    let width = 0;
    let height = 0;
    let rafId = 0;

    const nodes = Array.from({ length: 28 }, (_, i) => ({
      x: ((i * 73) % 100) / 100,
      y: ((i * 41 + 17) % 100) / 100,
      vx: ((i % 5) - 2) * 0.00018,
      vy: (((i * 3) % 5) - 2) * 0.00015,
      r: 1.3 + (i % 3) * 0.65,
      colorIdx: i % RIBBON_PALETTES.length,
    }));

    const resize = () => {
      const rect = canvas.getBoundingClientRect();
      if (rect.width === 0 || rect.height === 0) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      width = rect.width;
      height = rect.height;
      canvas.width = Math.floor(width * dpr);
      canvas.height = Math.floor(height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const drawFrame = (timeMs: number) => {
      if (!width || !height) return;
      const t = timeMs * 0.00055;

      ctx.fillStyle = '#090a0b';
      ctx.fillRect(0, 0, width, height);

      // Soft ambient glow behind the ribbons
      const glow = ctx.createRadialGradient(
        width * (0.52 + Math.sin(t * 0.7) * 0.08),
        height * (0.48 + Math.cos(t * 0.5) * 0.08),
        10,
        width * 0.5,
        height * 0.5,
        Math.max(width, height) * 0.65
      );
      glow.addColorStop(0, 'rgba(56, 189, 248, 0.14)');
      glow.addColorStop(0.45, 'rgba(167, 139, 250, 0.08)');
      glow.addColorStop(1, 'rgba(9, 10, 11, 0)');
      ctx.fillStyle = glow;
      ctx.fillRect(0, 0, width, height);

      // Three flowing gradient ribbons matching the Per-Blade Trio palette
      RIBBON_PALETTES.forEach(([start, end], idx) => {
        const phase = t * (0.9 + idx * 0.18) + idx * 2.1;
        const grad = ctx.createLinearGradient(0, height * 0.2, width, height * 0.85);
        grad.addColorStop(0, start);
        grad.addColorStop(1, end);

        ctx.save();
        ctx.globalAlpha = 0.22;
        ctx.strokeStyle = grad;
        ctx.lineWidth = 42 - idx * 6;
        ctx.lineCap = 'round';
        ctx.beginPath();
        const steps = 28;
        for (let s = 0; s <= steps; s++) {
          const p = s / steps;
          const px = p * (width + 80) - 40;
          const wave =
            Math.sin(p * 3.4 + phase) * (46 + idx * 14) +
            Math.cos(p * 1.9 - phase * 0.7) * 26;
          const py = height * (0.28 + idx * 0.21) + wave;
          if (s === 0) ctx.moveTo(px, py);
          else ctx.lineTo(px, py);
        }
        ctx.stroke();

        // Crisp luminous core line
        ctx.globalAlpha = 0.65;
        ctx.lineWidth = 1.6;
        ctx.stroke();
        ctx.restore();
      });

      // Drifting nodes and hairline connections
      for (const n of nodes) {
        if (!reducedMotion) {
          n.x = (n.x + n.vx + 1) % 1;
          n.y = (n.y + n.vy + 1) % 1;
        }
      }

      ctx.lineWidth = 0.75;
      for (let i = 0; i < nodes.length; i++) {
        const a = nodes[i]!;
        const ax = a.x * width;
        const ay = a.y * height;
        for (let j = i + 1; j < nodes.length; j++) {
          const b = nodes[j]!;
          const bx = b.x * width;
          const by = b.y * height;
          const dist = Math.hypot(ax - bx, ay - by);
          if (dist < 115) {
            ctx.strokeStyle = `rgba(236, 236, 236, ${(1 - dist / 115) * 0.14})`;
            ctx.beginPath();
            ctx.moveTo(ax, ay);
            ctx.lineTo(bx, by);
            ctx.stroke();
          }
        }
      }

      for (const n of nodes) {
        const [c] = RIBBON_PALETTES[n.colorIdx]!;
        ctx.fillStyle = c;
        ctx.globalAlpha = 0.75;
        ctx.beginPath();
        ctx.arc(n.x * width, n.y * height, n.r, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.globalAlpha = 1;

      if (!reducedMotion) {
        rafId = window.requestAnimationFrame(drawFrame);
      }
    };

    resize();
    const ro = new ResizeObserver(resize);
    ro.observe(canvas);
    rafId = window.requestAnimationFrame(drawFrame);

    return () => {
      window.cancelAnimationFrame(rafId);
      ro.disconnect();
    };
  }, []);

  return <canvas ref={canvasRef} className="h-full w-full block" />;
}

export function GramMark({
  size = 24,
  className,
}: {
  size?: number;
  color?: string;
  className?: string;
}) {
  const uid = useId().replace(/:/g, '');
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 64 64"
      fill="none"
      className={className}
      aria-hidden="true"
    >
      <defs>
        <linearGradient id={`${uid}-p1`} x1="18" y1="8" x2="54" y2="32" gradientUnits="userSpaceOnUse">
          <stop offset="0%" stopColor="#34D399" />
          <stop offset="100%" stopColor="#0EA5E9" />
        </linearGradient>
        <linearGradient id={`${uid}-p2`} x1="18" y1="8" x2="54" y2="32" gradientUnits="userSpaceOnUse">
          <stop offset="0%" stopColor="#A78BFA" />
          <stop offset="100%" stopColor="#38BDF8" />
        </linearGradient>
        <linearGradient id={`${uid}-p3`} x1="18" y1="8" x2="54" y2="32" gradientUnits="userSpaceOnUse">
          <stop offset="0%" stopColor="#FBBF24" />
          <stop offset="100%" stopColor="#FB7185" />
        </linearGradient>
      </defs>
      <g transform="translate(32 32) scale(1.18) translate(-32 -32)">
        <g transform="rotate(0 32 32)">
          <path
            fill={`url(#${uid}-p1)`}
            d="M32.5 8.5c12.4 0 21 8.8 21 20.6 0 1.6-1.6 2.6-3 1.8-4.2-2.3-7.4-5.4-9.8-9.3-2.5-4-6-6.2-10.2-6.2-2.9 0-5.4.8-7.4 2.2-1.3.9-2.8-.4-2.1-1.8C23.1 11.4 27.4 8.5 32.5 8.5z"
          />
        </g>
        <g transform="rotate(120 32 32)">
          <path
            fill={`url(#${uid}-p2)`}
            d="M32.5 8.5c12.4 0 21 8.8 21 20.6 0 1.6-1.6 2.6-3 1.8-4.2-2.3-7.4-5.4-9.8-9.3-2.5-4-6-6.2-10.2-6.2-2.9 0-5.4.8-7.4 2.2-1.3.9-2.8-.4-2.1-1.8C23.1 11.4 27.4 8.5 32.5 8.5z"
          />
        </g>
        <g transform="rotate(240 32 32)">
          <path
            fill={`url(#${uid}-p3)`}
            d="M32.5 8.5c12.4 0 21 8.8 21 20.6 0 1.6-1.6 2.6-3 1.8-4.2-2.3-7.4-5.4-9.8-9.3-2.5-4-6-6.2-10.2-6.2-2.9 0-5.4.8-7.4 2.2-1.3.9-2.8-.4-2.1-1.8C23.1 11.4 27.4 8.5 32.5 8.5z"
          />
        </g>
      </g>
    </svg>
  );
}

export function AuthPage({
  mode,
  name,
  email,
  password,
  error,
  submitting = false,
  googleSubmitting = false,
  onModeChange,
  onNameChange,
  onEmailChange,
  onPasswordChange,
  onSubmit,
  onGoogleSignIn,
  onBackToWorkspace,
}: AuthPageProps) {
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [forgotHint, setForgotHint] = useState(false);

  const isSignUp = mode === 'signup';

  return (
    <div
      className="min-h-screen w-full flex bg-[#0e0f10] text-[#ececec] overflow-hidden"
      style={{ fontFamily: 'var(--font-sans, Inter, system-ui, sans-serif)' }}
    >
      {/* Left Auth Column */}
      <div className="relative flex-1 flex flex-col justify-center items-center px-6 py-10 sm:px-12 bg-[#0e0f10]">
        <button
          type="button"
          onClick={onBackToWorkspace}
          className="absolute top-6 left-6 inline-flex items-center gap-1.5 text-[13px] font-medium text-[#8e9299] hover:text-[#ececec] transition-colors cursor-pointer"
        >
          <ArrowLeftIcon className="w-4 h-4" />
          Back to workspace
        </button>

        <div
          key={mode}
          className="w-full max-w-[380px] flex flex-col items-center gram-fade-up"
        >
          <div className="w-12 h-12 rounded-2xl flex items-center justify-center mb-5 bg-[#151719] border border-white/10 shadow-lg">
            <GramMark size={28} />
          </div>

          <h1
            id="gram-auth-heading"
            className="text-[28px] leading-tight font-semibold tracking-tight text-[#ececec] text-center"
          >
            {isSignUp ? 'Create your account' : 'Welcome back!'}
          </h1>

          <p className="mt-2 text-[13.5px] text-[#8e9299] text-center">
            {isSignUp ? 'Already have an account? ' : "Don't have an account yet? "}
            <button
              type="button"
              onClick={() => {
                setForgotHint(false);
                onModeChange(isSignUp ? 'signin' : 'signup');
              }}
              className="font-medium text-[#34d399] hover:text-[#6ee7b7] hover:underline cursor-pointer"
            >
              {isSignUp ? 'Sign in' : 'Sign up now'}
            </button>
          </p>

          <form
            aria-labelledby="gram-auth-heading"
            method="POST"
            onSubmit={onSubmit}
            className="w-full mt-7 flex flex-col gap-3.5"
          >
            {isSignUp && (
              <div className="flex flex-col gap-1">
                <label htmlFor="gram-auth-name" className="sr-only">
                  Full name
                </label>
                <input
                  id="gram-auth-name"
                  name="name"
                  type="text"
                  autoComplete="name"
                  placeholder="Full name"
                  value={name}
                  onChange={(e) => onNameChange(e.target.value)}
                  className="w-full h-12 px-4 rounded-xl border border-white/10 bg-[#151719] text-[14.5px] text-[#ececec] placeholder:text-[#6b7280] outline-none transition-all focus:border-[#34d399]/60 focus:ring-3 focus:ring-[#34d399]/15"
                />
              </div>
            )}

            <div className="flex flex-col gap-1">
              <label htmlFor="gram-auth-email" className="sr-only">
                Email address
              </label>
              <input
                id="gram-auth-email"
                name="email"
                type="email"
                required
                autoComplete="email"
                placeholder="Email address"
                value={email}
                onChange={(e) => onEmailChange(e.target.value)}
                className="w-full h-12 px-4 rounded-xl border border-white/10 bg-[#151719] text-[14.5px] text-[#ececec] placeholder:text-[#6b7280] outline-none transition-all focus:border-[#34d399]/60 focus:ring-3 focus:ring-[#34d399]/15"
              />
            </div>

            <div className="relative flex flex-col gap-1">
              <label htmlFor="gram-auth-password" className="sr-only">
                Password
              </label>
              <input
                id="gram-auth-password"
                name="password"
                type={showPassword ? 'text' : 'password'}
                required
                minLength={isSignUp ? 8 : undefined}
                autoComplete={isSignUp ? 'new-password' : 'current-password'}
                placeholder={isSignUp ? 'Password (8+ characters)' : 'Password'}
                value={password}
                onChange={(e) => onPasswordChange(e.target.value)}
                className="w-full h-12 pl-4 pr-11 rounded-xl border border-white/10 bg-[#151719] text-[14.5px] text-[#ececec] placeholder:text-[#6b7280] outline-none transition-all focus:border-[#34d399]/60 focus:ring-3 focus:ring-[#34d399]/15"
              />
              <button
                type="button"
                aria-label={showPassword ? 'Hide password' : 'Show password'}
                onClick={() => setShowPassword((v) => !v)}
                className="absolute right-3 top-1/2 -translate-y-1/2 p-1.5 rounded-lg text-[#8e9299] hover:text-[#ececec] transition-colors cursor-pointer"
              >
                {showPassword ? <EyeOffIcon className="w-4 h-4" /> : <EyeIcon className="w-4 h-4" />}
              </button>
            </div>

            {!isSignUp && (
              <div className="flex items-center justify-between pt-0.5 text-[13px]">
                <label className="inline-flex items-center gap-2 text-[#9ca3af] cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                    className="w-4 h-4 rounded border-white/20 bg-[#151719] accent-[#34d399]"
                  />
                  Remember me
                </label>
                <button
                  type="button"
                  onClick={() => setForgotHint((v) => !v)}
                  className="font-medium text-[#34d399] hover:text-[#6ee7b7] hover:underline cursor-pointer"
                >
                  Forgot password?
                </button>
              </div>
            )}

            {forgotHint && !isSignUp && (
              <div className="text-[12px] text-[#9ca3af] bg-[#151719] border border-white/10 rounded-lg px-3 py-2">
                Local accounts are stored in your Gram SQLite database. Create a new account or continue locally if needed.
              </div>
            )}

            {error && (
              <div
                role="alert"
                className="text-[12.5px] text-red-300 bg-red-500/10 border border-red-500/25 rounded-lg px-3 py-2 text-center"
              >
                {error}
              </div>
            )}

            <div className="pt-2 flex justify-center">
              <button
                type="submit"
                disabled={submitting}
                className="min-w-[148px] h-11 px-7 rounded-full bg-[#ececec] hover:bg-white active:scale-[0.99] text-[#0e0f10] text-[14px] font-semibold inline-flex items-center justify-center gap-2 shadow-sm transition-all cursor-pointer disabled:opacity-60"
              >
                {submitting && <LoaderCircleIcon className="w-4 h-4 animate-spin" />}
                {isSignUp ? 'Create account' : 'Log in'}
              </button>
            </div>
          </form>

          <div className="w-full flex items-center gap-3 my-6">
            <div className="flex-1 h-px bg-white/10" />
            <span className="text-[11px] font-medium uppercase tracking-wider text-[#6b7280]">
              OR
            </span>
            <div className="flex-1 h-px bg-white/10" />
          </div>

          {onGoogleSignIn && (
            <button
              type="button"
              disabled={googleSubmitting || submitting}
              onClick={onGoogleSignIn}
              className="w-full h-11 mb-4 px-4 rounded-xl border border-white/10 bg-[#151719] hover:bg-[#1c1f22] text-[#ececec] text-[14px] font-medium inline-flex items-center justify-center gap-2.5 transition-colors cursor-pointer disabled:opacity-60"
            >
              {googleSubmitting ? (
                <LoaderCircleIcon className="w-4 h-4 animate-spin text-[#8e9299]" />
              ) : (
                <svg className="w-4 h-4 shrink-0" viewBox="0 0 24 24" aria-hidden="true">
                  <path
                    fill="#4285F4"
                    d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.665-5.17 3.665-9.17Z"
                  />
                  <path
                    fill="#34A853"
                    d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.11-6.72-4.96H1.29v3.14C3.26 21.3 7.31 24 12 24Z"
                  />
                  <path
                    fill="#FBBC05"
                    d="M5.28 14.24c-.24-.72-.38-1.49-.38-2.24s.14-1.52.38-2.24V6.62H1.29C.47 8.24 0 10.06 0 12s.47 3.76 1.29 5.38l3.99-3.14Z"
                  />
                  <path
                    fill="#EA4335"
                    d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.31 0 3.26 2.7 1.29 6.62l3.99 3.14c.95-2.85 3.6-4.96 6.72-4.96Z"
                  />
                </svg>
              )}
              Continue with Google
            </button>
          )}

          <button
            type="button"
            onClick={onBackToWorkspace}
            className="text-[13.5px] font-medium text-[#8e9299] hover:text-[#ececec] hover:underline cursor-pointer"
          >
            Continue with local workspace
          </button>
        </div>
      </div>

      {/* Right Animated Canvas 2D Panel */}
      <div
        className="hidden lg:block w-[36%] min-w-[340px] max-w-[520px] relative overflow-hidden border-l border-white/10 bg-[#090a0b]"
        aria-hidden="true"
      >
        <ObsidianAuroraCanvas />
      </div>
    </div>
  );
}
