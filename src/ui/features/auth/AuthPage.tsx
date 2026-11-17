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

const CAPTIONS = [
  'Ideas become clearer\nwhen you explore them.',
  'Complexity dissolves\nwith the right questions.',
  'Every breakthrough starts\nas a simple curiosity.',
  'Precision is found\nin the spaces between thoughts.',
  'Structure gives form\nto raw imagination.',
  'Deep focus reveals\nwhat haste obscures.',
  'The best tools disappear\ninto your flow of thought.',
  'Great systems emerge\nfrom elegant iterations.',
  'Intelligence thrives\nwhere clarity meets intent.',
  'Code is poetry written\nto orchestrate reality.',
];

function GramPixelMatrixCanvas() {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const [captionIndex, setCaptionIndex] = useState(() => Math.floor(Math.random() * CAPTIONS.length));
  const [captionVisible, setCaptionVisible] = useState(true);

  useEffect(() => {
    const root = rootRef.current;
    const canvas = canvasRef.current;
    if (!root || !canvas) return;
    const ctx = canvas.getContext('2d', { alpha: false });
    if (!ctx) return;

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let frameId = 0;
    let isVisible = true;
    let isDocumentVisible = !document.hidden;
    let width = 0;
    let height = 0;
    let columns = 0;
    let rows = 0;
    let cellWidth = 0;
    let cellHeight = 0;
    const gap = 1.2;
    let basePattern = new Float32Array(0);
    const pointer = { x: -1000, y: -1000, active: false };
    const trail: Array<{ x: number; y: number; time: number }> = [];

    const initGridData = () => {
      basePattern = new Float32Array(columns * rows);
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < columns; c++) {
          const idx = r * columns + c;
          const nxBg = c / columns;
          const nyBg = r / rows;
          const v1 = Math.sin(nxBg * 12 + nyBg * 8);
          const v2 = Math.cos(nxBg * 6 - nyBg * 14);
          const v3 = Math.sin((nxBg + nyBg) * 16);
          const base = (v1 * 0.4 + v2 * 0.4 + v3 * 0.2 + 1) * 0.5;
          basePattern[idx] = Math.max(0, Math.min(1, base * 0.42));
        }
      }
    };

    const render = (timestamp: number) => {
      if (!width || !height) return;
      const time = timestamp * 0.001;
      const drawW = Math.max(1, cellWidth - gap);
      const drawH = Math.max(1, cellHeight - gap);

      ctx.fillStyle = '#090a0b';
      ctx.fillRect(0, 0, width, height);

      const now = performance.now();
      for (let t = trail.length - 1; t >= 0; t--) {
        if (now - trail[t]!.time > 650) {
          trail.splice(t, 1);
        }
      }

      const paletteStops: ReadonlyArray<readonly [number, number, number]> = [
        [52, 211, 153],  // #34D399 Aurora Mint
        [14, 165, 233],  // #0EA5E9 Cyan
        [167, 139, 250], // #A78BFA Nebula Violet
        [56, 189, 248],  // #38BDF8 Sky
        [251, 191, 36],  // #FBBF24 Solar Amber
        [251, 113, 133], // #FB7185 Rose
      ];

      for (let r = 0; r < rows; r++) {
        const y = r * cellHeight;
        const cellCenterY = y + cellHeight * 0.5;
        const ny = r / rows;
        for (let c = 0; c < columns; c++) {
          const idx = r * columns + c;
          const x = c * cellWidth;
          const cellCenterX = x + cellWidth * 0.5;
          const nx = c / columns;

          const waveX = Math.sin(c * 0.18 + time * 1.6);
          const waveY = Math.cos(r * 0.14 - time * 1.2);
          const sweep = Math.sin((c + r) * 0.12 - time * 2.4);

          let lum = (basePattern[idx] || 0) * 0.55 + (waveX * waveY * 0.15 + sweep * 0.12);

          if (pointer.active && pointer.x >= 0) {
            const dist = Math.hypot(cellCenterX - pointer.x, cellCenterY - pointer.y);
            if (dist < 140) {
              const factor = 1 - dist / 140;
              lum += factor * factor * 0.85;
            }
          }

          for (let t = 0; t < trail.length; t++) {
            const tp = trail[t]!;
            const age = (now - tp.time) / 650;
            const tDist = Math.hypot(cellCenterX - tp.x, cellCenterY - tp.y);
            if (tDist < 100) {
              const tFactor = (1 - tDist / 100) * (1 - age);
              lum += tFactor * 0.45;
            }
          }

          lum = Math.max(0.04, Math.min(1, lum));

          // Smooth spatial + temporal blend across the Per-Blade Trio stops
          const colorPos = ((nx * 0.65 + ny * 0.55 + time * 0.06) % 1 + 1) % 1;
          const scaled = colorPos * paletteStops.length;
          const i0 = Math.floor(scaled) % paletteStops.length;
          const i1 = (i0 + 1) % paletteStops.length;
          const frac = scaled - Math.floor(scaled);
          const c0 = paletteStops[i0]!;
          const c1 = paletteStops[i1]!;
          const cr = Math.round(c0[0] + (c1[0] - c0[0]) * frac);
          const cg = Math.round(c0[1] + (c1[1] - c0[1]) * frac);
          const cb = Math.round(c0[2] + (c1[2] - c0[2]) * frac);

          let fill = '';
          if (lum < 0.15) {
            fill = `rgba(26, 30, 36, ${0.32 + lum * 2})`;
          } else if (lum < 0.42) {
            fill = `rgba(${cr}, ${cg}, ${cb}, ${(0.22 + lum * 0.75).toFixed(3)})`;
          } else if (lum < 0.78) {
            fill = `rgba(${cr}, ${cg}, ${cb}, ${(0.55 + lum * 0.45).toFixed(3)})`;
          } else if (lum < 0.91) {
            const br = Math.min(255, Math.round(cr + (255 - cr) * 0.45));
            const bg = Math.min(255, Math.round(cg + (255 - cg) * 0.45));
            const bb = Math.min(255, Math.round(cb + (255 - cb) * 0.45));
            fill = `rgb(${br}, ${bg}, ${bb})`;
          } else {
            fill = '#f8fafc';
          }

          ctx.fillStyle = fill;
          ctx.fillRect(x + gap * 0.5, y + gap * 0.5, drawW, drawH);
        }
      }

      const vignette = ctx.createRadialGradient(
        width * 0.5,
        height * 0.5,
        Math.min(width, height) * 0.25,
        width * 0.5,
        height * 0.5,
        Math.max(width, height) * 0.72
      );
      vignette.addColorStop(0, 'rgba(9, 10, 11, 0)');
      vignette.addColorStop(0.65, 'rgba(9, 10, 11, 0.3)');
      vignette.addColorStop(1, 'rgba(9, 10, 11, 0.88)');
      ctx.fillStyle = vignette;
      ctx.fillRect(0, 0, width, height);
    };

    const loop = (timestamp: number) => {
      render(timestamp);
      if (isVisible && isDocumentVisible && !reduceMotion.matches) {
        frameId = window.requestAnimationFrame(loop);
      } else {
        frameId = 0;
      }
    };

    const updateAnimationState = () => {
      const shouldAnimate = isVisible && isDocumentVisible && !reduceMotion.matches;
      if (shouldAnimate && !frameId) {
        frameId = window.requestAnimationFrame(loop);
      } else if (!shouldAnimate && frameId) {
        window.cancelAnimationFrame(frameId);
        frameId = 0;
      }
    };

    const resize = () => {
      const rect = root.getBoundingClientRect();
      if (rect.width < 2 || rect.height < 2) return;
      width = rect.width;
      height = rect.height;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

      const cellSize = width > 900 ? 11 : 9.5;
      columns = Math.max(28, Math.floor(width / cellSize));
      rows = Math.max(36, Math.floor(height / cellSize));
      cellWidth = width / columns;
      cellHeight = height / rows;

      initGridData();
      render(performance.now());
      updateAnimationState();
    };

    const updatePointer = (e: PointerEvent) => {
      const rect = root.getBoundingClientRect();
      pointer.x = e.clientX - rect.left;
      pointer.y = e.clientY - rect.top;
    };

    const onPointerEnter = (e: PointerEvent) => {
      pointer.active = true;
      updatePointer(e);
    };

    const onPointerMove = (e: PointerEvent) => {
      pointer.active = true;
      updatePointer(e);
      trail.push({ x: pointer.x, y: pointer.y, time: performance.now() });
      if (trail.length > 14) trail.shift();
    };

    const onPointerLeave = () => {
      pointer.active = false;
    };

    const onVisibilityChange = () => {
      isDocumentVisible = !document.hidden;
      updateAnimationState();
    };

    window.addEventListener('pointerenter', onPointerEnter, { passive: true });
    window.addEventListener('pointermove', onPointerMove, { passive: true });
    document.addEventListener('pointerleave', onPointerLeave, { passive: true });
    document.addEventListener('visibilitychange', onVisibilityChange);

    const ro = new ResizeObserver(resize);
    ro.observe(root);
    const io = new IntersectionObserver(
      (entries) => {
        isVisible = entries[0]?.isIntersecting ?? true;
        updateAnimationState();
      },
      { threshold: 0.01 }
    );
    io.observe(root);

    resize();

    const captionTimer = window.setInterval(() => {
      if (!isVisible || !isDocumentVisible) return;
      setCaptionVisible(false);
      window.setTimeout(() => {
        setCaptionIndex((prev) => {
          let next = Math.floor(Math.random() * (CAPTIONS.length - 1));
          if (next >= prev) next += 1;
          return next;
        });
        setCaptionVisible(true);
      }, 380);
    }, 6500);

    return () => {
      if (frameId) window.cancelAnimationFrame(frameId);
      window.clearInterval(captionTimer);
      ro.disconnect();
      io.disconnect();
      window.removeEventListener('pointerenter', onPointerEnter);
      window.removeEventListener('pointermove', onPointerMove);
      document.removeEventListener('pointerleave', onPointerLeave);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);

  const captionLines = (CAPTIONS[captionIndex] || CAPTIONS[0]!).split('\n');

  return (
    <div ref={rootRef} className="relative h-full w-full overflow-hidden bg-[#090a0b]">
      <canvas
        ref={canvasRef}
        role="img"
        aria-label="Interactive Gram gradient pixel matrix artwork"
        className="block h-full w-full"
      />
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 bottom-0 h-36 bg-gradient-to-t from-[#090a0b]/85 via-[#090a0b]/35 to-transparent"
      />
      <div className="pointer-events-none fixed bottom-6 left-6 z-10 hidden sm:block max-w-xs">
        <strong
          className="block text-[15px] font-medium leading-snug tracking-tight text-[#ececec]/80 transition-all duration-400"
          style={{
            opacity: captionVisible ? 1 : 0,
            transform: captionVisible ? 'translateY(0)' : 'translateY(4px)',
          }}
        >
          {captionLines[0]}
          <br />
          {captionLines[1]}
        </strong>
      </div>
    </div>
  );
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
      className="relative min-h-screen w-full flex items-center justify-center bg-[#090a0b] text-[#ececec] overflow-hidden px-4 py-12"
      style={{ fontFamily: 'var(--font-sans, Inter, system-ui, sans-serif)' }}
    >
      {/* Full-bleed Animated Gradient Pixel Matrix Background */}
      <div className="fixed inset-0 z-0">
        <GramPixelMatrixCanvas />
      </div>

      <button
        type="button"
        onClick={onBackToWorkspace}
        className="fixed top-5 left-5 z-20 inline-flex items-center gap-1.5 rounded-xl border border-white/10 bg-[#0e0f10]/75 px-3 py-1.5 text-[13px] font-medium text-[#9ca3af] backdrop-blur-md hover:border-white/20 hover:text-[#ececec] transition-colors cursor-pointer"
      >
        <ArrowLeftIcon className="w-4 h-4" />
        Back to workspace
      </button>

      {/* Centered Obsidian Sign-In Card */}
      <div className="relative z-10 w-full max-w-[420px] rounded-2xl border border-white/12 bg-[#0e0f10]/86 p-7 sm:p-9 shadow-[0_24px_80px_rgba(0,0,0,0.75)] backdrop-blur-xl">
        <div
          key={mode}
          className="w-full flex flex-col items-center gram-fade-up"
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
            <div className="mb-4 flex items-center justify-center gap-3">
              <button
                type="button"
                disabled={googleSubmitting || submitting}
                onClick={onGoogleSignIn}
                aria-label="Continue with Google"
                title="Continue with Google"
                className="h-11 w-11 rounded-full border border-white/10 bg-[#151719] hover:bg-[#1c1f22] hover:border-white/20 inline-flex items-center justify-center transition-colors cursor-pointer disabled:opacity-60"
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
              </button>
            </div>
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
    </div>
  );
}
