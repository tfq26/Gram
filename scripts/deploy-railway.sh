#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Gram Build & Publish Script for Railway
# =============================================================================

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

echo "=========================================="
echo "  Gram AI Assistant - Railway Deployment  "
echo "=========================================="

echo ""
echo "🧪 [1/4] Running Test Suite..."
dotnet test Gram.Blazor/tests/Gram.Blazor.UnitTests/Gram.Blazor.UnitTests.csproj -c Release --verbosity normal

echo ""
echo "📦 [2/4] Verifying Release Compilation..."
rm -rf /tmp/gram-railway-publish
dotnet publish Gram.Blazor/Gram.Server/Gram.Server.csproj -c Release -o /tmp/gram-railway-publish /p:UseAppHost=false
rm -rf /tmp/gram-railway-publish
echo "✅ Compilation and packaging verified."

echo ""
echo "🚀 [3/4] Deploying to Railway (service: gram-web)..."
if command -v railway &> /dev/null; then
    railway up --service gram-web --detach
    echo "✅ Railway deployment triggered successfully."
else
    echo "⚠️ Railway CLI not found on PATH. Ensure it is installed via npm install -g @railway/cli."
fi

echo ""
echo "🌐 [4/4] Checking Deployment Status..."
if command -v railway &> /dev/null; then
    railway status
fi

echo ""
echo "🎉 Done! Live app URL: https://gram.2helix.org"
