// ============================================================================
// Gram Reconnection Monitor (Minimal Centered Pane)
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

    window.addEventListener("unhandledrejection", function (event) {
        const reason = event.reason ? (event.reason.message || String(event.reason)) : "Session interrupted";
        window.__gramReportException("SignalR Circuit", reason);
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
                if (modal && (modal.classList.contains("components-reconnect-show") || !modal.classList.contains("components-reconnect-failed"))) {
                    modal.classList.remove("components-reconnect-show");
                    modal.classList.add("components-reconnect-failed");
                    window.__gramReportException("Reconnection Timeout", `Connection timed out after ${reconnectTimeoutSecs} seconds.`);
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
            timerEl.textContent = `Timing out in ${secondsRemaining}s`;
        }
    }

    window.__gramCopyDiagnostics = function () {
        const report = `[Gram Diagnostic] ${lastExceptionInfo.timestamp} | ${lastExceptionInfo.subsystem} (${navigator.onLine ? "online" : "offline"}): ${lastExceptionInfo.detail}`;

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(report).then(function () {
                const btn = document.getElementById("reconnect-copy-btn");
                if (btn) {
                    btn.textContent = "Copied";
                    setTimeout(() => btn.textContent = "Copy Diagnostics", 1500);
                }
            });
        }
    };

    function initObserver() {
        const modal = document.getElementById("components-reconnect-modal");
        if (!modal) return;

        const observer = new MutationObserver(function (mutations) {
            for (const mutation of mutations) {
                if (mutation.type === "attributes" && mutation.attributeName === "class") {
                    if (modal.classList.contains("components-reconnect-show")) {
                        startCountdown();
                        updateDiagnosticsUI();
                    } else if (modal.classList.contains("components-reconnect-failed") || modal.classList.contains("components-reconnect-rejected")) {
                        stopCountdown();
                        updateDiagnosticsUI();
                    } else {
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
