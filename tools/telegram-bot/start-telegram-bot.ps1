$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$botCommand = Get-BotCommand
$apiUrl = Get-SandboxApiUrl
if (-not $apiUrl) {
    throw 'Песочница ещё не настроена. Сначала запустите setup-telegram-bot.bat.'
}

$token = [Environment]::GetEnvironmentVariable('TELEGRAM_BOT_TOKEN', 'User')
$userId = [Environment]::GetEnvironmentVariable('TELEGRAM_ALLOWED_USER_ID', 'User')
if (-not $token -or -not $userId) {
    throw 'Не заданы TELEGRAM_BOT_TOKEN / TELEGRAM_ALLOWED_USER_ID. Сначала запустите configure-telegram-bot.bat.'
}

$env:TELEGRAM_BOT_TOKEN = $token
$env:TELEGRAM_ALLOWED_USER_ID = $userId
$env:OPENCODE_TELEGRAM_HOME = $script:BotHome
$env:OPENCODE_API_URL = $apiUrl
Set-SandboxEnvironment

Write-Host ''
Write-Host '=== Telegram-бот Assembling Manager ===' -ForegroundColor Cyan
Write-Host "OpenCode-сервер: $apiUrl"
Write-Host 'Остановить: Ctrl+C в этом окне или закройте окно.'
Write-Host ''

try {
    $serverProcess = Start-SandboxServer $apiUrl
    if (-not (Wait-SandboxReady $apiUrl 90)) {
        throw "Агент revit или MCP-сервер не загрузились за 90 секунд. Проверьте логи в $($script:LogsDir)."
    }

    # Рабочая папка бота = workspace песочницы: если бот запустит сервер сам
    # (/opencode_start), он не подхватит конфиг репозитория.
    Push-Location $script:Workspace
    try {
        & $botCommand start
    }
    finally {
        Pop-Location
    }
}
finally {
    Stop-SandboxServer $apiUrl
    Write-Host ''
    Write-Host 'OpenCode-сервер песочницы остановлен.'
}
