// Gram Desktop Interactive Native PTY Bridge with dynamic theme synchronization
(function () {
    window.gramPty = {
        instances: {},

        getAppTheme: function () {
            const shell = document.querySelector('.app-shell') || document.documentElement;
            const cs = getComputedStyle(shell);
            const canvasBg = cs.getPropertyValue('--dt-bg-canvas').trim() || cs.getPropertyValue('--bg-body').trim() || '#050505';
            const textPrimary = cs.getPropertyValue('--dt-text-primary').trim() || cs.getPropertyValue('--text-primary').trim() || '#f4f4f5';
            const textSecondary = cs.getPropertyValue('--dt-text-secondary').trim() || cs.getPropertyValue('--text-secondary').trim() || '#8e8e93';
            const textMuted = cs.getPropertyValue('--dt-text-muted').trim() || cs.getPropertyValue('--text-muted').trim() || '#71717a';
            const accent = cs.getPropertyValue('--dt-accent').trim() || cs.getPropertyValue('--accent').trim() || '#38bdf8';
            const selectedBg = cs.getPropertyValue('--dt-bg-selected').trim() || 'rgba(255, 255, 255, 0.18)';
            const surfaceBg = cs.getPropertyValue('--dt-bg-surface').trim() || cs.getPropertyValue('--bg-panel-subtle').trim() || '#141414';

            return {
                background: canvasBg,
                foreground: textPrimary,
                cursor: accent,
                cursorAccent: canvasBg,
                selectionBackground: selectedBg,
                selectionInactiveBackground: 'rgba(255, 255, 255, 0.08)',
                black: surfaceBg,
                red: '#f87171',
                green: '#4ade80',
                yellow: '#facc15',
                blue: '#60a5fa',
                magenta: '#c084fc',
                cyan: accent,
                white: textPrimary,
                brightBlack: textMuted,
                brightRed: '#ef4444',
                brightGreen: '#22c55e',
                brightYellow: '#eab308',
                brightBlue: '#3b82f6',
                brightMagenta: '#a855f7',
                brightCyan: '#38bdf8',
                brightWhite: '#ffffff'
            };
        },

        init: function (elementId, tabId, dotNetRef, options, initialHistory) {
            if (window.gramPty.instances[tabId]) {
                window.gramPty.dispose(tabId);
            }

            const container = document.getElementById(elementId);
            if (!container) {
                console.warn('[gramPty] Container not found:', elementId);
                return false;
            }

            container.innerHTML = '';

            if (typeof Terminal === 'undefined') {
                console.error('[gramPty] xterm.js is not loaded.');
                return false;
            }

            options = options || {};
            const theme = window.gramPty.getAppTheme();
            const shell = document.querySelector('.app-shell') || document.documentElement;
            const cs = getComputedStyle(shell);
            const appMonoFont = cs.getPropertyValue('--dt-font-mono').trim() || '"Geist Mono", "JetBrains Mono", ui-monospace, Menlo, monospace';

            const term = new Terminal({
                cursorBlink: true,
                cursorStyle: 'bar',
                cursorWidth: 2,
                fontFamily: options.fontFamily || appMonoFont,
                fontSize: options.fontSize || 12.5,
                lineHeight: 1.35,
                letterSpacing: 0,
                theme: theme,
                convertEol: true,
                allowProposedApi: true,
                smoothScrollDuration: 100,
                scrollback: 5000
            });

            let fitAddon = null;
            if (typeof FitAddon !== 'undefined' && FitAddon.FitAddon) {
                fitAddon = new FitAddon.FitAddon();
                term.loadAddon(fitAddon);
            }

            term.open(container);

            let resizeObserver = null;
            if (window.ResizeObserver) {
                resizeObserver = new ResizeObserver(() => {
                    if (fitAddon && container.offsetWidth > 0 && container.offsetHeight > 0) {
                        try {
                            fitAddon.fit();
                            dotNetRef.invokeMethodAsync('OnPtyResize', term.cols, term.rows).catch(() => {});
                        } catch (e) { }
                    }
                });
                resizeObserver.observe(container);
            }

            // Immediately store instance object so event handlers can reference it reliably
            const inst = {
                term: term,
                fitAddon: fitAddon,
                resizeObserver: resizeObserver,
                dotNetRef: dotNetRef,
                currentLine: '',
                isSuggestionsOpen: false
            };
            window.gramPty.instances[tabId] = inst;

            if (fitAddon) {
                try { fitAddon.fit(); } catch (e) { }
            }

            if (initialHistory) {
                try {
                    term.write(initialHistory);
                } catch (e) { }
            }

            // WebKit capture listener: prevent Tab key from blurring or switching focus
            if (term.textarea) {
                term.textarea.addEventListener('keydown', function (e) {
                    if (e.key === 'Tab') {
                        e.preventDefault();
                        e.stopPropagation();
                    }
                }, { capture: true });
            }

            container.addEventListener('keydown', function (e) {
                if (e.key === 'Tab') {
                    e.preventDefault();
                    e.stopPropagation();
                }
            }, { capture: true });

            // Native macOS keyboard shortcuts & Autocomplete handling
            term.attachCustomKeyEventHandler(e => {
                const currentInst = window.gramPty.instances[tabId];
                if (e.type === 'keydown') {
                    // Cmd+C with active selection: copy to clipboard
                    if ((e.metaKey || (e.ctrlKey && e.shiftKey)) && e.key.toLowerCase() === 'c' && term.hasSelection()) {
                        navigator.clipboard.writeText(term.getSelection());
                        return false;
                    }
                    // Cmd+V: paste from clipboard
                    if (e.metaKey && e.key.toLowerCase() === 'v') {
                        navigator.clipboard.readText().then(text => {
                            if (text) {
                                if (currentInst) currentInst.currentLine = (currentInst.currentLine || '') + text;
                                dotNetRef.invokeMethodAsync('OnPtyInput', text).catch(() => {});
                            }
                        }).catch(() => {});
                        return false;
                    }
                    // Cmd+K: clear screen
                    if (e.metaKey && e.key.toLowerCase() === 'k') {
                        term.clear();
                        if (currentInst) {
                            currentInst.currentLine = '';
                            currentInst.isSuggestionsOpen = false;
                        }
                        try {
                            dotNetRef.invokeMethodAsync('OnPtyClear').catch(() => {});
                        } catch (err) { }
                        return false;
                    }

                    // TAB KEY: Open suggestions or accept active suggestion
                    if (e.key === 'Tab') {
                        e.preventDefault();
                        e.stopPropagation();
                        if (e.stopImmediatePropagation) e.stopImmediatePropagation();

                        if (currentInst && currentInst.isSuggestionsOpen) {
                            dotNetRef.invokeMethodAsync('OnPtySuggestionSelect').catch(err => {
                                console.error('[gramPty] OnPtySuggestionSelect failed:', err);
                            });
                        } else {
                            const line = (currentInst && currentInst.currentLine) ? currentInst.currentLine : '';
                            dotNetRef.invokeMethodAsync('OnPtyTabPressed', line).catch(err => {
                                console.error('[gramPty] OnPtyTabPressed failed:', err);
                            });
                        }
                        return false;
                    }

                    // When suggestions popup is open: intercept navigation, select, dismiss
                    if (currentInst && currentInst.isSuggestionsOpen) {
                        if (e.key === 'ArrowDown') {
                            e.preventDefault();
                            e.stopPropagation();
                            dotNetRef.invokeMethodAsync('OnPtySuggestionNavigate', 1).catch(() => {});
                            return false;
                        }
                        if (e.key === 'ArrowUp') {
                            e.preventDefault();
                            e.stopPropagation();
                            dotNetRef.invokeMethodAsync('OnPtySuggestionNavigate', -1).catch(() => {});
                            return false;
                        }
                        if (e.key === 'Enter') {
                            e.preventDefault();
                            e.stopPropagation();
                            dotNetRef.invokeMethodAsync('OnPtySuggestionSelect').catch(() => {});
                            return false;
                        }
                        if (e.key === 'Escape') {
                            e.preventDefault();
                            e.stopPropagation();
                            currentInst.isSuggestionsOpen = false;
                            dotNetRef.invokeMethodAsync('OnPtySuggestionClose').catch(() => {});
                            return false;
                        }
                    }

                    // Track typed line buffer for auto-completion & history
                    if (e.key === 'Enter') {
                        if (currentInst) {
                            const trimmed = (currentInst.currentLine || '').trim();
                            if (trimmed) {
                                dotNetRef.invokeMethodAsync('OnPtyCommandExecuted', trimmed).catch(() => {});
                            }
                            currentInst.currentLine = '';
                            currentInst.isSuggestionsOpen = false;
                        }
                    } else if (e.key === 'Backspace') {
                        if (currentInst && currentInst.currentLine && currentInst.currentLine.length > 0) {
                            currentInst.currentLine = currentInst.currentLine.slice(0, -1);
                            if (currentInst.isSuggestionsOpen) {
                                dotNetRef.invokeMethodAsync('OnPtyInputChanged', currentInst.currentLine).catch(() => {});
                            }
                        }
                    } else if (e.key.toLowerCase() === 'c' && e.ctrlKey) {
                        if (currentInst) {
                            currentInst.currentLine = '';
                            if (currentInst.isSuggestionsOpen) {
                                currentInst.isSuggestionsOpen = false;
                                dotNetRef.invokeMethodAsync('OnPtySuggestionClose').catch(() => {});
                            }
                        }
                    } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
                        if (currentInst) {
                            currentInst.currentLine = (currentInst.currentLine || '') + e.key;
                            if (currentInst.isSuggestionsOpen) {
                                dotNetRef.invokeMethodAsync('OnPtyInputChanged', currentInst.currentLine).catch(() => {});
                            }
                        }
                    }
                }
                return true;
            });

            term.onData(data => {
                try {
                    dotNetRef.invokeMethodAsync('OnPtyInput', data).catch(() => {});
                } catch (e) { }
            });

            term.onResize(size => {
                try {
                    dotNetRef.invokeMethodAsync('OnPtyResize', size.cols, size.rows).catch(() => {});
                } catch (e) { }
            });

            // Notify initial dimensions & auto-focus
            setTimeout(() => {
                if (fitAddon) {
                    try { fitAddon.fit(); } catch (e) { }
                }
                try {
                    dotNetRef.invokeMethodAsync('OnPtyReady', term.cols || 80, term.rows || 24).catch(() => {});
                } catch (e) { }
                try {
                    term.focus();
                } catch (e) { }
            }, 60);

            return true;
        },

        setSuggestionsOpen: function (tabId, isOpen) {
            const inst = window.gramPty.instances[tabId];
            if (inst) {
                inst.isSuggestionsOpen = !!isOpen;
            }
        },

        setCurrentLine: function (tabId, line) {
            const inst = window.gramPty.instances[tabId];
            if (inst) {
                inst.currentLine = line || '';
            }
        },

        write: function (tabId, text) {
            const inst = window.gramPty.instances[tabId];
            if (inst && inst.term) {
                inst.term.write(text);
            }
        },

        fit: function (tabId) {
            const inst = window.gramPty.instances[tabId];
            if (inst && inst.fitAddon) {
                try { inst.fitAddon.fit(); } catch (e) { }
            }
        },

        focus: function (tabId) {
            const inst = window.gramPty.instances[tabId];
            if (inst && inst.term) {
                inst.term.focus();
            }
        },

        clear: function (tabId) {
            const inst = window.gramPty.instances[tabId];
            if (inst && inst.term) {
                inst.term.clear();
            }
        },

        dispose: function (tabId) {
            const inst = window.gramPty.instances[tabId];
            if (inst) {
                if (inst.resizeObserver) {
                    try { inst.resizeObserver.disconnect(); } catch (e) { }
                }
                if (inst.term) {
                    try { inst.term.dispose(); } catch (e) { }
                }
                delete window.gramPty.instances[tabId];
            }
        }
    };

    // Live theme synchronization: observe style or theme attribute changes and sync all open terminals
    if (typeof MutationObserver !== 'undefined') {
        const themeObserver = new MutationObserver(() => {
            if (window.gramPty && window.gramPty.instances) {
                const updatedTheme = window.gramPty.getAppTheme();
                Object.values(window.gramPty.instances).forEach(inst => {
                    if (inst && inst.term) {
                        inst.term.options.theme = updatedTheme;
                    }
                });
            }
        });

        if (document.documentElement) {
            themeObserver.observe(document.documentElement, {
                attributes: true,
                attributeFilter: ['style', 'data-theme', 'class'],
                subtree: true
            });
        }
    }
})();
