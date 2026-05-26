# ============================================================
# Inject "Download PDF" button into each L2 Volume hub page.
# Idempotent: re-running replaces the existing block.
# Pulls page counts from the actual PDF files via PdfSharpCore
# (uses the merge_pdf binary's PdfSharpCore.dll loaded reflectively).
# ============================================================
[CmdletBinding()]
param(
  [string]$DistDir = "dist"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Load PdfSharp via the already-built merger's deps (gets page counts)
$pdfDll = (Get-ChildItem -Path "tools\merge_pdf\bin\Release\net9.0" -Filter "PdfSharpCore.dll" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if ($pdfDll) {
  try { Add-Type -Path $pdfDll -ErrorAction Stop } catch {}
}

function Get-PdfPageCount {
  param([string]$path)
  if (-not (Test-Path $path)) { return $null }
  try {
    $doc = [PdfSharpCore.Pdf.IO.PdfReader]::Open($path, [PdfSharpCore.Pdf.IO.PdfDocumentOpenMode]::InformationOnly)
    $n = $doc.PageCount
    $doc.Close()
    return $n
  } catch {
    return $null
  }
}

function Format-FileSize {
  param([string]$path)
  if (-not (Test-Path $path)) { return $null }
  $bytes = (Get-Item $path).Length
  if ($bytes -lt 1MB) { return "{0:N0} KB" -f ($bytes/1KB) }
  return "{0:N1} MB" -f ($bytes/1MB)
}

# Map: hub HTML -> PDF filename
$hubs = @(
  @{ Hub="VOL03_WH_Warehouse.html";       Pdf="AMES_L3_VOL03_Warehouse.pdf";              Vol="VOL.3" },
  @{ Hub="VOL04_PP_Production_Plan.html"; Pdf="AMES_L3_VOL04_Production_Planning.pdf";    Vol="VOL.4" },
  @{ Hub="VOL05_INJ_Injection.html";      Pdf="AMES_L3_VOL05_Injection_POP.pdf";          Vol="VOL.5" },
  @{ Hub="VOL06_IMG_Wrapping.html";       Pdf="AMES_L3_VOL06_Wrapping_POP.pdf";           Vol="VOL.6" },
  @{ Hub="VOL07_PNT_Painting.html";       Pdf="AMES_L3_VOL07_Painting_POP.pdf";           Vol="VOL.7" },
  @{ Hub="VOL08_QC_Quality.html";         Pdf="AMES_L3_VOL08_Quality_Control.pdf";        Vol="VOL.8" },
  @{ Hub="VOL09_FG_Finished_Goods.html";  Pdf="AMES_L3_VOL09_Finished_Goods.pdf";         Vol="VOL.9" },
  @{ Hub="VOL10_MNT_Maintenance.html";    Pdf="AMES_L3_VOL10_Maintenance.pdf";            Vol="VOL.10" },
  @{ Hub="VOL11_RPT_Reports.html";        Pdf="AMES_L3_VOL11_Reports.pdf";                Vol="VOL.11" },
  @{ Hub="VOL12_SYS_System.html";         Pdf="AMES_L3_VOL12_System_Admin.pdf";           Vol="VOL.12" },
  @{ Hub="VOL13_MD_Master_Data.html";     Pdf="AMES_L3_VOL13_Master_Data.pdf";            Vol="VOL.13" }
)

# CSS to inject once into the <head> (skipped if already present)
$cssBlock = @'
<style>
/* === A-MES L3 PDF Download Button =================================== */
.pdf-dl {
  display: inline-flex;
  align-items: center;
  gap: 11px;
  margin: 0 0 14px 0;
  padding: 9px 16px 9px 13px;
  background: linear-gradient(135deg, rgba(212,160,23,.22), rgba(212,160,23,.08));
  border: 1px solid rgba(212,160,23,.55);
  border-radius: 7px;
  color: #fde68a;
  text-decoration: none;
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.5px;
  transition: all .15s ease;
  cursor: pointer;
  box-shadow: 0 2px 6px rgba(0,0,0,.18);
}
.pdf-dl:hover {
  background: linear-gradient(135deg, rgba(212,160,23,.4), rgba(212,160,23,.18));
  border-color: #fbbf24;
  color: #fff;
  transform: translateY(-1px);
  box-shadow: 0 4px 10px rgba(0,0,0,.28);
}
.pdf-dl:active { transform: translateY(0); }
.pdf-dl .pdf-ico {
  font-size: 18px;
  line-height: 1;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  background: rgba(212,160,23,.18);
  border-radius: 5px;
}
.pdf-dl .pdf-txt {
  display: flex;
  flex-direction: column;
  gap: 1px;
  text-align: left;
}
.pdf-dl .pdf-lbl {
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 1.5px;
  text-transform: uppercase;
  color: #fbbf24;
}
.pdf-dl .pdf-meta {
  font-size: 9.5px;
  font-weight: 500;
  letter-spacing: 0.5px;
  color: rgba(253,230,138,.7);
  text-transform: none;
}
.pdf-dl:hover .pdf-lbl { color: #fff; }
.pdf-dl:hover .pdf-meta { color: rgba(255,255,255,.85); }
</style>
'@

$cssMarker = '<!-- @pdf-dl-css -->'
$blockStartMarker = '<!-- @pdf-dl-start -->'
$blockEndMarker = '<!-- @pdf-dl-end -->'

$results = @()
foreach ($h in $hubs) {
  $hubPath = $h.Hub
  $pdfFile = $h.Pdf
  $volTag  = $h.Vol
  $pdfPath = Join-Path $DistDir $pdfFile

  if (-not (Test-Path $hubPath)) {
    Write-Warning "missing hub: $hubPath"
    continue
  }

  $pages = Get-PdfPageCount $pdfPath
  $size  = Format-FileSize $pdfPath
  $pdfHref = "$DistDir/$pdfFile"
  $metaText = if ($pages -and $size) { "$pages pages | $size | L3 Spec" }
              elseif ($size) { "$size | L3 Spec" }
              else { "L3 Detailed Design Spec" }

  $btnHtml = @"
$blockStartMarker
      <a class="pdf-dl" href="$pdfHref" download>
        <span class="pdf-ico">📄</span>
        <span class="pdf-txt">
          <span class="pdf-lbl">Download PDF | $volTag</span>
          <span class="pdf-meta">$metaText</span>
        </span>
      </a>
$blockEndMarker
"@

  $raw = Get-Content $hubPath -Raw -Encoding UTF8
  $changed = $false

  # 1. Inject CSS into <head> if not already there
  if ($raw -notmatch [regex]::Escape($cssMarker)) {
    $insert = "$cssMarker`n$cssBlock"
    if ($raw -match '</head>') {
      $raw = $raw -replace '</head>', ($insert + "`n</head>")
      $changed = $true
    }
  }

  # 2. Replace existing block (idempotent), or insert before <div class="mod-eyebrow">
  if ($raw.Contains($blockStartMarker)) {
    $sIdx = $raw.IndexOf($blockStartMarker)
    $eIdx = $raw.IndexOf($blockEndMarker, $sIdx) + $blockEndMarker.Length
    if ($sIdx -ge 0 -and $eIdx -gt $sIdx) {
      $raw = $raw.Substring(0, $sIdx) + $btnHtml + $raw.Substring($eIdx)
      $changed = $true
    }
  } else {
    # Insert right before the mod-eyebrow div (inside .mod-hdr-inner)
    if ($raw -match '(<div class="mod-eyebrow">)') {
      $raw = $raw -replace '(<div class="mod-eyebrow">)', "$btnHtml`n      `$1"
      $changed = $true
    } else {
      Write-Warning "  $hubPath  - could not find .mod-eyebrow anchor; skipped button insert"
    }
  }

  if ($changed) {
    Set-Content -Path $hubPath -Value $raw -Encoding UTF8 -NoNewline
    Write-Host ("  [OK] {0,-40}  ->  {1} ({2})" -f $hubPath, $pdfFile, $metaText) -ForegroundColor Green
  } else {
    Write-Host ("  | {0,-40}  unchanged" -f $hubPath) -ForegroundColor DarkGray
  }
  $results += [pscustomobject]@{ Hub=$hubPath; PDF=$pdfFile; Pages=$pages; Size=$size }
}

Write-Host "`nProcessed $($results.Count) L2 hubs" -ForegroundColor Cyan
$results | Format-Table -AutoSize

# ====================================================================
# Inject the master "Download PDF Deliverables" grid into INDEX.html
# ====================================================================
$indexFile = "INDEX.html"
if (Test-Path $indexFile) {
  Write-Host "`nInjecting download grid into $indexFile" -ForegroundColor Yellow

  # Collect all PDFs we want listed (in order)
  $indexPdfs = @(
    @{ Code="VOL00"; Title="Foundation";          Sub="Index | ERD | DB Schema";     Color="#94a3b8"; Pdf="AMES_L3_VOL00_Foundation.pdf" },
    @{ Code="VOL03"; Title="Warehouse";           Sub="WH  - Inbound | Inventory";    Color="#0891b2"; Pdf="AMES_L3_VOL03_Warehouse.pdf" },
    @{ Code="VOL04"; Title="Production Planning"; Sub="PP  - Forecast | MRP | WO";    Color="#059669"; Pdf="AMES_L3_VOL04_Production_Planning.pdf" },
    @{ Code="VOL05"; Title="Injection POP";       Sub="INJ  - Operator Terminal";     Color="#2563eb"; Pdf="AMES_L3_VOL05_Injection_POP.pdf" },
    @{ Code="VOL06"; Title="Wrapping POP";        Sub="IMG  - Fabric | Adhesive";     Color="#7c3aed"; Pdf="AMES_L3_VOL06_Wrapping_POP.pdf" },
    @{ Code="VOL07"; Title="Painting POP";        Sub="PNT  - Oven | LOT | Label";    Color="#ea580c"; Pdf="AMES_L3_VOL07_Painting_POP.pdf" },
    @{ Code="VOL08"; Title="Quality Control";     Sub="QC  - NCR | CAPA | Trace";     Color="#dc2626"; Pdf="AMES_L3_VOL08_Quality_Control.pdf" },
    @{ Code="VOL09"; Title="Finished Goods";      Sub="FG  - FIFO | BOL | Returns";   Color="#0db87a"; Pdf="AMES_L3_VOL09_Finished_Goods.pdf" },
    @{ Code="VOL10"; Title="Maintenance";         Sub="MNT  - PM | Failure | Spares"; Color="#0284c7"; Pdf="AMES_L3_VOL10_Maintenance.pdf" },
    @{ Code="VOL11"; Title="Reports";             Sub="RPT  - KPI | Pareto | OEE";    Color="#6366f1"; Pdf="AMES_L3_VOL11_Reports.pdf" },
    @{ Code="VOL12"; Title="System Admin";        Sub="SYS  - RBAC | Audit | Health"; Color="#475569"; Pdf="AMES_L3_VOL12_System_Admin.pdf" },
    @{ Code="VOL13"; Title="Master Data";         Sub="MD  - 29 entities";            Color="#d97706"; Pdf="AMES_L3_VOL13_Master_Data.pdf" },
    @{ Code="VOL14"; Title="Demo Appendix";       Sub="125 interactive demo URLs";   Color="#16a34a"; Pdf="AMES_L3_VOL14_Demo_Appendix.pdf" }
  )

  $cellRows = ($indexPdfs | ForEach-Object {
    $p = $_
    $pdfPath = Join-Path $DistDir $p.Pdf
    $pages = Get-PdfPageCount $pdfPath
    $sz = Format-FileSize $pdfPath
    $meta = if ($pages -and $sz) { "$pages pages | $sz" }
            elseif ($sz) { $sz }
            else { "pending" }
    $disabled = if (Test-Path $pdfPath) { "" } else { ' style="opacity:0.45;pointer-events:none;" title="PDF not yet built"' }
    @"
  <a class="pdf-card" href="$DistDir/$($p.Pdf)" download style="--pc:$($p.Color)"$disabled>
    <div class="pc-badge">$($p.Code)</div>
    <div class="pc-icon">📄</div>
    <div class="pc-title">$($p.Title)</div>
    <div class="pc-sub">$($p.Sub)</div>
    <div class="pc-meta">$meta</div>
  </a>
"@
  }) -join "`n"

  $injectCss = @'
<!-- @pdf-grid-css -->
<style>
.pdf-bundle {
  background: linear-gradient(160deg, rgba(212,160,23,.08), rgba(15,23,42,.6));
  border: 1px solid rgba(212,160,23,.25);
  border-radius: 9px;
  padding: 18px 18px 22px;
  margin: 14px 0 22px;
}
.pdf-bundle-h {
  display:flex; align-items:center; justify-content:space-between;
  margin-bottom: 6px; flex-wrap:wrap; gap:8px;
}
.pdf-bundle-t {
  font-family:'Bebas Neue', sans-serif;
  font-size: 22px; letter-spacing: 3px;
  color: #d4a017;
}
.pdf-bundle-s {
  font-size: 11px; color:#94a3b8;
  font-family:'JetBrains Mono', monospace; letter-spacing: 1px;
}
.pdf-bundle-info {
  font-size: 11px; color:#cbd5e1;
  margin-bottom: 14px; line-height: 1.6;
}
.pdf-grid {
  display:grid;
  grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
  gap: 10px;
}
.pdf-card {
  display:flex; flex-direction:column; gap:3px;
  padding: 11px 13px 13px;
  background: rgba(255,255,255,.03);
  border: 1px solid #334155;
  border-left: 4px solid var(--pc, #d4a017);
  border-radius: 6px;
  text-decoration: none;
  position: relative;
  transition: all .15s ease;
}
.pdf-card:hover {
  background: rgba(212,160,23,.08);
  border-color: var(--pc, #d4a017);
  transform: translateY(-2px);
  box-shadow: 0 6px 16px rgba(0,0,0,.4);
}
.pdf-card .pc-badge {
  font-family:'JetBrains Mono', monospace;
  font-size: 9px; font-weight: 700; letter-spacing: 1px;
  color: var(--pc, #d4a017);
  margin-bottom: 2px;
}
.pdf-card .pc-icon {
  font-size: 22px;
  margin-bottom: 3px;
  filter: grayscale(0.2);
}
.pdf-card .pc-title {
  font-size: 13px; font-weight: 700;
  color: #f1f5f9;
}
.pdf-card .pc-sub {
  font-size: 10px;
  color: #94a3b8;
  line-height: 1.4;
}
.pdf-card .pc-meta {
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px dashed #334155;
  font-family:'JetBrains Mono', monospace;
  font-size: 9px;
  color: #fbbf24;
  letter-spacing: 0.5px;
}
</style>
'@

  $injectBlock = @"
<!-- @pdf-grid-start -->
<div class="sec-title">📥 PDF Deliverables &nbsp;PDF 산출물</div>
<div class="pdf-bundle">
  <div class="pdf-bundle-h">
    <div class="pdf-bundle-t">DOWNLOAD AS PDF</div>
    <div class="pdf-bundle-s">U.S. Letter | Auto-generated</div>
  </div>
  <div class="pdf-bundle-info">
    Each volume is available as a self-contained PDF (cover | TOC | all L3 specs).
    Click any tile below to download. Files are generated from the latest source HTML  -
    rebuild via <code style="background:#1e293b;padding:1px 5px;border-radius:3px;color:#fbbf24;">.\tools\build_pdf.ps1</code>.
  </div>
  <div class="pdf-grid">
$cellRows
  </div>
</div>
<!-- @pdf-grid-end -->
"@

  $raw = Get-Content $indexFile -Raw -Encoding UTF8
  $changed = $false

  # 1. CSS in head
  if ($raw -notmatch '<!-- @pdf-grid-css -->') {
    if ($raw -match '</head>') {
      $raw = $raw -replace '</head>', ($injectCss + "`n</head>")
      $changed = $true
    }
  }

  # 2. Replace or insert grid block
  if ($raw.Contains('<!-- @pdf-grid-start -->')) {
    $sIdx = $raw.IndexOf('<!-- @pdf-grid-start -->')
    $eMarker = '<!-- @pdf-grid-end -->'
    $eIdx = $raw.IndexOf($eMarker, $sIdx) + $eMarker.Length
    if ($sIdx -ge 0 -and $eIdx -gt $sIdx) {
      $raw = $raw.Substring(0, $sIdx) + $injectBlock + $raw.Substring($eIdx)
      $changed = $true
    }
  } else {
    # Inject right before "Overall Process Flow" section title
    $anchor = '<div class="sec-title">Overall Process Flow'
    if ($raw.Contains($anchor)) {
      $pos = $raw.IndexOf($anchor)
      $raw = $raw.Substring(0, $pos) + $injectBlock + "`n" + $raw.Substring($pos)
      $changed = $true
    } else {
      Write-Warning "  $indexFile  - could not find anchor; skipped"
    }
  }

  if ($changed) {
    Set-Content -Path $indexFile -Value $raw -Encoding UTF8 -NoNewline
    Write-Host "  [OK] INDEX.html  - download grid injected with $($indexPdfs.Count) PDFs" -ForegroundColor Green
  } else {
    Write-Host "  | INDEX.html  - unchanged" -ForegroundColor DarkGray
  }
}
