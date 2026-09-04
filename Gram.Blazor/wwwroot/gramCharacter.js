/**
 * Gram Interactive Expressive Mascot Character ("Hex")
 * High-performance 2.5D parallax physics, organic micro-expressions, 
 * autonomous behaviors, and reactive companion controller.
 */
(function () {
    const instances = new Set();
    let globalComposerBound = false;
    let audioCtx = null;

    function playSoftChime() {
        try {
            if (!audioCtx) {
                const AudioContext = window.AudioContext || window.webkitAudioContext;
                if (AudioContext) audioCtx = new AudioContext();
            }
            if (!audioCtx) return;
            if (audioCtx.state === 'suspended') {
                audioCtx.resume();
            }

            const now = audioCtx.currentTime;
            const osc1 = audioCtx.createOscillator();
            const osc2 = audioCtx.createOscillator();
            const gain = audioCtx.createGain();

            osc1.type = 'sine';
            osc1.frequency.setValueAtTime(587.33, now); // D5
            osc1.frequency.exponentialRampToValueAtTime(880.00, now + 0.15); // A5

            osc2.type = 'triangle';
            osc2.frequency.setValueAtTime(880.00, now);
            osc2.frequency.exponentialRampToValueAtTime(1174.66, now + 0.15); // D6

            gain.gain.setValueAtTime(0.04, now);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.35);

            osc1.connect(gain);
            osc2.connect(gain);
            gain.connect(audioCtx.destination);

            osc1.start(now);
            osc2.start(now);
            osc1.stop(now + 0.35);
            osc2.stop(now + 0.35);
        } catch (_) {
            // Audio policy / unsupported fallback
        }
    }

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

            // Interpolation coordinates for fluid 60fps eye tracking & 2.5D parallax head tilt
            this.current = { eyeX: 0, eyeY: 0, tiltX: 0, tiltY: 0, headRoll: 0 };
            this.target = { eyeX: 0, eyeY: 0, tiltX: 0, tiltY: 0, headRoll: 0 };
            this.pointerActive = false;
            this.frameId = 0;

            // Apply initial state
            this.applyState(this.state);

            // Bindings
            this.handleMouseMove = this.onMouseMove.bind(this);
            this.handleMouseLeave = this.onMouseLeave.bind(this);
            this.handleClick = this.onClick.bind(this);
            this.loop = this.renderLoop.bind(this);

            if (this.options.interactive) {
                window.addEventListener('mousemove', this.handleMouseMove, { passive: true });
                window.addEventListener('mouseleave', this.handleMouseLeave, { passive: true });
                this.root.addEventListener('click', this.handleClick);
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
                'state-researching', 'state-designing', 'state-reviewing', 'state-complete',
                'state-landing', 'is-jumping',
                'state-typing-name', 'is-recognizing',
                'state-typing-password', 'is-covering-eyes',
                'state-voice-listening', 'is-voice-listening',
                'state-voice-thinking', 'is-voice-thinking',
                'state-voice-speaking', 'is-voice-speaking',
                'state-voice-interrupted', 'is-voice-interrupted'
            );

            if (stateName === 'voice_listening' || stateName === 'listening') {
                this.root.classList.add('state-voice-listening', 'is-voice-listening');
            } else if (stateName === 'voice_thinking') {
                this.root.classList.add('state-voice-thinking', 'is-voice-thinking');
            } else if (stateName === 'voice_speaking' || stateName === 'speaking') {
                this.root.classList.add('state-voice-speaking', 'is-voice-speaking');
            } else if (stateName === 'voice_interrupted' || stateName === 'interrupted') {
                this.root.classList.add('state-voice-interrupted', 'is-voice-interrupted');
            } else if (stateName === 'landing' || stateName === 'excited' || stateName === 'jump') {
                this.root.classList.add('state-landing', 'is-jumping');
            } else if (stateName === 'typing_name' || stateName === 'name' || stateName === 'recognize') {
                this.root.classList.add('state-typing-name', 'is-recognizing');
            } else if (stateName === 'typing_password' || stateName === 'password' || stateName === 'peek_cover' || stateName === 'shy') {
                this.root.classList.add('state-typing-password', 'is-covering-eyes');
            } else if (stateName === 'wave') {
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
            const delay = initialDelay || (2400 + Math.random() * 2600);

            this.idleAnimationTimer = setTimeout(() => {
                this.idleAnimationTimer = null;
                if (this.state !== 'idle' || this.pointerActive) {
                    this.scheduleIdleAnimation();
                    return;
                }

                // Weighted selection prioritizing lively, delightful behaviors
                const roll = Math.random();
                let animationClass = 'idle-body-spin';
                let duration = 2400;

                if (roll < 0.22) {
                    // 1. Body Spin (eyes lock forward, hexagonal shell spins 360)
                    animationClass = 'idle-body-spin';
                    duration = 2000;
                } else if (roll < 0.42) {
                    // 2. Tapping the screen glass at the user with ripple
                    animationClass = 'idle-tap-glass';
                    duration = 2400;
                } else if (roll < 0.60) {
                    // 3. Checking holographic watch for time
                    animationClass = 'idle-check-watch';
                    duration = 2600;
                } else if (roll < 0.76) {
                    // 4. Curious head tilt & raised brow
                    animationClass = 'idle-curious';
                    duration = 1900;
                } else if (roll < 0.88) {
                    // 5. Glance look left/right
                    animationClass = roll < 0.82 ? 'idle-glance-left' : 'idle-glance-right';
                    duration = 1800;
                } else {
                    // 6. Attentive double blink
                    animationClass = 'idle-double-blink';
                    duration = 750;
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

        onClick(e) {
            playSoftChime();

            // Spring squish & pop feedback
            this.root.classList.remove('gram-char-click-squish');
            void this.root.offsetWidth; // Force reflow
            this.root.classList.add('gram-char-click-squish');
            setTimeout(() => this.root.classList.remove('gram-char-click-squish'), 450);

            const playfulActions = ['wave', 'body_spin', 'tap_glass', 'check_watch', 'curious', 'landing'];
            const chosen = playfulActions[Math.floor(Math.random() * playfulActions.length)];
            this.triggerAction(chosen, 2400);
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

            if (dist < 650) {
                this.pointerActive = true;
                const angle = Math.atan2(dy, dx);
                const mag = Math.min(dist / 240, 1.0);

                // Eye gaze target
                this.target.eyeX = Math.cos(angle) * (mag * 3.6);
                this.target.eyeY = Math.sin(angle) * (mag * 2.8);

                // 2.5D Parallax Head & Shell Tilt
                const normX = Math.max(-1, Math.min(1, dx / 350));
                const normY = Math.max(-1, Math.min(1, dy / 350));

                this.target.tiltX = -normY * 5.5; // Pitch up/down
                this.target.tiltY = normX * 6.5;  // Yaw left/right
                this.target.headRoll = normX * 3.0; // Subtle organic roll

                if (this.pointerInactivityTimer) clearTimeout(this.pointerInactivityTimer);
                this.pointerInactivityTimer = setTimeout(() => {
                    this.pointerActive = false;
                    this.target.eyeX = 0;
                    this.target.eyeY = 0;
                    this.target.tiltX = 0;
                    this.target.tiltY = 0;
                    this.target.headRoll = 0;
                    this.scheduleIdleAnimation(800);
                }, 1400);
            }
        }

        onMouseLeave() {
            this.pointerActive = false;
            this.target.eyeX = 0;
            this.target.eyeY = 0;
            this.target.tiltX = 0;
            this.target.tiltY = 0;
            this.target.headRoll = 0;
            this.scheduleIdleAnimation(600);
        }

        renderLoop() {
            if (this.state === 'idle') {
                // Spring damping factor for ultra-smooth 60fps interpolation
                const lerpRate = 0.12;
                this.current.eyeX += (this.target.eyeX - this.current.eyeX) * lerpRate;
                this.current.eyeY += (this.target.eyeY - this.current.eyeY) * lerpRate;
                this.current.tiltX += (this.target.tiltX - this.current.tiltX) * lerpRate;
                this.current.tiltY += (this.target.tiltY - this.current.tiltY) * lerpRate;
                this.current.headRoll += (this.target.headRoll - this.current.headRoll) * lerpRate;

                this.root.style.setProperty('--eye-x', `${this.current.eyeX.toFixed(2)}px`);
                this.root.style.setProperty('--eye-y', `${this.current.eyeY.toFixed(2)}px`);
                this.root.style.setProperty('--tilt-x', `${this.current.tiltX.toFixed(2)}deg`);
                this.root.style.setProperty('--tilt-y', `${this.current.tiltY.toFixed(2)}deg`);
                this.root.style.setProperty('--head-roll', `${this.current.headRoll.toFixed(2)}deg`);
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
            if (this.root) this.root.removeEventListener('click', this.handleClick);
            instances.delete(this);
        }
    }

    // Attach global listeners to the composer textarea
    function setupGlobalComposerListeners() {
        if (globalComposerBound) return;
        globalComposerBound = true;

        const isComposer = (el) => el && (el.id === 'composerTextarea' || el.classList?.contains('composer-textarea') || el.classList?.contains('prompt-textarea'));

        document.addEventListener('focusin', (e) => {
            if (isComposer(e.target)) {
                for (const char of instances) {
                    if (char.state !== 'thinking' && char.state !== 'voice_thinking') char.applyState('look_down');
                }
            }
        });

        document.addEventListener('focusout', (e) => {
            if (isComposer(e.target)) {
                for (const char of instances) {
                    if (char.state === 'look_down' || char.state === 'typing') char.applyState('idle');
                }
            }
        });

        document.addEventListener('input', (e) => {
            if (isComposer(e.target)) {
                for (const char of instances) {
                    if (char.state !== 'thinking' && char.state !== 'voice_thinking') char.applyState('typing');
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
