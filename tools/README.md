# A-MES PDF Build Tools

Generates per-Volume PDF deliverables from the L3 specification HTML files.

## Output

Per-Volume PDFs land in `dist/`:

```
dist/
├── AMES_L3_VOL00_Foundation.pdf          (INDEX + ERD + DB Schema)
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
└── AMES_L3_VOL14_Demo_Appendix.pdf        (URLs to 125 interactive demos)
```

Each PDF has:
- Cover page (Volume number, title, build date, plant references)
- Table of Contents
- Each L3 spec page (rendered from HTML)
- Letter Portrait, no Chrome header/footer
- Bookmarks/links preserved where possible

## Requirements

- Google Chrome (uses `--headless=new --print-to-pdf`)
- .NET 9 SDK (for the PDF merger tool — `tools/merge_pdf`)
- PowerShell 5.1+ or 7+

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

# Keep per-file intermediate PDFs under build/<VOL>/
.\tools\build_pdf.ps1 -Volume VOL07 -KeepIntermediate
```

## How it works

1. Reads `tools/volumes.json` — defines which HTML files belong to each volume and the order
2. For each volume:
   1. Render each `VOLxx_*.html` to its own PDF via Chrome headless (`--print-to-pdf`)
   2. Generate a Cover page (`build/<VOL>/000_cover.html` → PDF)
   3. Generate a TOC page from the section titles
   4. Merge `cover + toc + section PDFs` into one Volume PDF using `merge_pdf.exe`
3. Final PDFs are written to `dist/`

## Why per-file render + merge?

The natural approach of "concatenate all HTML into one master file and print" doesn't work because each L3 spec uses fixed layouts (`position: sticky`, `height: 100vh`, `display: flex` sidebar layouts) that don't compose cleanly when stacked. Chrome's `--print-to-pdf` only paginates the initial viewport in that case (output capped at ~8 pages regardless of total content).

Rendering each file separately preserves its intended print layout and produces clean output.

## Modifying which files go in each Volume

Edit `tools/volumes.json`. Each volume entry has:

```json
{
  "code": "VOL07",
  "shortTitle": "Painting POP",
  "title": "Painting POP (PNT)",
  "subtitle": "Plan · LOT · Loading · Oven · ...",
  "files": ["VOL07_PNT_Painting.html", "VOL07_PNT01_Daily_Plan.html", ...]
}
```

The `files` order is the order they appear in the final PDF. The special token `"__DEMO_APPENDIX__"` substitutes an auto-generated URL list of all 125 interactive demos.
