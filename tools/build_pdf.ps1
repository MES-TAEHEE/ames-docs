# ============================================================
# A-MES L3 Spec — PDF Build Script (per-file render + merge)
# For each Volume:
#   1. Build cover.html and toc.html (per-volume)
#   2. Render each VOL*.html individually via Chrome --print-to-pdf
#   3. Merge all PDFs in order using merge_pdf.exe (PdfSharp)
# Output: dist/AMES_L3_VOLxx_<short>.pdf
# ============================================================
[CmdletBinding()]
param(
  [string]$Volume = "ALL",
  [string]$OutDir = "dist",
  [string]$BuildDir = "build",
  [switch]$KeepIntermediate
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# --- Locate Chrome ---
$chromeCandidates = @(
  "C:\Program Files\Google\Chrome\Application\chrome.exe",
  "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
  "${env:LOCALAPPDATA}\Google\Chrome\Application\chrome.exe"
)
$chrome = $null
foreach ($c in $chromeCandidates) { if (Test-Path $c) { $chrome = $c; break } }
if (-not $chrome) { throw "Chrome.exe not found." }

# --- Locate merger ---
$merger = (Resolve-Path "tools\merge_pdf\bin\Release\net9.0\merge_pdf.exe" -ErrorAction SilentlyContinue)
if (-not $merger) { throw "Build merge_pdf first: cd tools/merge_pdf && dotnet build -c Release" }

Write-Host "[chrome] $chrome" -ForegroundColor DarkGray
Write-Host "[merger] $merger" -ForegroundColor DarkGray

# --- Load volumes.json ---
$volumes = Get-Content "tools\volumes.json" -Raw | ConvertFrom-Json

# --- Setup directories ---
$null = New-Item -ItemType Directory -Path $OutDir -Force
$null = New-Item -ItemType Directory -Path $BuildDir -Force

$today = Get-Date -Format "MM/dd/yyyy"
$buildTag = Get-Date -Format "yyyyMMdd-HHmmss"

Add-Type -AssemblyName System.Web

function Get-PageTitle {
  param([string]$html, [string]$fallback)
  if ($html -match '(?s)<title>(.*?)</title>') {
    $t = $matches[1].Trim()
    if ($t) { return $t }
  }
  return $fallback
}

function Build-CoverHtml {
  param($v)
  $sub = [System.Web.HttpUtility]::HtmlEncode($v.subtitle)
  $tt = [System.Web.HttpUtility]::HtmlEncode($v.title)
  $code = $v.code
@"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>$tt — Cover</title>
<style>
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800;900&family=JetBrains+Mono:wght@400;600;700&family=Bebas+Neue&display=swap');
@page { size: Letter portrait; margin: 0; }
* { box-sizing:border-box; margin:0; padding:0; }
html, body { height: 100vh; }
body {
  font-family: 'Inter', sans-serif;
  background: linear-gradient(160deg, #0d1117, #161b22 65%, #0d1117);
  color: #fff;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
  padding: 28mm;
  -webkit-print-color-adjust: exact;
  print-color-adjust: exact;
}
.brand { font-family:'Bebas Neue', sans-serif; font-size:84pt; letter-spacing:10pt; color:#d4a017; line-height:0.9; }
.brand-sub { font-size: 11pt; letter-spacing:6pt; color: rgba(255,255,255,.4); margin-top:6pt; }
.divider { width:60mm; height:2pt; background:#d4a017; margin: 30pt auto; }
.vol { font-family:'JetBrains Mono',monospace; font-size:14pt; letter-spacing:6pt; color:#fdba74; }
.title { font-size: 30pt; font-weight: 800; margin-top: 8pt; line-height: 1.2; max-width: 140mm; }
.subtitle { font-size: 13pt; color: rgba(255,255,255,.55); margin-top: 8pt; font-weight: 500; }
.meta {
  margin-top: 42pt;
  padding: 16pt 28pt;
  background: rgba(212,160,23,.08);
  border: 1pt solid rgba(212,160,23,.35);
  border-radius: 4pt;
  font-size: 10.5pt;
  color: rgba(255,255,255,.7);
  line-height: 2;
}
.meta b { color: #fdba74; font-weight: 700; }
.foot {
  position: absolute;
  bottom: 18mm;
  font-family: 'JetBrains Mono', monospace;
  font-size: 8.5pt;
  color: rgba(255,255,255,.25);
  letter-spacing: 2pt;
}
</style>
</head>
<body>
  <div class="brand">A-MES</div>
  <div class="brand-sub">AUTOMOTIVE MES · SEOYON E-HWA</div>
  <div class="divider"></div>
  <div class="vol">$code</div>
  <div class="title">$tt</div>
  <div class="subtitle">$sub</div>
  <div class="meta">
    <div>Document · L3 Detailed Design Specification</div>
    <div>Generated · $today · Build $buildTag</div>
    <div>Reference · Plant A — SAV Detroit, MI &nbsp;|&nbsp; Plant B — GEO Birmingham, AL</div>
  </div>
  <div class="foot">CONFIDENTIAL · FOR INTERNAL USE ONLY</div>
</body>
</html>
"@
}

function Build-TocHtml {
  param($v, $entries)
  $rows = ($entries | ForEach-Object {
    $code = [System.Web.HttpUtility]::HtmlEncode($_.code)
    $title = [System.Web.HttpUtility]::HtmlEncode($_.title)
    "<div class=`"toc-row`"><span class=`"toc-code`">$code</span><span class=`"toc-title`">$title</span></div>"
  }) -join "`n"
  $vt = [System.Web.HttpUtility]::HtmlEncode($v.title)
@"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>$vt — Table of Contents</title>
<style>
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800;900&family=JetBrains+Mono:wght@400;600;700&family=Bebas+Neue&display=swap');
@page { size: Letter portrait; margin: 18mm 16mm; }
* { box-sizing:border-box; margin:0; padding:0; }
body { font-family:'Inter',sans-serif; color:#24292f; padding:0; }
.eyebrow { font-family:'JetBrains Mono', monospace; font-size:9pt; letter-spacing:4pt; color:#d4a017; }
h1 { font-family:'Bebas Neue', sans-serif; font-size:32pt; letter-spacing:5pt; color:#0d1117; margin-top:4pt; }
.subtitle { font-size: 11pt; color:#656d76; margin-top:4pt; }
.rule { height:3pt; background:#d4a017; margin:14pt 0 18pt; width:50mm; }
.toc-row {
  display:flex; align-items:baseline;
  padding:7pt 0;
  border-bottom:1pt dotted #d0d7de;
  font-size: 10.5pt;
}
.toc-code {
  font-family:'JetBrains Mono', monospace;
  font-weight:700; color:#0891b2;
  min-width: 100pt;
  flex-shrink:0;
}
.toc-title { flex:1; color:#24292f; font-weight:600; }
.foot {
  position: fixed;
  bottom: 0; left: 0; right: 0;
  padding: 10pt 16mm;
  font-family:'JetBrains Mono', monospace;
  font-size: 8.5pt;
  color: #656d76;
  border-top: 1pt solid #d0d7de;
  display: flex; justify-content: space-between;
}
</style>
</head>
<body>
  <div class="eyebrow">A-MES · $($v.code)</div>
  <h1>Table of Contents</h1>
  <div class="subtitle">$vt</div>
  <div class="rule"></div>
  $rows
  <div class="foot"><span>A-MES L3 · $($v.code)</span><span>SEOYON E-HWA · CONFIDENTIAL</span></div>
</body>
</html>
"@
}

function Build-DemoAppendixHtml {
  param($v)
  $files = Get-ChildItem -Path . -Filter "DEMO_*.html" | Where-Object { $_.Name -notmatch "Site_Map|Office_Web|ShopFloor_POP|Logistics_PDA" } | Sort-Object Name
  $rows = ($files | ForEach-Object {
    $name = $_.Name
    $url = "https://mes-taehee.github.io/ames-docs/$name"
    $label = ($name -replace "^DEMO_","" -replace "\.html$","" -replace "_"," ")
    "<tr><td class=`"sc`">$label</td><td class=`"url`">$url</td></tr>"
  }) -join "`n"
@"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>Demo Appendix</title>
<style>
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700;800;900&family=JetBrains+Mono:wght@400;600;700&family=Bebas+Neue&display=swap');
@page { size: Letter portrait; margin: 14mm 12mm 16mm 12mm; }
* { box-sizing:border-box; margin:0; padding:0; }
body { font-family:'Inter',sans-serif; color:#24292f; font-size:10pt; line-height:1.55; }
.eyebrow { font-family:'JetBrains Mono', monospace; font-size:9pt; letter-spacing:4pt; color:#d4a017; }
h1 { font-family:'Bebas Neue', sans-serif; font-size:30pt; letter-spacing:4pt; color:#0d1117; margin-top:4pt; }
.subtitle { font-size: 11pt; color:#656d76; margin-top:4pt; }
.rule { height:3pt; background:#d4a017; margin:14pt 0 18pt; width:50mm; }
p { margin: 8pt 0; }
h2 { font-size: 14pt; margin: 18pt 0 8pt; color:#0d1117; }
.shells {
  background: #fffbeb; border: 1pt solid #fde68a; border-radius: 3pt;
  padding: 10pt 14pt; margin: 10pt 0;
}
.shells table { width:100%; border-collapse:collapse; }
.shells td { padding: 3pt 0; font-size: 10pt; }
.shells td:first-child { font-weight:700; width: 100pt; color:#92400e; }
.shells td a { color: #0891b2; font-family:'JetBrains Mono', monospace; font-size: 9pt; text-decoration:none; }
table.full { width: 100%; border-collapse: collapse; }
table.full thead th {
  background:#0d1117; color:#fff; padding: 6pt 8pt;
  font-size: 8.5pt; letter-spacing: 1pt; text-transform: uppercase; text-align:left;
}
table.full tbody tr { border-bottom: 1pt solid #e5e7eb; }
table.full tbody tr:nth-child(even) { background: #f9fafb; }
table.full td { padding: 4pt 8pt; font-size: 9pt; vertical-align: top; }
td.sc { font-weight: 700; color: #0891b2; font-family:'JetBrains Mono', monospace; font-size: 9pt; }
td.url { color: #24292f; font-family:'JetBrains Mono', monospace; font-size: 8pt; word-break:break-all; }
</style>
</head>
<body>
<div class="eyebrow">A-MES · $($v.code) · APPENDIX</div>
<h1>Interactive Demo Appendix</h1>
<div class="subtitle">125 live interactive demo screens</div>
<div class="rule"></div>
<p>The 125 interactive demo screens listed here are fully working HTML demonstrations with realistic U.S. sample data based on a SEOYON E-HWA automotive parts plant operating two facilities: <b>SAV (Detroit, MI)</b> and <b>GEO (Birmingham, AL)</b>. Customer accounts include <b>Ford</b>, <b>General Motors</b>, <b>Stellantis</b>, <b>Hyundai Motor Manufacturing Alabama (HMMA)</b>, and <b>BYD</b>.</p>
<p>To explore the demos, open the URLs below in any modern browser (Chrome, Edge, or Safari). All four <i>shell</i> URLs are entry points that load the individual screens via menu clicks.</p>
<h2>Demo Shells (entry points)</h2>
<div class="shells"><table>
  <tr><td>Site Map</td><td><a href="https://mes-taehee.github.io/ames-docs/DEMO_Site_Map.html">https://mes-taehee.github.io/ames-docs/DEMO_Site_Map.html</a></td></tr>
  <tr><td>Office Web</td><td><a href="https://mes-taehee.github.io/ames-docs/DEMO_Office_Web.html">https://mes-taehee.github.io/ames-docs/DEMO_Office_Web.html</a></td></tr>
  <tr><td>Shop-Floor POP</td><td><a href="https://mes-taehee.github.io/ames-docs/DEMO_ShopFloor_POP.html">https://mes-taehee.github.io/ames-docs/DEMO_ShopFloor_POP.html</a></td></tr>
  <tr><td>Logistics PDA</td><td><a href="https://mes-taehee.github.io/ames-docs/DEMO_Logistics_PDA.html">https://mes-taehee.github.io/ames-docs/DEMO_Logistics_PDA.html</a></td></tr>
</table></div>
<h2>All 125 Demo Screens</h2>
<table class="full">
  <thead><tr><th>Screen</th><th>URL</th></tr></thead>
  <tbody>
$rows
  </tbody>
</table>
</body>
</html>
"@
}

# Load the print.css once (injected into every source VOL HTML before render)
$printCssText = Get-Content "tools\print.css" -Raw -Encoding UTF8

function New-PrintReadyHtml {
  param([string]$sourceHtml, [string]$outputHtml)
  $raw = Get-Content $sourceHtml -Raw -Encoding UTF8
  $cssInjection = "<style data-injected=`"print-prep`">`n$printCssText`n</style>`n</head>"
  if ($raw -match '</head>') {
    $raw = $raw -replace '</head>', $cssInjection
  } else {
    # no </head>? prepend a minimal head
    $raw = "<head>$cssInjection`n" + $raw
  }
  $outDir = Split-Path $outputHtml -Parent
  if (-not (Test-Path $outDir)) { $null = New-Item -ItemType Directory -Path $outDir -Force }
  Set-Content -Path $outputHtml -Value $raw -Encoding UTF8 -NoNewline
  return $outputHtml
}

function Invoke-ChromePdf {
  param([string]$inputHtml, [string]$outputPdf)
  $absIn = (Resolve-Path $inputHtml).Path
  # ensure absolute output path
  if (-not [System.IO.Path]::IsPathRooted($outputPdf)) {
    $outputPdf = Join-Path (Get-Location).Path $outputPdf
  }
  # ensure output directory exists
  $outDir = Split-Path $outputPdf -Parent
  if (-not (Test-Path $outDir)) { $null = New-Item -ItemType Directory -Path $outDir -Force }
  $url = "file:///" + ($absIn -replace '\\','/')
  $chromeArgs = @(
    "--headless=new"
    "--disable-gpu"
    "--no-sandbox"
    "--disable-extensions"
    "--no-pdf-header-footer"
    "--virtual-time-budget=20000"
    "--print-to-pdf=`"$outputPdf`""
    "`"$url`""
  )
  $proc = Start-Process -FilePath $chrome -ArgumentList $chromeArgs -Wait -NoNewWindow -PassThru
  if (-not (Test-Path $outputPdf)) { throw "PDF not created: $outputPdf" }
  return $outputPdf
}

# --- Filter volumes ---
$volsToBuild = if ($Volume -eq "ALL") { $volumes } else { $volumes | Where-Object { $_.code -eq $Volume } }
if (-not $volsToBuild) { throw "No matching volume: $Volume" }

$results = @()
foreach ($v in $volsToBuild) {
  Write-Host "`n=== $($v.code) · $($v.title) ===" -ForegroundColor Yellow

  $volDir = Join-Path $BuildDir $v.code
  $null = New-Item -ItemType Directory -Path $volDir -Force

  $tocEntries = @()
  $pdfList = @()

  # --- 1. Cover (placeholder for now, build below) ---
  # --- 2. Build each section PDF ---
  $idx = 0
  foreach ($f in $v.files) {
    $idx++
    $idxStr = "{0:D3}" -f $idx
    if ($f -eq "__DEMO_APPENDIX__") {
      $appendixHtml = Join-Path $volDir "${idxStr}_appendix.html"
      Set-Content -Path $appendixHtml -Value (Build-DemoAppendixHtml $v) -Encoding UTF8
      $appendixPdf = Join-Path $volDir "${idxStr}_appendix.pdf"
      Write-Host "  → $idxStr appendix" -ForegroundColor DarkGray
      Invoke-ChromePdf $appendixHtml $appendixPdf
      $pdfList += $appendixPdf
      $tocEntries += [pscustomobject]@{ code = "APPX"; title = "Interactive Demo Appendix" }
      continue
    }
    if (-not (Test-Path $f)) {
      Write-Warning "  ! missing: $f"
      continue
    }
    $raw = Get-Content $f -Raw -Encoding UTF8
    $title = Get-PageTitle $raw $f
    $shortCode = $f -replace '\.html$','' -replace '^VOL\d+_',''
    $tocEntries += [pscustomobject]@{ code = $shortCode; title = $title }

    $secPdf = Join-Path $volDir "${idxStr}_$($shortCode).pdf"
    # Inject print.css into a working copy, then render that copy
    $prepHtml = Join-Path $volDir "${idxStr}_$($shortCode).prep.html"
    $null = New-PrintReadyHtml -sourceHtml (Resolve-Path $f).Path -outputHtml $prepHtml
    Write-Host "  → $idxStr $shortCode" -ForegroundColor DarkGray
    Invoke-ChromePdf $prepHtml $secPdf
    $pdfList += $secPdf
  }

  # --- 3. Build cover + toc PDFs ---
  $coverHtml = Join-Path $volDir "000_cover.html"
  Set-Content -Path $coverHtml -Value (Build-CoverHtml $v) -Encoding UTF8
  $coverPdf = Join-Path $volDir "000_cover.pdf"
  Invoke-ChromePdf $coverHtml $coverPdf

  $tocHtml = Join-Path $volDir "001_toc.html"
  Set-Content -Path $tocHtml -Value (Build-TocHtml $v $tocEntries) -Encoding UTF8
  $tocPdf = Join-Path $volDir "001_toc.pdf"
  Invoke-ChromePdf $tocHtml $tocPdf

  # --- 4. Merge: cover + toc + sections ---
  $finalPdf = Join-Path (Resolve-Path $OutDir).Path "AMES_L3_$($v.code)_$($v.shortTitle -replace '\s+','_').pdf"
  $mergeArgs = @($finalPdf, $coverPdf, $tocPdf) + $pdfList
  Write-Host "  → merging $($mergeArgs.Count - 1) PDFs" -ForegroundColor DarkGray
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  & $merger.Path @mergeArgs
  $sw.Stop()

  $size = [math]::Round((Get-Item $finalPdf).Length / 1KB, 1)
  Write-Host "  ✓ $(Split-Path $finalPdf -Leaf) — $($size) KB" -ForegroundColor Green
  $results += [pscustomobject]@{ Volume=$v.code; Files=$pdfList.Count + 2; SizeKB=$size }

  if (-not $KeepIntermediate) { Remove-Item $volDir -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize
$totalKB = ($results | Measure-Object -Property SizeKB -Sum).Sum
Write-Host "Total: $($results.Count) PDFs, $([math]::Round($totalKB/1024,1)) MB"
Write-Host "Output: $((Resolve-Path $OutDir).Path)"
