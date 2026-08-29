/**
 * Gram Interactive Purple Pixel Matrix Tapestry
 * Discrete tactile cyber pixel grid with fluid procedural wave harmonics,
 * direct pointer excitation, kinetic motion trails, and rotating thought-provoking captions.
 */
(function () {
    const instances = new Map();

    class GramPixelMatrix {
        constructor(root) {
            this.root = root;
            this.canvas = root.querySelector('canvas');
            this.ctx = this.canvas?.getContext('2d', { alpha: false });
            this.frameId = 0;
            this.time = 0;
            this.isVisible = true;
            this.isDocumentVisible = !document.hidden;
            this.pointer = { x: -1000, y: -1000, active: false };
            this.trail = [];
            this.reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

            this.onPointerMove = this.handlePointerMove.bind(this);
            this.onPointerEnter = this.handlePointerEnter.bind(this);
            this.onPointerLeave = this.handlePointerLeave.bind(this);
            this.onVisibilityChange = this.handleVisibilityChange.bind(this);
            this.loop = this.loop.bind(this);

            if (!this.canvas || !this.ctx) return;

            // Caption Rotation (10 thought-provoking messages)
            this.captionEl = root.querySelector('.gram-auth-artwork-caption strong');
            this.captions = [
                "Ideas become clearer<br />when you explore them.",
                "Complexity dissolves<br />with the right questions.",
                "Every breakthrough starts<br />as a simple curiosity.",
                "Precision is found<br />in the spaces between thoughts.",
                "Structure gives form<br />to raw imagination.",
                "Deep focus reveals<br />what haste obscures.",
                "The best tools disappear<br />into your flow of thought.",
                "Great systems emerge<br />from elegant iterations.",
                "Intelligence thrives<br />where clarity meets intent.",
                "Code is poetry written<br />to orchestrate reality."
            ];
            this.captionIndex = Math.floor(Math.random() * this.captions.length);
            this.captionTimer = null;
            if (this.captionEl) {
                this.captionEl.innerHTML = this.captions[this.captionIndex];
                this.startCaptionRotation();
            }

            root.addEventListener('pointermove', this.onPointerMove, { passive: true });
            root.addEventListener('pointerenter', this.onPointerEnter, { passive: true });
            root.addEventListener('pointerleave', this.onPointerLeave, { passive: true });
            document.addEventListener('visibilitychange', this.onVisibilityChange);

            this.resizeObserver = new ResizeObserver(() => this.resize());
            this.resizeObserver.observe(root);
            this.intersectionObserver = new IntersectionObserver(entries => {
                this.isVisible = entries[0]?.isIntersecting ?? true;
                this.updateAnimationState();
            }, { threshold: 0.01 });
            this.intersectionObserver.observe(root);

            this.resize();
        }

        resize() {
            if (!this.ctx) return;
            const rect = this.root.getBoundingClientRect();
            if (rect.width < 2 || rect.height < 2) return;

            this.width = rect.width;
            this.height = rect.height;
            this.pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
            this.canvas.width = Math.round(this.width * this.pixelRatio);
            this.canvas.height = Math.round(this.height * this.pixelRatio);
            this.ctx.setTransform(this.pixelRatio, 0, 0, this.pixelRatio, 0, 0);

            // Crisp tactile pixel blocks
            const cellSize = this.width > 900 ? 11 : 9.5;
            this.columns = Math.max(28, Math.floor(this.width / cellSize));
            this.rows = Math.max(36, Math.floor(this.height / cellSize));
            this.cellWidth = this.width / this.columns;
            this.cellHeight = this.height / this.rows;
            this.gap = 1.2;

            this.initGridData();
            this.render(performance.now());
            this.updateAnimationState();
        }

        initGridData() {
            const count = this.columns * this.rows;
            this.basePattern = new Float32Array(count);

            for (let r = 0; r < this.rows; r++) {
                for (let c = 0; c < this.columns; c++) {
                    const idx = r * this.columns + c;

                    // Multi-frequency background sinusoidal noise
                    const nx_bg = c / this.columns;
                    const ny_bg = r / this.rows;
                    const v1 = Math.sin(nx_bg * 12 + ny_bg * 8);
                    const v2 = Math.cos(nx_bg * 6 - ny_bg * 14);
                    const v3 = Math.sin((nx_bg + ny_bg) * 16);
                    const base = (v1 * 0.4 + v2 * 0.4 + v3 * 0.2 + 1) * 0.5;
                    this.basePattern[idx] = Math.max(0, Math.min(1, base * 0.42));
                }
            }
        }

        handlePointerEnter(e) {
            this.pointer.active = true;
            this.updatePointer(e);
        }

        handlePointerMove(e) {
            this.pointer.active = true;
            this.updatePointer(e);

            const now = performance.now();
            this.trail.push({ x: this.pointer.x, y: this.pointer.y, time: now });
            if (this.trail.length > 14) this.trail.shift();
        }

        handlePointerLeave() {
            this.pointer.active = false;
        }

        updatePointer(e) {
            const rect = this.root.getBoundingClientRect();
            this.pointer.x = e.clientX - rect.left;
            this.pointer.y = e.clientY - rect.top;
        }

        handleVisibilityChange() {
            this.isDocumentVisible = !document.hidden;
            this.updateAnimationState();
        }

        updateAnimationState() {
            const shouldAnimate = this.isVisible && this.isDocumentVisible && !this.reduceMotion.matches;
            if (shouldAnimate && !this.frameId) {
                this.frameId = requestAnimationFrame(this.loop);
            } else if (!shouldAnimate && this.frameId) {
                cancelAnimationFrame(this.frameId);
                this.frameId = 0;
            }
        }

        loop(timestamp) {
            this.render(timestamp);
            if (this.isVisible && this.isDocumentVisible && !this.reduceMotion.matches) {
                this.frameId = requestAnimationFrame(this.loop);
            } else {
                this.frameId = 0;
            }
        }

        render(timestamp) {
            if (!this.ctx) return;
            this.time = timestamp * 0.001;

            const ctx = this.ctx;
            const cols = this.columns;
            const rows = this.rows;
            const cw = this.cellWidth;
            const ch = this.cellHeight;
            const gap = this.gap;
            const drawW = Math.max(1, cw - gap);
            const drawH = Math.max(1, ch - gap);

            // Deep background
            ctx.fillStyle = '#06020a';
            ctx.fillRect(0, 0, this.width, this.height);

            const now = performance.now();
            const pointer = this.pointer;

            // Decay old trail points
            for (let t = this.trail.length - 1; t >= 0; t--) {
                if (now - this.trail[t].time > 650) {
                    this.trail.splice(t, 1);
                }
            }

            // Render Pixel Grid
            for (let r = 0; r < rows; r++) {
                const y = r * ch;
                const cellCenterY = y + ch * 0.5;

                for (let c = 0; c < cols; c++) {
                    const idx = r * cols + c;
                    const x = c * cw;
                    const cellCenterX = x + cw * 0.5;

                    // 1. Base procedural wave harmonics
                    const waveX = Math.sin(c * 0.18 + this.time * 1.6);
                    const waveY = Math.cos(r * 0.14 - this.time * 1.2);
                    const sweep = Math.sin((c + r) * 0.12 - this.time * 2.4);

                    let lum = this.basePattern[idx] * 0.55 + (waveX * waveY * 0.15 + sweep * 0.12);

                    // 2. Direct Pointer Light Excitation
                    if (pointer.active && pointer.x >= 0) {
                        const dist = Math.hypot(cellCenterX - pointer.x, cellCenterY - pointer.y);
                        if (dist < 140) {
                            const factor = 1 - (dist / 140);
                            lum += factor * factor * 0.85;
                        }
                    }

                    // 3. Pointer Motion Trail Ripple
                    for (let t = 0; t < this.trail.length; t++) {
                        const tp = this.trail[t];
                        const age = (now - tp.time) / 650; // 0 to 1
                        const tDist = Math.hypot(cellCenterX - tp.x, cellCenterY - tp.y);
                        if (tDist < 100) {
                            const tFactor = (1 - (tDist / 100)) * (1 - age);
                            lum += tFactor * 0.45;
                        }
                    }

                    lum = Math.max(0.04, Math.min(1, lum));

                    // Precision Color Mapping
                    let fill = '';
                    if (lum < 0.14) {
                        // Deep muted violet background pixel
                        fill = `rgba(36, 17, 60, ${0.3 + lum * 2})`;
                    } else if (lum < 0.38) {
                        // Mid-tone purple
                        const alpha = 0.5 + lum * 0.5;
                        fill = `rgba(107, 33, 168, ${alpha})`;
                    } else if (lum < 0.68) {
                        // Radiant bright amethyst
                        fill = `rgba(168, 85, 247, ${0.75 + lum * 0.25})`;
                    } else if (lum < 0.88) {
                        // Luminous lavender highlight
                        fill = '#c084fc';
                    } else {
                        // Super-excited white-hot violet highlight
                        fill = '#f3e8ff';
                    }

                    ctx.fillStyle = fill;
                    ctx.fillRect(x + gap * 0.5, y + gap * 0.5, drawW, drawH);
                }
            }

            // Cinematic Vignette Overlay
            const vignette = ctx.createRadialGradient(
                this.width * 0.5, this.height * 0.5, Math.min(this.width, this.height) * 0.25,
                this.width * 0.5, this.height * 0.5, Math.max(this.width, this.height) * 0.72
            );
            vignette.addColorStop(0, 'rgba(6, 2, 10, 0)');
            vignette.addColorStop(0.65, 'rgba(6, 2, 10, 0.3)');
            vignette.addColorStop(1, 'rgba(6, 2, 10, 0.85)');
            ctx.fillStyle = vignette;
            ctx.fillRect(0, 0, this.width, this.height);
        }

        startCaptionRotation() {
            if (!this.captionEl || this.captionTimer) return;
            this.captionTimer = setInterval(() => {
                if (!this.isVisible || !this.isDocumentVisible || !this.captionEl) return;

                let nextIdx = Math.floor(Math.random() * (this.captions.length - 1));
                if (nextIdx >= this.captionIndex) nextIdx++;
                this.captionIndex = nextIdx;

                this.captionEl.style.opacity = '0';
                this.captionEl.style.transform = 'translateY(4px)';

                setTimeout(() => {
                    if (!this.captionEl) return;
                    this.captionEl.innerHTML = this.captions[this.captionIndex];
                    this.captionEl.style.opacity = '1';
                    this.captionEl.style.transform = 'translateY(0)';
                }, 400);
            }, 6500);
        }

        stopCaptionRotation() {
            if (this.captionTimer) {
                clearInterval(this.captionTimer);
                this.captionTimer = null;
            }
        }

        destroy() {
            if (this.frameId) cancelAnimationFrame(this.frameId);
            this.stopCaptionRotation();
            this.resizeObserver?.disconnect();
            this.intersectionObserver?.disconnect();
            this.root.removeEventListener('pointermove', this.onPointerMove);
            this.root.removeEventListener('pointerenter', this.onPointerEnter);
            this.root.removeEventListener('pointerleave', this.onPointerLeave);
            document.removeEventListener('visibilitychange', this.onVisibilityChange);
        }
    }

    // Public API
    window.gramLoginArtwork = {
        init: function (rootId) {
            const root = typeof rootId === 'string' ? document.getElementById(rootId) : rootId;
            if (!root) return;
            if (instances.has(root)) {
                instances.get(root).destroy();
            }
            const instance = new GramPixelMatrix(root);
            instances.set(root, instance);
        },

        dispose: function (rootId) {
            const root = typeof rootId === 'string' ? document.getElementById(rootId) : rootId;
            if (root && instances.has(root)) {
                instances.get(root).destroy();
                instances.delete(root);
            }
        }
    };
})();
