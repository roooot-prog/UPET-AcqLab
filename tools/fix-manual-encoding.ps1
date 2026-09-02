# Fix mojibake + strip Romanian diacritics from UPET AcqLab manuals.
$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function S([int[]]$codes) {
  return -join ($codes | ForEach-Object { [char]$_ })
}

function Repair-RoText([string]$text) {
  if ($null -eq $text) { return $text }

  $map = [ordered]@{}
  $map[(S 0x00C8,0x02DC)] = "S"          # È˜ <- Ș
  $map[(S 0x00C8,0x2122)] = "s"          # È™ <- ș
  $map[(S 0x00C8,0x007E)] = "S"          # È~
  $map[(S 0x00C8,0x203A)] = "t"          # È› <- ț
  $map[(S 0x00C4,0x0192)] = "a"          # Äƒ <- ă
  $map[(S 0x00C3,0x017D)] = "I"          # ÃŽ <- Î
  $map[(S 0x00C3,0x00AE)] = "i"          # Ã® <- î
  $map[(S 0x00C3,0x00A2)] = "a"          # Ã¢ <- â
  $map[(S 0x00C3,0x0082)] = "A"          # Ã‚ <- Â
  $map[(S 0x00C5,0x015F)] = "s"          # ÅŸ sometimes
  $map[(S 0x00C5,0x017E)] = "s"
  $map[(S 0x00C5,0x0163)] = "t"
  $map[(S 0x00E2,0x20AC,0x201D)] = "-"   # â€
  $map[(S 0x00E2,0x20AC,0x201C)] = "-"
  $map[(S 0x00E2,0x20AC,0x2013)] = "-"
  $map[(S 0x00E2,0x20AC,0x2014)] = "-"
  $map[(S 0x00E2,0x20AC,0x2018)] = "'"
  $map[(S 0x00E2,0x20AC,0x2019)] = "'"
  $map[(S 0x0061,0x20AC,0x201D)] = "-"   # a€”
  $map[(S 0x0061,0x20AC,0x009D)] = "-"
  $map[(S 0x0061,0x20AC,0x0093)] = "-"
  $map[(S 0x0061,0x20AC,0x0094)] = "-"
  $map[(S 0x0041,0x00B7)] = "-"          # A·
  $map[(S 0x00C2,0x00B7)] = "-"          # Â·
  $map[(S 0x00C2)] = ""                  # lone Â
  $map[(S 0xFFFD)] = ""

  foreach ($k in @($map.Keys)) {
    $text = $text.Replace($k, [string]$map[$k])
  }

  # Real Romanian diacritics -> ASCII
  $dia = @{
    0x0103 = "a"; 0x00E2 = "a"; 0x00EE = "i"; 0x0219 = "s"; 0x021B = "t"
    0x0102 = "A"; 0x00C2 = "A"; 0x00CE = "I"; 0x0218 = "S"; 0x021A = "T"
    0x015F = "s"; 0x0163 = "t"; 0x015E = "S"; 0x0162 = "T"
  }
  $sb = New-Object System.Text.StringBuilder ($text.Length)
  foreach ($ch in $text.ToCharArray()) {
    $code = [int][char]$ch
    if ($dia.ContainsKey($code)) { [void]$sb.Append($dia[$code]) }
    else { [void]$sb.Append($ch) }
  }
  return $sb.ToString()
}

$desktop = [Environment]::GetFolderPath("Desktop")

$cleanCover = @"
<div class="cover">
  <img src="file:///C:/Users/acer/Desktop/Spider8DAQ/Spider8DAQ.App/Assets/upet-logo.png" alt="Universitatea din Petrosani"/>
  <div class="uni">UNIVERSITATEA DIN PETROSANI</div>
  <h1>UPET AcqLab</h1>
  <div class="sub">Manual de Utilizare</div>
  <div class="sub">Laborator achizitie / analiza date - HBM Spider8</div>
  <div class="meta">
    <p><strong>EXPERT:</strong> Sef lucr.dr.ing. VILCEANU Florin</p>
    <p><strong>Versiune aplicatie:</strong> 3.3.11 &nbsp;|&nbsp; <strong>Document:</strong> 1.0 (2026)</p>
    <p>Facultatea de Inginerie Mecanica si Electrica</p>
    <p>Departamentul de Inginerie Mecanica, Industriala si Transporturi</p>
  </div>
</div>
"@

$htmlPaths = @(
  (Join-Path $desktop "UPET-AcqLab-Manual-Utilizare.html"),
  (Join-Path $desktop "UPET-AcqLab-Installer\Documentatie\UPET-AcqLab-Manual-Utilizare.html")
) | Where-Object { Test-Path $_ }

foreach ($path in $htmlPaths) {
  $fixed = Repair-RoText ([IO.File]::ReadAllText($path, $utf8NoBom))
  $fixed = [regex]::Replace($fixed, '(?s)<div class="cover">.*?</div>\s*(?=<p><strong>Universitatea)', ($cleanCover.TrimEnd() + "`r`n"))
  $fixed = [regex]::Replace($fixed, '<title>.*?</title>', '<title>Manual de Utilizare - UPET AcqLab</title>')
  [IO.File]::WriteAllText($path, $fixed, $utf8NoBom)

  $check = [IO.File]::ReadAllText($path, $utf8NoBom)
  $bad = 0
  foreach ($p in @((S 0x00C8,0x02DC), (S 0x00C8,0x203A), (S 0x00C4,0x0192), (S 0x00C3,0x017D), ("a" + [char]0x20AC))) {
    $bad += ([regex]::Matches($check, [regex]::Escape($p))).Count
  }
  $diaLeft = [regex]::Matches($check, "[\u0103\u00E2\u00EE\u0219\u021B\u0102\u00CE\u0218\u021A\u015F\u0163\u015E\u0162]").Count
  Write-Output ("HTML: {0} | mojibakeLeft={1} diaLeft={2}" -f $path, $bad, $diaLeft)
}

$mdPaths = @(
  (Join-Path $desktop "UPET-AcqLab-Manual-Utilizare.md"),
  (Join-Path $desktop "UPET-AcqLab-Installer\Documentatie\UPET-AcqLab-Manual-Utilizare.md")
) | Where-Object { Test-Path $_ }
foreach ($path in $mdPaths) {
  $t = Repair-RoText ([IO.File]::ReadAllText($path, $utf8NoBom))
  [IO.File]::WriteAllText($path, $t, $utf8NoBom)
  Write-Output ("MD: {0}" -f $path)
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$docxPaths = @(
  (Join-Path $desktop "UPET-AcqLab-Manual-Utilizare-fara-diacritice.docx"),
  (Join-Path $desktop "UPET-AcqLab-Installer\Documentatie\UPET-AcqLab-Manual-Utilizare.docx"),
  (Join-Path $desktop "UPET-AcqLab-Manual-Utilizare.docx")
) | Where-Object { Test-Path $_ }

foreach ($docx in $docxPaths) {
  $tmp = Join-Path $env:TEMP ("docx_fix_" + [guid]::NewGuid().ToString("N"))
  $extract = Join-Path $tmp "x"
  $copy = Join-Path $tmp "in.docx"
  $out = Join-Path $tmp "out.docx"
  New-Item -ItemType Directory -Path $extract -Force | Out-Null
  Copy-Item $docx $copy -Force
  [IO.Compression.ZipFile]::ExtractToDirectory($copy, $extract)
  Get-ChildItem $extract -Recurse -Filter *.xml | ForEach-Object {
    $t = Repair-RoText ([IO.File]::ReadAllText($_.FullName, $utf8NoBom))
    [IO.File]::WriteAllText($_.FullName, $t, $utf8NoBom)
  }
  [IO.Compression.ZipFile]::CreateFromDirectory($extract, $out, [IO.Compression.CompressionLevel]::Optimal, $false)
  try {
    Copy-Item $out $docx -Force
    Write-Output ("DOCX OK: {0}" -f $docx)
  } catch {
    $alt = $docx -replace "\.docx$", "-clean.docx"
    Copy-Item $out $alt -Force
    Write-Output ("DOCX LOCKED -> {0}" -f $alt)
  }
  Remove-Item $tmp -Recurse -Force
}

Write-Output "---- COVER ----"
Get-Content (Join-Path $desktop "UPET-AcqLab-Manual-Utilizare.html") -Encoding UTF8 |
  Select-String -Pattern "class=`"uni`"|EXPERT|achizi|Mecanic|<title>|PETRO" |
  Select-Object -First 12 |
  ForEach-Object { $_.Line.Trim() }
