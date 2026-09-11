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
                'idle-tap-glass',
                'idle-lens-buff',
                'idle-antenna-twitch',
                'idle-shoulder-shimmy',
                'idle-float-dip',
                'idle-eye-roll',
                'idle-sigh-deflate',
                'idle-side-to-side-sway',
                'idle-yoyo',
                'idle-rubiks-cube',
                'idle-bubblegum',
                'idle-paper-airplane',
                'idle-gameboy',
                'idle-laser-pointer',
                'idle-juggling-bits',
                'idle-look-at-cursor',
                'idle-fly-swat',
                'idle-screen-smudge-wipe',
                'idle-binocular-gaze',
                'idle-mirror-check',
                'idle-foot-tap',
                'idle-twiddle-thumbs',
                'idle-drowsy-head-drop',
                'idle-deep-sleep',
                'idle-hiccups',
                'idle-magnet-clasp',
                'idle-thruster-sputter',
                'idle-stretchy-limbs'
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

            // Remove previous dynamic state classes
            const classesToRemove = Array.from(this.root.classList).filter(c => c.startsWith('state-') || c.startsWith('is-'));
            for (const c of classesToRemove) {
                this.root.classList.remove(c);
            }

            const normalized = (stateName || 'idle').toLowerCase().replace(/_/g, '-');
            this.root.classList.add('state-' + normalized);

            // Contextual & backward compatibility aliases
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
            } else if (stateName === 'body_spin' || stateName === 'bodyspin') {
                this.root.classList.add('state-bodyspin', 'is-spinning');
            } else if (stateName === 'check_watch' || stateName === 'checkwatch') {
                this.root.classList.add('state-checkwatch', 'is-checking-watch');
            } else if (stateName === 'tap_glass' || stateName === 'tapglass') {
                this.root.classList.add('state-tapglass', 'is-tapping-glass');
            } else if (stateName === 'look_down' || stateName === 'typing') {
                this.root.classList.add('state-lookdown', 'is-typing');
            } else if (stateName === 'thinking') {
                this.root.classList.add('state-thinking', 'is-thinking');
            } else if (stateName === 'sidebar_open' || stateName === 'sidebar_react' || stateName === 'sidebar') {
                this.root.classList.add('state-sidebar-open', 'state-sidebar-react');
            }

            if (stateName === 'idle') {
                this.scheduleIdleAnimation(2000);
            } else if (window.gramDesktop?.notifyCharacterAnimation) {
                window.gramDesktop.notifyCharacterAnimation(stateName);
            }
        }

        scheduleIdleAnimation(initialDelay = 0) {
            if (this.state !== 'idle' || this.idleAnimationTimer) return;
            const delay = initialDelay || (2200 + Math.random() * 2400);

            this.idleAnimationTimer = setTimeout(() => {
                this.idleAnimationTimer = null;
                if (this.state !== 'idle' || this.pointerActive) {
                    this.scheduleIdleAnimation();
                    return;
                }

                const inactiveMs = Date.now() - (window._gramLastActivity || Date.now());
                let animationClass = 'idle-blink';
                let duration = 1800;

                if (inactiveMs > 150000) {
                    // Stage 4 (> 2.5 min inactive): Deep Sleep with floating Zzz
                    animationClass = 'idle-deep-sleep';
                    duration = 5500;
                } else if (inactiveMs > 75000) {
                    // Stage 3 (> 75s inactive): Drowsy / heavy fatigue
                    const drowsyOptions = ['idle-drowsy-head-drop', 'idle-sigh-deflate', 'idle-twiddle-thumbs', 'idle-foot-tap'];
                    animationClass = drowsyOptions[Math.floor(Math.random() * drowsyOptions.length)];
                    duration = 3200;
                } else if (inactiveMs > 30000) {
                    // Stage 2 (> 30s inactive): Boredom & gadget play
                    const boredOptions = [
                        'idle-yoyo', 'idle-bubblegum', 'idle-foot-tap', 'idle-eye-roll',
                        'idle-sigh-deflate', 'idle-antenna-twitch', 'idle-lens-buff', 'idle-hiccups'
                    ];
                    animationClass = boredOptions[Math.floor(Math.random() * boredOptions.length)];
                    duration = 2600;
                } else {
                    // Stage 1 (Standard lively idle): weighted variety across all behaviors
                    const livelyPool = [
                        { cls: 'idle-body-spin', dur: 2000 },
                        { cls: 'idle-tap-glass', dur: 2400 },
                        { cls: 'idle-check-watch', dur: 2600 },
                        { cls: 'idle-curious', dur: 2000 },
                        { cls: 'idle-glance-left', dur: 1900 },
                        { cls: 'idle-glance-right', dur: 1900 },
                        { cls: 'idle-tilt', dur: 1600 },
                        { cls: 'idle-nod', dur: 1500 },
                        { cls: 'idle-blink', dur: 600 },
                        { cls: 'idle-double-blink', dur: 750 },
                        { cls: 'idle-lens-buff', dur: 2200 },
                        { cls: 'idle-antenna-twitch', dur: 2400 },
                        { cls: 'idle-float-dip', dur: 2000 },
                        { cls: 'idle-eye-roll', dur: 1600 },
                        { cls: 'idle-side-to-side-sway', dur: 3000 },
                        { cls: 'idle-yoyo', dur: 2800 },
                        { cls: 'idle-bubblegum', dur: 2800 },
                        { cls: 'idle-hiccups', dur: 1600 },
                        { cls: 'idle-shoulder-shimmy', dur: 2000 }
                    ];
                    const pick = livelyPool[Math.floor(Math.random() * livelyPool.length)];
                    animationClass = pick.cls;
                    duration = pick.dur;
                }

                this.root.classList.add(animationClass);
                if (window.gramDesktop?.notifyCharacterAnimation) {
                    const talkativeIdles = [
                        'idle-deep-sleep', 'idle-drowsy-head-drop', 'idle-yoyo', 
                        'idle-bubblegum', 'idle-check-watch', 'idle-tap-glass', 
                        'idle-lens-buff', 'idle-antenna-twitch', 'idle-sigh-deflate',
                        'idle-body-spin', 'idle-curious'
                    ];
                    if (talkativeIdles.includes(animationClass)) {
                        window.gramDesktop.notifyCharacterAnimation(animationClass);
                    }
                }
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

            const playfulActions = [
                'wave', 'body_spin', 'tap_glass', 'check_watch', 'curious',
                'thumbs_up', 'peace_sign', 'salute', 'finger_guns', 'smug',
                'ecstatic', 'shrug', 'caffeine_rush', 'eureka', 'dizzy',
                'victory_dance', 'screen_high_five', 'love', 'mind_blown',
                'applause', 'secret_shh', 'matrix_zen', 'test_passed',
                'idle_yoyo', 'idle_bubblegum', 'theme_sunglasses'
            ];
            const chosen = playfulActions[Math.floor(Math.random() * playfulActions.length)];
            this.triggerAction(chosen, 2600);
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

    // Global User Inactivity Tracking & Sleep/Wake System
    window._gramLastActivity = Date.now();
    function onUserGlobalActivity() {
        const wasDeepSleep = (Date.now() - window._gramLastActivity) > 120000;
        window._gramLastActivity = Date.now();
        if (wasDeepSleep) {
            for (const inst of instances) {
                if (inst.state === 'idle' || inst.root.classList.contains('idle-deep-sleep')) {
                    inst.triggerAction('wake_startled', 1800);
                }
            }
        }
    }
    window.addEventListener('mousemove', onUserGlobalActivity, { passive: true });
    window.addEventListener('keydown', onUserGlobalActivity, { passive: true });
    window.addEventListener('pointerdown', onUserGlobalActivity, { passive: true });

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
            const continuousStates = new Set(['idle', 'thinking', 'look_down', 'typing', 'voice_listening', 'voice_thinking', 'voice_speaking', 'deep_sleep', 'matrix_zen', 'researching', 'designing', 'reviewing']);
            for (const inst of instances) {
                if (!root || inst.root === root) {
                    if (durationMs > 0 || (!continuousStates.has(stateName) && durationMs === 0)) {
                        inst.triggerAction(stateName, durationMs || 2600);
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
