# A-MES PDF Build Tools

Generates per-Volume PDF deliverables from the L3 specification HTML files.

## Output

Per-Volume PDFs land in `dist/`:

```
dist/
├── README.md                                  (catalog with download links)
├── AMES_L3_VOL00_Foundation.pdf               (INDEX + ERD + DB Schema)
├── AMES_L3_VOL03_Warehouse.pdf
├── AMES_L3_VOL04_Production_Planning.pdf
├── AMES_L3_VOL05_Injection_POP.pdf
├── AMES_L3_VOL06_Wrapping_POP.pdf
├── AMES_L3_VOL07_Painting_POP.pdf
├── AMES_L3_VOL08_Quality_Control.pdf
├── AMES_L3_VOL09_Finished_Goods.pdf
├── AMES_L3_VOL10_Maintenance.pdf
├── AMES_L3_VOL11_Reports.pdf
├── AMES_L3_VOL12_System_Admin.pdf
├── AMES_L3_VOL13_Master_Data.pdf
└── AMES_L3_VOL14_Demo_Appendix.pdf            (URLs to 125 interactive demos)
```

Each PDF has:
- Cover page (Volume number, title, build date, plant references)
- Table of Contents
- Each L3 spec page (rendered from HTML — nav/sidebar hidden via print CSS)
- Letter Portrait · No Chrome header/footer · `print-color-adjust: exact`

## Requirements

- Google Chrome (uses `--headless=new --print-to-pdf`)
- .NET 9 SDK (for the PDF merger tool — `tools/merge_pdf`)
- PowerShell 5.1+ (or 7+)

## First-time setup

```powershell
# Build the C# PDF merger once
cd tools/merge_pdf
dotnet build -c Release
cd ../..
```

## Usage

```powershell
# Build all volumes (≈15–20 min total)
.\tools\build_pdf.ps1

# Build a single volume (for iterating)
.\tools\build_pdf.ps1 -Volume VOL07

# Keep per-file intermediate PDFs and prep HTMLs under build/<VOL>/
.\tools\build_pdf.ps1 -Volume VOL07 -KeepIntermediate

# Update size labels on hub pages after rebuild
.\tools\inject_pdf_button.ps1
```

## How it works

1. **Read `tools/volumes.json`** — declares which HTML files belong to each volume and their order
2. **For each volume**:
   1. For each source `VOLxx_*.html`:
      - Create a **print-prep copy** at `build/<VOL>/NNN_<name>.prep.html` with `tools/print.css` injected into `<head>` (see "Print CSS" below)
      - Render that prep copy to PDF via Chrome headless (`--print-to-pdf`)
   2. Generate a **Cover page** (Bebas Neue title block + plant info + build date)
   3. Generate a **TOC page** from the section titles
   4. Merge `cover + toc + section PDFs` into one Volume PDF using `merge_pdf.exe`
3. Final PDFs are written to `dist/`

## Print CSS (`tools/print.css`)

The source `VOL*.html` files are designed for on-screen viewing — they have a fixed top nav bar, a 240px sticky left sidebar, and a flex-based two-column layout. Rendering them directly to PDF would push the actual content to the right and waste the left margin.

`tools/print.css` solves this by being injected into a **temporary copy** of each source HTML before Chrome renders it. Key rules:

```css
@media print {
  /* Hide nav chrome */
  .ames-nav, .sidebar, .nav-home, .nav-vol, .sb-*, .pdf-dl { display: none !important; }
  /* Reset layout to single column */
  .page-wrap { display: block !important; }
  .main { margin: 0; padding: 0; width: 100%; }
  /* Force colored backgrounds to print */
  * { print-color-adjust: exact; }
}
@page { size: Letter portrait; margin: 14mm 11mm 16mm 11mm; }
```

**Source HTML files are not modified** — the prep copies live under `build/<VOL>/` and are cleaned up unless `-KeepIntermediate` is passed.

## Why per-file render + merge?

The alternative (concatenate all HTML into one master file and print) doesn't work because each L3 spec uses fixed layouts (`position: sticky`, `height: 100vh`, `display: flex` sidebar layouts) that don't compose cleanly when stacked. Chrome's `--print-to-pdf` only paginates the initial viewport in that case (output capped at ~8 pages regardless of total content).

Rendering each file separately preserves its intended print layout and produces clean output.

## Modifying which files go in each Volume

Edit `tools/volumes.json`. Each volume entry has:

```json
{
  "code": "VOL07",
  "shortTitle": "Painting POP",
  "title": "Painting POP (PNT)",
  "subtitle": "Plan · LOT · Loading · Oven · ...",
  "files": ["VOL07_PNT_Painting.html", "VOL07_PNT01_Daily_Plan.html", "..."]
}
```

The `files` order is the order they appear in the final PDF. The special token `"__DEMO_APPENDIX__"` substitutes an auto-generated URL list of all 125 interactive demos.

## Updating L2 hub download buttons

`tools/inject_pdf_button.ps1` is idempotent — re-running after a rebuild just updates the file size labels in the existing `<a class="pdf-dl">` blocks. It also refreshes the download grid in `INDEX.html`.

Run it after every PDF rebuild:

```powershell
.\tools\inject_pdf_button.ps1
```

## File map

| File | Purpose |
|---|---|
| `tools/build_pdf.ps1` | Main build script (PowerShell) |
| `tools/inject_pdf_button.ps1` | Adds/updates "Download PDF" buttons in L2 hubs and INDEX |
| `tools/print.css` | Print stylesheet injected into source HTML before render |
| `tools/volumes.json` | Volume manifest (files in order per volume) |
| `tools/merge_pdf/` | C# tool (PdfSharpCore) for concatenating PDFs |

## Troubleshooting

**"Chrome.exe not found"** — Install Google Chrome to its default location, or edit the `$chromeCandidates` array in `build_pdf.ps1`.

**"Build merge_pdf first"** — Run the first-time setup above.

**PDF shows nav/sidebar still visible** — Ensure `tools/print.css` has the `@media print { ... }` rule for `.ames-nav` and `.sidebar`. The rule is class-based, so newly-added nav classes in source HTML need to be added to the selector list.

**Korean text in PDFs** — The print CSS doesn't translate content. To localize, modify the source `VOL*.html` and rebuild.

**Encoding errors when running PowerShell scripts** — Both `.ps1` files must be saved with UTF-8 BOM for PowerShell 5.1 to parse Unicode characters in string literals. Re-save with:
```powershell
$content = [System.IO.File]::ReadAllText('tools\inject_pdf_button.ps1')
[System.IO.File]::WriteAllText('tools\inject_pdf_button.ps1', $content, (New-Object System.Text.UTF8Encoding($true)))
```
