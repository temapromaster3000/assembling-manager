param(
    [switch]$SkipTelegram,
    [switch]$SkipCredentials
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

Write-Host ''
Write-Host '=== Настройка Telegram-бота Assembling Manager ===' -ForegroundColor Cyan
Write-Host ''

$botCommand = Get-BotCommand
$opencodeExe = Get-OpenCodeExe
$mcpExe = Get-McpServerExe
Write-Host "MCP-сервер: $mcpExe"

Ensure-Directory $script:Workspace
Ensure-Directory $script:ConfigOpencode
Ensure-Directory $script:DataOpencode
Ensure-Directory $script:StateOpencode
Ensure-Directory $script:BotHome
Ensure-Directory $script:LogsDir

# Перечитываем конфиг с диска: если песочница уже была запущена, её нужно остановить.
$existingUrl = Get-SandboxApiUrl
if ($existingUrl) {
    Stop-SandboxServer $existingUrl
    Start-Sleep -Seconds 1
}

$configTemplate = Read-Utf8File (Join-Path $PSScriptRoot 'templates\opencode.jsonc.template')
$configContent = $configTemplate.Replace('__MCP_SERVER_EXE__', ($mcpExe -replace '\\', '/'))
Write-Utf8NoBom (Join-Path $script:ConfigOpencode 'opencode.jsonc') $configContent

$agentsTemplate = Read-Utf8File (Join-Path $PSScriptRoot 'templates\AGENTS.md')
Write-Utf8NoBom (Join-Path $script:Workspace 'AGENTS.md') $agentsTemplate

$apiUrl = $existingUrl
if (-not $apiUrl) {
    $port = Find-FreePort 49375
    $apiUrl = "http://127.0.0.1:$port"
}

$null = Get-ServerPassword

$inboxDir = Join-Path $script:RuntimeRoot 'inbox'
$envLines = @(
    'OPENCODE_SERVER_VERSION=v2',
    'OPENCODE_SERVER_MODE=standalone',
    "OPENCODE_API_URL=$apiUrl",
    'OPENCODE_SERVER_USERNAME=opencode',
    'OPENCODE_MODEL_PROVIDER=opencode-go',
    'OPENCODE_MODEL_ID=deepseek-v4.1-flash',
    'BOT_LOCALE=ru',
    'MESSAGE_FORMAT_MODE=markdown',
    'LOG_LEVEL=info',
    'TRACK_BACKGROUND_SESSIONS=false',
    "OPEN_BROWSER_ROOTS=$($script:Workspace)",
    "FILE_INBOX_DIR=$inboxDir"
)
Write-Utf8NoBom $script:BotEnvFile (($envLines -join "`r`n") + "`r`n")

if (-not $SkipTelegram) {
    $token = [Environment]::GetEnvironmentVariable('TELEGRAM_BOT_TOKEN', 'User')
    $userId = [Environment]::GetEnvironmentVariable('TELEGRAM_ALLOWED_USER_ID', 'User')
    if (-not $token -or -not $userId) {
        Write-Host ''
        Write-Host 'Токен бота и Telegram ID ещё не заданы.' -ForegroundColor Yellow
        & (Join-Path $PSScriptRoot 'configure-telegram-bot.ps1')
        $token = [Environment]::GetEnvironmentVariable('TELEGRAM_BOT_TOKEN', 'User')
        $userId = [Environment]::GetEnvironmentVariable('TELEGRAM_ALLOWED_USER_ID', 'User')
    }
    if (-not $token -or -not $userId) {
        throw 'Без TELEGRAM_BOT_TOKEN и TELEGRAM_ALLOWED_USER_ID бот не запустится.'
    }
}
else {
    Write-Host 'Проверка Telegram-токена пропущена (-SkipTelegram).' -ForegroundColor Yellow
}

if ($SkipCredentials) {
    Write-Host 'Импорт credentials пропущен (-SkipCredentials).' -ForegroundColor Yellow

    Write-Host ''
    Write-Host 'Настройка файлов завершена.' -ForegroundColor Green
    Write-Host "Песочница: $($script:RuntimeRoot)"
    Write-Host "Сервер:    $apiUrl"
    return
}

# --- Экспорт credentials из основного OpenCode и импорт в песочницу ---

Write-Host ''
Write-Host 'Проверяю основной OpenCode-сервис...'

$serviceStatus = ''
$serviceResult = Invoke-NativeCommand -FilePath $opencodeExe -Arguments @('service', 'status') -SuppressOutput
$serviceStatus = (($serviceResult.StdOut + "`n" + $serviceResult.StdErr).Trim())

if ($serviceStatus -notmatch 'http') {
    Write-Host 'Основной OpenCode-сервис не запущен, запускаю...'
    $null = Invoke-NativeCommand -FilePath $opencodeExe -Arguments @('service', 'start')
    Start-Sleep -Seconds 3
}

$mainPassword = ''
$passwordResult = Invoke-NativeCommand -FilePath $opencodeExe -Arguments @('service', 'get', 'password') -SuppressOutput
$mainPassword = $passwordResult.StdOut.Trim()

$exportFile = Join-Path $script:RuntimeRoot 'auth-export.tmp.json'
$serverProcess = $null

try {
    if ($mainPassword) {
        $env:OPENCODE_PASSWORD = $mainPassword
    }

    Write-Host 'Экспортирую credentials opencode-go из основного OpenCode...'
    $exportResult = Invoke-NativeCommand -FilePath $opencodeExe -Arguments @('auth', 'export', 'opencode-go') -SuppressOutput
    $exportJson = $exportResult.StdOut
    if ($exportResult.ExitCode -ne 0 -or -not $exportJson -or -not $exportJson.Trim()) {
        throw 'Не удалось экспортировать credentials opencode-go. Проверьте, что обычный opencode авторизован.'
    }
    Write-Utf8NoBom $exportFile $exportJson

    $serverProcess = Start-SandboxServer $apiUrl
    Set-SandboxEnvironment

    Write-Host 'Жду загрузки агента revit и MCP-сервера...'
    if (-not (Wait-SandboxReady $apiUrl 90)) {
        throw "Агент revit или MCP-сервер не загрузились за 90 секунд. Проверьте логи в $($script:LogsDir)."
    }

    Write-Host 'Импортирую credentials в песочницу...'
    $importResult = Invoke-NativeCommand -FilePath $opencodeExe -Arguments @('auth', 'import', $exportFile, '--server', $apiUrl)
    if ($importResult.ExitCode -ne 0) {
        throw 'Импорт credentials в песочницу завершился с ошибкой.'
    }

    $models = (Invoke-SandboxApi $apiUrl 'GET' '/api/model').data
    $model = $models |
        Where-Object { $_.providerID -eq 'opencode-go' -and $_.modelID -eq 'deepseek-v4.1-flash' } |
        Select-Object -First 1
    if (-not $model) {
        throw 'После импорта модель opencode-go/deepseek-v4.1-flash не появилась. Проверьте credentials.'
    }

    $agents = (Invoke-SandboxApi $apiUrl 'GET' '/api/agent').data
    $revitAgent = $agents | Where-Object { $_.name -eq 'revit' } | Select-Object -First 1
    if (-not $revitAgent) {
        throw "Агент revit не загрузился. Проверьте файл $($script:ConfigOpencode)\opencode.jsonc."
    }

    $existingSettings = $null
    if (Test-Path $script:BotSettingsFile) {
        try {
            $existingSettings = (Read-Utf8File $script:BotSettingsFile) | ConvertFrom-Json
        }
        catch {
            $existingSettings = $null
        }
    }

    $session = $null
    if ($existingSettings -and $existingSettings.currentSession -and $existingSettings.currentSession.id) {
        try {
            $session = (Invoke-SandboxApi $apiUrl 'GET' "/api/session/$($existingSettings.currentSession.id)").data
        }
        catch {
            $session = $null
        }
    }

    if (-not $session) {
        Write-Host 'Создаю стартовую сессию для Telegram...'
        $sessionBody = @{
            title    = 'Revit from Telegram'
            agent    = 'revit'
            model    = @{ providerID = 'opencode-go'; id = 'deepseek-v4.1-flash' }
            location = @{ directory = $script:Workspace }
        }
        $session = (Invoke-SandboxApi $apiUrl 'POST' '/api/session' $sessionBody).data
    }

    $sessionInfo = (Invoke-SandboxApi $apiUrl 'GET' "/api/session/$($session.id)").data
    $projectId = $sessionInfo.projectID
    $projectsResponse = Invoke-SandboxApi $apiUrl 'GET' '/api/project'
    $projects = $projectsResponse
    if ($projectsResponse -and $projectsResponse.PSObject.Properties.Name -contains 'data') {
        $projects = $projectsResponse.data
    }
    $project = $projects | Where-Object { $_.id -eq $projectId } | Select-Object -First 1
    if (-not $project) {
        throw 'Не удалось найти проект песочницы после создания сессии.'
    }

    $settings = @{}
    if ($existingSettings) {
        foreach ($property in $existingSettings.PSObject.Properties) {
            $settings[$property.Name] = $property.Value
        }
    }

    $sessionTitle = 'Revit from Telegram'
    if ($sessionInfo.title) {
        $sessionTitle = $sessionInfo.title
    }

    $settings['currentProject'] = @{
        id       = $project.id
        worktree = $project.canonical
        name     = 'Revit (Telegram)'
    }
    $settings['currentSession'] = @{
        id        = $sessionInfo.id
        title     = $sessionTitle
        directory = $project.canonical
    }
    $settings['currentAgent'] = 'revit'
    $settings['currentModel'] = @{
        providerID = 'opencode-go'
        modelID    = 'deepseek-v4.1-flash'
        variant    = 'default'
    }

    Write-Utf8NoBom $script:BotSettingsFile ($settings | ConvertTo-Json -Depth 20)

    Write-Host ''
    Write-Host 'Настройка завершена успешно.' -ForegroundColor Green
    Write-Host "Песочница: $($script:RuntimeRoot)"
    Write-Host "Сервер:    $apiUrl"
    Write-Host "Агент:     revit (только MCP revit, без файлов и shell)"
    Write-Host ''
    Write-Host 'Дальше:' -ForegroundColor Cyan
    Write-Host '1. Откройте Revit и включите в настройках плагина «Включить локальный мост».'
    Write-Host '2. Для изменения сортаментов включите «Разрешить изменение модели из чата».'
    Write-Host '3. Запустите start-telegram-bot.bat.'
}
finally {
    if ($exportFile -and (Test-Path $exportFile)) {
        Remove-Item $exportFile -Force -ErrorAction SilentlyContinue
    }
    if ($serverProcess -or (Test-SandboxHealth $apiUrl)) {
        Stop-SandboxServer $apiUrl
    }
}
