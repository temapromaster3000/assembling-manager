$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$apiUrl = Get-SandboxApiUrl
if (-not $apiUrl) {
    Write-Host 'Песочница ещё не настроена — останавливать нечего.'
    return
}

Stop-SandboxServer $apiUrl
Write-Host "OpenCode-сервер песочницы остановлен: $apiUrl"
Write-Host 'Если окно бота ещё открыто, нажмите в нём Ctrl+C или закройте окно.'
