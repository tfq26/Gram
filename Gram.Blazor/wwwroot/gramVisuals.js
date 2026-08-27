/**
 * Gram Canvas 2D Visual Elements Engine
 * High-performance, zero-dependency procedural visualizations inspired by Parallel.ai
 */
(function () {
    const instances = new Map();

    /**
     * Minimalist 3D Rotating Dot-Matrix Globe on Canvas 2D
     */
    class GramGlobe {
        constructor(canvas, options = {}) {
            this.canvas = canvas;
            this.ctx = canvas.getContext('2d', { alpha: true });
            this.options = Object.assign({
                pointCount: 380,
                radius: 42,
                dotSize: 1.4,
                rotationSpeed: 0.008,
                tilt: 0.22,
                accentColor: '#3b82f6',
                dimColor: 'rgba(255, 255, 255, 0.2)',
                glow: true,
                interactive: true
            }, options);

            this.rotation = 0;
            this.pointerX = 0;
            this.targetSpeed = this.options.rotationSpeed;
            this.currentSpeed = this.options.rotationSpeed;
            this.isRunning = false;
            this.points = [];

            this.initPoints();
            this.handleResize = this.resize.bind(this);
            this.handleMouseMove = this.onMouseMove.bind(this);
            this.handleMouseLeave = this.onMouseLeave.bind(this);
            this.render = this.render.bind(this);

            this.resize();
            window.addEventListener('resize', this.handleResize, { passive: true });

            if (this.options.interactive) {
                this.canvas.addEventListener('mousemove', this.handleMouseMove, { passive: true });
                this.canvas.addEventListener('mouseleave', this.handleMouseLeave, { passive: true });
            }

            this.observer = new IntersectionObserver((entries) => {
                const isVisible = entries[0]?.isIntersecting ?? true;
                if (isVisible && !this.isRunning) {
                    this.start();
                } else if (!isVisible && this.isRunning) {
                    this.stop();
                }
            }, { threshold: 0.05 });

            this.observer.observe(canvas);
            this.start();
        }

        initPoints() {
            this.points = [];
            const phi = Math.PI * (3 - Math.sqrt(5)); // Golden angle
            const count = this.options.pointCount;

            for (let i = 0; i < count; i++) {
                const y = 1 - (i / (count - 1)) * 2; // y goes from 1 to -1
                const radiusAtY = Math.sqrt(1 - y * y);
                const theta = phi * i;

                const x = Math.cos(theta) * radiusAtY;
                const z = Math.sin(theta) * radiusAtY;

                this.points.push({ x, y, z, baseAlpha: 0.3 + Math.random() * 0.7 });
            }
        }

        resize() {
            const rect = this.canvas.getBoundingClientRect();
            const dpr = Math.min(window.devicePixelRatio || 1, 2);
            const w = rect.width || this.options.radius * 2 + 16;
            const h = rect.height || this.options.radius * 2 + 16;

            this.canvas.width = w * dpr;
            this.canvas.height = h * dpr;
            this.ctx.scale(dpr, dpr);
            this.width = w;
            this.height = h;
            this.center = { x: w / 2, y: h / 2 };
            this.currentRadius = Math.min(this.options.radius, (Math.min(w, h) / 2) - 4);
        }

        onMouseMove(e) {
            const rect = this.canvas.getBoundingClientRect();
            const x = (e.clientX - rect.left) / rect.width - 0.5;
            this.targetSpeed = this.options.rotationSpeed * (1 + x * 2.5);
        }

        onMouseLeave() {
            this.targetSpeed = this.options.rotationSpeed;
        }

        start() {
            if (!this.isRunning) {
                this.isRunning = true;
                this.raf = requestAnimationFrame(this.render);
            }
        }

        stop() {
            this.isRunning = false;
            if (this.raf) cancelAnimationFrame(this.raf);
        }

        render() {
            if (!this.isRunning) return;

            this.currentSpeed += (this.targetSpeed - this.currentSpeed) * 0.08;
            this.rotation += this.currentSpeed;

            const ctx = this.ctx;
            ctx.clearRect(0, 0, this.width, this.height);

            const cx = this.center.x;
            const cy = this.center.y;
            const r = this.currentRadius;
            const tilt = this.options.tilt;
            const cosTilt = Math.cos(tilt);
            const sinTilt = Math.sin(tilt);

            const cosRot = Math.cos(this.rotation);
            const sinRot = Math.sin(this.rotation);

            // Sort points by Z to draw back-to-front
            const projected = [];

            for (let i = 0; i < this.points.length; i++) {
                const pt = this.points[i];

                // Y-axis rotation
                const x1 = pt.x * cosRot + pt.z * sinRot;
                const z1 = -pt.x * sinRot + pt.z * cosRot;
                const y1 = pt.y;

                // X-axis tilt
                const y2 = y1 * cosTilt - z1 * sinTilt;
                const z2 = y1 * sinTilt + z1 * cosTilt;
                const x2 = x1;

                projected.push({
                    x: cx + x2 * r,
                    y: cy + y2 * r,
                    z: z2,
                    alpha: pt.baseAlpha
                });
            }

            // Draw connecting longitude/latitude subtle rings if glow enabled
            if (this.options.glow) {
                ctx.beginPath();
                ctx.arc(cx, cy, r, 0, Math.PI * 2);
                ctx.strokeStyle = 'rgba(255, 255, 255, 0.06)';
                ctx.lineWidth = 1;
                ctx.stroke();

                // Subtle equator line
                ctx.beginPath();
                ctx.ellipse(cx, cy, r, r * Math.sin(tilt), tilt, 0, Math.PI * 2);
                ctx.strokeStyle = 'rgba(59, 130, 246, 0.12)';
                ctx.lineWidth = 0.8;
                ctx.stroke();
            }

            // Render points
            for (let i = 0; i < projected.length; i++) {
                const p = projected[i];
                // z2 ranges from -1 to 1. Front hemisphere: z2 > 0
                const isFront = p.z > 0;
                const depthScale = 0.7 + (p.z + 1) * 0.3; // 0.7 to 1.3
                const size = this.options.dotSize * depthScale;

                ctx.beginPath();
                ctx.arc(p.x, p.y, Math.max(0.6, size), 0, Math.PI * 2);

                if (isFront) {
                    const normZ = p.z; // 0 to 1
                    const alpha = 0.35 + normZ * 0.65;
                    ctx.fillStyle = normZ > 0.6 
                        ? this.options.accentColor 
                        : `rgba(226, 232, 240, ${alpha})`;
                    
                    if (this.options.glow && normZ > 0.75) {
                        ctx.shadowColor = this.options.accentColor;
                        ctx.shadowBlur = 4;
                    } else {
                        ctx.shadowBlur = 0;
                    }
                } else {
                    const alpha = 0.08 + (p.z + 1) * 0.12;
                    ctx.fillStyle = `rgba(148, 163, 184, ${alpha})`;
                    ctx.shadowBlur = 0;
                }

                ctx.fill();
            }
            ctx.shadowBlur = 0;

            this.raf = requestAnimationFrame(this.render);
        }

        destroy() {
            this.stop();
            this.observer.disconnect();
            window.removeEventListener('resize', this.handleResize);
            if (this.options.interactive) {
                this.canvas.removeEventListener('mousemove', this.handleMouseMove);
                this.canvas.removeEventListener('mouseleave', this.handleMouseLeave);
            }
        }
    }

    /**
     * Precision Radar Sweep Visualizer for live research & search
     */
    class GramRadar {
        constructor(canvas, options = {}) {
            this.canvas = canvas;
            this.ctx = canvas.getContext('2d', { alpha: true });
            this.options = Object.assign({
                radius: 18,
                speed: 0.045,
                sweepColor: 'rgba(59, 130, 246, 0.35)',
                ringColor: 'rgba(255, 255, 255, 0.1)',
                blipColor: '#38bdf8'
            }, options);

            this.angle = 0;
            this.isRunning = false;
            this.blips = [
                { r: 0.45, a: 0.8, life: 0 },
                { r: 0.75, a: 2.3, life: 0 },
                { r: 0.6, a: 4.1, life: 0 }
            ];

            this.render = this.render.bind(this);
            this.resize();
            this.start();
        }

        resize() {
            const rect = this.canvas.getBoundingClientRect();
            const dpr = Math.min(window.devicePixelRatio || 1, 2);
            const w = rect.width || this.options.radius * 2;
            const h = rect.height || this.options.radius * 2;

            this.canvas.width = w * dpr;
            this.canvas.height = h * dpr;
            this.ctx.scale(dpr, dpr);
            this.width = w;
            this.height = h;
            this.center = { x: w / 2, y: h / 2 };
            this.radius = Math.min(w, h) / 2 - 2;
        }

        start() {
            if (!this.isRunning) {
                this.isRunning = true;
                this.raf = requestAnimationFrame(this.render);
            }
        }

        stop() {
            this.isRunning = false;
            if (this.raf) cancelAnimationFrame(this.raf);
        }

        render() {
            if (!this.isRunning) return;

            this.angle = (this.angle + this.options.speed) % (Math.PI * 2);
            const ctx = this.ctx;
            const cx = this.center.x;
            const cy = this.center.y;
            const r = this.radius;

            ctx.clearRect(0, 0, this.width, this.height);

            // Concentric rings
            ctx.strokeStyle = this.options.ringColor;
            ctx.lineWidth = 1;
            for (let f = 0.33; f <= 1.0; f += 0.33) {
                ctx.beginPath();
                ctx.arc(cx, cy, r * f, 0, Math.PI * 2);
                ctx.stroke();
            }

            // Crosshair
            ctx.beginPath();
            ctx.moveTo(cx - r, cy);
            ctx.lineTo(cx + r, cy);
            ctx.moveTo(cx, cy - r);
            ctx.lineTo(cx, cy + r);
            ctx.strokeStyle = 'rgba(255, 255, 255, 0.05)';
            ctx.stroke();

            // Radar sweep trail
            const sweepArc = Math.PI * 0.45;
            const gradient = ctx.createConicGradient(this.angle - sweepArc, cx, cy);
            gradient.addColorStop(0, 'rgba(59, 130, 246, 0)');
            gradient.addColorStop(0.2, 'rgba(59, 130, 246, 0.12)');
            gradient.addColorStop(0.35, this.options.sweepColor);
            gradient.addColorStop(0.4, 'rgba(59, 130, 246, 0)');

            ctx.fillStyle = gradient;
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.arc(cx, cy, r, this.angle - sweepArc, this.angle);
            ctx.closePath();
            ctx.fill();

            // Sweep leading edge line
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.lineTo(cx + Math.cos(this.angle) * r, cy + Math.sin(this.angle) * r);
            ctx.strokeStyle = 'rgba(147, 197, 253, 0.8)';
            ctx.lineWidth = 1.2;
            ctx.stroke();

            // Blips detection
            for (let i = 0; i < this.blips.length; i++) {
                const blip = this.blips[i];
                const diff = (this.angle - blip.a + Math.PI * 2) % (Math.PI * 2);

                if (diff < 0.15) {
                    blip.life = 1.0;
                }

                if (blip.life > 0.02) {
                    const bx = cx + Math.cos(blip.a) * (r * blip.r);
                    const by = cy + Math.sin(blip.a) * (r * blip.r);

                    ctx.beginPath();
                    ctx.arc(bx, by, 2.2 * blip.life, 0, Math.PI * 2);
                    ctx.fillStyle = `rgba(56, 189, 248, ${blip.life})`;
                    ctx.shadowColor = '#38bdf8';
                    ctx.shadowBlur = 6 * blip.life;
                    ctx.fill();
                    ctx.shadowBlur = 0;

                    blip.life *= 0.94;
                }
            }

            this.raf = requestAnimationFrame(this.render);
        }

        destroy() {
            this.stop();
        }
    }

    // Public API for Blazor
    window.gramVisuals = {
        initGlobe: function (canvasId, options) {
            const canvas = typeof canvasId === 'string' ? document.getElementById(canvasId) : canvasId;
            if (!canvas) return null;
            if (instances.has(canvas)) {
                instances.get(canvas).destroy();
            }
            const globe = new GramGlobe(canvas, options || {});
            instances.set(canvas, globe);
            return true;
        },

        initRadar: function (canvasId, options) {
            const canvas = typeof canvasId === 'string' ? document.getElementById(canvasId) : canvasId;
            if (!canvas) return null;
            if (instances.has(canvas)) {
                instances.get(canvas).destroy();
            }
            const radar = new GramRadar(canvas, options || {});
            instances.set(canvas, radar);
            return true;
        },

        destroy: function (canvasId) {
            const canvas = typeof canvasId === 'string' ? document.getElementById(canvasId) : canvasId;
            if (canvas && instances.has(canvas)) {
                instances.get(canvas).destroy();
                instances.delete(canvas);
            }
        }
    };

    window.openHtmlInNewWindow = function (htmlContent, title) {
        const win = window.open('', '_blank');
        if (win) {
            win.document.open();
            win.document.write(htmlContent);
            win.document.close();
            if (title) {
                try { win.document.title = title; } catch (_) {}
            }
        }
    };
})();
