# A-MES Office Web 화면 참조 문서 (Screen Reference)

> **출처**: https://mes-taehee.github.io/ames-docs/DEMO_Office_Web.html 데모 셸과 각 `DEMO_*.html` 화면에서 추출.
> **용도**: `AMES.Web`(Blazor Server, 사무실 포탈) 화면 구현·수정 시 UI/기능 기준으로 참조한다.
> 데모의 각 화면은 헤더(화면 ID · 영문 제목 · 라우트) → 필터 툴바 → KPI 카드 → 그리드/패널 → 등록·수정 모달 구조를 따른다.
> 라우트·컬럼·필드명은 이 문서를 기준으로 하되, 데모의 샘플 데이터(미국 지명·날짜 등)는 무시한다.
> 픽셀 단위 레이아웃·동작이 필요하면 **데모** 파일(리포지토리 루트의 같은 이름 HTML)을 직접 열어 확인하고, 설계 의도·데이터 규칙은 **상세 설계서**(`VOLxx_*.html`)를 본다.

## 모듈 구성 — 5모듈 · 74화면

| 모듈 | 이름 | 화면 수 | 라우트 prefix |
|------|------|---------|----------------|
| PP  | Production Planning (생산계획) | 13 | `/pp/*` |
| MNT | Maintenance (설비보전) | 9 | `/mnt/*` |
| RPT | Reports & Analytics (보고서) | 10 | `/rpt/*` |
| SYS | System Admin (시스템 관리) | 8 | `/sys/*` |
| MD  | Master Data (기준정보) | 29 + 카테고리 설계서 5 | `/md/*` |

MD 그룹의 메뉴 34개 중 5개(`MD L3-A~E`)는 화면이 아니라 카테고리별 상세 설계서 페이지다(문서 끝 참조).

---

## PP — Production Planning (생산계획) · 13화면

### PP-01 Forecast Management — `/pp/forecast`
- **개요**: Customer demand forecast import, mapping, and variance analysis
- **필터**: Customer (All / SAV / GEO) · Period (2025-Q4 / 2025-Q3 / 2026-Q1) · Item Search (입력: Item code or name...)
- **KPI 카드**: Mapped Items · Unmapped · Variance > 20% · Gap Alert · Last Upload
- **탭**: 📊 Forecast Status · 🕓 Upload History
- **그리드 컬럼**: Customer | Customer Item No. | A-MES Code Internal Code | Item Name | M+1 | M+2 | M+3 | M+4~6 Reference | Confirmed Order | Gap | Action
- **그리드 컬럼**: Uploaded | Customer | File | Rows | Mapped | Unmapped | Uploaded By
- **버튼/기능**: ↺ Reset · Excel Upload · Export
- **모달**: Excel Upload — Customer Forecast
- **모달**: Customer Item Mapping
- **데모**: `DEMO_PP01_Forecast.html` · **상세 설계서**: `VOL04_PP01_Forecast.html`

### PP-02 SAP B1 Supply Plan Import — `/pp/supply-plan/import`
- **개요**: Import confirmed sales orders from SAP Business One
- **필터**: Method (Excel Upload (Phase 1) / Service Layer API (Phase 2)) · Customer (All / SAV / GEO) · Status (All / Confirmed / Open) · Search (입력: SO·Item...)
- **KPI 카드**: Today's SOs · Confirmed · Open · Last Sync
- **그리드 컬럼**: SO No. | Customer | Item Code | Item Name | Order Qty | Due Date | Status | WO Link | Action
- **버튼/기능**: ↺ Reset · Sample Template · Excel Import
- **모달**: SAP B1 Supply Plan Excel Import
- **데모**: `DEMO_PP02_SAP_Import.html` · **상세 설계서**: `VOL04_PP02_SAP_Import.html`

### PP-03 Plan & Confirm — `/pp/plan`
- **개요**: Production planning — supply plan availability review and batch WO creation
- **필터**: Due From · Due To · Line (All / INJ-L1 / INJ-L2 / IMG-L1 / PNT-L1) · Search (입력: SO·Item...)
- **KPI 카드**: Plan Lines · Selected · WO Created · Phase-0 Blocked
- **그리드 컬럼**: SO No. | Item | Order | FG Stock | Net Req. | Forecast | Line Load | Due | Status
- **버튼/기능**: ↺ Reset · Calendar View · Batch Create WO (0)
- **모달**: Batch Create Work Orders — Confirm
- **데모**: `DEMO_PP03_Plan_Confirm.html` · **상세 설계서**: `VOL04_PP03_Plan_Confirm.html`

### PP-04 Work Order Management — `/pp/wo`
- **개요**: WO lifecycle: Draft → Released → In Progress → Closed
- **필터**: Status (All / Draft / Released / In Progress / Closed / Blocked) · Routing (All / A · INJ→IMG→QC→FG / B · INJ→PNT→QC→FG) · Search (입력: WO·Item·SO...)
- **KPI 카드**: Draft · In Drafting · Released · In Progress · ⚠ Blocked · D-3 Critical · D-Day Alert
- **그리드 컬럼**: WO No. | Item | Plan Qty | Routing | Progress | D-Day | Status | SAP SO | Action
- **버튼/기능**: ↺ Reset · Export · Manual WO
- **모달**: Create Manual Work Order
- **모달**: Cancel Work Order
- **데모**: `DEMO_PP04_Work_Order.html` · **상세 설계서**: `VOL04_PP04_Work_Order.html`

### PP-05 MRP — Material Requirements Planning — `/pp/mrp`
- **개요**: BOM explosion · shortage calculation · WO blocking · PR linkage
- **필터**: Scope (All Materials / Shortages Only / Sufficient Only) · Search (입력: Material code/name...)
- **KPI 카드**: Materials Calculated · Sufficient · Shortage · WO Blocked
- **그리드 컬럼**: Material | Required | Stock | On Order | Shortage/Surplus | L/T | Order Due | Status | Action
- **버튼/기능**: Run MRP · ↺ Reset · Create Shortage PRs · Export
- **모달**: Batch Create Shortage PRs
- **데모**: `DEMO_PP05_MRP.html` · **상세 설계서**: `VOL04_PP05_MRP.html`

### PP-06 Purchase Requisition → SAP B1 — `/pp/pr`
- **개요**: PR creation, SAP B1 Service Layer transmission, and PO tracking
- **필터**: Status (All / Draft / Sent / Approved / Failed) · Required From · Search (입력: PR·DocNum·Material...)
- **KPI 카드**: Draft · In Drafting · Sent · Transmitted · Approved · PO Created · Failed · Transmission Failed
- **그리드 컬럼**: PR No. | SAP DocNum | Material Item | Qty | Required | Linked WO | Status | Action
- **버튼/기능**: ↺ Reset · Export · Batch Send to SAP B1
- **모달**: Batch Send to SAP B1 — Confirm
- **데모**: `DEMO_PP06_Purchase_Req.html` · **상세 설계서**: `VOL04_PP06_Purchase_Req.html`

### PP-07 Work Order Release — `/pp/wo/release`
- **개요**: 4-gate validation before production start
- **필터**: Line (All / INJ-L1 / INJ-L2 / PNT-L1 / PNT-L2) · Result (All / Ready to Release / Blocked / Released) · Search (입력: WO·Item...)
- **KPI 카드**: Awaiting Release · Draft · Ready · Blocked · Released
- **그리드 컬럼**: WO No. | Item | Qty | Line | D-Day | G1·G2·G3·G4 Gates | Result | Action
- **버튼/기능**: ↺ Reset · Batch Release Ready WOs
- **모달**: WO Release Validation
- **데모**: `DEMO_PP07_WO_Release.html` · **상세 설계서**: `VOL04_PP07_WO_Release.html`

### PP-CAL Production Calendar — `/pp/calendar`
- **개요**: Monthly view of WO due dates · drag-and-drop reschedule
- **필터**: View (Month View / Week View / Day View) · Line (All / INJ-L1 / INJ-L2 / IMG-L1 / PNT-L1 / PNT-L2)
- **KPI 카드**: Month WOs · D-3 Critical · Overdue · Plan Qty Total
- **버튼/기능**: Prev · Next › · Today
- **모달**: Past Date Reschedule — Manager Approval
- **데모**: `DEMO_PP_CAL_Calendar.html` · **상세 설계서**: `VOL04_PP_CAL_Calendar.html`

### PP-OTD On-Time Delivery KPI — `/pp/otd`
- **개요**: OTD by customer · trend · delay root cause analysis
- **필터**: Customer (All / SAV / GEO) · Period (Last 12 Months / 2025-Q3 / 2025-Q2 / YTD 2025) · Root Cause (All Causes / Material Shortage / Equipment Breakdown / Quality Fail / Logistics Delay / Other)
- **KPI 카드**: SAV OTD · GEO OTD · Total Shipments · Delayed · Avg Delay
- **그리드 컬럼**: Shipment | Customer | Item | Due | Actual | Delay | Root Cause
- **패널/카드**: 12-Month OTD Trend · Delay Root Cause Pareto Click a row to filter
- **버튼/기능**: Apply · ↺ Reset · Export Delays · Clear Cause Filter
- **데모**: `DEMO_PP_OTD_Delivery.html` · **상세 설계서**: `VOL04_PP_OTD_Delivery.html`

### PP-LSB Line Schedule Board — `/pp/lsb`
- **개요**: Minute-level timeline of run, changeover, and maintenance blocks
- **필터**: Date (05/20/2026 (Wed) / 05/21/2026 (Thu) / 05/22/2026 (Fri)) · Plant (SAV Detroit, MI) · Line (All / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01)
- **KPI 카드**: Run Blocks · Planned Downtime · PM/Changeover · Planned Qty · Avg Line Utilization · Schedule Conflicts
- **패널/카드**: Minute-Level Schedule Timeline
- **버튼/기능**: Apply · ↺ Reset · ＋ Add Planned PM · Conflict Check · Export
- **모달**: Block Detail
- **모달 「Add Planned Maintenance (PM) Block」 입력 필드**: Target Line, Description, Start Hour, Duration (min)
- **데모**: `DEMO_PP_LSB_Line_Schedule.html` · **상세 설계서**: `VOL04_PP_LSB_Line_Schedule_Board.html`

### PP-ODM Operating · Downtime Monitor — `/pp/odm`
- **개요**: Real-time line uptime/downtime status monitoring
- **필터**: Plant (SAV Detroit, MI) · Line (All / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01) · Status (All / Running / Down)
- **KPI 카드**: Running Lines · Down Lines · Today's Downtime (min) · Avg Availability · Today's Events
- **그리드 컬럼**: Time | Line | Type | Reason | Duration (min) | Status
- **버튼/기능**: Apply · ↺ Reset · Export
- **모달 「Stop Line」 입력 필드**: Downtime Type, Reason
- **데모**: `DEMO_PP_ODM_Downtime_Monitor.html` · **상세 설계서**: `VOL04_PP_ODM_Operating_Downtime_Monitor.html`

### PP-OEE Line Availability · OEE — `/pp/oee`
- **개요**: Availability × Performance × Quality = Overall Equipment Effectiveness
- **필터**: Date (05/19/2026 (Mon) / 05/18/2026 (Sun) / Last 7-Day Avg) · Shift (All (Day · Night) / Day Shift / Night Shift) · Line (All / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01)
- **KPI 카드**: Overall OEE · Availability · Performance · Quality · Total Loss (min)
- **그리드 컬럼**: Line | Availability A | Performance P | Quality Q | OEE | Grade | Loss (min)
- **패널/카드**: OEE by Line — Click a row to drill down · Six Big Losses — All Lines · OEE Composition — All Lines
- **버튼/기능**: Apply · ↺ Reset · Export
- **데모**: `DEMO_PP_OEE_Line_OEE.html` · **상세 설계서**: `VOL04_PP_OEE_Line_Availability.html`

### PP-DTL Downtime Log Entry — `/pp/dtl`
- **개요**: Capture line stoppage reasons, durations, and corrective actions
- **필터**: Date (05/20/2026 (Wed) / 05/19/2026 (Tue)) · Line (All / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01) · Type (All / Planned / Unplanned)
- **KPI 카드**: Today's Entries · Total Downtime (min) · In Progress · Open · Top Cause
- **그리드 컬럼**: No | Line | Type | Reason | Start | End | Duration | Status
- **패널/카드**: New Downtime Entry · Today's Downtime Log
- **버튼/기능**: ↺ Reset · Export · Clear · ＋ Register Downtime Log
- **모달 「Close Downtime Entry」 입력 필드**: End Time *, Action Taken
- **모달**: Downtime Log Detail
- **데모**: `DEMO_PP_DTL_Downtime_Log.html` · **상세 설계서**: `VOL04_PP_DTL_Downtime_Log.html`

---

## MNT — Maintenance (설비보전) · 9화면

### MNT-01 Equipment Card — `/mnt/equipment`
- **개요**: Equipment master register / lookup
- **필터**: Equipment Type (All / Injection / Assembly / Paint / Inspection / Utility) · Status (All / Running / Under Inspection / Failure / Idle) · Search (입력: Name or code)
- **KPI 카드**: Total Equipment · Running · Under Inspection · Failure · Avg. Age
- **그리드 컬럼**: Equip Code | Name | Type | Line | Status
- **패널/카드**: Equipment List
- **버튼/기능**: ↺ Reset · ＋ New Equipment · Export
- **모달 「Register New Equipment」 입력 필드**: Equipment Name *, Type *, Line, Maker, Model, Install Date *, Capacity
- **데모**: `DEMO_MNT01_Equipment_Card.html` · **상세 설계서**: `VOL10_MNT01_Equipment_Card.html`

### MNT-02 Failure Register — `/mnt/failure`
- **개요**: Equipment failure intake / repair / completion workflow
- **필터**: Equipment (All) · Status (All / New / In Progress / Resolved) · Severity (All / Critical / High / Normal / Low)
- **KPI 카드**: Today's Failures · Unresolved · New · In Progress · Resolved · Avg MTTR · min
- **그리드 컬럼**: Failure ID | Equipment | Occurred | Symptom | Severity | Status | Repair (min)
- **버튼/기능**: ↺ Reset · ＋ Report Failure · Export
- **모달 「🚨 Report New Failure」 입력 필드**: Equipment *, Occurred At *, Severity *, Symptom *, Reporter
- **모달 「Complete Repair」 입력 필드**: Root Cause *, Action Taken *, Repair Completed At *
- **모달**: Failure Detail
- **데모**: `DEMO_MNT02_Failure_Register.html` · **상세 설계서**: `VOL10_MNT02_Failure_Register.html`

### MNT-03 OEE Analysis — `/mnt/oee`
- **개요**: Equipment OEE / Reliability (MTBF · MTTR) Trends
- **필터**: Equipment Type (All / Injection / Assembly / Paint / Inspection) · Period (Last 6 Months / Last 3 Months / 2026 YTD)
- **KPI 카드**: Average OEE · Avg MTBF · hours · Avg MTTR · minutes · Avg Availability · MTD Failures
- **그리드 컬럼**: Equipment | Type | OEE | 6-Month Trend | MTBF(h) | MTTR(min) | MTD Failures | Availability
- **패널/카드**: Equipment OEE / Reliability — click a row for trend · Monthly OEE Trend — Overall Average Availability A Performance P Quality Q
- **버튼/기능**: Apply · ↺ Reset · Export
- **데모**: `DEMO_MNT03_OEE_Analysis.html` · **상세 설계서**: `VOL10_MNT03_OEE_Analysis.html`

### MNT-04 Mold Management — `/mnt/mold`
- **개요**: Mold register · shot count · life tracking
- **필터**: Mold Type (All / 2-Plate / 3-Plate / Hot Runner) · Status (All / Available / Service Required / Under Maintenance / Scrap Pending) · Search (입력: Name or code)
- **KPI 카드**: Total Molds · Available · Service Needed / In · Near End of Life · ≥85% · Avg Life Used
- **그리드 컬럼**: Mold Code | Name | Shots / Limit | Life Used | Status
- **패널/카드**: Mold List
- **버튼/기능**: ↺ Reset · ＋ New Mold · Export
- **모달 「Update Shot Count」 입력 필드**: Additional Shots *
- **모달 「Register Mold Maintenance」 입력 필드**: Maintenance Type *, Reset Shot Count, Performed By
- **모달 「Register New Mold」 입력 필드**: Mold Name *, Product *, Mold Type, Cavities, Life Limit (Shots) *
- **데모**: `DEMO_MNT04_Mold_Management.html` · **상세 설계서**: `VOL10_MNT04_Mold_Management.html`

### MNT-05 PM Schedule — `/mnt/pm`
- **개요**: Preventive maintenance calendar / completion
- **필터**: Month (May 2026) · Equipment (All) · Status (All / Completed / Due Today / Overdue / Planned)
- **KPI 카드**: MTD PM Plan · Total · Completed · Upcoming · Overdue · PM Compliance
- **패널/카드**: May 2026 Preventive Maintenance Calendar Completed Today Overdue Planned
- **버튼/기능**: ↺ Reset · ＋ Add PM · Export
- **모달**: PM Task Detail
- **모달 「Add Preventive Maintenance」 입력 필드**: Equipment *, PM Type *, Scheduled Date *, Cycle, Assigned To
- **데모**: `DEMO_MNT05_PM_Schedule.html` · **상세 설계서**: `VOL10_MNT05_PM_Schedule.html`

### MNT-06 Downtime Log — `/mnt/downtime`
- **개요**: Equipment downtime history / root cause analysis
- **필터**: Period (May 2026 (MTD) / Last 7 Days / Apr 2026) · Equipment (All) · Cause (All Causes)
- **KPI 카드**: Total Events · Total Downtime · min · Equipment Failure % · Avg per Event · min · Worst Equipment
- **그리드 컬럼**: Date/Time | Equipment | Line | Cause | Downtime (min) | Note
- **패널/카드**: Downtime by Cause (Pareto) click row = filter · Downtime by Equipment (Rank)
- **버튼/기능**: Apply · ↺ Reset · Export · Clear Cause Filter
- **데모**: `DEMO_MNT06_Downtime_Log.html` · **상세 설계서**: `VOL10_MNT06_Downtime_Log.html`

### MNT-07 Maintenance Work Order — `/mnt/wo`
- **개요**: Maintenance WO issue / assign / progress / complete
- **필터**: Status (All / Issued / Assigned / In Progress / Completed) · Priority (All / Critical / High / Normal) · Type (All / Preventive / Corrective / Improvement)
- **KPI 카드**: Unassigned · Issued · Assigned · In Progress · Completed · Critical Open
- **그리드 컬럼**: WO Number | Equipment | Description | Type | Priority | Assignee | Status
- **버튼/기능**: ↺ Reset · ＋ Issue Work Order · Export
- **모달 「Issue Maintenance Work Order」 입력 필드**: Equipment *, Description *, Type *, Priority *, Target Completion
- **모달 「Assign Maintenance Technician」 입력 필드**: Assigned To *
- **모달 「Complete Maintenance」 입력 필드**: Result *, Duration (min) *, Parts Used
- **모달**: Work Order Detail
- **데모**: `DEMO_MNT07_Work_Order.html` · **상세 설계서**: `VOL10_MNT07_Work_Order.html`

### MNT-08 Spare Parts — `/mnt/spare`
- **개요**: MRO inventory · receive / issue management
- **필터**: Category (All / Bearings / Motor Parts / Electrical / Hydraulic / Mold Parts / Consumables) · Stock Status (All / Normal / Low / Out of Stock) · Search (입력: Name or code)
- **KPI 카드**: Total Items · Low Stock · Out of Stock · Inventory Value · Today's Txns
- **그리드 컬럼**: Part Code | Name | Category | On Hand | Stock % | Status
- **패널/카드**: Spare Parts List
- **버튼/기능**: ↺ Reset · ＋ Add Part · Export
- **모달 「Receive Stock」 입력 필드**: Receive Qty *, Reference
- **모달 「Add New Part」 입력 필드**: Part Name *, Category *, Storage Location, Initial Stock *, Safety Stock *, Unit Price ($)
- **데모**: `DEMO_MNT08_Spare_Parts.html` · **상세 설계서**: `VOL10_MNT08_Spare_Parts.html`

### MNT-09 Equipment Dashboard — `/mnt/dashboard`
- **개요**: Comprehensive maintenance overview
- **필터**: Plant (SAV Detroit, MI / GEO Birmingham, AL) · Period (Today / Last 7 Days / MTD)
- **KPI 카드**: Equipment Uptime · Average OEE · Today's Failures · Open Maint. WOs · Stock Alerts · Low / Out
- **패널/카드**: Equipment Status Distribution · Live Failure Alerts most recent · Upcoming Preventive Maintenance within 7 days · Downtime Causes — Top 5 MTD · minutes · Maintenance Work Orders · Low Stock Parts reorder recommended
- **버튼/기능**: Refresh · Export
- **데모**: `DEMO_MNT09_Dashboard.html` · **상세 설계서**: `VOL10_MNT09_Dashboard.html`

---

## RPT — Reports & Analytics (보고서/분석) · 10화면

### RPT-01 Daily Production Report — `/rpt/daily-prod`
- **개요**: Production Output by Line and Shift
- **필터**: Date (05/20/2026 (Wed) / 05/19/2026 (Tue) / 05/18/2026 (Mon)) · Line (All Lines / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01) · Shift (All (Day & Night) / Day Shift / Night Shift)
- **KPI 카드**: Total Production · Actual · Plan vs Actual · Achievement · Yield Rate · Defect Quantity · Planned Quantity
- **그리드 컬럼**: Line | Product | Plan | Actual | Achievement | Good | Defect | Yield
- **패널/카드**: Hourly Production Trend Click bar for details · Shift Summary
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT01_Daily_Production.html` · **상세 설계서**: `VOL11_RPT01_Daily_Production.html`

### RPT-02 Defect Pareto Analysis — `/rpt/defect-pareto`
- **개요**: Focused Analysis by Defect Type
- **필터**: Period (May 2026 (MTD) / Last 7 Days / April 2026) · Line (All Lines / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01) · Process (All / Injection / Assembly / Paint / Inspection)
- **KPI 카드**: Total Defects · Defect Rate · Worst Defect Type · Vital Few (≤80% Cumulative) · Inspected Quantity
- **그리드 컬럼**: Rank | Defect Type | Count | Ratio | Cumulative % | Process
- **패널/카드**: Defect Pareto Chart by Type Green Bars=Defect Count · Blue Dots=Cumulative% · Click Bar=Filter · Defect Distribution by Process
- **버튼/기능**: Apply · ↺ Reset · Export · Clear Filter
- **데모**: `DEMO_RPT02_Defect_Pareto.html` · **상세 설계서**: `VOL11_RPT02_Defect_Pareto.html`

### RPT-03 Daily Shipment Report — `/rpt/daily-ship`
- **개요**: Shipment Performance by Customer and Round
- **필터**: Date (05/20/2026 (Wed) / 05/19/2026 (Tue)) · Customer (All Customers / SAV / GEO) · Round (All / Round 1 / Round 2 / Round 3)
- **KPI 카드**: Shipped Quantity · Shipments · On-Time Shipment Rate · Late Shipments · Shipment Amount · $K USD
- **그리드 컬럼**: Shipment No. | Customer | Round | Item | Qty | Ship Time | Vehicle | Status
- **패널/카드**: Shipment by Customer · Shipment Summary by Round
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT03_Daily_Shipment.html` · **상세 설계서**: `VOL11_RPT03_Daily_Shipment.html`

### RPT-04 On-Time Delivery Report — `/rpt/otd`
- **개요**: OTD Analysis by Customer and Month
- **필터**: Period (Last 6 Months / 2026 YTD / Last 3 Months) · Customer (All Customers / SAV / GEO)
- **KPI 카드**: Overall OTD · On-Time Deliveries · Late Deliveries · Avg Delay Days · Customers Below Target
- **그리드 컬럼**: Item | Customer | Deliveries | On-Time | Late | Avg Delay Days | OTD
- **패널/카드**: OTD Heatmap by Customer × Month
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT04_OnTime_Delivery.html` · **상세 설계서**: `VOL11_RPT04_OnTime_Delivery.html`

### RPT-05 Inventory Status Report — `/rpt/inventory`
- **개요**: Inventory Aggregation by Warehouse and Category
- **필터**: As-of Date (05/20/2026 / 05/19/2026) · Warehouse (All Warehouses / Raw Material WH / WIP WH / Finished Goods WH) · Category (All / Raw Material / WIP / Finished Goods / Sub Material)
- **KPI 카드**: SKU Count · Inventory Value · $K USD · Below Safety Stock · Low · Over Stock · Avg Turnover · Times/Month
- **그리드 컬럼**: Item Code | Item Name | Category | Warehouse | On Hand | Safety Stock | Value | Turnover | Status
- **패널/카드**: Inventory Value Distribution by Category · Inventory by Warehouse
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT05_Inventory_Status.html` · **상세 설계서**: `VOL11_RPT05_Inventory_Status.html`

### RPT-06 Equipment OEE Report — `/rpt/oee`
- **개요**: Monthly Overall Equipment Effectiveness Summary
- **필터**: Month (May 2026 / April 2026 / March 2026) · Line (All Lines / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01)
- **KPI 카드**: Overall Plant OEE · Availability · Performance · Quality · Best Line
- **그리드 컬럼**: Line | Availability A | Performance P | Quality Q | OEE | vs Prev Month | Grade
- **패널/카드**: Monthly Plant OEE Trend Click bar for details · Best / Needs Improvement Line
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT06_Equipment_OEE.html` · **상세 설계서**: `VOL11_RPT06_Equipment_OEE.html`

### RPT-07 Monthly KPI Scorecard — `/rpt/monthly-kpi`
- **개요**: Key Performance Indicators Comprehensive Scorecard
- **필터**: Month (May 2026 / April 2026 / March 2026) · Category (All / Production / Quality / Delivery / Equipment / Cost)
- **KPI 카드**: Target Met · Pass · Near Target · Near · Below Target · Fail · Avg Achievement
- **그리드 컬럼**: KPI Indicator | Target | Actual | Achievement | 6-Month Trend | Status
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT07_Monthly_KPI.html` · **상세 설계서**: `VOL11_RPT07_Monthly_KPI.html`

### RPT-08 Schedule Adherence Report — `/rpt/adherence`
- **개요**: Production Plan vs Actual Adherence
- **필터**: Period (Last 10 Days / Current Month / Last 7 Days) · Line (All Lines / SAV-INJ-01 / SAV-INJ-02 / SAV-ASSY-01 / SAV-PNT-01)
- **KPI 카드**: Schedule Adherence · Planned Work Orders · On-Schedule · Late Completion · Incomplete
- **그리드 컬럼**: Work Order | Line | Item | Plan Qty | Actual Qty | Plan Date | Actual Date | Deviation (Days) | Status
- **패널/카드**: Daily Schedule Adherence Trend Click bar for details · Adherence by Line
- **버튼/기능**: Apply · ↺ Reset · Print · Export
- **데모**: `DEMO_RPT08_Schedule_Adherence.html` · **상세 설계서**: `VOL11_RPT08_Schedule_Adherence.html`

### RPT-09 Report Center — `/rpt/center`
- **개요**: Standard Report Catalog · Run · Subscribe
- **필터**: Category (All / Production / Quality / Delivery / Equipment / Inventory) · Search (입력: Report name) · View (All / Favorites Only)
- **KPI 카드**: Standard Templates · Favorites · Scheduled Subscriptions · Generated Today
- **패널/카드**: Report Catalog · Recently Generated Reports · Scheduled Subscriptions
- **버튼/기능**: ↺ Reset · Add Subscription
- **모달 「Schedule Report Subscription」 입력 필드**: Report, Frequency, Send Time, Recipients
- **데모**: `DEMO_RPT09_Report_Center.html` · **상세 설계서**: `VOL11_RPT09_Report_Center.html`

### RPT-10 Report Builder — `/rpt/builder`
- **개요**: Custom Analysis Report Designer
- **버튼/기능**: ↺ Reset · Export · Save Report
- **화면 구성**: 셀프서비스 리포트 디자이너. 좌측 빌더 패널에서 ① Data Source ② Group By(Row) ③ Measures ④ Period Filter(월/분기/YTD) ⑤ Visualization(Table·Bar·Line·Pie)을 선택하면 우측에 리포트가 즉시 생성. 버튼: Reset / Export / Save Report.
- **데모**: `DEMO_RPT10_Report_Builder.html` · **상세 설계서**: `VOL11_RPT10_Report_Builder.html`

---

## SYS — System Admin (시스템 관리) · 8화면

### SYS-01 User Management — `/sys/users`
- **개요**: Account registration, roles & status — SAV Detroit, MI
- **필터**: Dept (All / Production Operations / Quality Assurance / Maintenance / Logistics / Admin / Finance) · Role (All / System Administrator / Manager / Operator / View-Only) · Status (All / Active / Inactive / Locked) · Search (입력: Name / Username)
- **KPI 카드**: Total Users · Active Accounts · Inactive · Locked Accounts · Admin / Manager
- **그리드 컬럼**: User | Department | Role | Last Login | Status
- **패널/카드**: User List
- **버튼/기능**: ↺ Reset · ＋ New User · Export
- **모달 「New User」 입력 필드**: Full Name *, Username (ID) *, Department *, Title, Role *, Status, Email
- **데모**: `DEMO_SYS01_User_Management.html` · **상세 설계서**: `VOL12_SYS01_User_Management.html`

### SYS-02 RBAC Permissions — `/sys/rbac`
- **개요**: Role-based access control matrix
- **KPI 카드**: Defined Roles · Managed Modules · Users in Role · Granted Permissions
- **패널/카드**: Roles · Permission Matrix
- **버튼/기능**: Export · Save Permissions
- **화면 구성**: 좌측 역할(Roles) 목록에서 역할 선택 → 우측 권한 매트릭스(모듈 × 권한)의 셀을 클릭해 허용/회수 토글. 미저장 변경 표시 후 Save Permissions로 일괄 저장. 버튼: Export / Save Permissions.
- **데모**: `DEMO_SYS02_RBAC_Permissions.html` · **상세 설계서**: `VOL12_SYS02_RBAC_Permissions.html`

### SYS-03 Factory Calendar — `/sys/calendar`
- **개요**: Working days, off-days, special shifts & holidays
- **필터**: Month (May 2026) · Plant (SAV — Detroit, MI / GEO — Birmingham, AL)
- **KPI 카드**: Working Days · Off Days · Special Work · Holidays · Total Op Hours · h
- **패널/카드**: May 2026 — Plant Operating Calendar Working Off-Day Special Holiday
- **버튼/기능**: ↺ Restore Defaults · Export
- **모달 「Set Day Type」 입력 필드**: Operating Type, Note (holiday or special-shift reason)
- **데모**: `DEMO_SYS03_Factory_Calendar.html` · **상세 설계서**: `VOL12_SYS03_Factory_Calendar.html`

### SYS-04 Interface Monitor — `/sys/interface`
- **개요**: External system integration status monitoring
- **필터**: System (All / SAP S/4HANA / Equipment (OPC-UA) / Logistics (Barcode) / Microsoft Teams) · Status (All / Healthy / Error / Idle)
- **KPI 카드**: Total Interfaces · Healthy · Errors · Today's Messages · Avg Response · ms
- **그리드 컬럼**: Time | Interface | Direction | Records | Response (ms) | Result
- **버튼/기능**: ↺ Reset · Export
- **데모**: `DEMO_SYS04_Interface_Monitor.html` · **상세 설계서**: `VOL12_SYS04_Interface_Monitor.html`

### SYS-05 Audit Log — `/sys/audit`
- **개요**: User activity tracking & security audit
- **필터**: Period (Last 7 Days / Today / This Month) · User (All) · Action (All / Login / Logout / Create / Update / Delete / Approve / Permission) · Search (입력: Target / Module)
- **KPI 카드**: Total Events · Today's Logs · Sign-ins · Data Changes · CRUD · Security Events
- **그리드 컬럼**: Timestamp | User | Action | Module | Target | IP Address | Result
- **버튼/기능**: ↺ Reset · Export
- **모달**: Audit Log Detail
- **데모**: `DEMO_SYS05_Audit_Log.html` · **상세 설계서**: `VOL12_SYS05_Audit_Log.html`

### SYS-06 Notification Settings — `/sys/notification`
- **개요**: Event notification rules & channel management
- **필터**: Category (All / Production / Quality / Maintenance / Logistics / System) · Status (All / Active / Inactive)
- **KPI 카드**: Notification Rules · Active Rules · Inactive · Critical Alerts · Email Channel
- **버튼/기능**: ↺ Reset · Send Test · Export
- **모달 「Edit Notification Rule」 입력 필드**: Delivery Channels (multi-select), Recipient, Trigger Threshold, Severity
- **데모**: `DEMO_SYS06_Notification_Settings.html` · **상세 설계서**: `VOL12_SYS06_Notification_Settings.html`

### SYS-07 System Configuration — `/sys/config`
- **개요**: Global operating parameter management
- **필터**: Category (All / General / Production / Quality / Security / Interface) · Search (입력: Setting name)
- **KPI 카드**: Parameters · Categories · Modified · A-MES Version
- **버튼/기능**: ↺ Restore Defaults · Export · Save Config
- **데모**: `DEMO_SYS07_System_Config.html` · **상세 설계서**: `VOL12_SYS07_System_Config.html`

### SYS-08 System Health — `/sys/health`
- **개요**: Server, service & resource health monitoring
- **KPI 카드**: Services Up · CPU Usage · Memory Usage · Disk Usage
- **그리드 컬럼**: Time | Level | Component | Message
- **패널/카드**: Server & Service Status · Resource Utilization
- **버튼/기능**: Refresh · Export
- **데모**: `DEMO_SYS08_System_Health.html` · **상세 설계서**: `VOL12_SYS08_System_Health.html`

---

## MD — Master Data (기준정보) · 29화면

### MD-01 Item Master — `/md/item`
- **개요**: Item Master — Finished Goods · WIP · Materials
- **필터**: Item Type (All / Finished Good / WIP / Raw Material / Sub Material) · Status (All / Active / Inactive) · Search
- **KPI 카드**: Total Items · Finished Goods · Raw / Sub Materials · Active
- **그리드 컬럼**: Item Code | Item Name | Type | UOM | Status
- **패널/카드**: Item List
- **버튼/기능**: ↺ Reset · ＋ Add Item · Export
- **모달 「Add Item」 입력 필드**: Item Code *, Item Type * (Finished Good/WIP/Raw Material/Sub Material), Item Name *, UOM * (EA/SET/LB/FT/GAL), Safety Stock, Spec / Description, Customer (SAV/GEO/Common)
- **화면 구성**: 마스터-디테일 구조: 좌측 아이템 리스트에서 선택 → 우측 상세 패널 표시.
- **데모**: `DEMO_MD01_Item.html` · **상세 설계서**: `VOL13_MD01_Item.html`

### MD-02 Bill of Materials — `/md/bom`
- **개요**: BOM — Component Structure by Product
- **패널/카드**: Product List
- **버튼/기능**: ↺ Reset · ＋ Add Component · Export
- **화면 구성**: 좌측 제품 리스트에서 제품 선택 → 우측에 BOM 트리(구성품·수량·UOM·Loss Rate·재료비) 표시. KPI: BOM Products / Total Components / Avg Parts per Product / Selected Product Material Cost. 모달(Add BOM Component): Component*, Quantity*, UOM(EA·SET·LB·FT·GAL), Loss Rate(%), Level(1·2·3).
- **데모**: `DEMO_MD02_BOM.html` · **상세 설계서**: `VOL13_MD02_BOM.html`

### MD-03 BOM Version — `/md/bom-version`
- **개요**: BOM Version — Design Change History & Effective Period
- **엔티티**: BOM Version (표준 마스터 CRUD 패턴)
- **필터**: Status
- **KPI 카드**: Total BOM Versions · Active · Pending · Obsolete
- **그리드 컬럼**: Version Code | Target Product | Version | Effective From | Status
- **모달 「Add/Edit BOM Version」 입력 필드**: Version Code *, Target Product *, Version *, Effective From *, Effective To, Status, Change Reason, Author
- **데모**: `DEMO_MD03_BOM_Version.html` · **상세 설계서**: `VOL13_MD03_BOM_Version.html`

### MD-04 BOP Routing — `/md/bop`
- **개요**: BOP Routing — Operation Sequence & Standard Time by Product
- **엔티티**: Routing (표준 마스터 CRUD 패턴)
- **필터**: Target Product · Status
- **KPI 카드**: Total Operations · Active Operations · Avg C/T (sec) · Products Routed
- **그리드 컬럼**: Operation Code | Target Product | Sequence | Operation Name | Work Center | Status
- **모달 「Add/Edit BOP / Routing」 입력 필드**: Operation Code *, Target Product *, Operation Sequence *, Operation Name *, Work Center, Standard C/T (sec) *, Setup Time (min), Inspection Op, Status
- **데모**: `DEMO_MD04_BOP.html` · **상세 설계서**: `VOL13_MD04_BOP.html`

### MD-05 Work Center — `/md/work-center`
- **개요**: Work Center — Production Cell & Capacity Master
- **엔티티**: Work Center (표준 마스터 CRUD 패턴)
- **필터**: Line · Type · Status
- **KPI 카드**: Total Work Centers · Running · Maintenance · Total Hourly Capacity (EA/h)
- **그리드 컬럼**: Work Center Code | Work Center Name | Line | Type | Status
- **모달 「Add/Edit Work Center」 입력 필드**: Work Center Code *, Work Center Name *, Line, Type, Hourly Capacity (EA/h) *, Standard Headcount, Status
- **데모**: `DEMO_MD05_Work_Center.html` · **상세 설계서**: `VOL13_MD05_Work_Center.html`

### MD-06 Inspection Standard — `/md/inspection`
- **개요**: Inspection Standard — Quality Inspection Item & Spec Master
- **엔티티**: Inspection Std (표준 마스터 CRUD 패턴)
- **필터**: Process · Method · Status
- **KPI 카드**: Total Standards · Active · Measurement Items · Items Covered
- **그리드 컬럼**: Std Code | Target Item | Inspection Item | Method | Status
- **모달 「Add/Edit Inspection Std」 입력 필드**: Standard Code *, Target Item *, Inspection Item *, Process, Inspection Method, Standard / Spec *, LSL, USL, Sample Size, Status
- **데모**: `DEMO_MD06_Inspection_Std.html` · **상세 설계서**: `VOL13_MD06_Inspection_Std.html`

### MD-07 Vendor — `/md/vendor`
- **개요**: Vendor — Material & Outsourcing Supplier Master
- **엔티티**: Vendor (표준 마스터 CRUD 패턴)
- **필터**: Vendor Type · Status
- **KPI 카드**: Total Vendors · Active · Material Suppliers · Subcontractors
- **그리드 컬럼**: Vendor Code | Vendor Name | Vendor Type | Contact | Status
- **모달 「Add/Edit Vendor」 입력 필드**: Vendor Code *, Vendor Name *, Vendor Type, Tax ID (EIN), Contact, Phone, Payment Terms, Status
- **데모**: `DEMO_MD07_Vendor.html` · **상세 설계서**: `VOL13_MD07_Vendor.html`

### MD-08 Equipment Master — `/md/equipment`
- **개요**: Equipment — Production Equipment Master
- **엔티티**: Equipment (표준 마스터 CRUD 패턴)
- **필터**: Equipment Type · Line · Status
- **KPI 카드**: Total Equipment · Active · Maintenance · Injection Machines
- **그리드 컬럼**: Equipment Code | Equipment Name | Type | Line | Status
- **모달 「Add/Edit Equipment」 입력 필드**: Equipment Code *, Equipment Name *, Equipment Type, Installation Line, Manufacturer, Model, Install Date, Rated Capacity, Status
- **데모**: `DEMO_MD08_Equipment.html` · **상세 설계서**: `VOL13_MD08_Equipment.html`

### MD-09 Mold Master — `/md/mold`
- **개요**: Mold — Injection Mold Master
- **엔티티**: Mold (표준 마스터 CRUD 패턴)
- **필터**: Mold Type · Status
- **KPI 카드**: Total Molds · Active · Maintenance · Hot Runner Molds
- **그리드 컬럼**: Mold Code | Mold Name | Product | Cavities | Status
- **모달 「Add/Edit Mold」 입력 필드**: Mold Code *, Mold Name *, Product *, Mold Type, Cavities *, Life Limit (shots) *, Manufacturer, Storage Location, Status
- **데모**: `DEMO_MD09_Mold.html` · **상세 설계서**: `VOL13_MD09_Mold.html`

### MD-10 Paint / Fabric — `/md/paint-fabric`
- **개요**: Paint / Fabric — Coating & Interior Fabric Master
- **엔티티**: Paint / Fabric (표준 마스터 CRUD 패턴)
- **필터**: Material Type · Status
- **KPI 카드**: Total Paint / Fabric · Active · Paint Items · Fabric Items
- **그리드 컬럼**: Material Code | Material Name | Type | Color | Status
- **모달 「Add/Edit Paint / Fabric」 입력 필드**: Material Code *, Material Name *, Material Type, Color, Spec / Description, UOM, Supplier, Status
- **데모**: `DEMO_MD10_Paint_Fabric.html` · **상세 설계서**: `VOL13_MD10_Paint_Fabric.html`

### MD-11 Shipment Destination — `/md/shipment-dest`
- **개요**: Ship-To — Delivery Destination Master
- **엔티티**: Ship-To (표준 마스터 CRUD 패턴)
- **필터**: Customer · Region · Status
- **KPI 카드**: Total Ship-To Points · Active · SAV Ship-To · GEO Ship-To
- **그리드 컬럼**: Ship-To Code | Ship-To Name | Customer | Region | Status
- **모달 「Add/Edit Ship-To」 입력 필드**: Ship-To Code *, Ship-To Name *, Customer, Region, Address, Contact, Transport Lead Time (h), Status
- **데모**: `DEMO_MD11_Shipment_Dest.html` · **상세 설계서**: `VOL13_MD11_Shipment_Dest.html`

### MD-12 Customer — `/md/customer`
- **개요**: Customer — OEM Customer Master
- **엔티티**: Customer (표준 마스터 CRUD 패턴)
- **필터**: Country · Grade · Status
- **KPI 카드**: Total Customers · Active · Strategic Customers (A) · US Customers
- **그리드 컬럼**: Customer Code | Customer Name | Country | Grade | Status
- **모달 「Add/Edit Customer」 입력 필드**: Customer Code *, Customer Name *, Country, Customer Grade, Contact, Phone, Delivery Terms, Status
- **데모**: `DEMO_MD12_Customer.html` · **상세 설계서**: `VOL13_MD12_Customer.html`

### MD-13 Unit of Measure — `/md/uom`
- **개요**: UOM — Unit of Measure Master
- **엔티티**: UOM (표준 마스터 CRUD 패턴)
- **필터**: UOM Type · Status
- **KPI 카드**: Total UOMs · Active · Quantity UOMs · UOM Types
- **그리드 컬럼**: UOM Code | UOM Name | Type | Conversion | Status
- **모달 「Add/Edit UOM」 입력 필드**: UOM Code *, UOM Name *, UOM Type, Base Unit, Conversion to Base *, Description, Status
- **데모**: `DEMO_MD13_UOM.html` · **상세 설계서**: `VOL13_MD13_UOM.html`

### MD-14 Calendar Master — `/md/calendar`
- **개요**: Calendar — Plant Operating Calendar Master
- **엔티티**: Calendar (표준 마스터 CRUD 패턴)
- **필터**: Year · Type · Status
- **KPI 카드**: Total Calendars · Active · 2026 Calendars · Avg Working Days
- **그리드 컬럼**: Calendar Code | Calendar Name | Year | Type | Status
- **모달 「Add/Edit Calendar」 입력 필드**: Calendar Code *, Calendar Name *, Applicable Year, Calendar Type, Annual Working Days *, Annual Off-Days, Default Shift, Status
- **데모**: `DEMO_MD14_Calendar.html` · **상세 설계서**: `VOL13_MD14_Calendar.html`

### MD-15 Jig / Fixture Master — `/md/jig`
- **개요**: Jig / Fixture — Production Jig & Tooling Master
- **엔티티**: Jig (표준 마스터 CRUD 패턴)
- **필터**: Jig Type · Line · Status
- **KPI 카드**: Total Jigs · Active · Maintenance · Assembly Line Jigs
- **그리드 컬럼**: Jig Code | Jig Name | Type | Line | Status
- **모달 「Add/Edit Jig」 입력 필드**: Jig Code *, Jig Name *, Jig Type, Line, Applicable Item, Manufacturer, Cumulative Use Count, Status
- **데모**: `DEMO_MD15_Jig.html` · **상세 설계서**: `VOL13_MD15_Jig.html`

### MD-16 RFID Tag — `/md/rfid-tag`
- **개요**: RFID Tag — RFID tag registration & mapping master data
- **엔티티**: RFID Tag (표준 마스터 CRUD 패턴)
- **필터**: Tag Type · Status
- **KPI 카드**: Total RFID Tags · Active Tags · Pallet Tags · Lost / Unused
- **그리드 컬럼**: Tag ID | Type | Mapped Object | EPC Code | Status
- **모달 「Add/Edit RFID Tag」 입력 필드**: Tag ID *, Tag Type, Mapped Object *, EPC Code *, Frequency Band, Issue Date, Status
- **데모**: `DEMO_MD16_RFID_Tag.html` · **상세 설계서**: `VOL13_MD16_RFID_Tag.html`

### MD-17 RAL Paint Color — `/md/ral-color`
- **개요**: RAL Color — Paint color RAL code master data
- **엔티티**: RAL Color (표준 마스터 CRUD 패턴)
- **필터**: Color Family · Gloss · Status
- **KPI 카드**: Total RAL Colors · Production · Active · Pending Approval · Color Families
- **그리드 컬럼**: RAL Code | Color Name | Color Family | HEX | Status
- **모달 「Add/Edit RAL Color」 입력 필드**: RAL Code *, Color Name *, Color Family, HEX Value *, Gloss, Applied Paint, Status
- **데모**: `DEMO_MD17_RAL_Color.html` · **상세 설계서**: `VOL13_MD17_RAL_Color.html`

### MD-18 Oven Master — `/md/oven`
- **개요**: Oven — Paint cure oven master data
- **엔티티**: Oven (표준 마스터 CRUD 패턴)
- **필터**: Line · Oven Type · Status
- **KPI 카드**: Total Ovens · Running · Maintenance · Total Zones
- **그리드 컬럼**: Oven Code | Oven Name | Line | Std Temp (°F) | Status
- **모달 「Add/Edit Oven」 입력 필드**: Oven Code *, Oven Name *, Installed Line, Oven Type, Zone Count *, Standard Temp (°F) *, Control Range (±°F), Cure Time (min), Status
- **데모**: `DEMO_MD18_Oven.html` · **상세 설계서**: `VOL13_MD18_Oven.html`

### MD-19 RFID Reader — `/md/rfid-reader`
- **개요**: RFID Reader — RFID reader & gate master data
- **엔티티**: RFID Reader (표준 마스터 CRUD 패턴)
- **필터**: Install Zone · Reader Type · Status
- **KPI 카드**: Total RFID Readers · Online · Maintenance / Offline · Gate Readers
- **그리드 컬럼**: Reader Code | Reader Name | Install Zone | IP Address | Status
- **모달 「Add/Edit RFID Reader」 입력 필드**: Reader Code *, Reader Name *, Install Zone, Reader Type, Frequency Band, IP Address *, Antenna Count, Status
- **데모**: `DEMO_MD19_RFID_Reader.html` · **상세 설계서**: `VOL13_MD19_RFID_Reader.html`

### MD-20 Line Master — `/md/line`
- **개요**: Line — Production line master data
- **엔티티**: Line (표준 마스터 CRUD 패턴)
- **필터**: Plant · Process · Status
- **KPI 카드**: Total Lines · Running · Injection Lines · Total Capacity · EA/h
- **그리드 컬럼**: Line Code | Line Name | Plant | Process | Status
- **모달 「Add/Edit Line Master」 입력 필드**: Line Code *, Line Name *, Plant, Process, Capacity (EA/h) *, Shift Pattern, Start of Operation, Status
- **데모**: `DEMO_MD20_Line.html` · **상세 설계서**: `VOL13_MD20_Line.html`

### MD-21 Defect Code — `/md/defect-code`
- **개요**: Defect Code — Quality defect type code master data
- **엔티티**: Defect Code (표준 마스터 CRUD 패턴)
- **필터**: Category · Source Process · Status
- **KPI 카드**: Total Defect Codes · Active · Critical Defects · Defect Categories
- **그리드 컬럼**: Defect Code | Defect Name | Category | Source Process | Status
- **모달 「Add/Edit Defect Code」 입력 필드**: Defect Code *, Defect Name *, Defect Category, Source Process, Severity, Default Action, Description, Status
- **데모**: `DEMO_MD21_Defect_Code.html` · **상세 설계서**: `VOL13_MD21_Defect_Code.html`

### MD-22 Defect Cause — `/md/defect-cause`
- **개요**: Defect Cause — Root cause classification master data
- **엔티티**: Defect Cause (표준 마스터 CRUD 패턴)
- **필터**: 4M Category · Cause Level · Status
- **KPI 카드**: Total Defect Causes · Active · Root Cause Codes · 4M Categories
- **그리드 컬럼**: Cause Code | Cause Name | 4M Category | Level | Status
- **모달 「Add/Edit Defect Cause」 입력 필드**: Cause Code *, Cause Name *, 4M Category, Cause Level, Standard Countermeasure, Owning Department, Status
- **데모**: `DEMO_MD22_Defect_Cause.html` · **상세 설계서**: `VOL13_MD22_Defect_Cause.html`

### MD-23 Packaging Spec — `/md/packaging`
- **개요**: Packaging Spec — Per-item packaging specification master data
- **엔티티**: Packaging Spec (표준 마스터 CRUD 패턴)
- **필터**: Pack Type · Status
- **KPI 카드**: Total Packaging Specs · Active · Pallet Units · Distinct Items
- **그리드 컬럼**: Spec Code | Item | Pack Type | Qty per Box | Status
- **모달 「Add/Edit Packaging Spec」 입력 필드**: Spec Code *, Item *, Pack Type, Qty per Box/Tray *, Box Dim (in), Boxes per Pallet, Packaging Material, Status
- **데모**: `DEMO_MD23_Packaging_Spec.html` · **상세 설계서**: `VOL13_MD23_Packaging_Spec.html`

### MD-24 Label Template — `/md/label-template`
- **개요**: Label Template — Identification & shipping label master data
- **엔티티**: Label Template (표준 마스터 CRUD 패턴)
- **필터**: Label Type · Code Format · Status
- **KPI 카드**: Total Label Templates · Active · QR/RFID Used · Label Types
- **그리드 컬럼**: Template Code | Template Name | Type | Size | Status
- **모달 「Add/Edit Label Template」 입력 필드**: Template Code *, Template Name *, Label Type, Size (in) *, Code Format, Displayed Fields, Applied Printer, Status
- **데모**: `DEMO_MD24_Label_Template.html` · **상세 설계서**: `VOL13_MD24_Label_Template.html`

### MD-25 Reason Code — `/md/reason-code`
- **개요**: Reason Code — Business reason code master data
- **엔티티**: Reason Code (표준 마스터 CRUD 패턴)
- **필터**: Reason Category · Status
- **KPI 카드**: Total Reason Codes · Active · Approval Required · Reason Categories
- **그리드 컬럼**: Reason Code | Reason Name | Category | Used In | Status
- **모달 「Add/Edit Reason Code」 입력 필드**: Reason Code *, Reason Name *, Reason Category, Used In, Approval Required, Description, Status
- **데모**: `DEMO_MD25_Reason_Code.html` · **상세 설계서**: `VOL13_MD25_Reason_Code.html`

### MD-26 Common Code Group — `/md/code-group`
- **개요**: Common Code — System common code group master data
- **엔티티**: Common Code (표준 마스터 CRUD 패턴)
- **필터**: Code Group · Status
- **KPI 카드**: Total Common Codes · Active · Code Groups · Inactive Codes
- **그리드 컬럼**: Code | Code Name | Code Group | Sort | Status
- **모달 「Add/Edit Common Code Group」 입력 필드**: Code *, Code Name *, Code Group, Code Value, Sort Order, Description, Status
- **데모**: `DEMO_MD26_Code_Group.html` · **상세 설계서**: `VOL13_MD26_Code_Group.html`

### MD-27 Spare Parts — `/md/spare-parts`
- **개요**: Spare Parts — Equipment maintenance spare parts master data
- **엔티티**: Spare Part (표준 마스터 CRUD 패턴)
- **필터**: Category · Status
- **KPI 카드**: Total Spare Parts · Active · Categories · Safety Stock Value · $K
- **그리드 컬럼**: Part Code | Part Name | Category | Safety Stock | Status
- **모달 「Add/Edit Spare Parts」 입력 필드**: Part Code *, Part Name *, Category, Specification, UOM, Safety Stock *, Unit Price ($), Vendor, Status
- **데모**: `DEMO_MD27_Spare_Parts.html` · **상세 설계서**: `VOL13_MD27_Spare_Parts.html`

### MD-28 PM Template — `/md/pm-template`
- **개요**: PM Template — Preventive maintenance standard inspection template
- **엔티티**: PM Template (표준 마스터 CRUD 패턴)
- **필터**: Equipment Type · Frequency · Status
- **KPI 카드**: Total PM Templates · Active · Monthly+ Templates · Total Task Items
- **그리드 컬럼**: Template Code | Template Name | Equipment Type | Frequency | Status
- **모달 「Add/Edit PM Template」 입력 필드**: Template Code *, Template Name *, Target Equipment Type, Frequency, Task Count *, Standard Duration (min) *, Standard Crew Size, Key Inspection Items, Status
- **데모**: `DEMO_MD28_PM_Template.html` · **상세 설계서**: `VOL13_MD28_PM_Template.html`

### MD-29 Line Time Pattern — `/md/line-time-pattern`
- **개요**: Line Time Pattern — Per-line minute-level operating pattern
- **엔티티**: Time Pattern (표준 마스터 CRUD 패턴)
- **필터**: Line · Shift · Status
- **KPI 카드**: Total Time Patterns · Active · Applied Lines · Avg Net Work · min
- **그리드 컬럼**: Pattern Code | Pattern Name | Applied Line | Net Work (min) | Status
- **모달 「Add/Edit Line Time Pattern」 입력 필드**: Pattern Code *, Pattern Name *, Applied Line, Shift, Start Time *, End Time *, Break Time (min) *, Planned Downtime (min), Net Work Time (min) *, Status
- **데모**: `DEMO_MD29_Line_Time_Pattern.html` · **상세 설계서**: `VOL13_MD29_Line_Time_Pattern.html`

---

## MD 카테고리 상세 설계서 (L3 페이지)

MD 메뉴의 나머지 5개 항목은 기준정보를 카테고리로 묶은 상세 설계서다. 특정 마스터의 설계 배경·엔티티 관계가 필요할 때 연다.

| 카테고리 | 범위 | 파일 |
|----------|------|------|
| L3-A Foundation | 기반 마스터 | `VOL13_MD_L3_Foundation.html` |
| L3-B Routing & Production | 라우팅·생산 | `VOL13_MD_L3_Production.html` |
| L3-C Resources & Equipment | 자원·설비 | `VOL13_MD_L3_Resources.html` |
| L3-D RFID & Materials | RFID·자재 | `VOL13_MD_L3_RFID_Color.html` |
| L3-E Quality & Logistics | 품질·물류 | `VOL13_MD_L3_Quality.html` |
