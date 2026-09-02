#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$mdPath   = 'C:\Users\acer\Desktop\UPET-AcqLab-Manual-Utilizare.md'
$htmlPath = 'C:\Users\acer\Desktop\UPET-AcqLab-Manual-Utilizare.html'
$docxPath = 'C:\Users\acer\Desktop\UPET-AcqLab-Manual-Utilizare.docx'
$logoPath = 'C:\Users\acer\Desktop\Spider8DAQ\Spider8DAQ.App\Assets\upet-logo.png'
if (-not (Test-Path $logoPath)) {
    $logoPath = 'C:\Users\acer\Desktop\Spider8DAQ\publish-v2\Assets\upet-logo.png'
}

function Escape-Html([string]$s) {
    if ($null -eq $s) { return '' }
    return ($s -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;')
}

function Format-Inline([string]$t) {
    $t = Escape-Html $t
    $t = [regex]::Replace($t, '\*\*(.+?)\*\*', '<strong>$1</strong>')
    $t = [regex]::Replace($t, '`([^`]+)`', '<code>$1</code>')
    $t = [regex]::Replace($t, '\[(.+?)\]\(([^)]+)\)', '<a href="$2">$1</a>')
    return $t
}

function Convert-MdToHtmlBody([string]$mdText) {
    $lines = $mdText -split "`r`n|`n"
    $sb = New-Object System.Text.StringBuilder
    $inTable = $false
    $inCode = $false
    $inList = $false
    $inOl = $false

    foreach ($line in $lines) {
        if ($line -match '^```') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            if ($inTable) { [void]$sb.AppendLine('</tbody></table>'); $inTable = $false }
            if (-not $inCode) {
                [void]$sb.AppendLine('<pre><code>')
                $inCode = $true
            }
            else {
                [void]$sb.AppendLine('</code></pre>')
                $inCode = $false
            }
            continue
        }
        if ($inCode) {
            [void]$sb.AppendLine((Escape-Html $line))
            continue
        }

        if ($line -match '^\|') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            if ($line -match '^\|\s*[-:| ]+\|') { continue }
            $cells = @(($line.Trim().Trim('|') -split '\|') | ForEach-Object { $_.Trim() })
            if (-not $inTable) {
                [void]$sb.AppendLine('<table><thead><tr>')
                foreach ($c in $cells) {
                    [void]$sb.Append('<th>')
                    [void]$sb.Append((Format-Inline $c))
                    [void]$sb.Append('</th>')
                }
                [void]$sb.AppendLine('</tr></thead><tbody>')
                $inTable = $true
            }
            else {
                [void]$sb.Append('<tr>')
                foreach ($c in $cells) {
                    [void]$sb.Append('<td>')
                    [void]$sb.Append((Format-Inline $c))
                    [void]$sb.Append('</td>')
                }
                [void]$sb.AppendLine('</tr>')
            }
            continue
        }
        elseif ($inTable) {
            [void]$sb.AppendLine('</tbody></table>')
            $inTable = $false
        }

        if ($line -match '^---\s*$') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            [void]$sb.AppendLine('<hr/>')
            continue
        }
        if ($line -match '^####\s+(.+)$') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            [void]$sb.AppendLine('<h4>' + (Format-Inline $Matches[1]) + '</h4>')
            continue
        }
        if ($line -match '^###\s+(.+)$') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            [void]$sb.AppendLine('<h3>' + (Format-Inline $Matches[1]) + '</h3>')
            continue
        }
        if ($line -match '^##\s+(.+)$') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            [void]$sb.AppendLine('<h2>' + (Format-Inline $Matches[1]) + '</h2>')
            continue
        }
        if ($line -match '^#\s+(.+)$') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            # Skip duplicate H1 — cover already has title
            continue
        }
        if ($line -match '^[-*]\s+(.+)$') {
            if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
            if (-not $inList) { [void]$sb.AppendLine('<ul>'); $inList = $true }
            [void]$sb.AppendLine('<li>' + (Format-Inline $Matches[1]) + '</li>')
            continue
        }
        if ($line -match '^\d+\.\s+(.+)$') {
            if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
            if (-not $inOl) { [void]$sb.AppendLine('<ol>'); $inOl = $true }
            [void]$sb.AppendLine('<li>' + (Format-Inline $Matches[1]) + '</li>')
            continue
        }

        if ($inList) { [void]$sb.AppendLine('</ul>'); $inList = $false }
        if ($inOl) { [void]$sb.AppendLine('</ol>'); $inOl = $false }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        [void]$sb.AppendLine('<p>' + (Format-Inline $line) + '</p>')
    }

    if ($inList) { [void]$sb.AppendLine('</ul>') }
    if ($inOl) { [void]$sb.AppendLine('</ol>') }
    if ($inTable) { [void]$sb.AppendLine('</tbody></table>') }
    if ($inCode) { [void]$sb.AppendLine('</code></pre>') }
    return $sb.ToString()
}

$md = Get-Content -Path $mdPath -Raw -Encoding UTF8
$body = Convert-MdToHtmlBody $md
$logoUri = ([Uri]$logoPath).AbsoluteUri

$html = @"
<!DOCTYPE html>
<html lang="ro">
<head>
<meta charset="utf-8"/>
<title>Manual de Utilizare — UPET AcqLab</title>
<style>
  @page { margin: 2cm; }
  body { font-family: Calibri, 'Segoe UI', Arial, sans-serif; font-size: 11pt; color: #1a1a1a; line-height: 1.35; }
  .cover { text-align: center; padding: 28pt 12pt 22pt; border-bottom: 2.5pt solid #0072C6; margin-bottom: 18pt; }
  .cover img { height: 110px; margin-bottom: 14pt; }
  .cover .uni { font-size: 10pt; letter-spacing: 1.2pt; color: #0072C6; font-weight: bold; }
  .cover h1 { font-size: 24pt; margin: 8pt 0 4pt; color: #0B4F8C; }
  .cover .sub { font-size: 13pt; color: #333; margin: 2pt 0; }
  .cover .meta { margin-top: 20pt; font-size: 11pt; text-align: left; display: inline-block; }
  h2 { color: #0072C6; font-size: 14pt; border-bottom: 1pt solid #C5D4E3; padding-bottom: 4pt; margin-top: 18pt; }
  h3 { color: #1F4E79; font-size: 12pt; margin-top: 14pt; }
  h4 { font-size: 11pt; margin-top: 10pt; }
  table { border-collapse: collapse; width: 100%; margin: 8pt 0 12pt; font-size: 9.5pt; }
  th, td { border: 1pt solid #A8B8C8; padding: 4pt 6pt; vertical-align: top; }
  th { background: #E8F1FA; color: #0B4F8C; text-align: left; }
  code, pre { font-family: Consolas, 'Courier New', monospace; font-size: 9pt; }
  pre { background: #F4F7FA; border: 1pt solid #D0D8E0; padding: 8pt; white-space: pre-wrap; }
  ul, ol { margin: 4pt 0 8pt 18pt; }
  hr { border: none; border-top: 1pt solid #C5D4E3; margin: 16pt 0; }
  a { color: #0072C6; }
  p { margin: 4pt 0 8pt; }
</style>
</head>
<body>
<div class="cover">
  <img src="$logoUri" alt="Universitatea din Petroșani"/>
  <div class="uni">UNIVERSITATEA DIN PETROȘANI</div>
  <h1>UPET AcqLab</h1>
  <div class="sub">Manual de Utilizare</div>
  <div class="sub">Laborator achiziție / analiză date — HBM Spider8</div>
  <div class="meta">
    <p><strong>EXPERT:</strong> Șef lucr.dr.ing. VÎLCEANU Florin</p>
    <p><strong>Versiune aplicație:</strong> 3.3.11 &nbsp;|&nbsp; <strong>Document:</strong> 1.0 (2026)</p>
    <p>Facultatea de Inginerie Mecanică și Electrică</p>
    <p>Departamentul de Inginerie Mecanică, Industrială și Transporturi</p>
  </div>
</div>
$body
</body>
</html>
"@

[System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.UTF8Encoding]::new($false))
Write-Host "HTML: $htmlPath ($((Get-Item $htmlPath).Length) bytes)"

$word = $null
$doc = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Open($htmlPath)
    if (Test-Path $docxPath) { Remove-Item $docxPath -Force }
    $wdFormatXMLDocument = 12
    $null = $doc.SaveAs([ref]$docxPath, [ref]$wdFormatXMLDocument)
    Write-Host "DOCX: $docxPath ($((Get-Item $docxPath).Length) bytes)"

    $doc.Repaginate()
    $pages = $doc.ComputeStatistics(2)
    $words = $doc.ComputeStatistics(0)
    Write-Host "PAGES=$pages WORDS=$words"
}
finally {
    if ($doc) { $doc.Close($false) | Out-Null }
    if ($word) { $word.Quit() | Out-Null }
    if ($doc) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) }
    if ($word) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}

$h2 = ([regex]::Matches($md, '(?m)^## ')).Count
$h3 = ([regex]::Matches($md, '(?m)^### ')).Count
Write-Host "SECTIONS_H2=$h2 SUBSECTIONS_H3=$h3"
Write-Host "MD_BYTES=$((Get-Item $mdPath).Length)"
