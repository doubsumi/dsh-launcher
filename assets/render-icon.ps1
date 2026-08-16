# Render the official DeepSeek Harness favicon (black whale) to PNG and build a multi-size .ico.
# Uses only System.Drawing (built into Windows PowerShell) - no browser needed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$svgFile = Join-Path $dir 'gui-favicon.svg'
$pngOut  = Join-Path $dir 'deepseek-whale-1024.png'
$icoOut  = Join-Path $dir 'deepseek-whale.ico'

# ---------- 1) extract path data ----------
$svg = Get-Content -Raw $svgFile
$d = [regex]::Match($svg, '\sd="([^"]+)"').Groups[1].Value
if (-not $d) { throw 'Could not extract path data from SVG' }

# ---------- 2) parse path into GraphicsPath ----------
$pattern = '[MmZzLlHhVvCcSsQqTtAa]|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?'
$tokens = [regex]::Matches($d, $pattern) | ForEach-Object { $_.Value }
$i = 0
$n = $tokens.Count

$x = 0.0; $y = 0.0; $sx = 0.0; $sy = 0.0
$lastCmd = ''
$ctrlX = 0.0; $ctrlY = 0.0   # previous cubic control point (for S/T if ever used)
$gp = New-Object System.Drawing.Drawing2D.GraphicsPath
$started = $false

function Add-Pt([double]$nx, [double]$ny) {
    $script:x = $nx; $script:y = $ny
}

function Read-Num {
    if ($script:i -ge $script:n) { throw 'Path ended unexpectedly' }
    $v = [double]$script:tokens[$script:i]
    $script:i++
    return $v
}

while ($i -lt $n) {
    $tok = $tokens[$i]
    if ($tok -match '^[A-Za-z]$') { $cmd = $tok; $i++ } else { $cmd = $lastCmd }
    if (-not $cmd) { throw 'Path does not start with a command' }
    $rel = ($cmd -ceq $cmd.ToLower()) -and ($cmd.ToUpper() -ne 'Z')
    switch ($cmd.ToUpper()) {
        'M' {
            $x2 = Read-Num; $y2 = Read-Num
            if ($rel) { $x += $x2; $y += $y2 } else { $x = $x2; $y = $y2 }
            if ($started) { $gp.StartFigure() } else { $started = $true }
            $sx = $x; $sy = $y
        }
        'L' {
            $x2 = Read-Num; $y2 = Read-Num
            if ($rel) { $x2 += $x; $y2 += $y }
            $gp.AddLine($x, $y, $x2, $y2)
            $x = $x2; $y = $y2
        }
        'H' {
            $x2 = Read-Num; if ($rel) { $x2 += $x }
            $gp.AddLine($x, $y, $x2, $y); $x = $x2
        }
        'V' {
            $y2 = Read-Num; if ($rel) { $y2 += $y }
            $gp.AddLine($x, $y, $x, $y2); $y = $y2
        }
        'C' {
            $x1 = Read-Num; $y1 = Read-Num; $x2 = Read-Num; $y2 = Read-Num; $x3 = Read-Num; $y3 = Read-Num
            if ($rel) { $x1 += $x; $y1 += $y; $x2 += $x; $y2 += $y; $x3 += $x; $y3 += $y }
            $gp.AddBezier($x, $y, $x1, $y1, $x2, $y2, $x3, $y3)
            $ctrlX = $x2; $ctrlY = $y2
            $x = $x3; $y = $y3
        }
        'S' {
            $x2 = Read-Num; $y2 = Read-Num; $x3 = Read-Num; $y3 = Read-Num
            if ($rel) { $x2 += $x; $y2 += $y; $x3 += $x; $y3 += $y }
            if ($lastCmd -eq 'C' -or $lastCmd -eq 'S') { $x1 = 2 * $x - $ctrlX; $y1 = 2 * $y - $ctrlY } else { $x1 = $x; $y1 = $y }
            $gp.AddBezier($x, $y, $x1, $y1, $x2, $y2, $x3, $y3)
            $ctrlX = $x2; $ctrlY = $y2
            $x = $x3; $y = $y3
        }
        'Q' {
            $x1 = Read-Num; $y1 = Read-Num; $x2 = Read-Num; $y2 = Read-Num
            if ($rel) { $x1 += $x; $y1 += $y; $x2 += $x; $y2 += $y }
            $gp.AddBezier($x, $y, $x + 2 / 3 * ($x1 - $x), $y + 2 / 3 * ($y1 - $y), $x2 + 2 / 3 * ($x1 - $x2), $y2 + 2 / 3 * ($y1 - $y2), $x2, $y2)
            $ctrlX = $x1; $ctrlY = $y1
            $x = $x2; $y = $y2
        }
        'T' {
            $x2 = Read-Num; $y2 = Read-Num
            if ($rel) { $x2 += $x; $y2 += $y }
            if ($lastCmd -eq 'Q' -or $lastCmd -eq 'T') { $x1 = 2 * $x - $ctrlX; $y1 = 2 * $y - $ctrlY } else { $x1 = $x; $y1 = $y }
            $gp.AddBezier($x, $y, $x + 2 / 3 * ($x1 - $x), $y + 2 / 3 * ($y1 - $y), $x2 + 2 / 3 * ($x1 - $x2), $y2 + 2 / 3 * ($y1 - $y2), $x2, $y2)
            $ctrlX = $x1; $ctrlY = $y1
            $x = $x2; $y = $y2
        }
        'A' { throw 'Arc (A) not supported in this renderer - path uses unsupported command' }
        'Z' {
            $gp.CloseFigure()
            $x = $sx; $y = $sy
        }
        default { throw "Unknown command: $cmd" }
    }
    $lastCmd = $cmd
}
$gp.FillMode = [System.Drawing.Drawing2D.FillMode]::Winding   # SVG default = nonzero
Write-Output "Parsed path: $($gp.PointCount) points"

# ---------- 3) render 1024x1024 ----------
$scale = 1024.0 / 50.0
$bmp = New-Object System.Drawing.Bitmap(1024, 1024, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::Transparent)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.ScaleTransform($scale, $scale)
$g.FillPath([System.Drawing.Brushes]::Black, $gp)
$g.Dispose()
$bmp.Save($pngOut, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "Rendered: $pngOut"

# ---------- 4) verification: bounding box + color + ASCII preview ----------
$minX = 1024; $minY = 1024; $maxX = -1; $maxY = -1; $count = 0
$rSum = 0L; $gSum = 0L; $bSum = 0L
for ($yy = 0; $yy -lt 1024; $yy += 2) {
    for ($xx = 0; $xx -lt 1024; $xx += 2) {
        $p = $bmp.GetPixel($xx, $yy)
        if ($p.A -gt 40) {
            $count++
            if ($xx -lt $minX) { $minX = $xx }; if ($xx -gt $maxX) { $maxX = $xx }
            if ($yy -lt $minY) { $minY = $yy }; if ($yy -gt $maxY) { $maxY = $yy }
            $rSum += $p.R; $gSum += $p.G; $bSum += $p.B
        }
    }
}
Write-Output ("opaque px (sampled): {0}" -f $count)
if ($count -gt 0) {
    Write-Output ("bbox px: x {0}..{1}  y {2}..{3}   (viewBox units: x {4:F2}..{5:F2}  y {6:F2}..{7:F2})" -f $minX, $maxX, $minY, $maxY, ($minX / $scale), ($maxX / $scale), ($minY / $scale), ($maxY / $scale))
    Write-Output ("avg color: R={0} G={1} B={2}  (expect ~0 for black)" -f [int]($rSum / $count), [int]($gSum / $count), [int]($bSum / $count))
}
# ASCII preview (40 x 20)
Write-Output '--- ASCII preview (40x20, # = whale) ---'
for ($row = 0; $row -lt 20; $row++) {
    $line = ''
    for ($col = 0; $col -lt 40; $col++) {
        $px = $col * 1024 / 40; $py = $row * 1024 / 20
        $p = $bmp.GetPixel([int]$px, [int]$py)
        if ($p.A -gt 100) { $line += '#' } else { $line += '.' }
    }
    Write-Output $line
}

# ---------- 5) build multi-size ICO (PNG-compressed entries) ----------
$sizes = 16, 24, 32, 48, 64, 128, 256
$entries = @()
foreach ($s in $sizes) {
    $sb = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($sb)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $sg.Clear([System.Drawing.Color]::Transparent)
    $sg.DrawImage($bmp, 0, 0, $s, $s)
    $sg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $sb.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += @{ size = $s; data = $ms.ToArray() }
    $ms.Dispose(); $sb.Dispose()
}
$msOut = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($msOut)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e.size
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.data.Length)
    $bw.Write([uint32]$offset)
    $offset += $e.data.Length
}
foreach ($e in $entries) { $bw.Write($e.data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoOut, $msOut.ToArray())
$bw.Dispose(); $msOut.Dispose()
$bmp.Dispose()
Write-Output "Built multi-size ICO: $icoOut  ($((Get-Item $icoOut).Length) bytes, sizes: $($sizes -join ', '))"
