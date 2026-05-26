# A-MES L3 Design Specification — PDF Deliverables

Per-Volume PDF exports of the A-MES (Automotive Manufacturing Execution System) L3 detailed design specification for **SEOYON E-HWA**.

Each PDF contains:
- Cover page with Volume metadata
- Table of Contents
- All L3 spec pages (each L3 screen / entity)

Paper: **U.S. Letter Portrait** · No printer header/footer · Optimized for screen reading and double-sided printing.

## Download

| Volume | Title | Module | Direct Download |
|---|---|---|---|
| **VOL00** | Foundation | Index · ERD · DB Schema | [📄 AMES_L3_VOL00_Foundation.pdf](AMES_L3_VOL00_Foundation.pdf) |
| **VOL03** | Warehouse | WH | [📄 AMES_L3_VOL03_Warehouse.pdf](AMES_L3_VOL03_Warehouse.pdf) |
| **VOL04** | Production Planning | PP | [📄 AMES_L3_VOL04_Production_Planning.pdf](AMES_L3_VOL04_Production_Planning.pdf) |
| **VOL05** | Injection POP | INJ | [📄 AMES_L3_VOL05_Injection_POP.pdf](AMES_L3_VOL05_Injection_POP.pdf) |
| **VOL06** | Wrapping POP | IMG | [📄 AMES_L3_VOL06_Wrapping_POP.pdf](AMES_L3_VOL06_Wrapping_POP.pdf) |
| **VOL07** | Painting POP | PNT | [📄 AMES_L3_VOL07_Painting_POP.pdf](AMES_L3_VOL07_Painting_POP.pdf) |
| **VOL08** | Quality Control | QC | [📄 AMES_L3_VOL08_Quality_Control.pdf](AMES_L3_VOL08_Quality_Control.pdf) |
| **VOL09** | Finished Goods | FG | [📄 AMES_L3_VOL09_Finished_Goods.pdf](AMES_L3_VOL09_Finished_Goods.pdf) |
| **VOL10** | Maintenance | MNT | [📄 AMES_L3_VOL10_Maintenance.pdf](AMES_L3_VOL10_Maintenance.pdf) |
| **VOL11** | Reports & Analytics | RPT | [📄 AMES_L3_VOL11_Reports.pdf](AMES_L3_VOL11_Reports.pdf) |
| **VOL12** | System Administration | SYS | [📄 AMES_L3_VOL12_System_Admin.pdf](AMES_L3_VOL12_System_Admin.pdf) |
| **VOL13** | Master Data | MD | [📄 AMES_L3_VOL13_Master_Data.pdf](AMES_L3_VOL13_Master_Data.pdf) |
| **VOL14** | Demo Appendix | (URLs to 125 demos) | [📄 AMES_L3_VOL14_Demo_Appendix.pdf](AMES_L3_VOL14_Demo_Appendix.pdf) |

> File sizes and page counts vary per build. Open the PDF in any standard viewer (Adobe Acrobat Reader, Preview, modern web browsers all work).

## Reference plant context

The sample data throughout the L3 specs assumes a U.S. automotive parts plant operating two facilities:

- **SAV** — Detroit, MI
- **GEO** — Birmingham, AL

Customer accounts referenced: Ford, General Motors, Stellantis, Hyundai Motor Manufacturing Alabama (HMMA), BYD.

## Interactive demos

These PDFs are static deliverables. The interactive **HTML demos** (125 screens) live alongside the specs and are linked from VOL14 (Demo Appendix) and from `INDEX.html`. To explore the demos, open:

- [Demo Site Map](../DEMO_Site_Map.html) — overview of all 3 systems
- [Office Web Demo](../DEMO_Office_Web.html) — Planner/Engineer web (74 screens)
- [Shop-Floor POP Demo](../DEMO_ShopFloor_POP.html) — Operator touch terminal (33 screens)
- [Logistics PDA Demo](../DEMO_Logistics_PDA.html) — Handheld scanner (18 screens)

## Regenerating

To rebuild PDFs after modifying source HTML:

```powershell
# All volumes (~15–20 minutes)
.\tools\build_pdf.ps1

# Single volume (faster iteration)
.\tools\build_pdf.ps1 -Volume VOL07

# Update size labels on hub pages after rebuild
.\tools\inject_pdf_button.ps1
```

See [`tools/README.md`](../tools/README.md) for build internals.

---

**CONFIDENTIAL · For Internal Use Only**
