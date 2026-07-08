# AMES — A-MES Manufacturing Execution System
# Claude Code 가이드

## 프로젝트 개요

자동차 부품 제조 현장을 위한 MES(Manufacturing Execution System).
공장 터미널(POP), 핸디 스캐너(PDA), 사무실 포탈(Web), REST API로 구성된 다중 클라이언트 시스템.

- **회사**: Seyon (한국 자동차 부품사)
- **DB**: `AMES_DEV` @ SQL Server 2022 (`localhost`, mixed-mode auth, user `ames_app`)
- **솔루션**: `src/AMES.sln` (Visual Studio 2022)

---

## 솔루션 구조

```
01_Shared/AMES.Contracts   ← DTO + Enum, 의존성 없음 (net10.0)
02_Data/AMES.Data          ← Repository 20개, ADO.NET + SqlClient (net10.0)
03_Pop/AMES.Pop            ← WinForms + BlazorWebView 하이브리드, 공장 터미널 (net10.0-windows)
04_Api/AMES.Api            ← Minimal API, PDA REST 서버 (net10.0)
05_Pda/AMES.Pda            ← .NET MAUI Blazor Hybrid, 핸디 스캐너 (net10.0-android/windows)
06_Web/AMES.Web            ← Blazor Server + ASP.NET Identity, 사무실 포탈 (net10.0)
```

### 의존성 방향
```
Pop / Web / Pda  →  Data  →  Contracts
Api              →  Data  →  Contracts
Pda              →  Api (HTTP)
```

---

## 기술 스택

| 항목 | 내용 |
|------|------|
| .NET | 10.0 |
| C# | nullable enable, implicit usings |
| DB 접근 | ADO.NET (raw SqlCommand) — ORM 없음 (operational data) |
| ORM | EF Core 10.0.0 — AMES.Web Identity 테이블 전용 |
| WinForms UI | `Microsoft.AspNetCore.Components.WebView.WindowsForms` 10.0.0 |
| API | ASP.NET Minimal API (`MapGroup` 패턴) |
| MAUI | `Microsoft.Maui.Controls` + `Microsoft.AspNetCore.Components.WebView.Maui` |
| 인증 | Pop: PIN 기반 커스텀 / Web: ASP.NET Identity 쿠키 / API: Bearer Token (`TokenStore`) |

---

## 빌드 및 실행

```powershell
# 전체 빌드
dotnet build src\AMES.sln

# 공장 터미널
dotnet run --project src\03_Pop\AMES.Pop\AMES.Pop.csproj

# REST API (PDA 서버)
dotnet run --project src\04_Api\AMES.Api\AMES.Api.csproj

# 사무실 웹
dotnet run --project src\06_Web\AMES.Web\AMES.Web.csproj
```

**DB 전제조건**: `dist/AMES_Schema.sql` (149개 테이블)을 `AMES_DEV`에 적용 후 실행.

---

## 구현된 화면 목록

### AMES.Pop — 공장 터미널 (WinForms + Blazor Hybrid)

터미널은 `appsettings.json`의 `PopTerminal:ModuleCode` (또는 `LineId` 접두사)로 모듈 자동 분기.

#### INJ (사출 공정) — 8화면
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| Login | `Pages/Login.razor` | PIN 인증, 사원 선택 |
| INJ-02 | `Pages/Inj02Dashboard.razor` | 라인 대시보드, 시간대별 생산량 |
| INJ-03 | `Pages/Inj03WoConfirm.razor` | 작업지시 확인/접수 |
| INJ-04 | `Pages/Inj04ProductionEntry.razor` | 생산 실적 입력 |
| INJ-05 | `Pages/Inj05Defect.razor` | 불량 입력 |
| INJ-06 | `Pages/Inj06MoldChange.razor` | 금형 교체 |
| INJ-07 | `Pages/Inj07ProdStatus.razor` | 생산 현황 |
| INJ-08 | `Pages/Inj08Andon.razor` | 안돈 (라인 정지 요청) |

#### IMG (원단/래핑 공정) — 6화면
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| IMG-02 | `Pages/Img02Dashboard.razor` | 래핑 라인 대시보드 |
| IMG-03 | `Pages/Img03ProductionEntry.razor` | 생산 실적 입력 |
| IMG-04 | `Pages/Img04FabricInput.razor` | 원단 투입 |
| IMG-05 | `Pages/Img05Defect.razor` | 불량 입력 |
| IMG-06 | `Pages/Img06BondSetup.razor` | 본딩 설정 |
| IMG-07 | `Pages/Img07ProdStatus.razor` | 생산 현황 |

#### PNT (도장 공정) — 9화면
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| PNT-01 | `Pages/Pnt01DailyPlan.razor` | 일일 도장 계획 |
| PNT-02 | `Pages/Pnt02LotPreIssue.razor` | 로트 사전 불출 |
| PNT-03 | `Pages/Pnt03Loading.razor` | 행거 로딩 |
| PNT-04 | `Pages/Pnt04LineBoard.razor` | 도장 라인 현황판 |
| PNT-05 | `Pages/Pnt05OvenMonitor.razor` | 오븐 온도 모니터 |
| PNT-06 | `Pages/Pnt06Unloading.razor` | 언로딩 |
| PNT-07 | `Pages/Pnt07LabelApply.razor` | 라벨 부착 |
| PNT-08 | `Pages/Pnt08Defect.razor` | 불량 입력 |
| PNT-09 | `Pages/Pnt09ShiftReport.razor` | 교대 보고 |

#### QC (품질 공정) — 9화면
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| QC-01 | `Pages/Qc01Incoming.razor` | 수입 검사 |
| QC-02 | `Pages/Qc02InProcess.razor` | 공정 검사 |
| QC-03 | `Pages/Qc03Final.razor` | 최종 검사 |
| QC-04 | `Pages/Qc04Ncr.razor` | 부적합 보고 (NCR) |
| QC-05 | `Pages/Qc05Hold.razor` | 홀드 관리 |
| QC-06 | `Pages/Qc06Capa.razor` | 시정 조치 (CAPA) |
| QC-07 | `Pages/Qc07Dashboard.razor` | 품질 대시보드 |
| QC-08 | `Pages/Qc08InspectionStd.razor` | 검사 기준 |
| QC-TRC | `Pages/QcTrcTraceability.razor` | 추적성 (트레이서빌리티) |

모든 모듈에 Help 다이어그램 포함 (`Pages/Help/`).

---

### AMES.Pda — 핸디 스캐너 (MAUI Blazor Hybrid)

API 서버(`AMES.Api`)와 HTTP 통신. Bearer Token 인증.

#### WH (창고) — 8화면
`Wh01InboundSchedule` / `Wh02PdaInbound` / `Wh03InventoryStatus` / `Wh04LocationMap`
`Wh05InventoryAdjust` / `Wh06ReleaseSchedule` / `Wh07PdaRelease` / `Wh08TransactionHistory`

#### FG (완성품 출하) — 10화면
`Fg01Stocking` / `Fg02Inventory` / `Fg03ShipmentOrder` / `Fg04FifoPicking` / `Fg05Loading`
`Fg06DeliveryNote` / `Fg07DayEndClose` / `Fg08ShipmentHistory` / `Fg09Dashboard` / `FgRtnReturn`

---

### AMES.Web — 사무실 포탈 (Blazor Server)

ASP.NET Identity 쿠키 인증. 개발 기본 계정: `admin@ames.local / Dev2026!`

#### PP (생산계획) — 13화면
`WorkOrder` / `WoRelease` / `LineSchedule` / `Calendar` / `PlanConfirm`
`Forecast` / `Delivery` / `Mrp` / `SupplyPlanImport` / `PurchaseReq`
`Oee` / `Downtime` / `DowntimeMonitor`

#### MNT (설비보전) — 9화면
`Dashboard` / `EquipmentCard` / `WorkOrder` / `PmSchedule` / `Downtime`
`Failure` / `Mold` / `OeeAnalysis` / `SpareParts`

#### RPT (보고서) — 10화면
`DailyProduction` / `DailyShipment` / `DefectPareto` / `EquipmentOee`
`Inventory` / `MonthlyKpi` / `OnTime` / `ScheduleAdherence`
`ReportBuilder` / `ReportCenter`

#### SYS (시스템) — 8화면
`Users` / `Rbac` / `Audit` / `Health` / `Notifications`
`Config` / `Interfaces` / `Calendar`

---

### AMES.Api — REST API

Minimal API, Bearer Token 인증. 기본 포트: `https://localhost:7xxx`

| 그룹 | prefix | 설명 |
|------|--------|------|
| Auth | `/api/auth` | 로그인, 토큰 발급 |
| WH | `/api/wh` | 창고 (PDA용) |
| FG | `/api/fg` | 완성품 출하 |
| PP | `/api/pp` | 생산계획 |
| MNT | `/api/mnt` | 설비보전 |
| RPT | `/api/rpt` | 보고서 |
| SYS | `/api/sys` | 시스템 관리 |

Health check: `GET /api/health`

---

## 아키텍처 원칙

### Repository 패턴
- 모든 DB 접근은 `AMES.Data.Repositories.*Repository` 경유
- 각 메서드마다 `using var conn = _connFactory.OpenConnection()` (connection-per-method)
- `MapToDto()` static 헬퍼로 `SqlDataReader` → DTO 변환
- 향후 `dbo.SP_*` 스토어드 프로시저로 전환 예정 (현재 inline SQL)

### Pop 모듈 분기
`AppConfig.Current.ModuleCode` 값(`INJ` / `IMG` / `PNT` / `QC`)으로 라우팅.
`appsettings.json`에 명시하거나 `LineId` 접두사에서 자동 추론.

### 인증 흐름
- **Pop**: `PopAuthService` → `AuthRepository.ValidateLogin()` → `PinHasher` (PBKDF2) → `PopSessionRepository.CreateSession()`
- **Api**: `POST /api/auth/login` → `TokenStore.Issue()` → Bearer 헤더 검증 (`BearerAuth` 미들웨어)
- **Web**: ASP.NET Identity, `ApplicationDbContext` (EF Core, Identity 테이블 전용)

---

## 코드 컨벤션

- **네임스페이스**: `AMES.<Project>.<Subfolder>` (e.g. `AMES.Pop.Pages`, `AMES.Data.Repositories`)
- **DTO**: `AMES.Contracts.Dto.*Dto` — 계산 프로퍼티 허용 (`ProgressPct`, `DaysToDue` 등)
- **Enum**: `AMES.Contracts.Enums.*` (`ItemType`, `AuthResult`, `AuthMethod`)
- **Pop 공통 컴포넌트**: `Common/` — `AppConfig`, `PopServices`, `ToastService`, `ConfirmService`, `HelpModal`
- **주석**: 비명확한 WHY에만 최소 작성, WHAT 설명 주석 금지
- **Pop 화면 파일명**: `{ModuleCode}{화면번호}{기능명}.razor` (e.g. `Inj04ProductionEntry.razor`)

---

## DB 스키마 영역

149개 테이블, 기능 접두사로 구분:

| 접두사 | 영역 |
|--------|------|
| `HR_` | 인사 (사원, 부서) |
| `MD_` | 마스터 데이터 (품목, 고객, BOM) |
| `PP_` | 생산계획 (작업지시, 일정) |
| `PR_` | 생산 실적 (생산량, 불량) |
| `WH_` | 창고 (입출고, 재고) |
| `FG_` | 완성품 출하 |
| `MNT_` | 설비보전 |
| `QC_` | 품질 |
| `SYS_` | 시스템 (감사로그, 설정) |
| `Auth_` | 인증 (PIN 해시, 세션) |

---

## 다음 개발 항목

- `AMES.Pda`: 추가 모듈 (PP, MNT 등) MAUI 화면
- `AMES.Data`: inline SQL → `dbo.SP_*` 스토어드 프로시저 전환
- `AMES.Web`: 실제 데이터 바인딩 완성 (일부 화면 scaffold 상태)
- `AMES.Pop`: 추가 공정 모듈 (필요 시)
