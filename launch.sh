#!/usr/bin/env bash
# ==============================================================================
# Gram v2 Unified Platform Launcher
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
cd "${SCRIPT_DIR}"

if ! command -v bun >/dev/null 2>&1; then
    echo "Error: Bun is required to launch Gram v2. Install it from https://bun.sh" >&2
    exit 1
fi

PORT="${GRAM_PORT:-7337}"
STUDIO_URL="http://localhost:${PORT}"

usage() {
    cat <<'USAGE'
Gram v2 Launcher

Usage: ./launch.sh [mode] [options]

Modes:
  --web, -w              Launch Gram Studio Web UI & AHP Daemon (Default)
  --cli [prompt]         Launch Gram in fast CLI / terminal mode
  --server, -s           Run the background AHP v2 sync daemon only
  --doctor               Run the environment and toolchain doctor
  --graph                Scan and display the repository Semantic AST Graph
  --help, -h             Show this help message

Examples:
  ./launch.sh
  ./launch.sh --cli "verify my project setup"
  ./launch.sh --doctor
USAGE
}

MODE="${1:-web}"

case "${MODE}" in
    --web|-w|web)
        echo "⚡ Launching Gram v2 Studio & AHP Daemon on ${STUDIO_URL}..."
        if command -v open >/dev/null 2>&1; then
            (sleep 1.2 && open "${STUDIO_URL}") &
        elif command -v xdg-open >/dev/null 2>&1; then
            (sleep 1.2 && xdg-open "${STUDIO_URL}") &
        fi
        exec bun run "${SCRIPT_DIR}/src/server/index.ts"
        ;;
    --cli|-c|cli)
        shift || true
        exec bun run "${SCRIPT_DIR}/src/cli/index.ts" "$@"
        ;;
    --server|-s|server)
        exec bun run "${SCRIPT_DIR}/src/server/index.ts"
        ;;
    --doctor)
        exec bun run "${SCRIPT_DIR}/src/cli/index.ts" doctor
        ;;
    --graph)
        exec bun run "${SCRIPT_DIR}/src/cli/index.ts" graph
        ;;
    --help|-h)
        usage
        exit 0
        ;;
    *)
        # If a prompt string was given directly: ./launch.sh "prompt"
        exec bun run "${SCRIPT_DIR}/src/cli/index.ts" "$@"
        ;;
esac
