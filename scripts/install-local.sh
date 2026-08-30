#!/usr/bin/env bash
set -euo pipefail

CARNOT_DIR="${HOME}/.carnot"
BIN_DIR="${CARNOT_DIR}/bin"
DATA_DIR="${CARNOT_DIR}/data"
ARTIFACTS_DIR="${CARNOT_DIR}/artifacts"

echo "🎪 Installing Carnot Cycle Circus locally..."
mkdir -p "${BIN_DIR}" "${DATA_DIR}" "${ARTIFACTS_DIR}" "${DATA_DIR}/vault" "${DATA_DIR}/skills" "${ARTIFACTS_DIR}/adrs"

echo "📦 Publishing Carnot Desktop Client (Photino.Blazor)..."
dotnet publish src/CarnotCycleCircus.Desktop/CarnotCycleCircus.Desktop.csproj -c Release -o "${BIN_DIR}/desktop"

echo "📦 Publishing Carnot Headless Agent Server..."
dotnet publish src/CarnotCycleCircus.Server/CarnotCycleCircus.Server.csproj -c Release -o "${BIN_DIR}/server"

# Create launcher wrappers
cat << 'LAUNCHER' > "${BIN_DIR}/carnot-desktop"
#!/usr/bin/env bash
exec "${HOME}/.carnot/bin/desktop/CarnotCycleCircus.Desktop" "$@"
LAUNCHER
chmod +x "${BIN_DIR}/carnot-desktop"

cat << 'LAUNCHER' > "${BIN_DIR}/carnot-server"
#!/usr/bin/env bash
export CARNOT_DATA_DIR="${HOME}/.carnot/data"
export CARNOT_ARTIFACTS_DIR="${HOME}/.carnot/artifacts"
exec "${HOME}/.carnot/bin/server/CarnotCycleCircus.Server" "$@"
LAUNCHER
chmod +x "${BIN_DIR}/carnot-server"

echo "✅ Carnot Cycle Circus successfully installed into ~/.carnot!"
echo "   - Desktop Launcher: ~/.carnot/bin/carnot-desktop"
echo "   - Server Launcher:  ~/.carnot/bin/carnot-server"
echo "   - Data Directory:   ~/.carnot/data"
echo "   - Artifacts:        ~/.carnot/artifacts"
