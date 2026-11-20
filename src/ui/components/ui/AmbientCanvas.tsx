import React, { useEffect, useRef } from 'react';

const PALETTE: ReadonlyArray<readonly [number, number, number]> = [
  [52, 211, 153],  // #34D399 Aurora Mint
  [14, 165, 233],  // #0EA5E9 Cyan
  [167, 139, 250], // #A78BFA Nebula Violet
  [56, 189, 248],  // #38BDF8 Sky
  [251, 191, 36],  // #FBBF24 Solar Amber
  [251, 113, 133], // #FB7185 Rose
];

interface LavaBubble {
  x: number; // normalized 0..1
  y: number; // normalized -0.15..1.15
  radius: number; // px
  speed: number; // normalized y/sec (positive = rising up, negative = sinking)
  swayAmp: number; // px
  swayFreq: number;
  phase: number;
  colorIdx: number;
  wobblePhase: number;
  direction: 1 | -1; // 1 = rising from bottom to top, -1 = cooling/sinking
}

function createBubble(index: number, total: number, randomY = true): LavaBubble {
  const sizeTier = index % 5;
  const radius =
    sizeTier === 0
      ? 68 + (index % 3) * 18 // large hero orbs
      : sizeTier === 1
        ? 44 + (index % 4) * 8 // medium-large
        : sizeTier === 2
          ? 28 + (index % 3) * 6 // medium
          : 14 + (index % 4) * 4; // small rising sparks

  // Mostly rising from bottom to top; a few large orbs gently sink back down
  const direction: 1 | -1 = sizeTier === 0 && index % 2 === 1 ? -1 : 1;
  const baseSpeed = direction === 1 ? (0.022 + (1 / Math.sqrt(radius)) * 0.14) : -(0.016 + (index % 3) * 0.004);

  return {
    x: 0.08 + ((index * 0.6180339 + 0.13) % 0.84),
    y: randomY ? (index / total) * 1.1 - 0.05 : direction === 1 ? 1.12 : -0.12,
    radius,
    speed: baseSpeed,
    swayAmp: 8 + (index % 4) * 5,
    swayFreq: 0.35 + (index % 3) * 0.15,
    phase: index * 1.47,
    colorIdx: index % PALETTE.length,
    wobblePhase: index * 0.91,
    direction,
  };
}

export function PlanAmbientCanvas({
  className = '',
  ideaActivity = 0,
}: {
  className?: string;
  ideaActivity?: number;
}) {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const prevActivityRef = useRef(ideaActivity);
  const boostRef = useRef(0);

  useEffect(() => {
    const delta = Math.abs(ideaActivity - prevActivityRef.current);
    if (delta > 0) {
      boostRef.current = Math.min(1, boostRef.current + Math.min(0.5, delta * 0.04));
      prevActivityRef.current = ideaActivity;
    }
  }, [ideaActivity]);

  useEffect(() => {
    const root = rootRef.current;
    const canvas = canvasRef.current;
    if (!root || !canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const bubbles = Array.from({ length: 18 }, (_, i) => createBubble(i, 18, true));
    let frameId = 0;
    let width = 0;
    let height = 0;
    let isVisible = true;
    let isDocumentVisible = !document.hidden;
    let lastTs = performance.now();

    const drawBubble = (b: LavaBubble, timeSec: number, boost: number) => {
      const [r, g, bl] = PALETTE[b.colorIdx]!;
      const cx = b.x * width + Math.sin(timeSec * b.swayFreq + b.phase) * b.swayAmp;
      const cy = b.y * height;

      // Fade smoothly near top and bottom edges
      const edgeFade = Math.min(
        1,
        Math.max(0, Math.min((b.y + 0.08) / 0.18, (1.08 - b.y) / 0.18))
      );
      if (edgeFade <= 0.01) return;

      // Subtle organic lava-lamp squash & stretch
      const wobble = Math.sin(timeSec * 1.1 + b.wobblePhase) * 0.06;
      const rx = b.radius * (1 - wobble * 0.7);
      const ry = b.radius * (1 + wobble);

      ctx.save();
      ctx.translate(cx, cy);
      ctx.scale(rx / b.radius, ry / b.radius);

      // 1. Soft outer ambient halo
      const haloGrad = ctx.createRadialGradient(0, 0, b.radius * 0.5, 0, 0, b.radius * 1.75);
      haloGrad.addColorStop(0, `rgba(${r}, ${g}, ${bl}, ${0.12 * edgeFade})`);
      haloGrad.addColorStop(0.6, `rgba(${r}, ${g}, ${bl}, ${0.045 * edgeFade})`);
      haloGrad.addColorStop(1, `rgba(${r}, ${g}, ${bl}, 0)`);
      ctx.fillStyle = haloGrad;
      ctx.beginPath();
      ctx.arc(0, 0, b.radius * 1.75, 0, Math.PI * 2);
      ctx.fill();

      // 2. Translucent glossy orb body (luminous rim, softer center)
      const bodyGrad = ctx.createRadialGradient(
        -b.radius * 0.28,
        -b.radius * 0.28,
        b.radius * 0.1,
        0,
        0,
        b.radius
      );
      const energyAlpha = 1 + boost * 0.25;
      bodyGrad.addColorStop(0, `rgba(${Math.min(255, r + 55)}, ${Math.min(255, g + 55)}, ${Math.min(255, bl + 55)}, ${Math.min(0.32, 0.22 * edgeFade * energyAlpha)})`);
      bodyGrad.addColorStop(0.55, `rgba(${r}, ${g}, ${bl}, ${Math.min(0.24, 0.14 * edgeFade * energyAlpha)})`);
      bodyGrad.addColorStop(0.88, `rgba(${r}, ${g}, ${bl}, ${Math.min(0.34, 0.24 * edgeFade * energyAlpha)})`);
      bodyGrad.addColorStop(1, `rgba(${r}, ${g}, ${bl}, ${0.05 * edgeFade})`);
      ctx.fillStyle = bodyGrad;
      ctx.beginPath();
      ctx.arc(0, 0, b.radius, 0, Math.PI * 2);
      ctx.fill();

      // 3. Crisp upper-left specular shine crescent
      ctx.save();
      ctx.rotate(-Math.PI * 0.22);
      const shineGrad = ctx.createRadialGradient(
        0,
        -b.radius * 0.48,
        0,
        0,
        -b.radius * 0.48,
        b.radius * 0.45
      );
      shineGrad.addColorStop(0, `rgba(255, 255, 255, ${0.42 * edgeFade})`);
      shineGrad.addColorStop(0.5, `rgba(255, 255, 255, ${0.14 * edgeFade})`);
      shineGrad.addColorStop(1, 'rgba(255, 255, 255, 0)');
      ctx.fillStyle = shineGrad;
      ctx.beginPath();
      ctx.ellipse(0, -b.radius * 0.5, b.radius * 0.38, b.radius * 0.18, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.restore();

      // 4. Tiny secondary bottom-right rim reflection
      ctx.fillStyle = `rgba(255, 255, 255, ${0.16 * edgeFade})`;
      ctx.beginPath();
      ctx.arc(b.radius * 0.36, b.radius * 0.42, Math.max(1.5, b.radius * 0.08), 0, Math.PI * 2);
      ctx.fill();

      ctx.restore();
    };

    const render = (dt: number, timeSec: number) => {
      if (!width || !height) return;
      const boost = boostRef.current;
      boostRef.current = Math.max(0, boost - dt * 0.7);

      ctx.clearRect(0, 0, width, height);

      // Update & draw largest-to-smallest for natural depth
      const speedMult = 1 + boost * 1.15;
      for (let i = 0; i < bubbles.length; i++) {
        const b = bubbles[i]!;
        b.y -= b.speed * dt * speedMult;
        if (b.direction === 1 && b.y < -0.14) {
          b.y = 1.12;
          b.x = 0.08 + ((b.x + 0.37 + Math.sin(timeSec + i) * 0.12) % 0.84);
        } else if (b.direction === -1 && b.y > 1.14) {
          b.y = -0.12;
          b.x = 0.08 + ((b.x + 0.29 + Math.cos(timeSec + i) * 0.12) % 0.84);
        }
      }

      const sorted = [...bubbles].sort((a, b) => b.radius - a.radius);
      for (const b of sorted) {
        drawBubble(b, timeSec, boost);
      }
    };

    const tick = (ts: number) => {
      frameId = 0;
      if (!isVisible || !isDocumentVisible || reduceMotion.matches) return;
      const dt = Math.min(0.05, Math.max(0.001, (ts - lastTs) / 1000));
      lastTs = ts;
      render(dt, ts * 0.001);
      frameId = window.requestAnimationFrame(tick);
    };

    const startLoop = () => {
      if (!frameId && isVisible && isDocumentVisible && !reduceMotion.matches) {
        lastTs = performance.now();
        frameId = window.requestAnimationFrame(tick);
      }
    };

    const stopLoop = () => {
      if (frameId) {
        window.cancelAnimationFrame(frameId);
        frameId = 0;
      }
    };

    const handleResize = () => {
      const rect = root.getBoundingClientRect();
      width = Math.max(1, Math.floor(rect.width));
      height = Math.max(1, Math.floor(rect.height));
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.floor(width * dpr);
      canvas.height = Math.floor(height * dpr);
      canvas.style.width = `${width}px`;
      canvas.style.height = `${height}px`;
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      render(0, performance.now() * 0.001);
    };

    const ro = new ResizeObserver(handleResize);
    ro.observe(root);
    const io = new IntersectionObserver(
      (entries) => {
        isVisible = entries.some((e) => e.isIntersecting);
        if (isVisible) startLoop();
        else stopLoop();
      },
      { threshold: 0.05 }
    );
    io.observe(root);

    const onVisibility = () => {
      isDocumentVisible = !document.hidden;
      if (isDocumentVisible) startLoop();
      else stopLoop();
    };
    document.addEventListener('visibilitychange', onVisibility);
    handleResize();
    startLoop();

    return () => {
      stopLoop();
      ro.disconnect();
      io.disconnect();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, []);

  return (
    <div
      ref={rootRef}
      aria-hidden="true"
      className={`pointer-events-none absolute inset-0 overflow-hidden opacity-85 ${className}`}
      style={{
        maskImage: 'radial-gradient(ellipse 90% 82% at 50% 48%, black 42%, transparent 100%)',
        WebkitMaskImage: 'radial-gradient(ellipse 90% 82% at 50% 48%, black 42%, transparent 100%)',
      }}
    >
      <canvas ref={canvasRef} className="block h-full w-full" />
    </div>
  );
}
