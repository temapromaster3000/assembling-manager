param(
    [string]$Token,
    [string]$UserId
)

$ErrorActionPreference = 'Stop'

if (-not $Token) {
    Write-Host 'Токен бота создаётся в Telegram у @BotFather: /newbot -> скопируйте токен.'
    $Token = Read-Host 'Вставьте токен бота'
}
$Token = $Token.Trim()

if (-not $UserId) {
    Write-Host 'Свой числовой Telegram ID можно узнать у @userinfobot: отправьте ему любое сообщение.'
    $UserId = Read-Host 'Введите ваш Telegram ID'
}
$UserId = $UserId.Trim()

if ($Token -notmatch '^\d{6,}:[A-Za-z0-9_-]{20,}$') {
    throw 'Токен не похож на токен Telegram-бота. Скопируйте его из BotFather целиком.'
}
if ($UserId -notmatch '^\d+$') {
    throw 'Telegram ID должен состоять только из цифр.'
}

[Environment]::SetEnvironmentVariable('TELEGRAM_BOT_TOKEN', $Token, 'User')
[Environment]::SetEnvironmentVariable('TELEGRAM_ALLOWED_USER_ID', $UserId, 'User')

# Для текущего процесса, чтобы можно было сразу проверить запуск.
$env:TELEGRAM_BOT_TOKEN = $Token
$env:TELEGRAM_ALLOWED_USER_ID = $UserId

Write-Host ''
Write-Host 'Готово. Токен и ID сохранены в переменных окружения пользователя.'
Write-Host 'Токен в файлы репозитория не записывается.'
Write-Host "Разрешённый Telegram ID: $UserId"
