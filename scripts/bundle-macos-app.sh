#!/usr/bin/env bash
# ==============================================================================
# Bundle Gram.Desktop into a native macOS Gram.app Application Bundle
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
APP_BUNDLE="${ROOT_DIR}/Gram.app"
PUBLISH_DIR="${ROOT_DIR}/Gram.Blazor/clients/Gram.Desktop/bin/Release/net9.0/osx-arm64/publish"
ICON_PATH="${ROOT_DIR}/Gram.Blazor/clients/Gram.Desktop/Resources/AppIcon.icns"

echo "Publishing Gram.Desktop for osx-arm64..."
cd "${ROOT_DIR}"
dotnet publish Gram.Blazor/clients/Gram.Desktop/Gram.Desktop.csproj \
    -c Release \
    -r osx-arm64 \
    --self-contained false \
    -p:PublishReadyToRun=false

echo "Creating macOS Application Bundle: ${APP_BUNDLE}..."
rm -rf "${APP_BUNDLE}"
mkdir -p "${APP_BUNDLE}/Contents/MacOS"
mkdir -p "${APP_BUNDLE}/Contents/Resources"

# Copy all published files into Contents/MacOS
cp -R "${PUBLISH_DIR}/"* "${APP_BUNDLE}/Contents/MacOS/"

# Make sure executable permissions are preserved
chmod +x "${APP_BUNDLE}/Contents/MacOS/Gram.Desktop"

# Copy Icon if present
if [[ -f "${ICON_PATH}" ]]; then
    cp -f "${ICON_PATH}" "${APP_BUNDLE}/Contents/Resources/AppIcon.icns"
fi

# Copy colors directory if present
if [[ -d "${ROOT_DIR}/colors" ]]; then
    cp -R "${ROOT_DIR}/colors" "${APP_BUNDLE}/Contents/Resources/"
    cp -R "${ROOT_DIR}/colors" "${APP_BUNDLE}/Contents/MacOS/"
fi

# Create Info.plist
cat > "${APP_BUNDLE}/Contents/Info.plist" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Gram</string>
    <key>CFBundleDisplayName</key>
    <string>Gram</string>
    <key>CFBundleIdentifier</key>
    <string>org.2helix.gram.desktop</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleSignature</key>
    <string>GRAM</string>
    <key>CFBundleExecutable</key>
    <string>Gram.Desktop</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
EOF

# Create PkgInfo
echo -n "APPLGRAM" > "${APP_BUNDLE}/Contents/PkgInfo"

touch "${APP_BUNDLE}"

echo "Successfully created ${APP_BUNDLE}!"
echo "You can launch it via: open \"${APP_BUNDLE}\""
