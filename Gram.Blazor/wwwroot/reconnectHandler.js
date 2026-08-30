// ============================================================================
// Gram Reconnection Monitor (Cardless Minimal Frosted Stage)
// ============================================================================

(function () {
    let reconnectTimeoutSecs = 18;
    let countdownInterval = null;
    let secondsRemaining = reconnectTimeoutSecs;
    let lastExceptionInfo = {
        subsystem: "SignalR Circuit",
        detail: "Real-time session disconnected from server.",
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
        online: navigator.onLine
    };

    window.__gramReportException = function (subsystem, detail) {
        lastExceptionInfo = {
            subsystem: subsystem || "SignalR Circuit",
            detail: typeof detail === "string" ? detail : (detail?.message || JSON.stringify(detail)),
            timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
            online: navigator.onLine
        };
        updateDiagnosticsUI();
    };

    window.addEventListener("offline", function () {
        window.__gramReportException("Network Gateway", "Device is offline. Please check your internet connection.");
    });

    window.addEventListener("online", function () {
        if (lastExceptionInfo.subsystem === "Network Gateway") {
            window.__gramReportException("Network Gateway", "Internet restored. Attempting to re-establish session...");
        }
    });

    function updateDiagnosticsUI() {
        const detailEl = document.getElementById("reconnect-diag-detail");
        if (detailEl && lastExceptionInfo.detail) {
            detailEl.textContent = lastExceptionInfo.detail;
        }
    }

    function startCountdown() {
        stopCountdown();
        secondsRemaining = reconnectTimeoutSecs;
        updateTimerDisplay();

        countdownInterval = setInterval(function () {
            secondsRemaining--;
            updateTimerDisplay();

            if (secondsRemaining <= 0) {
                stopCountdown();
                const modal = document.getElementById("components-reconnect-modal");
                if (modal && modal.classList.contains("components-reconnect-show")) {
                    modal.classList.remove("components-reconnect-show");
                    modal.classList.add("components-reconnect-failed");
                    window.__gramReportException("Connection Timeout", "Connection timed out. Please reload the page.");
                }
            }
        }, 1000);
    }

    function stopCountdown() {
        if (countdownInterval) {
            clearInterval(countdownInterval);
            countdownInterval = null;
        }
    }

    function updateTimerDisplay() {
        const timerEl = document.getElementById("reconnect-countdown-text");
        if (timerEl) {
            timerEl.textContent = `Reconnecting in ${secondsRemaining}s`;
        }
    }

    function initObserver() {
        const modal = document.getElementById("components-reconnect-modal");
        if (!modal) return;

        // Ensure initially hidden with no pointer blocking
        if (!modal.classList.contains("components-reconnect-show") && 
            !modal.classList.contains("components-reconnect-failed") && 
            !modal.classList.contains("components-reconnect-rejected")) {
            modal.style.display = "none";
            modal.style.pointerEvents = "none";
        }

        const observer = new MutationObserver(function (mutations) {
            for (const mutation of mutations) {
                if (mutation.type === "attributes" && mutation.attributeName === "class") {
                    if (modal.classList.contains("components-reconnect-show")) {
                        modal.style.display = "flex";
                        modal.style.pointerEvents = "auto";
                        startCountdown();
                        updateDiagnosticsUI();
                    } else if (modal.classList.contains("components-reconnect-failed") || modal.classList.contains("components-reconnect-rejected")) {
                        modal.style.display = "flex";
                        modal.style.pointerEvents = "auto";
                        stopCountdown();
                        updateDiagnosticsUI();
                    } else {
                        modal.style.display = "none";
                        modal.style.pointerEvents = "none";
                        stopCountdown();
                    }
                }
            }
        });

        observer.observe(modal, { attributes: true });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initObserver);
    } else {
        initObserver();
    }
})();
