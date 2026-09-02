# AMES — A-MES Manufacturing Execution System
# Claude Code 가이드

## 프로젝트 개요

자동차 부품 제조 현장을 위한 MES(Manufacturing Execution System).
공장 터미널(POP), 핸디 스캐너(PDA), 사무실 포탈(Web), REST API로 구성된 다중 클라이언트 시스템.

- **회사**: Seyon (한국 자동차 부품사)
- **DB**: `AMES_DEV` @ SQL Server 2022, mixed-mode auth, user `ames_app` — **콜레이션 `Korean_Wansung_CI_AS`**
  - 기본(개발서버): `192.168.2.137` — 소스의 모든 활성 접속문자열이 여기를 가리킨다
  - 비상용(로컬): `localhost\MSSQLSERVER01` — **명명 인스턴스**다. 개발서버가 죽었을 때만 쓰며, 개발서버와 동일하게 유지한다
  - **`Connect Timeout=30` 을 낮추지 말 것.** 5로 두면 원격 + `Encrypt=True` 의 TLS 사전 로그인 핸드셰이크(실측 5초 초과)에 걸려 연결이 끊긴다. TCP 1433 은 열려 있어서 오진하기 쉽다
  - 전환은 파일 수정이 아니라 환경변수 `ConnectionStrings__AMES` 오버라이드로 한다
- **솔루션**: `src/AMES.sln` (Visual Studio 2022) — 11개 프로젝트

---

## 솔루션 구조

```
01_Shared/AMES.Contracts   ← DTO + Enum, 의존성 없음 (net10.0)
01_Shared/AMES.Devices     ← ZPL 라벨, 의존성 없음 (net10.0)
02_Data/AMES.Data          ← Repository 20개, ADO.NET + SqlClient (net10.0)
03_Pop/AMES.Pop            ← WinForms + BlazorWebView 하이브리드, 공장 터미널 (net10.0-windows)
04_Api/AMES.Api            ← Minimal API, PDA REST 서버 (net10.0)
05_Pda/AMES.Pda            ← .NET MAUI Blazor Hybrid, 핸디 스캐너 (net10.0-android/windows)
06_Web/AMES.Web            ← Blazor Server + ASP.NET Identity, 사무실 포탈 (net10.0)
07_Etc/AMES.InjAgent       ← WinForms 상주 에이전트, 사출기 Modbus/취출로봇 FEnet 수집 (net10.0-windows)
                              ※ 라벨 발행은 하지 않는다 — AMES.Pop 의 LabelDispatcher 담당
08_Tablet/AMES.Tablet      ← .NET MAUI Blazor Hybrid, 현장 태블릿 (net10.0-android/maccatalyst/windows)
                              ※ 현재 스캐폴드 단계 (Home/NotFound 만 존재), Data 직접 참조

03_Pop/AMES.Pop.Tests      ← 테스트
07_Etc/AMES.InjAgent.Tests ← 테스트
```

### 의존성 방향
```
Pop / Web / Pda / Tablet  →  Data  →  Contracts
Api                       →  Data  →  Contracts
Pda                       →  Api (HTTP)
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

# 사출 PLC 수집 에이전트 (PLC_Simulator 와 연동)
dotnet run --project src\07_Etc\AMES.InjAgent\AMES.InjAgent.csproj
```

**DB 전제조건**: ① `dist/create_database.sql`로 `AMES_DEV`를 **`COLLATE Korean_Wansung_CI_AS`**로 생성 → ② `dist/AMES_Schema.sql`(149개 테이블) 적용 후 실행. (스키마는 컬럼 COLLATE 미지정이라 DB 기본 콜레이션을 상속 — DB를 Korean으로 먼저 만들어야 함)

**솔루션 전체 빌드는 6~16분 걸린다**(MAUI: Pda, Tablet). **두 개를 동시에 돌리면 `NETSDK1047`·`MSB3061` 가짜 실패**가 나므로 순차 실행할 것.

---

## AMES.Web 배포

로컬 IIS(`w3wp`)로 구동된다. `dotnet run`/IIS Express 아님.

```powershell
tools\publish-web.ps1                               # 개발서버 복사용 패키지 → publish\AMES.Web
tools\publish-web.ps1 -Zip                          # + zip
tools\publish-web.ps1 -Target Local                 # 로컬 IIS 반영 (개발서버 DB)
tools\publish-web.ps1 -Target Local -DbTarget Local # 로컬 IIS 반영 (비상: 로컬 DB)
```

- **라이브 IIS 폴더로 직접 게시하면 반드시 실패한다** — `w3wp`가 `AMES.Web.dll`을 잡고 있다.
  `-Target Local`은 `app_offline.htm`을 먼저 떨궈 ANCM이 앱을 내리게 하므로 잠금이 풀리고 관리자 권한도 필요 없다.
- `**/Properties/PublishProfiles/`는 gitignore 대상 → 게시 프로필 수정은 그 PC에만 적용된다.
- `dotnet publish` CLI는 pubxml의 `PublishUrl`을 무시한다(VS 전용). `-o`로 지정할 것.

**앱풀 필수 설정** (`loadUserProfile` 뿐 아니라 **`setProfileEnvironment` 도** 켜야 한다. 후자는 IIS 관리자 UI에 없다):

```powershell
appcmd set apppool "AMES.Web" /processModel.loadUserProfile:true /processModel.setProfileEnvironment:true
```

끄면 Data Protection이 ephemeral 키를 써서 **앱풀 재활용마다 로그인 사용자가 전원 로그아웃**된다.
`dist/setup-iis.ps1`에는 이 두 설정이 빠져 있으니 그 스크립트로 구성한 서버는 따로 적용해야 한다.

**서버 반영 절차**: ① 앱풀 중지 또는 `app_offline.htm` 배치 → ② `publish\AMES.Web\*` 덮어쓰기 → ③ `app_offline.htm` 제거 / 앱풀 시작.
서버 사전 조건은 **ASP.NET Core 10 Hosting Bundle**(9.x만 있으면 HTTP 500.31), 앱풀 "관리 코드 없음", 배포 폴더에 앱풀 계정 읽기/실행 권한.

장애 원인은 **이벤트 뷰어 > 응용 프로그램 > `IIS AspNetCore Module V2`** 가 가장 확실하다. `web.config`의 stdout 로그는 `logs` 폴더 쓰기 권한이 없으면 조용히 실패한다.

---

## 구현된 화면 목록

### AMES.Pop — 공장 터미널 (WinForms + Blazor Hybrid)

터미널은 로그인 시 선택한 라인의 WC ProcessCode 로 모듈 자동 분기.

#### INJ (사출 공정) — 통합 메인 + 팝업 구조
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| Login | `Pages/Login.razor` | PIN 인증, 사원 선택 |
| INJ-MAIN | `Pages/InjMain.razor` | **통합 작업 화면** (기본 진입점) — 좌측 스테이션 BOP 품번 × 당일 PLAN/INPUT/NG/FINAL 그리드 + 스캔 실적확정 + 우측 패널 기능 버튼 (하단바 없음, 로그아웃은 상단바). WO 접수 없음: 품번 행 선택 → `WorkOrderRepository.FindOpenForItem` 이 열린 WO 를 자동 해석(`ConfirmByLotCode` 와 같은 규칙) |
| (팝업) | `Pages/InjPopups/ManualEntryPopup.razor` | 수동 실적 입력 (구 INJ-04 키패드) |
| (팝업) | `Pages/InjPopups/DefectPopup.razor` | 불량 입력 (구 INJ-05) |
| (팝업) | `Pages/InjPopups/AndonPopup.razor` | 안돈 — 전체 화면 오버레이 (구 INJ-08) |

대시보드(INJ-02)·작업지시 접수(INJ-03)·금형 교체(INJ-06)·생산 현황(INJ-07)은 미사용으로 삭제됨 (화면·팝업·레거시 WinForms 폼 포함).
구 단독 화면(`/inj02`~`/inj08` 라우트)도 모두 삭제됨 — INJ 는 INJ-MAIN + 팝업(수동입력·불량·안돈)만 남는다. 팝업 공통 셸은 `Pages/InjPopups/PopupShell.razor`.
좌측 품번 목록은 `MD_Bop.StationCode` = 세션 스테이션(`PopSessionDto.TerminalId`) 기준이고, 당일 수치는 `InjLotRepository.GetDailyItemSummary` — LOT 생성일 기준으로 `INPUT = FINAL + NG + 미확정` 이 성립한다. dev DB 는 `dist/seed_md_bop_inj_dev.sql` 로 ST-INJ-01 BOP 를 채운다.
INJ 는 `AcceptWo` 를 부르지 않으므로 `BumpStepCompleted` 가 첫 실적에서 단계·헤더를 `Released → In Progress` 로 올리고 `ActualStart` 를 찍는다. `TerminalLock` 은 INJ 에서 기록하지 않는다(IMG 는 `AcceptWo` 그대로).

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
로그인 화면에서 작업자가 Line/Station을 선택하면, 선택 라인의
`MD_Line.WCID → MD_WorkCenter.ProcessCode`(INJ/IMG/PNT/QC)로 모듈이 결정된다.
appsettings 의 `PopTerminal:ModuleCode`/`LineId`/`StationId` 는 제거됐다 — 매 로그인 선택.
모듈 코드는 `AppState.ModuleCode` 에 실리고, 라우팅과 라벨 디스패처 게이트가 이를 본다.

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

사출 자동수집 테이블(`PR_InjLot` · `MD_InjCondItem` · `PR_InjCondLog` · `PR_RobotInspection`)은 `dist/migrate_inj_agent.sql`, 금형 마스터(`MD_MoldColor` · `MD_MoldItem` · `MD_MoldLine`)는 `dist/migrate_mold_master.sql` 참조.
라벨 발행 선점 컬럼(`PR_InjLot.PrintClaimTS` · `PrintClaimStation`)은 `dist/migrate_inj_lot_print_claim.sql` — `migrate_inj_agent.sql` 적용 후에 실행하며, 이게 없으면 Pop 의 `LabelDispatcher` 가 동작하지 않는다.
LotNo 채번 기반(`SYS_LotSeq` · `MD_Line.LotPrefix` · `tbl_Lot.LotCode` 유니크 인덱스)은 `dist/migrate_lotno_rule.sql` — INJ 원천 Lot 과 실적 배치 Lot(`ProductionRepository.RecordCycle`, IMG-03 등)은 9자리 신규칙(`[년1][월1][일1][라인코드2][순번4]`, 년=A~Z 26년 순환)으로 `LotNoGenerator` 가 채번하며, `LotPrefix` 미등록 라인은 채번이 예외로 막힌다 (시드: INJ I1·I2 / IMG W1 / PNT P1·P2).
WO 공정 단계(`PP_WorkOrderRouting.CompletedQty` · 인덱스 · 백필)는 `dist/migrate_wo_step_line.sql` — `migrate_routing_step.sql` 다음에 적용. 이 뒤로 라인 배정·상태·완료수량의 정본은 단계 행이며 `PP_WorkOrder.LineID` 는 쓰지 않는다(컬럼만 잔존). Pop 은 단계 `LineID` 로 WO 를 받고, 실적은 `WorkOrderRepository.BumpStepCompleted` 한 곳으로만 반영된다.
백필된 WO 중 헤더 라인이 마지막 라인 단계가 아닌 건(예: A 라우팅을 INJ 라인으로 발행)은 첫 후속 실적에서 헤더 `CompletedQty` 가 마지막 단계 값으로 내려갈 수 있다 — PP-04 진척률이 한 번 감소해 보인다.

---

## 사출 라벨 발행 — 배포 순서 (중요)

라벨 발행 주체가 InjAgent 에서 Pop 으로 이전됐다. 두 프로세스는 독립 배포되므로 **순서를 지켜야 한다.**

```
1. dist/migrate_inj_lot_print_claim.sql   (클레임 컬럼)
2. AMES.InjAgent  신버전                   (발행 중단)
3. AMES.Pop       신버전                   (발행 시작)
```

롤백은 정확히 역순 (Pop → InjAgent).

**순서를 뒤집으면 안 되는 이유:**

| 잘못된 상태 | 결과 |
|---|---|
| Pop 신버전 + InjAgent 구버전 | **모든 LOT 이 두 장씩 나온다.** 에이전트가 생성 직후 뽑고, Pop 디스패처가 1초 뒤 같은 LOT 을 클레임해 또 뽑는다. 에이전트가 `PrintedCount` 를 올리기 전에 Pop 이 클레임하는 창이 실제로 열린다 |
| InjAgent 신버전 + Pop 구버전 | 라벨이 안 나온다. 미확정 LOT 목록의 재출력 버튼으로 복구 가능 — **안전한 실패 쪽이다** |
| 마이그레이션 없이 Pop 신버전 | 매 틱 `ClaimForPrint` 예외. **작업자 화면에는 아무 표시가 없고** 라벨만 안 나온다. 배포 전 컬럼 존재를 반드시 확인할 것 |

**운영 전제 2가지:**

- **INJ 라인의 자동 라벨 발행은 그 라인에 INJ 모듈로 로그인된 Pop 터미널이 있는 동안만 동작한다.**
  라인은 로그인 화면에서 선택되며(appsettings 고정 아님), 클레임이 세션 `LineId` 로 걸러진다.
  같은 라인에 여러 터미널이 로그인해도 클레임이 원자적이라 중복 발행은 없다.
- **Pop 재시작·재로그인은 워터마크를 리셋한다.** 그 이전의 미출력 LOT 은 자동 발행 대상에서 빠지고 재출력 버튼으로만 복구된다. 교대 인수인계 시 유의.

장애 추적은 `{PopTerminal:Printer:OutputDir}/dispatch-YYYYMMDD.log` — 무인 루프라 토스트로 알릴 수 없는 실패가 여기에만 남는다.

---

## WO 공정 단계 — 배포 순서

라인 배정·상태·완료수량의 정본이 헤더(`PP_WorkOrder.LineID`)에서 단계 행(`PP_WorkOrderRouting`)으로 이전됐다. DB·Pop·Web 이 독립 배포되므로 **순서를 지켜야 한다.**

```
1. dist/migrate_wo_step_line.sql   (컬럼·백필)
2. AMES.Pop                        신버전
3. AMES.Web                        신버전
```

롤백은 정확히 역순 (Web → Pop → 마이그레이션).

**순서를 뒤집으면 안 되는 이유:**

| 잘못된 상태 | 결과 |
|---|---|
| 구 Pop + 신 Web | 신 Web 이 발행한 WO 는 헤더 `LineID` 가 NULL 이라 **구 Pop 의 WO 목록에 아예 안 보인다.** 구 Pop 이 올린 실적은 헤더 `CompletedQty` 만 올리고 단계 행은 그대로라, 신 Pop 배포 후 단계 진척이 0 에서 다시 시작한다 |
| 마이그레이션 없이 신 바이너리 | `PP_WorkOrderRouting.CompletedQty`·`TerminalLock` 컬럼이 없어 **PP-04 라인 로드·Pop WO 목록 조회가 매번 예외.** 배포 전 컬럼 존재를 반드시 확인할 것 |
| 구 Web + 신 DB | 라벨 순서와 달리 **안전한 실패 쪽이다.** 구 Web 은 헤더 `LineID` 에 기록하고 단계는 `Pending` 으로 남으며, 마이그레이션 §3(백필)을 다시 돌리면 단계 행이 정리된다 |

**INJ 스테이션마다 `MD_Bop`(StationCode) 등록이 선행돼야 한다.** 비어 있으면 INJ-MAIN 좌측 패널이 비고(당일 실적 있는 품번만 "미등록"으로 뜸) 수동입력·불량 팝업이 동작하지 않는다 — 스캔 확정은 LOT 품번으로 WO를 찾으므로 계속 동작한다. dev 는 `dist/seed_md_bop_inj_dev.sql`, 운영은 MD-005 화면에서 등록.

---

## 다음 개발 항목

- `AMES.Pda`: 추가 모듈 (PP, MNT 등) MAUI 화면
- `AMES.Data`: inline SQL → `dbo.SP_*` 스토어드 프로시저 전환
- `AMES.Web`: 실제 데이터 바인딩 완성 (일부 화면 scaffold 상태)
- `AMES.Pop`: 추가 공정 모듈 (필요 시)
