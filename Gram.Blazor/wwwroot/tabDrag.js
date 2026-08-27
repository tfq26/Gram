/**
 * Tab Bar Drag Controller & Split Screen Dock for Gram
 */
window.gramTabDrag = {
    dotNetHelper: null,
    draggedTabId: null,
    draggedTitle: '',
    isDragging: false,
    startX: 0,
    startY: 0,
    ghostEl: null,
    isOverSplitDock: false,

    init: function (dotNetHelper) {
        this.dotNetHelper = dotNetHelper;
    },

    startDrag: function (e, tabId, title) {
        if (e.button !== 0) return; // Only left click
        if (e.target.closest('.topbar-tab-close') || e.target.closest('.btn-new-tab')) return;

        this.draggedTabId = tabId;
        this.draggedTitle = title || 'Chat';
        this.startX = e.clientX;
        this.startY = e.clientY;
        this.isDragging = false;
        this.isOverSplitDock = false;

        var self = this;
        var onPointerMove = function (moveEvent) {
            var dx = moveEvent.clientX - self.startX;
            var dy = moveEvent.clientY - self.startY;

            if (!self.isDragging && (Math.abs(dx) > 4 || Math.abs(dy) > 4)) {
                self.isDragging = true;
                self.createGhost(moveEvent.clientX, moveEvent.clientY);
                document.body.classList.add('tab-drag-in-progress');
                if (self.dotNetHelper) {
                    self.dotNetHelper.invokeMethodAsync('OnDragStarted', self.draggedTabId);
                }
            }

            if (self.isDragging) {
                self.updateGhost(moveEvent.clientX, moveEvent.clientY);
                self.checkHoverTargets(moveEvent.clientX, moveEvent.clientY);
            }
        };

        var onPointerUp = function (upEvent) {
            document.removeEventListener('pointermove', onPointerMove);
            document.removeEventListener('pointerup', onPointerUp);
            document.body.classList.remove('tab-drag-in-progress');

            if (self.isDragging) {
                self.finishDrag(upEvent.clientX, upEvent.clientY);
            }

            self.cleanupGhost();
            self.isDragging = false;
            self.draggedTabId = null;
        };

        document.addEventListener('pointermove', onPointerMove);
        document.addEventListener('pointerup', onPointerUp);
    },

    createGhost: function (x, y) {
        this.cleanupGhost();
        var ghost = document.createElement('div');
        ghost.className = 'tab-drag-ghost-pill';
        ghost.innerHTML = `
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                <line x1="12" y1="3" x2="12" y2="21"></line>
            </svg>
            <span>${this.draggedTitle}</span>
        `;
        document.body.appendChild(ghost);
        this.ghostEl = ghost;
        this.updateGhost(x, y);
    },

    updateGhost: function (x, y) {
        if (!this.ghostEl) return;
        this.ghostEl.style.left = (x + 10) + 'px';
        this.ghostEl.style.top = (y + 10) + 'px';
    },

    cleanupGhost: function () {
        if (this.ghostEl && this.ghostEl.parentNode) {
            this.ghostEl.parentNode.removeChild(this.ghostEl);
        }
        this.ghostEl = null;
    },

    checkHoverTargets: function (x, y) {
        var el = document.elementFromPoint(x, y);
        var splitDock = el ? el.closest('.topbar-tab-split-dock') : null;
        var splitTarget = el ? el.closest('.split-drag-drop-target') : null;
        var tabList = document.querySelector('.topbar-tabs-list');

        // Check if cursor is over the explicit split dock or dragged past the right edge of tabs
        var isSplit = false;
        if (splitDock || splitTarget) {
            isSplit = true;
        } else if (tabList) {
            var rect = tabList.getBoundingClientRect();
            if (x > rect.right - 40 && y >= rect.top - 10 && y <= rect.bottom + 10) {
                isSplit = true;
            }
        }

        var dockEl = document.querySelector('.topbar-tab-split-dock');
        if (dockEl) {
            if (isSplit) {
                dockEl.classList.add('hover-active');
            } else {
                dockEl.classList.remove('hover-active');
            }
        }

        var splitTargetEl = document.querySelector('.split-drag-drop-target');
        if (splitTargetEl) {
            splitTargetEl.classList.toggle('hover-active', !!splitTarget);
        }
        this.isOverSplitDock = isSplit;
    },

    finishDrag: function (x, y) {
        var self = this;

        if (self.isOverSplitDock) {
            if (self.dotNetHelper) {
                self.dotNetHelper.invokeMethodAsync('OnTabSnapped', self.draggedTabId, 'right');
            }
            return;
        }

        // Check if dropped onto another tab in the tab bar for reordering
        var tabUnderCursor = document.elementFromPoint(x, y);
        if (tabUnderCursor) {
            var targetTabEl = tabUnderCursor.closest('.topbar-tab');
            if (targetTabEl && targetTabEl.dataset && targetTabEl.dataset.tabid) {
                var targetTabId = targetTabEl.dataset.tabid;
                if (targetTabId && targetTabId !== self.draggedTabId && self.dotNetHelper) {
                    self.dotNetHelper.invokeMethodAsync('OnTabReordered', self.draggedTabId, targetTabId);
                    return;
                }
            }
        }

        if (self.dotNetHelper) {
            self.dotNetHelper.invokeMethodAsync('OnDragEnded');
        }
    }
};
