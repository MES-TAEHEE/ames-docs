# A-MES L3 Design Specification — PDF Deliverables

## 통합 DB 생성 스크립트

`AMES_Schema.sql`은 2026-09-23의 `AMES_DEV` 실DB 구조를 기준으로 갱신했습니다.
PDA 구조를 포함한 테이블 173개, 컬럼 2,465개, 프로시저 39개, 외래키 28개,
CHECK 제약 5개와 `FG_Stock → dbo.FG_Inventory` 동의어를 포함합니다.
자료형·길이·NULL 허용·IDENTITY·기본값·계산 컬럼·콜레이션·인덱스와
설명 확장 속성도 반영했습니다. `TEST_*` 테이블 3개와 해당 제약은 제외했습니다.

이 파일은 포함된 테이블을 **삭제 후 재생성**합니다. 데이터 보존용 마이그레이션이
아니므로 기존 데이터가 필요한 DB에는 실행하지 마세요. 실DB 업무 데이터·로그인·
비밀번호를 내보낸 파일이 아니며, 파일 끝의 샘플 시드는 기존 저장소 내용을 유지합니다.
출하 API 공통코드 등 후속 시드는 `rebuild_db.sh`의 별도 단계입니다.

신규 구축은 `create_database.sql`로 DB/콜레이션을 준비한 뒤 통합 스크립트를 적용합니다.
`rebuild_db.sh`에서는 중복 PDA 스키마와 구형 `WorkerNo` 기반 마이그레이션을 제외했습니다.
`pda/PDA_SCHEMA.sql`은 기존 DB용 증분 스크립트로 남겨 두었으며 신규 구축에는 필요 없습니다.
새 구조 변경은 통합 생성 스크립트에도 반영해야 합니다.

로컬 임시 DB에서 생성 및 반복 실행을 검증하고, 실DB와 컬럼·인덱스 구성·외래키·
CHECK·프로시저·동의어 메타데이터를 비교했습니다. SQL Server 내부 컬럼 ID의 삭제 이력에
따른 공백과 자동 생성 PK 이름의 무작위 접미사는 비교에서 정규화했습니다.
`rebuild_db.sh`에 나열된 후속 SQL 30개도 같은 임시 DB에서 순서대로 실행해 통과했습니다.
검증용 임시 DB는 삭제했습니다. PDA 개발 시드는 앱 실행 전의 새 DB에서도 사용할 수
있도록 누락된 `Operator` 역할을 생성하며, 기존 역할이 있으면 재사용합니다.

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
| **VOL01** | Technology Stack | Architecture · .NET · Blazor · MAUI · MSSQL | [📄 AMES_L3_VOL01_Tech_Stack.pdf](AMES_L3_VOL01_Tech_Stack.pdf) |
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

## Database Cleanup

Use the cleanup script below when an existing local or shared development DB
still has old `SIS_TEST` demo objects from early SIS prototyping:

```powershell
# Local SQL Server Express
sqlcmd -S .\SQLEXPRESS -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\cleanup_legacy_sis_test.sql

# Shared development DB
sqlcmd -S 192.168.1.100,1433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\cleanup_legacy_sis_test.sql
```

The script removes the `SIS_TEST` schema, its procedures, tables, and seed data.
It does not touch current application objects under `dbo.WH_*`, `dbo.MD_*`, or
`dbo.FG_*`.

`dist/rebuild_db.sh` runs this cleanup automatically at the end of a full Docker
database rebuild.

---

**CONFIDENTIAL · For Internal Use Only**
