#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Building Carnot Cycle Circus solution..."
dotnet build "$ROOT_DIR/CarnotCycleCircus.slnx"
