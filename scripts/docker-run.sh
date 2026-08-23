#!/usr/bin/env bash
# ==============================================================================
# Carnot Cycle Circus - Docker Stack Management Script
# ==============================================================================

set -euo pipefail

ACTION="${1:-up}"

case "$ACTION" in
  up|start)
    echo "🎪 Starting Carnot Cycle Circus Persistent Stack..."
    docker compose up --build -d
    echo "✅ Circus Stack is running! Access Blazor Web UI at: http://localhost:5000"
    ;;
  down|stop)
    echo "🛑 Stopping Carnot Cycle Circus Stack..."
    docker compose down
    echo "✅ Containers stopped. Persistent volumes (carnot_data, carnot_artifacts, carnot_skills) remain safely preserved."
    ;;
  logs)
    docker compose logs -f circus-web
    ;;
  health)
    curl -s http://localhost:5000/health | jq . || curl -s http://localhost:5000/health
    ;;
  status|ps)
    docker compose ps
    ;;
  backup)
    BACKUP_DIR="backups/carnot_backup_$(date +%Y%m%d_%H%M%S)"
    mkdir -p "$BACKUP_DIR"
    echo "📦 Backing up persistent volumes to $BACKUP_DIR..."
    docker run --rm -v carnot_data:/data -v "$(pwd)/$BACKUP_DIR":/backup alpine tar czf /backup/carnot_data.tar.gz -C /data .
    echo "✅ Backup complete at $BACKUP_DIR/carnot_data.tar.gz"
    ;;
  clean-volumes)
    read -p "⚠️ WARNING: This will permanently delete all persisted memories, tickets, and skills! Continue? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
      docker compose down -v
      echo "🗑️ Persistent volumes deleted."
    fi
    ;;
  *)
    echo "Usage: $0 {up|down|logs|health|status|backup|clean-volumes}"
    exit 1
    ;;
esac
