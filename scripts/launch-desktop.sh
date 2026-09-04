#!/usr/bin/env bash
# ==============================================================================
# Gram Desktop Client Launcher
# Starts backend server (if not already running) and launches Gram.Desktop
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

BACKEND_PORT=7337
BACKEND_URL="http://localhost:${BACKEND_PORT}"
BACKEND_PID=""

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

cleanup() {
    if [[ -n "${BACKEND_PID}" ]]; then
        echo -e "\n${YELLOW}Stopping background Gram backend (PID: ${BACKEND_PID})...${NC}"
        kill "${BACKEND_PID}" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM

echo -e "${CYAN}===============================================${NC}"
echo -e "${CYAN}       Gram Desktop Native Client Launcher     ${NC}"
echo -e "${CYAN}===============================================${NC}"

# 1. Verify dotnet SDK
if ! command -v dotnet >/dev/null 2>&1; then
    echo -e "${RED}Error: .NET SDK (dotnet) is not installed or not in PATH.${NC}"
    echo "Please install .NET 9 SDK: https://dotnet.microsoft.com/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo -e "Using .NET SDK: ${GREEN}${DOTNET_VERSION}${NC}"

# 2. Check or launch Gram Backend
echo -n "Checking Gram backend at ${BACKEND_URL}... "
if curl -s -f -m 2 "${BACKEND_URL}/api/v1/models" >/dev/null 2>&1; then
    echo -e "${GREEN}Running.${NC}"
else
    echo -e "${YELLOW}Not running. Starting backend in background...${NC}"
    cd "${ROOT_DIR}"
    dotnet run --project Gram.Blazor/Gram.Blazor.csproj >/dev/null 2>&1 &
    BACKEND_PID=$!
    echo -e "Backend process launched (PID: ${BACKEND_PID}). Waiting for health check..."

    RETRIES=25
    READY=0
    for ((i=1; i<=RETRIES; i++)); do
        if curl -s -f -m 1 "${BACKEND_URL}/api/v1/models" >/dev/null 2>&1; then
            READY=1
            break
        fi
        sleep 0.8
    done

    if [[ ${READY} -eq 1 ]]; then
        echo -e "${GREEN}Backend is healthy and listening on ${BACKEND_URL}.${NC}"
    else
        echo -e "${RED}Timed out waiting for backend to start.${NC}"
        exit 1
    fi
fi

# 3. Handle Arguments (Headless CLI vs GUI Mode)
cd "${ROOT_DIR}"

if [[ "${1:-}" == "--cli" || "${1:-}" == "--message" ]]; then
    MESSAGE="${2:-Hello Gram}"
    echo -e "${CYAN}Launching in Headless CLI Mode: \"${MESSAGE}\"${NC}"
    exec dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj -- --backend "${BACKEND_URL}" --message "${MESSAGE}"
fi

# 4. Native GUI Launch
echo -e "${CYAN}Launching Gram Desktop Native GUI Window...${NC}"

if [[ -d "${ROOT_DIR}/Gram.app" ]]; then
    echo -e "${GREEN}Opening native macOS application bundle: Gram.app${NC}"
    open "${ROOT_DIR}/Gram.app"
    echo -e "${GREEN}Gram Desktop is running in your active macOS session.${NC}"
else
    echo -e "Press ${YELLOW}Ctrl+C${NC} in this terminal to exit.\n"
    dotnet run --project Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj -- --backend "${BACKEND_URL}"
fi
