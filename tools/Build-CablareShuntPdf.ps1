#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$repo     = 'C:\Users\acer\Desktop\Spider8DAQ'
$mdPath   = Join-Path $repo 'docs\UPET-AcqLab-Cablare-DA15-si-Shunt-Half-dummy.md'
$docxPath = Join-Path $repo 'docs\UPET-AcqLab-Cablare-DA15-si-Shunt-Half-dummy.docx'
$pdfRepo  = Join-Path $repo 'docs\UPET-AcqLab-Cablare-DA15-si-Shunt-Half-dummy.pdf'
$pdfDesk  = 'C:\Users\acer\Desktop\UPET-AcqLab-Cablare-DA15-si-Shunt-Half-dummy.pdf'

if (-not (Test-Path $mdPath)) { throw "Missing markdown: $mdPath" }

$pandoc = Get-Command pandoc -ErrorAction Stop
Write-Host "PANDOC $($pandoc.Source)"

if (Test-Path $docxPath) { Remove-Item $docxPath -Force }

& pandoc $mdPath `
    -o $docxPath `
    --from=markdown+yaml_metadata_block+pipe_tables+fenced_code_blocks+auto_identifiers `
    --to=docx `
    --toc `
    --toc-depth=2 `
    --standalone `
    --metadata=lang:ro

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $docxPath)) {
    throw "pandoc failed (exit $LASTEXITCODE)"
}
Write-Host "DOCX: $docxPath ($((Get-Item $docxPath).Length) bytes)"

function Set-WordRgb([object]$font, [int]$r, [int]$g, [int]$b) {
    # Word Color is BGR integer
    $font.Color = $b + ($g * 256) + ($r * 65536)
}

$word = $null
$doc = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Open($docxPath)

    $cm = { param($x) $word.CentimetersToPoints($x) }
    foreach ($sec in $doc.Sections) {
        $ps = $sec.PageSetup
        $ps.PaperSize = 7          # wdPaperA4
        $ps.TopMargin = & $cm 1.8
        $ps.BottomMargin = & $cm 1.8
        $ps.LeftMargin = & $cm 1.8
        $ps.RightMargin = & $cm 1.8
        $ps.HeaderDistance = & $cm 0.8
        $ps.FooterDistance = & $cm 0.8
        $ps.DifferentFirstPageHeaderFooter = $false
    }

    try {
        $h1 = $doc.Styles.Item('Heading 1')
        $h1.Font.Name = 'Calibri'
        $h1.Font.Size = 16
        $h1.Font.Bold = $true
        Set-WordRgb $h1.Font 11 79 140
        $h2 = $doc.Styles.Item('Heading 2')
        $h2.Font.Name = 'Calibri'
        $h2.Font.Size = 13
        $h2.Font.Bold = $true
        Set-WordRgb $h2.Font 0 114 198
        $h3 = $doc.Styles.Item('Heading 3')
        $h3.Font.Name = 'Calibri'
        $h3.Font.Size = 12
        $h3.Font.Bold = $true
        Set-WordRgb $h3.Font 31 78 121
        $normal = $doc.Styles.Item('Normal')
        $normal.Font.Name = 'Calibri'
        $normal.Font.Size = 11
        $normal.ParagraphFormat.SpaceAfter = 6
        $normal.ParagraphFormat.LineSpacingRule = 1 # wdLineSpace1pt5? 0=single, 1=1.5, 2=double — use single
        $normal.ParagraphFormat.LineSpacingRule = 0
    } catch {
        Write-Host "STYLE_WARN: $($_.Exception.Message)"
    }

    $headerText = "Universitatea din Petroșani  ·  UPET AcqLab 3.3.81  ·  HBM Spider8-30"
    $footerLeft  = "Cablare DA-15 și șunt Half+dummy  ·  v1.0  ·  Autor soft: drd. ing. Iucal Ilie  ·  Expert: Șef lucr. dr. ing. Vîlceanu Florin"

    foreach ($sec in $doc.Sections) {
        $header = $sec.Headers.Item(1) # wdHeaderFooterPrimary
        $header.Range.Text = $headerText
        $header.Range.Font.Name = 'Calibri'
        $header.Range.Font.Size = 9
        $header.Range.Font.Italic = $true
        Set-WordRgb $header.Range.Font 11 79 140
        $header.Range.ParagraphFormat.Alignment = 1 # center
        $header.Range.ParagraphFormat.SpaceAfter = 0

        $footer = $sec.Footers.Item(1)
        $fr = $footer.Range
        $fr.Font.Name = 'Calibri'
        $fr.Font.Size = 8
        Set-WordRgb $fr.Font 80 80 80
        $fr.ParagraphFormat.Alignment = 1
        $fr.Text = $footerLeft + "  ·  pag. "
        $fr.Collapse(0) # wdCollapseEnd
        $null = $doc.Fields.Add($fr, 33) # wdFieldPage
        $fr2 = $footer.Range
        $fr2.Collapse(0)
        $fr2.Text = " / "
        $fr2.Collapse(0)
        $null = $doc.Fields.Add($fr2, 26) # wdFieldNumPages
    }

    if ($doc.TablesOfContents.Count -gt 0) {
        $doc.TablesOfContents.Item(1).Update()
    }
    $doc.Fields.Update() | Out-Null
    $doc.Repaginate()

    $pages = $doc.ComputeStatistics(2) # wdStatisticPages
    $words = $doc.ComputeStatistics(0)
    Write-Host "PAGES=$pages WORDS=$words"

    $null = $doc.Save()
    Write-Host "SAVED_DOCX"

    $wdFormatPDF = 17
    foreach ($outPdf in @($pdfRepo, $pdfDesk)) {
        if (Test-Path $outPdf) { Remove-Item $outPdf -Force }
        Write-Host "EXPORT $outPdf"
        try {
            $doc.SaveAs2([string]$outPdf, $wdFormatPDF)
        } catch {
            Write-Host ("SaveAs2 failed: " + $_.Exception.Message + " - trying SaveAs")
            $p = [string]$outPdf
            $fmt = $wdFormatPDF
            $doc.SaveAs([ref]$p, [ref]$fmt)
        }
        if (-not (Test-Path $outPdf)) { throw "PDF not written: $outPdf" }
        $pdfLen = (Get-Item -LiteralPath $outPdf).Length
        Write-Host "PDF: $outPdf ($pdfLen bytes)"
    }

    Write-Host "DONE PAGES=$pages"
}
finally {
    if ($doc) { $doc.Close($false) | Out-Null }
    if ($word) { $word.Quit() | Out-Null }
    if ($doc) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) }
    if ($word) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
