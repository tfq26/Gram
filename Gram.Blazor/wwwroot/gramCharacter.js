/**
 * Gram Interactive Expressive Mascot Character ("Hex")
 * High-performance state & interaction controller with rich autonomous idle behaviors
 */
(function () {
    const instances = new Set();
    let globalComposerBound = false;

    class GramCharacterInstance {
        constructor(root, options = {}) {
            this.root = typeof root === 'string' ? document.getElementById(root) : root;
            if (!this.root) return;

            this.options = Object.assign({
                state: 'idle',
                interactive: true,
                autoWaveOnLoad: true
            }, options);

            this.state = this.options.state || 'idle';
            this.actionTimer = null;
            this.idleAnimationTimer = null;
            this.idleAnimationEndTimer = null;
            this.pointerInactivityTimer = null;

            this.idleAnimationClasses = [
                'idle-blink',
                'idle-double-blink',
                'idle-glance-left',
                'idle-glance-right',
                'idle-tilt',
                'idle-curious',
                'idle-nod',
                'idle-body-spin',
                'idle-check-watch',
                'idle-tap-glass'
            ];

            // Interpolation coordinates for fluid 60fps eye tracking
            this.current = { eyeX: 0, eyeY: 0 };
            this.target = { eyeX: 0, eyeY: 0 };
            this.pointerActive = false;
            this.frameId = 0;

            // Apply initial state
            this.applyState(this.state);

            // Bindings
            this.handleMouseMove = this.onMouseMove.bind(this);
            this.handleMouseLeave = this.onMouseLeave.bind(this);
            this.loop = this.renderLoop.bind(this);

            if (this.options.interactive) {
                window.addEventListener('mousemove', this.handleMouseMove, { passive: true });
                window.addEventListener('mouseleave', this.handleMouseLeave, { passive: true });
            }

            // Start smooth update loop
            this.frameId = requestAnimationFrame(this.loop);

            // Start autonomous idle animation loop
            this.scheduleIdleAnimation(1400);

            // Play celebratory welcome wave on load
            if (this.options.autoWaveOnLoad) {
                setTimeout(() => this.triggerAction('wave', 2800), 300);
            }

            instances.add(this);
        }

        applyState(stateName) {
            this.clearIdleAnimation();
            this.state = stateName;
            this.root.classList.remove(
                'state-wave', 'is-waving',
                'state-bodyspin', 'is-spinning',
                'state-checkwatch', 'is-checking-watch',
                'state-tapglass', 'is-tapping-glass',
                'state-sidebar-open', 'is-sidebar-open',
                'state-sidebar-close', 'is-sidebar-close',
                'state-sidebar-react', 'idle-sidebar-react',
                'state-lookdown', 'is-typing',
                'state-thinking', 'is-thinking',
                'state-researching', 'state-designing', 'state-reviewing', 'state-complete'
            );

            if (stateName === 'wave') {
                this.root.classList.add('state-wave', 'is-waving');
            } else if (stateName === 'sidebar_open' || stateName === 'sidebar_react' || stateName === 'sidebar') {
                this.root.classList.add('state-sidebar-open', 'state-sidebar-react');
            } else if (stateName === 'sidebar_close') {
                this.root.classList.add('state-sidebar-close');
            } else if (stateName === 'glance_left' || stateName === 'glanceleft') {
                this.root.classList.add('idle-glance-left');
            } else if (stateName === 'curious') {
                this.root.classList.add('idle-curious');
            } else if (stateName === 'nod') {
                this.root.classList.add('idle-nod');
            } else if (stateName === 'tilt') {
                this.root.classList.add('idle-tilt');
            } else if (stateName === 'body_spin' || stateName === 'bodyspin') {
                this.root.classList.add('state-bodyspin', 'is-spinning', 'idle-body-spin');
            } else if (stateName === 'check_watch' || stateName === 'checkwatch') {
                this.root.classList.add('state-checkwatch', 'is-checking-watch', 'idle-check-watch');
            } else if (stateName === 'tap_glass' || stateName === 'tapglass') {
                this.root.classList.add('state-tapglass', 'is-tapping-glass', 'idle-tap-glass');
            } else if (stateName === 'look_down' || stateName === 'typing') {
                this.root.classList.add('state-lookdown', 'is-typing');
            } else if (stateName === 'thinking') {
                this.root.classList.add('state-thinking', 'is-thinking');
            } else if (stateName === 'researching' || stateName === 'planning') {
                this.root.classList.add('state-researching');
            } else if (stateName === 'designing' || stateName === 'assembling') {
                this.root.classList.add('state-designing');
            } else if (stateName === 'reviewing') {
                this.root.classList.add('state-reviewing');
            } else if (stateName === 'complete') {
                this.root.classList.add('state-complete');
            }

            if (stateName === 'idle') {
                this.scheduleIdleAnimation(2200);
            }
        }

        scheduleIdleAnimation(initialDelay = 0) {
            if (this.state !== 'idle' || this.idleAnimationTimer) return;
            const delay = initialDelay || (2200 + Math.random() * 2800);

            this.idleAnimationTimer = setTimeout(() => {
                this.idleAnimationTimer = null;
                if (this.state !== 'idle' || this.pointerActive) {
                    this.scheduleIdleAnimation();
                    return;
                }

                // Weighted selection prioritizing the rich noticeable animations
                const roll = Math.random();
                let animationClass = 'idle-body-spin';
                let duration = 2400;

                if (roll < 0.22) {
                    // 1. Noticeable: Body Spin (eyes still, body 360 spin)
                    animationClass = 'idle-body-spin';
                    duration = 2000;
                } else if (roll < 0.44) {
                    // 2. Noticeable: Tapping the screen glass at the user
                    animationClass = 'idle-tap-glass';
                    duration = 2400;
                } else if (roll < 0.64) {
                    // 3. Noticeable: Checking holographic watch for time
                    animationClass = 'idle-check-watch';
                    duration = 2600;
                } else if (roll < 0.78) {
                    // Curious head tilt & eyebrow raise
                    animationClass = 'idle-curious';
                    duration = 1900;
                } else if (roll < 0.90) {
                    // Glance look left/right
                    animationClass = roll < 0.84 ? 'idle-glance-left' : 'idle-glance-right';
                    duration = 1800;
                } else {
                    // Attentive double blink
                    animationClass = 'idle-double-blink';
                    duration = 700;
                }

                this.root.classList.add(animationClass);
                this.idleAnimationEndTimer = setTimeout(() => {
                    this.root.classList.remove(animationClass);
                    this.idleAnimationEndTimer = null;
                    this.scheduleIdleAnimation();
                }, duration);
            }, delay);
        }

        clearIdleAnimation() {
            if (this.idleAnimationTimer) clearTimeout(this.idleAnimationTimer);
            if (this.idleAnimationEndTimer) clearTimeout(this.idleAnimationEndTimer);
            this.idleAnimationTimer = null;
            this.idleAnimationEndTimer = null;
            for (const className of this.idleAnimationClasses) {
                this.root.classList.remove(className);
            }
        }

        triggerAction(actionName, durationMs = 2800) {
            if (this.actionTimer) clearTimeout(this.actionTimer);
            this.applyState(actionName);
            this.actionTimer = setTimeout(() => {
                this.applyState('idle');
                this.actionTimer = null;
            }, durationMs);
        }

        onMouseMove(e) {
            if (!this.root || this.state !== 'idle') return;
            const rect = this.root.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) return;

            const charCenterX = rect.left + rect.width / 2;
            const charCenterY = rect.top + rect.height * 0.45;

            const dx = e.clientX - charCenterX;
            const dy = e.clientY - charCenterY;
            const dist = Math.hypot(dx, dy);

            if (dist < 550) {
                this.pointerActive = true;
                const angle = Math.atan2(dy, dx);
                const mag = Math.min(dist / 220, 1.0);
                this.target.eyeX = Math.cos(angle) * (mag * 3.4);
                this.target.eyeY = Math.sin(angle) * (mag * 2.6);

                if (this.pointerInactivityTimer) clearTimeout(this.pointerInactivityTimer);
                this.pointerInactivityTimer = setTimeout(() => {
                    this.pointerActive = false;
                    this.target.eyeX = 0;
                    this.target.eyeY = 0;
                    this.scheduleIdleAnimation(800);
                }, 1400);
            }
        }

        onMouseLeave() {
            this.pointerActive = false;
            this.target.eyeX = 0;
            this.target.eyeY = 0;
            this.scheduleIdleAnimation(600);
        }

        renderLoop() {
            if (this.state === 'idle') {
                const lerpRate = 0.16;
                this.current.eyeX += (this.target.eyeX - this.current.eyeX) * lerpRate;
                this.current.eyeY += (this.target.eyeY - this.current.eyeY) * lerpRate;

                this.root.style.setProperty('--eye-x', `${this.current.eyeX.toFixed(2)}px`);
                this.root.style.setProperty('--eye-y', `${this.current.eyeY.toFixed(2)}px`);
            }

            this.frameId = requestAnimationFrame(this.loop);
        }

        destroy() {
            if (this.frameId) cancelAnimationFrame(this.frameId);
            if (this.actionTimer) clearTimeout(this.actionTimer);
            if (this.pointerInactivityTimer) clearTimeout(this.pointerInactivityTimer);
            this.clearIdleAnimation();
            window.removeEventListener('mousemove', this.handleMouseMove);
            window.removeEventListener('mouseleave', this.handleMouseLeave);
            instances.delete(this);
        }
    }

    // Attach global listeners to the composer textarea
    function setupGlobalComposerListeners() {
        if (globalComposerBound) return;
        globalComposerBound = true;

        document.addEventListener('focusin', (e) => {
            if (e.target && (e.target.id === 'composerTextarea' || e.target.classList.contains('composer-textarea'))) {
                for (const char of instances) {
                    if (char.state !== 'thinking') char.applyState('look_down');
                }
            }
        });

        document.addEventListener('focusout', (e) => {
            if (e.target && (e.target.id === 'composerTextarea' || e.target.classList.contains('composer-textarea'))) {
                for (const char of instances) {
                    if (char.state === 'look_down' || char.state === 'typing') char.applyState('idle');
                }
            }
        });

        document.addEventListener('input', (e) => {
            if (e.target && (e.target.id === 'composerTextarea' || e.target.classList.contains('composer-textarea'))) {
                for (const char of instances) {
                    if (char.state !== 'thinking') char.applyState('typing');
                }
            }
        });
    }

    // Public API
    window.gramCharacter = {
        init: function (elementOrId, options = {}) {
            const root = typeof elementOrId === 'string' ? document.getElementById(elementOrId) : elementOrId;
            if (!root) return null;

            const instance = new GramCharacterInstance(root, options);
            setupGlobalComposerListeners();
            return true;
        },

        setState: function (elementOrId, stateName, durationMs = 0) {
            const root = typeof elementOrId === 'string' ? document.getElementById(elementOrId) : elementOrId;
            for (const inst of instances) {
                if (!root || inst.root === root) {
                    if (durationMs > 0 || stateName === 'wave' || stateName === 'body_spin' || stateName === 'check_watch' || stateName === 'tap_glass') {
                        inst.triggerAction(stateName, durationMs || 2800);
                    } else {
                        inst.applyState(stateName);
                    }
                }
            }
        },

        destroy: function (elementOrId) {
            const root = typeof elementOrId === 'string' ? document.getElementById(elementOrId) : elementOrId;
            for (const inst of instances) {
                if (!root || inst.root === root) {
                    inst.destroy();
                }
            }
        }
    };
})();
