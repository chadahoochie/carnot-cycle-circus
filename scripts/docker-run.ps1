<#
.SYNOPSIS
    Manages the Carnot Cycle Circus Docker persistent stack.
.EXAMPLE
    .\scripts\docker-run.ps1 -Action up
    .\scripts\docker-run.ps1 -Action down
    .\scripts\docker-run.ps1 -Action health
#>
[CmdletBinding()]
param(
    [ValidateSet('up', 'start', 'down', 'stop', 'logs', 'health', 'status', 'backup', 'clean-volumes')]
    [string]$Action = 'up'
)

switch ($Action) {
    { $_ -in 'up', 'start' } {
        Write-Host "🎪 Starting Carnot Cycle Circus Persistent Stack..." -ForegroundColor Cyan
        docker compose up --build -d
        Write-Host "✅ Circus Stack is running! Access Web UI at: http://localhost:5000" -ForegroundColor Green
    }
    { $_ -in 'down', 'stop' } {
        Write-Host "🛑 Stopping Carnot Cycle Circus Stack..." -ForegroundColor Yellow
        docker compose down
        Write-Host "✅ Containers stopped. Persistent volumes remain safely preserved." -ForegroundColor Green
    }
    'logs' {
        docker compose logs -f circus-web
    }
    'health' {
        Invoke-RestMethod -Uri "http://localhost:5000/health" | ConvertTo-Json -Depth 4
    }
    'status' {
        docker compose ps
    }
    'backup' {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupDir = "backups/carnot_backup_$timestamp"
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        Write-Host "📦 Backing up persistent volumes to $backupDir..." -ForegroundColor Cyan
        docker run --rm -v carnot_data:/data -v "${PWD}/${backupDir}:/backup" alpine tar czf /backup/carnot_data.tar.gz -C /data .
        Write-Host "✅ Backup complete at $backupDir/carnot_data.tar.gz" -ForegroundColor Green
    }
    'clean-volumes' {
        $confirm = Read-Host "⚠️ WARNING: This will permanently delete all persisted data! Continue? (y/N)"
        if ($confirm -eq 'y') {
            docker compose down -v
            Write-Host "🗑️ Persistent volumes deleted." -ForegroundColor Red
        }
    }
}
