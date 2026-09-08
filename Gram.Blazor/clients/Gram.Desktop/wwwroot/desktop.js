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

// Math rendering placeholder for KaTeX / LaTeX if present
window.gramAudio.renderMath = function (element) {
    if (!element || !window.renderMathInElement) return;
    try {
        window.renderMathInElement(element, {
            delimiters: [
                { left: '$$', right: '$$', display: true },
                { left: '$', right: '$', display: false }
            ],
            throwOnError: false
        });
    } catch (_) {}
};

window.gramDesktop = window.gramDesktop || {};

window.gramDesktop.copyText = function (text) {
    if (!navigator.clipboard) return Promise.resolve(false);
    return navigator.clipboard.writeText(text).then(() => true).catch(() => false);
};

window.gramDesktop.autoResizeTextarea = function (element, minHeight, maxHeight) {
    if (!element) return;
    minHeight = minHeight || 28;
    maxHeight = maxHeight || 120;
    element.style.height = 'auto';
    const newHeight = Math.min(Math.max(element.scrollHeight, minHeight), maxHeight);
    element.style.height = newHeight + 'px';
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

// Global native desktop keyboard shortcuts (⌘+, and Esc)
window.addEventListener('keydown', function (e) {
    if ((e.metaKey || e.ctrlKey) && e.key === ',') {
        e.preventDefault();
        const settingsBtn = document.querySelector('.dt-topbar-right .dt-btn-text');
        if (settingsBtn) settingsBtn.click();
    } else if (e.key === 'Escape') {
        const backToChatBtn = document.querySelector('.btn-back-to-chat');
        if (backToChatBtn) {
            e.preventDefault();
            backToChatBtn.click();
        }
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
