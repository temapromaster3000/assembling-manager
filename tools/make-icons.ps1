<#
.SYNOPSIS
    Генерирует иконки кнопок ленты плагина Assembling Manager (16 и 32 px, PNG, прозрачный фон).

.DESCRIPTION
    Иконки рисуются программно (System.Drawing) в единой гамме: синий #0078D4 + серый + белый.
    Все координаты задаются в пространстве 32x32 и масштабируются для 16 px.
    Запуск:  powershell -File tools\make-icons.ps1
    Параметр -PreviewPath позволяет дополнительно сохранить контактный лист для визуальной проверки.
#>
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\src\AssemblingManager.Revit\Resources"),
    [string]$PreviewPath = ""
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# --- Единая палитра ---
$script:Blue      = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)    # #0078D4
$script:DarkBlue  = [System.Drawing.Color]::FromArgb(255, 0, 90, 158)     # #005A9E
$script:LightBlue = [System.Drawing.Color]::FromArgb(255, 179, 215, 242)  # #B3D7F2
$script:Gray      = [System.Drawing.Color]::FromArgb(255, 138, 150, 160)  # #8A96A0
$script:DarkGray  = [System.Drawing.Color]::FromArgb(255, 90, 107, 123)   # #5A6B7B
$script:White     = [System.Drawing.Color]::White

function New-Brush([System.Drawing.Color]$color) {
    return New-Object System.Drawing.SolidBrush($color)
}

function New-Pen([System.Drawing.Color]$color, [single]$width) {
    $pen = New-Object System.Drawing.Pen($color, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Fill-Polygon([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, [object[]]$points) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'System.Drawing.PointF[]' $points.Count
    for ($i = 0; $i -lt $points.Count; $i++) {
        $pts[$i] = New-Object System.Drawing.PointF([single]$points[$i][0], [single]$points[$i][1])
    }
    $path.AddPolygon($pts)
    $g.FillPath((New-Brush $color), $path)
    $path.Dispose()
}

function Punch-Hole([System.Drawing.Graphics]$g, [single]$cx, [single]$cy, [single]$r) {
    $old = $g.CompositingMode
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $transparent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)
    $g.FillEllipse($transparent, $cx - $r, $cy - $r, $r * 2, $r * 2)
    $g.CompositingMode = $old
}

# --- Настройки: шестерёнка ---
function Draw-Gear([System.Drawing.Graphics]$g) {
    $cx = 16.0; $cy = 16.0
    $bodyR = 7.5
    $toothW = 5.0
    $toothH = 4.0
    $blue = New-Brush $script:Blue

    for ($i = 0; $i -lt 8; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($cx, $cy)
        $g.RotateTransform($i * 45)
        $toothX = [single](-($toothW / 2))
        $toothY = [single](-($bodyR + $toothH - 0.5))
        $g.FillRectangle($blue, $toothX, $toothY, [single]$toothW, [single]($toothH + 0.5))
        $g.Restore($state)
    }

    $g.FillEllipse($blue, $cx - $bodyR, $cy - $bodyR, $bodyR * 2, $bodyR * 2)
    Punch-Hole $g $cx $cy 3.5
}

# --- Сформировать виды: изометрический 3D-куб ---
function Draw-Cube([System.Drawing.Graphics]$g) {
    Fill-Polygon $g $script:LightBlue @(
        @(16, 5), @(27, 10.5), @(16, 16), @(5, 10.5)
    )
    Fill-Polygon $g $script:DarkBlue @(
        @(5, 10.5), @(16, 16), @(16, 27), @(5, 21.5)
    )
    Fill-Polygon $g $script:Blue @(
        @(16, 16), @(27, 10.5), @(27, 21.5), @(16, 27)
    )
}

# --- Переименовать виды: карандаш ---
function Draw-Rename([System.Drawing.Graphics]$g) {
    # Карандаш рисуется вертикально, затем наклоняется на 45 градусов.
    $state = $g.Save()
    $g.TranslateTransform(15, 14)
    $g.RotateTransform(45)

    $blue = New-Brush $script:Blue
    $dark = New-Brush $script:DarkBlue
    $wood = New-Brush $script:LightBlue

    $g.FillRectangle($blue, -3.5, -12, 7, 19)                       # корпус
    $g.FillRectangle($dark, -3.5, -14, 7, 2.5)                      # ластик
    Fill-Polygon $g $script:LightBlue @(@(-3.5, 7), @(3.5, 7), @(0, 12.5))   # заточка
    Fill-Polygon $g $script:DarkBlue @(@(-1.2, 11), @(1.2, 11), @(0, 13.2))  # грифель
    $g.Restore($state)
}

# --- Разместить на листах: лист с рамкой вида ---
function Draw-PlaceSheets([System.Drawing.Graphics]$g) {
    $white = New-Brush $script:White
    $blue = New-Brush $script:Blue
    $light = New-Brush $script:LightBlue
    $grayPen = New-Pen $script:DarkGray 2
    $bluePen = New-Pen $script:DarkBlue 2

    $g.FillRectangle($white, 4, 3, 24, 26)                 # лист
    $g.DrawRectangle($grayPen, 4, 3, 24, 26)
    $g.FillRectangle($blue, 5, 4, 22, 4.5)                 # заголовок листа
    $g.FillRectangle($light, 8, 13, 16, 12)                # рамка вида
    $g.DrawRectangle($bluePen, 8, 13, 16, 12)
}

# --- Сортировка листов: строки + стрелки вверх/вниз ---
function Draw-SortSheets([System.Drawing.Graphics]$g, [int]$size) {
    $gray = New-Brush $script:Gray
    $blue = New-Brush $script:Blue

    if ($size -le 16) {
        $g.FillRectangle($gray, 2, 3, 10, 3)
        $g.FillRectangle($gray, 2, 7, 10, 3)
        $g.FillRectangle($gray, 2, 11, 10, 3)
        $g.FillRectangle($blue, 14.5, 3, 3, 5)                                  # стрелка вниз
        Fill-Polygon $g $script:Blue @(@(12.5, 8), @(19.5, 8), @(16, 13))
        return
    }

    $g.FillRectangle($gray, 3, 7, 15, 4)
    $g.FillRectangle($gray, 3, 14, 15, 4)
    $g.FillRectangle($gray, 3, 21, 15, 4)

    Fill-Polygon $g $script:Blue @(@(20, 9.5), @(26.5, 9.5), @(23.25, 4.5))    # стрелка вверх
    $g.FillRectangle($blue, 21.75, 9.5, 3, 6.5)
    Fill-Polygon $g $script:Blue @(@(20, 22.5), @(26.5, 22.5), @(23.25, 27.5)) # стрелка вниз
    $g.FillRectangle($blue, 21.75, 16, 3, 6.5)
}

# --- Проставить позиции: нумерованный список ---
function Draw-Positions([System.Drawing.Graphics]$g, [int]$size) {
    $blue = New-Brush $script:Blue
    $gray = New-Brush $script:Gray
    $white = New-Brush $script:White

    $tops = @(2.0, 12.0, 22.0)

    foreach ($top in $tops) {
        $chipTop = [single]$top
        $g.FillEllipse($blue, 3.5, $chipTop, 8, 8)
        $g.FillRectangle($gray, 15, [single]($top + 2.5), 13, 3)
    }

    if ($size -ge 32) {
        $font = New-Object System.Drawing.Font('Segoe UI', 6.5, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $labels = @('1', '2', '3')
        for ($i = 0; $i -lt $tops.Count; $i++) {
            $rect = New-Object System.Drawing.RectangleF([single]3.5, [single]$tops[$i], [single]8, [single]8)
            $g.DrawString($labels[$i], $font, $white, $rect, $format)
        }
        $font.Dispose()
        $format.Dispose()
    }
}

# --- Сборка иконки ---
function New-Icon([string]$kind, [int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::Transparent)

        $s = $size / 32.0
        $g.ScaleTransform([single]$s, [single]$s)

        switch ($kind) {
            'Settings'    { Draw-Gear $g }
            'CreateViews' { Draw-Cube $g }
            'Rename'      { Draw-Rename $g }
            'PlaceSheets' { Draw-PlaceSheets $g }
            'SortSheets'  { Draw-SortSheets $g $size }
            'Positions'   { Draw-Positions $g $size }
            default       { throw "Unknown icon kind: $kind" }
        }
    }
    finally {
        $g.Dispose()
    }
    return $bmp
}

# --- Генерация ---
$kinds = @('Settings', 'CreateViews', 'Rename', 'PlaceSheets', 'SortSheets', 'Positions')
$sizes = @(16, 32)

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$generated = @()
foreach ($kind in $kinds) {
    foreach ($size in $sizes) {
        $bmp = New-Icon $kind $size
        $file = Join-Path $OutputDir ("{0}{1}.png" -f $kind, $size)
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()

        # контроль: перечитать и проверить размер
        $check = [System.Drawing.Image]::FromFile($file)
        if ($check.Width -ne $size -or $check.Height -ne $size) {
            $check.Dispose()
            throw "Size mismatch for $file"
        }
        $check.Dispose()
        $generated += $file
        Write-Host ("OK  {0}" -f $file)
    }
}

Write-Host ("Generated {0} icons in {1}" -f $generated.Count, $OutputDir)

# --- Контактный лист для визуальной проверки ---
if ($PreviewPath) {
    $cell = 250
    $scale = 7
    $preview = [System.Drawing.Bitmap]::new(($cell * $kinds.Count + 40), ($cell * 2 + 60))
    $pg = [System.Drawing.Graphics]::FromImage($preview)
    try {
        $pg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $pg.Clear([System.Drawing.Color]::FromArgb(255, 240, 240, 240))
        $labelFont = New-Object System.Drawing.Font('Segoe UI', 11)
        $labelBrush = New-Brush $script:DarkGray
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center

        for ($row = 0; $row -lt 2; $row++) {
            for ($col = 0; $col -lt $kinds.Count; $col++) {
                $kind = $kinds[$col]
                $srcSize = 32
                $iconPath = Join-Path $OutputDir ("{0}32.png" -f $kind)
                if ($row -eq 1) {
                    $srcSize = 16
                    $iconPath = Join-Path $OutputDir ("{0}16.png" -f $kind)
                }

                $icon = [System.Drawing.Image]::FromFile($iconPath)
                $drawn = $srcSize * $scale
                $cx = 20 + $col * $cell + ($cell - $drawn) / 2
                $cy = 20 + $row * $cell + ($cell - $drawn) / 2
                $pg.DrawImage($icon,
                    (New-Object System.Drawing.Rectangle([int]$cx, [int]$cy, $drawn, $drawn)),
                    0, 0, $srcSize, $srcSize,
                    [System.Drawing.GraphicsUnit]::Pixel)
                $icon.Dispose()

                $lx = 20 + $col * $cell
                $ly = 20 + $row * $cell + $cell - 30
                $labelRect = New-Object System.Drawing.RectangleF([single]$lx, [single]$ly, [single]$cell, [single]24)
                $pg.DrawString($kind, $labelFont, $labelBrush, $labelRect, $format)
            }
        }

        $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host ("Preview saved: {0}" -f $PreviewPath)
    }
    finally {
        $pg.Dispose()
        $preview.Dispose()
    }
}
