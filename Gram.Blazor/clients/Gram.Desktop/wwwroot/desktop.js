/**
 * Gram Desktop Helper Scripts
 * Code block copying, syntax actions, and UI micro-interactions
 */
window.gramAudio = window.gramAudio || {};

// One-click code copying with visual feedback
window.gramAudio.copyCode = function (buttonElement) {
    if (!buttonElement) return;
    const wrapper = buttonElement.closest('.code-block-wrapper, .mermaid-wrapper');
    if (!wrapper) return;
    const codeEl = wrapper.querySelector('code') || wrapper.querySelector('.mermaid');
    if (!codeEl) return;
    const text = codeEl.innerText || codeEl.textContent;

    navigator.clipboard.writeText(text).then(() => {
        const span = buttonElement.querySelector('span');
        if (span) {
            const orig = span.innerText;
            span.innerText = 'Copied!';
            buttonElement.classList.add('copied');
            setTimeout(() => {
                span.innerText = orig;
                buttonElement.classList.remove('copied');
            }, 2000);
        }
    }).catch(err => {
        console.warn('Clipboard write failed:', err);
    });
};

// Math rendering placeholder for KaTeX & Prism syntax highlighting
window.gramAudio.renderMath = function (element) {
    if (!element) return;

    // 1. Math rendering with KaTeX
    if (window.renderMathInElement) {
        try {
            window.renderMathInElement(element, {
                delimiters: [
                    { left: '$$', right: '$$', display: true },
                    { left: '$', right: '$', display: false }
                ],
                throwOnError: false
            });
        } catch (_) {}
    }

    // 2. Syntax highlighting with Prism
    if (window.Prism && window.Prism.highlightAllUnder) {
        try {
            window.Prism.highlightAllUnder(element);
        } catch (e) {
            console.warn('[Prism] Highlight error:', e);
        }
    }
};

// One-click copy table as Markdown with visual feedback
window.gramAudio.copyTable = function (buttonElement) {
    if (!buttonElement) return;
    const wrapper = buttonElement.closest('.dt-table-wrapper');
    if (!wrapper) return;
    const table = wrapper.querySelector('table');
    if (!table) return;

    const rows = Array.from(table.querySelectorAll('tr'));
    if (!rows.length) return;

    const matrix = rows.map(row =>
        Array.from(row.querySelectorAll('th, td')).map(cell => {
            let text = (cell.innerText || cell.textContent || '').trim();
            return text.replace(/\|/g, '\\|').replace(/\n+/g, ' ');
        })
    );

    let md = '';
    if (matrix.length > 0) {
        md += '| ' + matrix[0].join(' | ') + ' |\n';
        md += '| ' + matrix[0].map(() => '---').join(' | ') + ' |\n';
        for (let i = 1; i < matrix.length; i++) {
            md += '| ' + matrix[i].join(' | ') + ' |\n';
        }
    }

    navigator.clipboard.writeText(md).then(() => {
        const span = buttonElement.querySelector('span');
        if (span) {
            const orig = span.innerText;
            span.innerText = 'Copied!';
            buttonElement.classList.add('copied');
            setTimeout(() => {
                span.innerText = orig;
                buttonElement.classList.remove('copied');
            }, 2000);
        }
    }).catch(err => {
        console.warn('Clipboard write failed:', err);
    });
};

window.gramDesktop = window.gramDesktop || {};

window.gramDesktop.copyText = function (text) {
    if (!navigator.clipboard) return Promise.resolve(false);
    return navigator.clipboard.writeText(text).then(() => true).catch(() => false);
};

window.gramDesktop.autoResizeTextarea = function (element, minHeight, maxHeight) {
    if (!element) return;
    minHeight = minHeight || 26;
    maxHeight = maxHeight || 160;
    element.style.height = 'auto';
    const newHeight = Math.min(Math.max(element.scrollHeight, minHeight), maxHeight);
    element.style.height = newHeight + 'px';
};

// Composer initialization, auto-expanding height, and Enter vs Shift+Enter handling
window.gramDesktop.initComposer = function (textarea, dotNetHelper) {
    if (!textarea || textarea._gramComposerInit) return;
    textarea._gramComposerInit = true;
    textarea._dotNetHelper = dotNetHelper;

    function updateDockHeight() {
        const dock = textarea.closest('.dt-composer-dock');
        if (dock && dock.offsetHeight > 0) {
            document.documentElement.style.setProperty('--dt-composer-height', dock.offsetHeight + 'px');
        }
    }

    function resize() {
        textarea.style.height = 'auto';
        const minHeight = 26;
        const maxHeight = 160;
        const newHeight = Math.min(Math.max(textarea.scrollHeight, minHeight), maxHeight);
        textarea.style.height = newHeight + 'px';
        updateDockHeight();
    }

    textarea._gramResize = resize;

    textarea.addEventListener('input', function () {
        resize();
    });

    textarea.addEventListener('keydown', function (e) {
        if (e.isComposing) return;

        const suggestionsOpen = textarea.getAttribute('data-suggestions-open') === 'true';

        if (suggestionsOpen) {
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (textarea._dotNetHelper) textarea._dotNetHelper.invokeMethodAsync('OnArrowKeyFromJs', 1);
                return;
            }
            if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (textarea._dotNetHelper) textarea._dotNetHelper.invokeMethodAsync('OnArrowKeyFromJs', -1);
                return;
            }
            if (e.key === 'Enter' || e.key === 'Tab') {
                e.preventDefault();
                if (textarea._dotNetHelper) textarea._dotNetHelper.invokeMethodAsync('OnSelectSuggestionFromJs');
                return;
            }
            if (e.key === 'Escape') {
                e.preventDefault();
                if (textarea._dotNetHelper) textarea._dotNetHelper.invokeMethodAsync('OnEscapeFromJs');
                return;
            }
        }

        if (e.key === 'Enter') {
            if (!e.shiftKey) {
                // Regular Enter -> Instant 0ms Submit message
                e.preventDefault();
                const text = (textarea.value || '').trim();
                if (!text) return;

                // 1. Instant 0ms synchronous clear in browser thread
                textarea.value = '';
                textarea.style.height = '26px';
                updateDockHeight();

                // 2. Micro-feedback visual pulse on composer box
                const composerBox = textarea.closest('.dt-composer-box');
                if (composerBox) {
                    composerBox.classList.add('dt-sent-pulse');
                    setTimeout(() => composerBox.classList.remove('dt-sent-pulse'), 250);
                }

                // 3. Dispatch to Blazor for optimistic message insertion and streaming
                if (textarea._dotNetHelper) {
                    textarea._dotNetHelper.invokeMethodAsync('OnEnterFromJs', text);
                }
            } else {
                // Shift + Enter -> Allow browser newline, then smoothly expand height
                setTimeout(resize, 0);
            }
        }
    });

    // Initial resize
    resize();
    setTimeout(updateDockHeight, 50);
};

window.gramDesktop.resetComposerHeight = function (textarea) {
    if (!textarea) return;
    textarea.value = '';
    textarea.style.height = '26px';
    const dock = textarea.closest('.dt-composer-dock');
    if (dock && dock.offsetHeight > 0) {
        document.documentElement.style.setProperty('--dt-composer-height', dock.offsetHeight + 'px');
    }
};

window.gramDesktop.resizeComposer = function (textarea) {
    if (!textarea) return;
    if (textarea._gramResize) {
        textarea._gramResize();
    } else {
        window.gramDesktop.autoResizeTextarea(textarea, 26, 160);
    }
};

window.gramDesktop.focusComposer = function (textarea) {
    if (!textarea) return;
    textarea.focus();
    if (typeof textarea.selectionStart === 'number') {
        textarea.selectionStart = textarea.selectionEnd = textarea.value.length;
    }
};

window.gramDesktop.disposeComposer = function (textarea) {
    if (!textarea) return;
    textarea._dotNetHelper = null;
    textarea._gramComposerInit = false;
};

// Message list auto-scroll pinning on load and streaming
window.gramDesktop.initMessagesScroll = function (element) {
    if (!element) return;
    if (element._gramScrollObserver) {
        element._gramScrollObserver.disconnect();
    }

    element._userScrolledUp = false;

    if (!element._hasScrollListener) {
        element._hasScrollListener = true;
        element.addEventListener('scroll', function () {
            const threshold = 120;
            const distanceToBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
            element._userScrolledUp = distanceToBottom > threshold;
        }, { passive: true });

        // Capture image load events inside message container
        element.addEventListener('load', function (e) {
            if (e.target && e.target.tagName === 'IMG') {
                if (!element._userScrolledUp) {
                    element.scrollTop = element.scrollHeight;
                }
            }
        }, true);
    }

    const observer = new MutationObserver(function () {
        if (!element._userScrolledUp) {
            element.scrollTop = element.scrollHeight;
        }
    });

    observer.observe(element, {
        childList: true,
        subtree: true,
        characterData: true
    });

    element._gramScrollObserver = observer;

    window.gramDesktop.scrollMessagesToBottom(element, true);
};

window.gramDesktop.scrollMessagesToBottom = function (element, force) {
    if (!element) return;
    if (force) {
        element._userScrolledUp = false;
    }

    const threshold = 120;
    const distanceToBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    if (force || distanceToBottom <= threshold) {
        element.scrollTop = element.scrollHeight;
        requestAnimationFrame(() => {
            element.scrollTop = element.scrollHeight;
            requestAnimationFrame(() => {
                element.scrollTop = element.scrollHeight;
            });
        });
    }
};

window.gramDesktop.disposeMessagesScroll = function (element) {
    if (!element) return;
    if (element._gramScrollObserver) {
        element._gramScrollObserver.disconnect();
        element._gramScrollObserver = null;
    }
    element._gramScrollInit = false;
};

window.gramDesktop.enableTabsWheel = function (element) {
    if (!element || element._hasTabsWheel) return;
    element._hasTabsWheel = true;
    element.addEventListener('wheel', function (e) {
        if (e.deltaY !== 0) {
            e.preventDefault();
            element.scrollLeft += e.deltaY;
        }
    }, { passive: false });
};

// Global native desktop keyboard shortcuts & App Bridge
window.gramDesktop.registerApp = function (dotNetHelper) {
    window.gramDesktop._appHelper = dotNetHelper;
};

window.gramDesktop.notifyCharacterAnimation = function (animName) {
    if (window.gramDesktop._appHelper) {
        window.gramDesktop._appHelper.invokeMethodAsync('OnCharacterAnimationTriggered', animName).catch(function () {});
    }
};

function invokeDesktopShortcut(action, fallbackSelector) {
    if (window.gramDesktop._appHelper) {
        window.gramDesktop._appHelper.invokeMethodAsync('HandleGlobalShortcut', action).catch(function () {});
        return;
    }
    if (fallbackSelector) {
        const el = document.querySelector(fallbackSelector);
        if (el) el.click();
    }
}

window.addEventListener('keydown', function (e) {
    const isCmdOrCtrl = e.metaKey || e.ctrlKey;

    if (isCmdOrCtrl && !e.shiftKey && !e.altKey && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault();
        invokeDesktopShortcut('command-palette', '[data-action="open-command-palette"]');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && (e.key === 'b' || e.key === 'B')) {
        e.preventDefault();
        invokeDesktopShortcut('toggle-sidebar', '[data-action="toggle-sidebar"]');
    } else if (isCmdOrCtrl && e.shiftKey && !e.altKey && (e.key === 'b' || e.key === 'B')) {
        e.preventDefault();
        invokeDesktopShortcut('build-solution');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && (e.key === 'i' || e.key === 'I')) {
        e.preventDefault();
        invokeDesktopShortcut('toggle-infosheet', '[data-action="toggle-infosheet"]');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && (e.key === 'e' || e.key === 'E')) {
        e.preventDefault();
        invokeDesktopShortcut('toggle-expanded-chat', '[data-action="toggle-expanded-chat"]');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && (e.key === 't' || e.key === 'T')) {
        e.preventDefault();
        invokeDesktopShortcut('new-chat', '[data-action="new-chat"]');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && (e.key === 'w' || e.key === 'W')) {
        e.preventDefault();
        invokeDesktopShortcut('close-tab');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && e.key === ',') {
        e.preventDefault();
        invokeDesktopShortcut('toggle-settings', '.dt-topbar-right .dt-btn-text');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && e.key === '\\') {
        e.preventDefault();
        invokeDesktopShortcut('toggle-split');
    } else if (e.ctrlKey && !e.metaKey && (e.key === '`' || e.key === '~')) {
        e.preventDefault();
        invokeDesktopShortcut('toggle-terminal');
    } else if (e.altKey && !e.metaKey && !e.ctrlKey && (e.code === 'KeyG' || e.key === 'g' || e.key === 'G')) {
        e.preventDefault();
        invokeDesktopShortcut('toggle-git');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && e.key === '[') {
        e.preventDefault();
        invokeDesktopShortcut('prev-tab');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && e.key === ']') {
        e.preventDefault();
        invokeDesktopShortcut('next-tab');
    } else if (isCmdOrCtrl && !e.shiftKey && !e.altKey && e.key >= '1' && e.key <= '9') {
        e.preventDefault();
        invokeDesktopShortcut('tab-' + e.key);
    } else if (e.key === 'Escape') {
        invokeDesktopShortcut('escape', '.btn-back-to-chat');
    }
});

window.addEventListener('resize', function () {
    const dock = document.querySelector('.dt-composer-dock');
    if (dock && dock.offsetHeight > 0) {
        document.documentElement.style.setProperty('--dt-composer-height', dock.offsetHeight + 'px');
    }
});

window.gramDesktop.attachTerminal = function (inputElement, dotNetHelper) {
    if (!inputElement || inputElement._gramAttached) return;
    inputElement._gramAttached = true;
    inputElement.addEventListener('keydown', function (e) {
        if (e.key === 'Tab') {
            e.preventDefault();
            if (dotNetHelper) {
                dotNetHelper.invokeMethodAsync('OnTabPressed');
            }
        }
    });
};

window.gramDesktop.initDragDrop = function (element, dotNetHelper) {
    if (!element || element._hasDragDrop) return;
    element._hasDragDrop = true;

    element.addEventListener('dragenter', function (e) {
        e.preventDefault();
        element.classList.add('is-dragover');
    });

    element.addEventListener('dragover', function (e) {
        e.preventDefault();
        element.classList.add('is-dragover');
    });

    element.addEventListener('dragleave', function (e) {
        e.preventDefault();
        if (!element.contains(e.relatedTarget)) {
            element.classList.remove('is-dragover');
        }
    });

    element.addEventListener('drop', async function (e) {
        e.preventDefault();
        element.classList.remove('is-dragover');

        if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            for (let i = 0; i < e.dataTransfer.files.length; i++) {
                const file = e.dataTransfer.files[i];
                let text = '';
                try {
                    if (file.size < 2 * 1024 * 1024) {
                        text = await file.text();
                    }
                } catch (_) {}
                const filePath = file.path || file.name;
                if (dotNetHelper) {
                    await dotNetHelper.invokeMethodAsync('OnNativeFileDropped', file.name, filePath, text, file.size);
                }
            }
        } else if (e.dataTransfer) {
            const text = e.dataTransfer.getData('text');
            if (text && dotNetHelper) {
                await dotNetHelper.invokeMethodAsync('OnTextDropped', text);
            }
        }
    });
};

// Open external web links in user's default browser instead of desktop webview window
window.gramDesktop.openExternal = function (url) {
    if (!url) return;
    if (url.startsWith('www.')) {
        url = 'https://' + url;
    }
    if (window.DotNet) {
        DotNet.invokeMethodAsync('Gram.Desktop', 'OpenExternalUrl', url).catch(err => {
            console.warn('[gramDesktop] Failed to open external URL:', err);
        });
    }
};

// Global link click handler: intercept all clicks on external links and file links in responses & markdown
function handleGlobalLinkClick(e) {
    if (e.button !== 0 && e.button !== 1) return;
    const anchor = e.target.closest('a');
    if (!anchor) return;

    const href = anchor.getAttribute('href');
    if (!href || href === '#' || href.startsWith('javascript:')) return;

    if (/^file:\/\//i.test(href)) {
        e.preventDefault();
        e.stopPropagation();

        // Extract clean local path without file:// and line hash
        let filePath = decodeURIComponent(href.replace(/^file:\/\//i, ''));
        const hashIdx = filePath.indexOf('#');
        if (hashIdx !== -1) {
            filePath = filePath.substring(0, hashIdx);
        }

        // Copy path to clipboard
        navigator.clipboard.writeText(filePath).then(() => {
            anchor.classList.add('dt-file-copied');
            const originalTitle = anchor.getAttribute('title') || '';
            anchor.setAttribute('title', 'Copied path to clipboard!');
            setTimeout(() => {
                anchor.classList.remove('dt-file-copied');
                if (originalTitle) anchor.setAttribute('title', originalTitle);
            }, 1800);
        });

        // Also open file via OS desktop handler
        window.gramDesktop.openExternal('file://' + filePath);
        return;
    }

    if (/^(https?|mailto):/i.test(href) || href.startsWith('www.')) {
        e.preventDefault();
        e.stopPropagation();
        window.gramDesktop.openExternal(href);
    }
}

document.addEventListener('click', handleGlobalLinkClick, true);
document.addEventListener('auxclick', handleGlobalLinkClick, true);

// Override window.open to ensure any scripts open in default browser
const originalWindowOpen = window.open;
window.open = function (url, target, features) {
    if (url && (/^(https?|mailto):/i.test(url) || url.startsWith('www.'))) {
        window.gramDesktop.openExternal(url);
        return null;
    }
    return originalWindowOpen ? originalWindowOpen.call(window, url, target, features) : null;
};

// Draggable & Resizable Companion Mascot Pod
window.gramDesktop.initDraggableMascot = function (wrapperEl) {
    if (!wrapperEl || wrapperEl._mascotInit) return;
    wrapperEl._mascotInit = true;

    const podEl = wrapperEl.querySelector('.dt-corner-mascot-pod');
    const resizeHandle = wrapperEl.querySelector('.dt-mascot-resize-handle');
    if (!podEl) return;

    const STORAGE_KEY = 'gram_mascot_layout_v2';

    // Restore saved layout if present
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw) {
            const layout = JSON.parse(raw);
            if (layout.size && typeof layout.size === 'number') {
                const clampedSize = Math.max(64, Math.min(260, layout.size));
                podEl.style.width = clampedSize + 'px';
                podEl.style.height = clampedSize + 'px';
                wrapperEl.style.width = clampedSize + 'px';
                wrapperEl.style.height = clampedSize + 'px';
            }
            if (layout.customPosition && typeof layout.left === 'number' && typeof layout.top === 'number') {
                const currentSize = podEl.offsetWidth || 140;
                const maxLeft = Math.max(0, window.innerWidth - currentSize - 10);
                const maxTop = Math.max(0, window.innerHeight - currentSize - 10);
                const clampedLeft = Math.min(Math.max(10, layout.left), maxLeft);
                const clampedTop = Math.min(Math.max(40, layout.top), maxTop);
                wrapperEl.style.left = clampedLeft + 'px';
                wrapperEl.style.top = clampedTop + 'px';
                wrapperEl.style.right = 'auto';
                wrapperEl.style.bottom = 'auto';
                wrapperEl.classList.add('is-custom-positioned');
            }
        }
    } catch (_) {}

    function saveLayout() {
        try {
            const isCustom = wrapperEl.classList.contains('is-custom-positioned');
            const rect = podEl.getBoundingClientRect();
            const currentSize = podEl.offsetWidth || 140;
            const layoutData = {
                size: currentSize,
                customPosition: isCustom,
                left: isCustom ? Math.round(rect.left) : undefined,
                top: isCustom ? Math.round(rect.top) : undefined
            };
            localStorage.setItem(STORAGE_KEY, JSON.stringify(layoutData));
        } catch (_) {}
    }

    // Keep mascot within screen bounds on window resize
    window.addEventListener('resize', function () {
        if (!wrapperEl.classList.contains('is-custom-positioned')) return;
        const currentSize = podEl.offsetWidth || 140;
        const rect = wrapperEl.getBoundingClientRect();
        const maxLeft = Math.max(0, window.innerWidth - currentSize - 10);
        const maxTop = Math.max(0, window.innerHeight - currentSize - 10);
        const clampedLeft = Math.min(Math.max(10, rect.left), maxLeft);
        const clampedTop = Math.min(Math.max(40, rect.top), maxTop);
        wrapperEl.style.left = clampedLeft + 'px';
        wrapperEl.style.top = clampedTop + 'px';
    });

    // 1. Resizing via handle (top-left grip)
    if (resizeHandle) {
        resizeHandle.addEventListener('pointerdown', function (e) {
            if (e.button !== 0) return;
            e.stopPropagation();
            e.preventDefault();

            const startX = e.clientX;
            const startY = e.clientY;
            const initialRect = wrapperEl.getBoundingClientRect();
            const startSize = podEl.offsetWidth || 140;
            const isCustom = wrapperEl.classList.contains('is-custom-positioned');

            wrapperEl.classList.add('is-resizing');

            function onPointerMove(ev) {
                // Dragging up and left expands (+), dragging down and right contracts (-)
                const deltaX = startX - ev.clientX;
                const deltaY = startY - ev.clientY;
                const delta = (deltaX + deltaY) / 2;
                let newSize = Math.round(startSize + delta);
                newSize = Math.max(64, Math.min(260, newSize));

                podEl.style.width = newSize + 'px';
                podEl.style.height = newSize + 'px';
                wrapperEl.style.width = newSize + 'px';
                wrapperEl.style.height = newSize + 'px';

                if (isCustom) {
                    // Anchor bottom-right corner during resize
                    const rightAnchor = initialRect.right;
                    const bottomAnchor = initialRect.bottom;
                    const newLeft = Math.max(10, rightAnchor - newSize);
                    const newTop = Math.max(40, bottomAnchor - newSize);
                    wrapperEl.style.left = newLeft + 'px';
                    wrapperEl.style.top = newTop + 'px';
                }
            }

            function onPointerUp() {
                wrapperEl.classList.remove('is-resizing');
                window.removeEventListener('pointermove', onPointerMove);
                window.removeEventListener('pointerup', onPointerUp);
                saveLayout();
            }

            window.addEventListener('pointermove', onPointerMove);
            window.addEventListener('pointerup', onPointerUp);
        });

        // Double click on resize handle resets mascot to default position and size
        resizeHandle.addEventListener('dblclick', function (e) {
            e.stopPropagation();
            e.preventDefault();
            try {
                localStorage.removeItem(STORAGE_KEY);
            } catch (_) {}
            wrapperEl.style.left = '';
            wrapperEl.style.top = '';
            wrapperEl.style.right = '';
            wrapperEl.style.bottom = '';
            wrapperEl.classList.remove('is-custom-positioned');
            podEl.style.width = '140px';
            podEl.style.height = '140px';
            wrapperEl.style.width = '140px';
            wrapperEl.style.height = '140px';
        });
    }

    // 2. Dragging via Pod / Mascot Body
    podEl.addEventListener('pointerdown', function (e) {
        if (e.button !== 0) return;
        if (e.target.closest('.dt-mascot-resize-handle')) return;

        const startX = e.clientX;
        const startY = e.clientY;
        const initialRect = podEl.getBoundingClientRect();
        let hasDragged = false;

        function onPointerMove(ev) {
            const dx = ev.clientX - startX;
            const dy = ev.clientY - startY;

            if (!hasDragged) {
                if (Math.hypot(dx, dy) < 5) return;
                hasDragged = true;
                wrapperEl.classList.add('is-dragging');
                wrapperEl.classList.add('is-custom-positioned');
            }

            const currentSize = podEl.offsetWidth || 140;
            const maxLeft = Math.max(0, window.innerWidth - currentSize - 10);
            const maxTop = Math.max(0, window.innerHeight - currentSize - 10);
            const newLeft = Math.min(Math.max(10, initialRect.left + dx), maxLeft);
            const newTop = Math.min(Math.max(40, initialRect.top + dy), maxTop);

            wrapperEl.style.left = newLeft + 'px';
            wrapperEl.style.top = newTop + 'px';
            wrapperEl.style.right = 'auto';
            wrapperEl.style.bottom = 'auto';
        }

        function onPointerUp() {
            window.removeEventListener('pointermove', onPointerMove);
            window.removeEventListener('pointerup', onPointerUp);
            wrapperEl.classList.remove('is-dragging');

            if (hasDragged) {
                // Suppress click so character animations don't trigger after drag
                const stopClick = function (ev) {
                    ev.stopPropagation();
                    ev.preventDefault();
                };
                window.addEventListener('click', stopClick, { capture: true, once: true });
                setTimeout(function () {
                    window.removeEventListener('click', stopClick, { capture: true });
                }, 100);

                saveLayout();
            }
        }

        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);
    });
};


