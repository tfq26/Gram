#!/usr/bin/env bash
# ==============================================================================
# Opens the Gram Desktop launcher in an interactive macOS Terminal window
# ==============================================================================

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

osascript -e "tell application \"Terminal\" to do script \"cd '${ROOT_DIR}' && ./scripts/launch-desktop.sh\""
echo "Gram Desktop opened in a dedicated macOS Terminal window."
