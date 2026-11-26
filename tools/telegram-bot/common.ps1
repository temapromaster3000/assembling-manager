$ErrorActionPreference = 'Stop'

try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
}
catch {
    # Старые консоли могут не поддерживать UTF-8; сообщения всё равно останутся читаемыми.
}

# Общие пути и функции для настройки/запуска Telegram-бота.
# Скрипты рассчитаны на PowerShell 5.1 (Windows 10/11).

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:RuntimeRoot = Join-Path $env:LOCALAPPDATA 'AssemblingManager\TelegramBot'
$script:Workspace = Join-Path $script:RuntimeRoot 'workspace'
$script:ConfigRoot = Join-Path $script:RuntimeRoot 'config'
$script:ConfigOpencode = Join-Path $script:ConfigRoot 'opencode'
$script:DataRoot = Join-Path $script:RuntimeRoot 'data'
$script:DataOpencode = Join-Path $script:DataRoot 'opencode'
$script:StateRoot = Join-Path $script:RuntimeRoot 'state'
$script:StateOpencode = Join-Path $script:StateRoot 'opencode'
$script:BotHome = Join-Path $script:RuntimeRoot 'bot'
$script:LogsDir = Join-Path $script:RuntimeRoot 'logs'
$script:PasswordFile = Join-Path $script:RuntimeRoot 'server-password.txt'
$script:BotEnvFile = Join-Path $script:BotHome '.env'
$script:BotSettingsFile = Join-Path $script:BotHome 'settings.json'
$script:PidFile = Join-Path $script:RuntimeRoot 'server.pid'

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )
    $parent = Split-Path $Path -Parent
    if ($parent) {
        Ensure-Directory $parent
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Read-Utf8File {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Get-OpenCodeExe {
    # Start-Process не умеет запускать .ps1-обёртку, поэтому ищем настоящий exe.
    $npmExe = Join-Path $env:APPDATA 'npm\node_modules\@opencode\cli\bin\opencode.exe'
    if (Test-Path $npmExe) {
        return (Resolve-Path $npmExe).Path
    }
    $command = Get-Command 'opencode' -ErrorAction SilentlyContinue
    if ($command -and (Test-Path $command.Source)) {
        return $command.Source
    }
    throw 'opencode не найден. Установите OpenCode V2 и повторите.'
}

function Get-OpenCodeCommand {
    $command = Get-Command 'opencode' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    throw 'Команда opencode не найдена в PATH.'
}

function Get-BotCommand {
    $command = Get-Command 'opencode-telegram' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    Write-Host 'Telegram-бот не установлен. Устанавливаю @grinev/opencode-telegram-bot...'
    $npmCommand = Get-Command 'npm' -ErrorAction SilentlyContinue
    if (-not $npmCommand) {
        throw 'npm не найден. Установите Node.js 22+ и повторите.'
    }
    $installResult = Invoke-NativeCommand -FilePath $npmCommand.Source -Arguments @('install', '-g', '@grinev/opencode-telegram-bot@latest')
    if ($installResult.ExitCode -ne 0) {
        throw 'npm install завершился с ошибкой.'
    }

    $command = Get-Command 'opencode-telegram' -ErrorAction SilentlyContinue
    if (-not $command) {
        throw 'Команда opencode-telegram не найдена после установки. Перезапустите окно терминала и повторите.'
    }
    return $command.Source
}

function Get-McpServerExe {
    $installed = Join-Path $env:APPDATA 'AssemblingManager\McpServer\AssemblingManager.McpServer.exe'
    if (Test-Path $installed) {
        return (Resolve-Path $installed).Path
    }

    $dev = Join-Path $script:RepoRoot 'src\AssemblingManager.McpServer\bin\Debug\net8.0\AssemblingManager.McpServer.exe'
    if (Test-Path $dev) {
        return (Resolve-Path $dev).Path
    }

    throw 'MCP-сервер не найден. Соберите и разверните плагин через build-and-deploy.bat или соберите src\AssemblingManager.McpServer.'
}

function New-RandomPassword {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-ServerPassword {
    if (-not (Test-Path $script:PasswordFile)) {
        Write-Utf8NoBom $script:PasswordFile (New-RandomPassword)
    }
    $password = (Read-Utf8File $script:PasswordFile).Trim()
    if (-not $password) {
        throw "Файл пароля сервера пуст: $script:PasswordFile. Удалите его и повторите настройку."
    }
    return $password
}

function Get-SandboxApiUrl {
    if (-not (Test-Path $script:BotEnvFile)) {
        return $null
    }
    $content = Read-Utf8File $script:BotEnvFile
    foreach ($line in ($content -split "`r?`n")) {
        if ($line -match '^\s*OPENCODE_API_URL\s*=\s*(.+?)\s*$') {
            return $Matches[1].Trim()
        }
    }
    return $null
}

function Set-SandboxEnvironment {
    $env:XDG_CONFIG_HOME = $script:ConfigRoot
    $env:XDG_DATA_HOME = $script:DataRoot
    $env:XDG_STATE_HOME = $script:StateRoot
    $env:OPENCODE_TELEGRAM_HOME = $script:BotHome
    $env:OPENCODE_SERVER_PASSWORD = Get-ServerPassword
    $env:OPENCODE_PASSWORD = $env:OPENCODE_SERVER_PASSWORD
}

function Get-AuthHeaders {
    $pair = 'opencode:' + (Get-ServerPassword)
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes($pair))
    return @{ Authorization = 'Basic ' + $encoded }
}

function Test-SandboxHealth {
    param([string]$ApiUrl)
    try {
        $info = Invoke-RestMethod -Uri "$ApiUrl/api/info" -Headers (Get-AuthHeaders) -TimeoutSec 2
        return ($null -ne $info.version)
    }
    catch {
        return $false
    }
}

function Wait-SandboxHealth {
    param(
        [string]$ApiUrl,
        [int]$TimeoutSec = 60
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-SandboxHealth $ApiUrl) {
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Wait-SandboxReady {
    # /api/info отвечает раньше, чем загрузятся конфиг, агенты и MCP.
    # Ждём, пока в песочнице появятся агент revit и подключённый MCP-сервер revit.
    param(
        [string]$ApiUrl,
        [int]$TimeoutSec = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        try {
            $agentsResponse = Invoke-SandboxApi $ApiUrl 'GET' '/api/agent'
            $agents = $agentsResponse.data
            $hasRevitAgent = $null -ne ($agents | Where-Object { $_.name -eq 'revit' } | Select-Object -First 1)

            $mcpResponse = Invoke-SandboxApi $ApiUrl 'GET' '/api/mcp'
            $mcpServers = $mcpResponse.data
            $revitMcp = $mcpServers | Where-Object { $_.name -eq 'revit' } | Select-Object -First 1
            $mcpConnected = $revitMcp -and $revitMcp.status -and $revitMcp.status.status -eq 'connected'

            if ($hasRevitAgent -and $mcpConnected) {
                return $true
            }
        }
        catch {
            # Сервер ещё загружается.
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Test-PortFree {
    param([int]$Port)
    $listener = $null
    try {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $Port)
        $listener.Start()
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($listener) {
            $listener.Stop()
        }
    }
}

function Find-FreePort {
    param([int]$Start = 49375)
    for ($port = $Start; $port -lt ($Start + 50); $port++) {
        if (Test-PortFree $port) {
            return $port
        }
    }
    throw "Не удалось найти свободный порт в диапазоне $Start..$($Start + 49)."
}

function Start-SandboxServer {
    param([string]$ApiUrl)

    if (Test-SandboxHealth $ApiUrl) {
        Write-Host "OpenCode-сервер уже запущен: $ApiUrl"
        return $null
    }

    $uri = [Uri]$ApiUrl
    $port = $uri.Port
    if (-not (Test-PortFree $port)) {
        throw "Порт $port занят другим процессом. Закройте его или удалите $script:BotEnvFile и повторите настройку."
    }

    Set-SandboxEnvironment
    Ensure-Directory $script:LogsDir

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $outLog = Join-Path $script:LogsDir "opencode-server-$stamp.out.log"
    $errLog = Join-Path $script:LogsDir "opencode-server-$stamp.err.log"
    $exe = Get-OpenCodeExe

    $process = Start-Process -FilePath $exe `
        -ArgumentList @('serve', '--hostname', '127.0.0.1', '--port', "$port") `
        -WorkingDirectory $script:Workspace `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError $errLog

    Write-Utf8NoBom $script:PidFile "$($process.Id)"

    if (-not (Wait-SandboxHealth $ApiUrl 60)) {
        $details = ''
        if (Test-Path $outLog) {
            $details += ((Get-Content $outLog -Tail 20) -join "`n")
        }
        if (Test-Path $errLog) {
            $details += "`n" + ((Get-Content $errLog -Tail 20) -join "`n")
        }
        throw "OpenCode-сервер не запустился на $ApiUrl.`nЛог: $outLog`n$details"
    }

    Write-Host "OpenCode-сервер запущен: $ApiUrl"
    return $process
}

function Stop-SandboxServer {
    param([string]$ApiUrl)

    if (-not $ApiUrl) {
        return
    }

    $port = ([Uri]$ApiUrl).Port
    $owners = @()
    try {
        $owners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique
    }
    catch {
        $owners = @()
    }

    foreach ($ownerPid in $owners) {
        if ($ownerPid -and $ownerPid -ne $PID) {
            Stop-Process -Id $ownerPid -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path $script:PidFile) {
        $pidText = (Read-Utf8File $script:PidFile).Trim()
        if ($pidText -match '^\d+$') {
            Stop-Process -Id ([int]$pidText) -Force -ErrorAction SilentlyContinue
        }
        Remove-Item $script:PidFile -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SandboxApi {
    param(
        [string]$ApiUrl,
        [string]$Method,
        [string]$Path,
        $Body = $null
    )

    $request = @{
        Method     = $Method
        Uri        = "$ApiUrl$Path"
        Headers    = (Get-AuthHeaders)
        TimeoutSec = 120
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 12
        $request.Body = [System.Text.Encoding]::UTF8.GetBytes($json)
        $request.ContentType = 'application/json; charset=utf-8'
    }

    return Invoke-RestMethod @request
}

function Invoke-NativeCommand {
    # Запускает native-команду, не превращая её stderr в terminating error PowerShell.
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [switch]$SuppressOutput
    )

    $outFile = [System.IO.Path]::GetTempFileName()
    $errFile = [System.IO.Path]::GetTempFileName()
    try {
        $process = Start-Process -FilePath $FilePath `
            -ArgumentList $Arguments `
            -Wait `
            -PassThru `
            -NoNewWindow `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError $errFile

        $stdout = Read-Utf8File $outFile
        $stderr = Read-Utf8File $errFile

        if (-not $SuppressOutput) {
            if ($stdout.Trim()) {
                Write-Host $stdout.TrimEnd()
            }
            if ($stderr.Trim() -and $process.ExitCode -ne 0) {
                Write-Host $stderr.TrimEnd() -ForegroundColor Red
            }
        }

        return @{
            ExitCode = $process.ExitCode
            StdOut   = $stdout
            StdErr   = $stderr
        }
    }
    finally {
        Remove-Item $outFile, $errFile -Force -ErrorAction SilentlyContinue
    }
}
