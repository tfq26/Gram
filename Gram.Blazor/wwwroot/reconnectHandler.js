// ============================================================================
// Gram Reconnection & Diagnostic Health Monitor
// ============================================================================

(function () {
    let reconnectTimeoutSecs = 18;
    let countdownInterval = null;
    let secondsRemaining = reconnectTimeoutSecs;
    let isDetailsExpanded = false;
    let lastExceptionInfo = {
        subsystem: "SignalR Realtime Circuit",
        detail: "Real-time interactive session disconnected from server.",
        timestamp: new Date().toLocaleTimeString(),
        online: navigator.onLine
    };

    // Global exception / diagnostic report hook
    window.__gramReportException = function (subsystem, detail) {
        lastExceptionInfo = {
            subsystem: subsystem || "Application Core",
            detail: typeof detail === "string" ? detail : (detail?.message || JSON.stringify(detail)),
            timestamp: new Date().toLocaleTimeString(),
            online: navigator.onLine
        };
        updateDiagnosticsUI();
    };

    // Listen to browser network changes
    window.addEventListener("offline", function () {
        window.__gramReportException("Network Gateway", "Browser detected that device is offline (no active internet connection).");
    });

    window.addEventListener("online", function () {
        if (lastExceptionInfo.subsystem === "Network Gateway") {
            window.__gramReportException("Network Gateway", "Internet connection restored. Attempting to re-establish session...");
        }
    });

    // Capture global unhandled exceptions
    window.addEventListener("unhandledrejection", function (event) {
        const reason = event.reason ? (event.reason.message || String(event.reason)) : "Unknown rejection";
        if (reason.toLowerCase().includes("blazor") || reason.toLowerCase().includes("circuit") || reason.toLowerCase().includes("websocket")) {
            window.__gramReportException("SignalR Realtime Circuit", reason);
        } else if (reason.toLowerCase().includes("fetch") || reason.toLowerCase().includes("api")) {
            window.__gramReportException("Backend API Server", reason);
        }
    });

    function updateDiagnosticsUI() {
        const subEl = document.getElementById("reconnect-diag-subsystem");
        const detailEl = document.getElementById("reconnect-diag-detail");
        const timeEl = document.getElementById("reconnect-diag-time");
        const netEl = document.getElementById("reconnect-diag-net");

        if (subEl) subEl.textContent = lastExceptionInfo.subsystem;
        if (detailEl) detailEl.textContent = lastExceptionInfo.detail;
        if (timeEl) timeEl.textContent = lastExceptionInfo.timestamp;
        if (netEl) netEl.textContent = navigator.onLine ? "Online (Connected)" : "Offline (No Internet)";
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
                    window.__gramReportException("Reconnection Timeout", `Session failed to reconnect within ${reconnectTimeoutSecs} seconds.`);
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
            timerEl.textContent = `(${secondsRemaining}s timeout)`;
        }
    }

    window.__gramToggleReconnectDetails = function () {
        const detailsPanel = document.getElementById("reconnect-details-drawer");
        const toggleBtn = document.getElementById("reconnect-toggle-btn");
        if (!detailsPanel || !toggleBtn) return;

        isDetailsExpanded = !isDetailsExpanded;
        if (isDetailsExpanded) {
            detailsPanel.classList.add("is-expanded");
            toggleBtn.innerHTML = `Hide diagnostics ▲`;
            updateDiagnosticsUI();
        } else {
            detailsPanel.classList.remove("is-expanded");
            toggleBtn.innerHTML = `View diagnostics ▼`;
        }
    };

    window.__gramCopyDiagnostics = function () {
        const report = `[Gram Connection Diagnostic Report]
Timestamp: ${lastExceptionInfo.timestamp}
Subsystem: ${lastExceptionInfo.subsystem}
Network State: ${navigator.onLine ? "Online" : "Offline"}
Detail: ${lastExceptionInfo.detail}
User-Agent: ${navigator.userAgent}`;

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(report).then(function () {
                const btn = document.getElementById("reconnect-copy-btn");
                if (btn) {
                    const originalText = btn.textContent;
                    btn.textContent = "Copied!";
                    setTimeout(() => btn.textContent = originalText, 2000);
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
