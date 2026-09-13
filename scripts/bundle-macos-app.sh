#!/usr/bin/env bash
# ==============================================================================
# Bundle Gram.Maui into a native macOS Gram.app Application Bundle
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
APP_BUNDLE="${ROOT_DIR}/Gram.app"
MAUI_PROJECT="${ROOT_DIR}/Gram.Blazor/clients/Gram.Maui/Gram.Maui.csproj"

echo "Publishing Gram.Maui for MacCatalyst (net9.0-maccatalyst)..."
cd "${ROOT_DIR}"
dotnet publish "${MAUI_PROJECT}" \
    -f net9.0-maccatalyst \
    -c Release \
    -p:CreatePackage=false

# Locate the published .app bundle
CANDIDATE_APP=$(find "${ROOT_DIR}/Gram.Blazor/clients/Gram.Maui/bin/Release/net9.0-maccatalyst" -name "*.app" -type d | head -n 1)

if [[ -z "${CANDIDATE_APP}" || ! -d "${CANDIDATE_APP}" ]]; then
    echo "Searching for bundle in publish directory..."
    CANDIDATE_APP=$(find "${ROOT_DIR}/Gram.Blazor/clients/Gram.Maui/bin/Release" -name "*.app" -type d | head -n 1)
fi

if [[ -n "${CANDIDATE_APP}" && -d "${CANDIDATE_APP}" ]]; then
    echo "Found built application bundle at: ${CANDIDATE_APP}"
    rm -rf "${APP_BUNDLE}"
    cp -R "${CANDIDATE_APP}" "${APP_BUNDLE}"
    echo "Successfully created ${APP_BUNDLE}!"
    echo "You can launch it via: open \"${APP_BUNDLE}\""
else
    echo "Error: Could not find generated .app bundle after publishing."
    exit 1
fi
exit 0
