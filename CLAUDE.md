# AMES — A-MES Manufacturing Execution System
# Claude Code 가이드

## 프로젝트 개요

자동차 부품 제조 현장을 위한 MES(Manufacturing Execution System).
공장 터미널(POP), 핸디 스캐너(PDA), 사무실 포탈(Web), REST API로 구성된 다중 클라이언트 시스템.

- **회사**: Seyon (한국 자동차 부품사)
- **DB**: `AMES_DEV` @ SQL Server 2022, mixed-mode auth, user `ames_app` — **콜레이션 `Korean_Wansung_CI_AS`**
  - 기본(개발서버): `192.168.0.132` — 소스의 모든 활성 접속문자열이 여기를 가리킨다 (`192.168.2.137` → 09-03 `192.168.1.137` → 09-04 `192.168.1.100` → 10-06 `192.168.1.102` → 10-07 `192.168.0.132`(서브넷도 바뀜, MAC 은 10-06 과 같음 — ping 은 응답하지 않으니 ARP MAC·`@@SERVERNAME` 으로 확인), `dist/*.sql` 머리말의 적용 예시는 구 IP 그대로). 개발서버 `DESKTOP-POEDIBS` 는 Wi-Fi·DHCP 라 **IP 가 떠다닌다** — 09-04 에는 DHCP 를 잃고 `169.254.x` 로 떨어져 로그인이 500 났다. 공유기 DHCP 예약이 근본 대책 — MAC 은 09-04 `64-cb-a3-f6-ed-53`, 10-06 `.102` 는 `34-13-e8-c7-36-8e`(다른 어댑터로 붙은 것으로 보인다 — 예약은 실제 쓰는 어댑터 MAC 으로)
  - **IP 가 바뀌면 게시본도 다시 올릴 것.** 배포본 `appsettings` 가 구 IP 로 남으면 기동 시드가 접속 시도에 붙잡혀 ANCM `startupTimeLimit`(기본 120초)을 넘기고 **모든 요청이 HTTP 500** 이 된다. 이벤트 로그에는 `Managed server didn't initialize after 120000 ms` 로 찍힌다
  - 비상용(로컬): `localhost\MSSQLSERVER01` — **명명 인스턴스**다. 개발서버가 죽었을 때만 쓰며, 개발서버와 동일하게 유지한다
  - **`Connect Timeout=30` 을 낮추지 말 것.** 5로 두면 원격 + `Encrypt=True` 의 TLS 사전 로그인 핸드셰이크(실측 5초 초과)에 걸려 연결이 끊긴다. TCP 1433 은 열려 있어서 오진하기 쉽다
  - 전환은 파일 수정이 아니라 환경변수 `ConnectionStrings__AMES` 오버라이드로 한다
- **솔루션**: `src/AMES.sln` (Visual Studio 2022) — 13개 프로젝트

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

02_Data/AMES.Data.Tests    ← AMES.Data 테스트 (순수 함수 + AMES_DEV 통합, net10.0)
04_Api/AMES.Api.Tests      ← AMES.Api 테스트 (ScheduledWorker 스케줄 규칙·수동 실행 HTTP 규칙, DB 불필요)
03_Pop/AMES.Pop.Tests      ← AMES.Pop · AMES.Devices(ZPL·스캔 파서) 테스트
07_Etc/AMES.InjAgent.Tests ← AMES.InjAgent 테스트만 (PLC 코덱·FEnet·폴러)
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

**DB 전제조건**: ① `dist/create_database.sql`로 `AMES_DEV`를 **`COLLATE Korean_Wansung_CI_AS`**로 생성 → ② `dist/AMES_Schema.sql`(176개 테이블 — 10-08 개발 DB 에서 SMO 로 다시 뜬 최종 구조) 적용 → ③ `dist/migrate_*.sql` 후 실행(스키마가 이미 최종 상태라 대부분 건너뛴다). DB를 Korean 콜레이션으로 먼저 만들어야 한다.
**스키마 파일 재생성**(10-08): `tools/schema_from_db`(`dotnet run -- "<개발 DB 연결문자열>" dist/AMES_Schema.sql <출력>`)가 개발 DB 의 테이블·키·인덱스·기본값·CHECK·외래키·컬럼 설명·프로시저를 SMO 로 뜨고(`TEST_*`·SSMS 다이어그램 객체 제외), 기존 파일의 저장소 시드(`-- Repository sample seeds` ~ `-- Persistent box labels.` 직전)를 뒤에 붙이며 `SYS_Screen` 시드는 개발 DB 현재 값으로 다시 뜬다. 예전 파일 꼬리에 이어 붙어 있던 마이그레이션 블록(SCM 박스·케이스, FG 출하·재고 통합 등)은 개발 DB 에 이미 반영돼 버렸다. 확인은 빈 DB 에 새 파일을 돌린 뒤 개발 DB 와 테이블·컬럼(순서 등수·형식·NULL·기본값·콜레이션)·키·인덱스·외래키·CHECK·프로시저 정의를 비교해 차이 0 이어야 한다. **재구축 주의**: `rebuild_db.sh` 는 스키마 뒤에 `migrate_wh_core_tables.sql`(개발 DB 의 `WH_ReleaseSchedule` 삭제·WH 프로시저 9개를 옛 정의로 덮음)·`migrate_fg_inventory_consolidation.sql`(오류로 실패)을 돌리고, `migrate_scm_portal_screens.sql`·`seed_portal_dev.sql`·`pda/PDA_SEED.sql` 도 실패한다 — 예전 스키마 파일로도 같은 4개가 실패하던 기존 문제다(10-08 로컬 재현).

**솔루션 전체 빌드는 6~16분 걸린다**(MAUI: Pda, Tablet). **두 개를 동시에 돌리면 `NETSDK1047`·`MSB3061` 가짜 실패**가 나므로 순차 실행할 것.

---

## AMES.Web 배포

로컬 IIS(`w3wp`)로 구동된다. `dotnet run`/IIS Express 아님.

```powershell
tools\publish-web.ps1                               # 개발서버 복사용 패키지 → publish\AMES.Web
tools\publish-web.ps1 -Zip                          # + zip
tools\publish-web.ps1 -Target Local                 # 로컬 IIS 반영 (개발서버 DB)
tools\publish-web.ps1 -Target Local -DbTarget Local # 로컬 IIS 반영 (비상: 로컬 DB)
tools\publish-api.ps1 [-Zip] [-Target Local [-EnsureIis] [-DbTarget Local]]   # AMES.Api 도 같은 방식 → publish\AMES.Api / C:\inetpub\wwwroot\Source\AMES.Api
```

AMES.Api 는 로컬 IIS 사이트 `AMES.Api`(앱풀 `AMES.Api`, `http *:5210` — Web 의 `Services:ApiBaseUrl` 과 같은 포트, 물리 경로 `C:\inetpub\wwwroot\Source\AMES.Api`)로 구동된다(09-16). 앱풀은 Web 과 반드시 분리한다 — in-process 는 앱풀 하나에 앱 하나만 허용해 같이 두면 500.30 이 난다. 사이트·앱풀이 없는 PC 에서는 `-EnsureIis`(관리자)로 만든다. IIS 아래에서는 appsettings 의 `Urls` 가 무시되고 바인딩 포트를 쓴다. 기동 확인은 `GET http://localhost:5210/api/health`.

- **라이브 IIS 폴더로 직접 게시하면 반드시 실패한다** — `w3wp`가 `AMES.Web.dll`을 잡고 있다.
  `-Target Local`은 `app_offline.htm`을 먼저 떨궈 ANCM이 앱을 내리게 하므로 잠금이 풀리고 관리자 권한도 필요 없다.
- `**/Properties/PublishProfiles/`는 gitignore 대상 → 게시 프로필 수정은 그 PC에만 적용된다.
- `dotnet publish` CLI는 pubxml의 `PublishUrl`을 무시한다(VS 전용). `-o`로 지정할 것.

**앱풀 필수 설정** (`loadUserProfile` 뿐 아니라 **`setProfileEnvironment` 도** 켜야 한다. 후자는 IIS 관리자 UI에 없다):

```powershell
appcmd set apppool "AMES.Web" /processModel.loadUserProfile:true /processModel.setProfileEnvironment:true
```

끄면 Data Protection이 ephemeral 키를 써서 **앱풀 재활용마다 로그인 사용자가 전원 로그아웃**된다.
`dist/setup-iis.ps1` 이 이 두 설정과 Data Protection 키 폴더(앱풀 계정 Modify)를 같이 구성한다(09-19). 이미 운영 중인 서버는 `dist\setup-iis.ps1 -ConfigOnly [-PoolName …]`(관리자) — 게시·사이트 경로·바인딩·런타임/계정 설정·`iisreset` 은 건드리지 않고 두 설정 + 키 폴더 권한만 적용한 뒤 그 앱풀만 재활용한다. 옵션 없이 돌리면 `C:\inetpub\ames-web`·포트 5000 으로 **게시하고 사이트 경로·바인딩을 바꾸므로** 기존 서버에서는 반드시 `-ConfigOnly` 를 쓴다.

**09-18 부터 AMES.Web 은 이 설정에 기대지 않는다** — `Program.cs` 가 키를 `%ProgramData%\AMES\DataProtection-Keys\AMES.Web`(설정 `DataProtection:KeyPath` 로 변경 가능)에 두고 머신 범위 DPAPI 로 암호화한다. 배포 폴더 안에 두지 않는 이유는 `publish-web.ps1` 의 `robocopy /MIR` 가 게시 때마다 지우기 때문이다. 폴더를 못 만들거나 쓰기 권한이 없으면 기동은 계속하고 경고 로그(`Data Protection key folder … is not writable`)만 남긴 채 프레임워크 기본 동작으로 돌아가므로, 그 경우에만 위 앱풀 설정이 다시 필요하다. 키는 서버마다 따로 생기며, 이 버전을 처음 올릴 때는 키가 바뀌어 **로그인 사용자가 한 번 전원 로그아웃**된다.

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
| INJ-MAIN | `Pages/InjMain.razor` | **통합 작업 화면** (기본 진입점) — 좌측 스테이션 BOP 품번 × 선택일(기본 금일, 헤더 달력으로 과거일 조회) PLAN/INPUT/NG/FINAL 그리드 + 스캔 실적확정 + 우측 패널 기능 버튼 (하단바 없음, 로그아웃은 상단바). WO 접수 없음: 품번 행 선택 → `WorkOrderRepository.FindOpenForItem` 이 열린 WO 를 자동 해석(`ConfirmByLotCode` 와 같은 규칙) |
| (버튼) | `Pages/InjMain.razor` `CreateManualLabel` | 수동 라벨 생성 — 누를 때마다 원천 LOT(RAW) 1개(`CreateManualRawLots` qty=1). 수량 입력 없음. 라벨은 `LabelDispatcher` 가 뽑고, 스캔해야 실적 확정 |
| (팝업) | `Pages/InjPopups/DefectPopup.razor` | 불량 등록 — LOT 라벨 스캔 → 불량코드 → 등록(1 LOT = 1 EA, 수량 입력 없음). 등록된 LOT 은 DEFECT 가 되어 REWORK 스테이션으로 간다. 확정 후 LOT 은 실적을 역분개(−1)한다 |
| (팝업) | `Pages/InjPopups/AndonPopup.razor` | 안돈 — 전체 화면 오버레이. 확인창 → 슈퍼바이저 배지 스캔 → 원인·부서 호출 → 담당자 배지 도착 → ACK → 자동 종료. 흐름은 `Services/AndonWorkflow`(단위 테스트 `AndonWorkflowTests`) |

대시보드(INJ-02)·작업지시 접수(INJ-03)·금형 교체(INJ-06)·생산 현황(INJ-07)은 미사용으로 삭제됨 (화면·팝업·레거시 WinForms 폼 포함).
구 단독 화면(`/inj02`~`/inj08` 라우트)도 모두 삭제됨 — INJ 는 INJ-MAIN(+ 수동 라벨 생성 버튼) + 팝업(불량·안돈)만 남는다. 팝업 공통 셸은 `Pages/InjPopups/PopupShell.razor`.
좌측 품번 목록은 `MD_Bop.StationCode` = 세션 스테이션(`PopSessionDto.TerminalId`) 기준이고, 좌측 수치는 `InjLotRepository.GetDailyItemSummary(line, station, date)` — 우측 칩은 좌측 날짜와 무관하게 금일 수치이고, 스캔 확정에 성공하면 좌측도 금일로 돌아간다. LOT 생성일 기준으로 `INPUT = FINAL + NG + 미확정` 이 성립한다. 전부 LOT 상태로 센다 — FINAL = CONFIRMED, NG = NG_BLOCKED + DEFECT + SCRAPPED (IMG 는 DEFECT + SCRAPPED), 미확정 = RAW. PR_DefectDetail 은 집계에 쓰지 않는다. INJ 단계의 생산 품번은 **BOM 의 코어 SUB**(`AMES.Data.Services.CoreItemResolver` — 유효 BOM 의 SUB 하위 중 품명이 `CORE` 로 시작하는 품번 정확히 1개, 0·2개 이상은 PP-003 이 `NoCore` 로 거부, SUB 하위가 없으면 WO 품번 그대로)이며 WO 발행 때 `PP_WorkOrderRouting.ItemNo` 에 스냅샷된다(`dist/migrate_wo_step_item.sql`). INJ 스테이션 BOP·금형 매핑·LOT 품번은 코어이고 WO 품번은 완제품이다. 열린 WO 해석은 전부 `COALESCE(r.ItemNo, w.ItemNo)`(`WorkOrderRepository.OpenStepForItemFilter`)이고 INJ-MAIN 의 PLAN 도 단계 품번으로 센다. 수동 라벨은 좌측 선택 품번(열린 WO 가 있으면 `WorkOrderDto.StepItemNo`)으로 LOT 을 만든다. **열린 WO 가 없어도 생산은 막히지 않는다**(10-01 사용자 결정) — 스캔 확정은 `PR_ProductionResult`·`tbl_Lot` 의 `WoID` 를 NULL 로 남기고 단계 반영(`BumpStepCompleted`)만 없다. 보고서는 WO 를 LEFT JOIN 하므로 그대로 집계되고, 불량 역분개·재작업도 WoID NULL 로 따라간다. 좌측 `WO 없음` 배지는 안내일 뿐 막지 않는다. dev DB 는 `dist/seed_md_bop_inj_dev.sql` 로 ST-INJ-01 BOP 를 채운다.
INJ 는 `AcceptWo` 를 부르지 않으므로 `BumpStepCompleted` 가 첫 실적에서 단계·헤더를 `Released → In Progress` 로 올리고 `ActualStart` 를 찍는다. `TerminalLock` 은 INJ 에서 기록하지 않는다(IMG 는 `AcceptWo` 그대로).

#### IMG (원단/래핑 공정) — 통합 메인 + 팝업 구조
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| IMG-MAIN | `Pages/ImgMain.razor` | **통합 작업 화면** (IMG 기본 진입점) — 좌측 스테이션 BOP 품번 × 선택일(기본 금일, 헤더 달력으로 과거일 조회) PLAN/INPUT/NG/FINAL 그리드 + 우측 **사출 Core 스캔 → 완제품 라벨 → OK/NG 판정 팝업**(`Pages/InjPopups/ImgJudgePopup.razor`) · 오늘 발행 LOT 목록(Core·대기/OK · 재출력) + 예외용 **라벨 발행** 버튼 + 불량·안돈 팝업 버튼. INJ-MAIN 과 같은 레이아웃(`injm-*` CSS 공유)이며 WO 접수 없음: 품번은 Core 에서 정해지고 열린 WO 는 `FindOpenForItem` 규칙으로 해석 |
| (팝업) | `Pages/InjPopups/DefectPopup.razor` | 불량 등록 — LOT 라벨 스캔 → 불량코드 → 등록(1 LOT = 1 EA, 수량 입력 없음). 등록된 LOT 은 DEFECT 가 되어 REWORK 스테이션으로 간다. 확정 후 LOT 은 실적을 역분개(−1)한다 — `ProcessCode="IMG"` 로 INJ 와 공유 (IMG 는 로봇 NG LOT 구역 없음) |
| (팝업) | `Pages/InjPopups/AndonPopup.razor` | 안돈 — INJ 와 공유 |

구 단독 화면(IMG-02~07, `/img02`~`/img07`)은 모두 삭제됨 (화면·도움말·레거시 WinForms 폼·전용 CSS 포함) — IMG 는 IMG-MAIN + 팝업(불량·안돈)만 남는다.
IMG 도 INJ 와 같은 LOT 모델(1 LOT = 1 EA, RAW → CONFIRMED)을 쓰되 테이블은 별도 `PR_ImgLot`(`dist/migrate_img_lot.sql`)이고 리포지토리는 `ImgLotRepository` 다. 에이전트가 없으므로 LOT 은 **사출 Core 스캔**이 만든다(`ImgLotRepository.CreateFromCore`): Core(INJ LOT)는 `CONFIRMED` 만 받고(규칙 정본 `AMES.Data.Services.CoreLotRules`, 테스트 `CoreLotRulesTests`) 한 번만 쓸 수 있으며, 완제품 품번 = 그 코어를 INJ 단계 품번으로 갖는 **IMG 라인의 열린 WO 품번**(`WorkOrderRepository.OpenStepByCoreFilter`, 라우팅 A = INJ → IMG — 코어 품번이 아니다), 연결은 완제품 `tbl_Lot.ParentLotID` = Core LotID 다. 그런 WO 가 없으면 **좌측에서 고른 완제품 품번으로 WO 없이 만든다**(10-01 사용자 결정 — 선택 품번의 유효 BOM 코어(`CoreItemResolver`)가 스캔 코어와 다르면 `CoreMismatch`, 선택이 없거나 마스터에 없는 품번이면 `NoFinishedItem`, 둘 다 반환 품번은 코어; BOM 이 코어를 못 정하는 품번은 선택을 믿는다). PGN·ALC·MountPos 는 완제품 품번, 수주처는 그 WO 로 읽는다(WO 없으면 NULL — 라벨 V 토큰이 빈다). 판정 OK 도 WO 없이 확정된다(`WoID` NULL, 단계 반영 없음). 예외용 라벨 발행 버튼도 좌측 선택 품번만 있으면 된다. 생성 즉시 `LabelPrinter.Print(ImgLotDto, shift)` 로 동기 출력하고 OK/NG 판정 팝업을 띄운다 — OK = `ConfirmByLotCode`, NG = 불량코드 터치 → `RegisterDefect`(DEFECT → REWORK). 판정 없이 닫은 LOT 은 RAW 로 남고 완제품 라벨을 다시 스캔하면 팝업이 다시 뜬다(RAW 라벨 스캔은 더 이상 바로 확정하지 않는다). 예외용 **라벨 발행** 버튼(`CreateRawLot`)은 Core 없이 발행하며 같은 팝업을 띄운다. `LabelDispatcher` 는 INJ 세션에서만 돌아 이중 발행이 없다. DB 통합 테스트는 `AMES.Data.Tests/ImgLotRepositoryTests`(AMES_DEV 필요).
IMG 라벨은 INJ 양식이 아니라 **완제품 고객 표준 라벨**(`AMES.Devices.ImgLabelBuilder`)이다: 좌측 DataMatrix = `[)>RS06GSV{수주처}GSP{품번,하이픈제거}GSS{PGN+ALC}GST{yyMMdd}{part4M}{LotNo}GSEGSC:RSEOT`(`^FH_` 16진 이스케이프), 우측 글자 = ALC(대)·장착위치·발행일·품번·LotNo. part4M = `1` + 하이픈 뺀 품번 6·7번째 글자 + 교대 글자(DAY=A·NIGHT=B·그 외 C, `LabelPrinter.ShiftLetter`). 수주처 코드는 발행 시점 열린 WO → `PP_CustomerOrder.SoID` → `MD_Customer.CustomerCode` 로 정해 `PR_ImgLot.CustomerCode` 에 박아 둔다(재출력 불변). PGN·ALC 는 `MD_Item`, 장착위치는 `MD_Item.MountPos`(`dist/migrate_md_item_mount_pos.sql`, FL/FR/RL/RR). 샘플 라벨 우측 하단 'D' 칸은 정의 전이라 비워 둔다.
스캐너는 DataMatrix 문자열 전체를 보내므로 `ImgScanParser.ExtractLotCode` 가 T 토큰 끝 9자를 LotNo 로 뽑는다 — 시리얼(제어문자 보존)·HID 웨지(제어문자 소실)·단순 LotNo 라벨 모두 처리하며 단위 테스트(`AMES.Pop.Tests/ImgScanParserTests`)가 정본이다. 발행은 실적이 아니며 판정 OK 가 `ConfirmByLotCode` 로 한 트랜잭션으로 열린 WO 해석 → PR_ProductionResult 1 EA → LOT CONFIRMED + `BumpStepCompleted` 를 처리한다. 원단 롤·본딩은 다루지 않는다(차감·롤 ID·본딩 사이클 로그·`BondSetupID` 기록과 화면 칩 모두 없음).
좌측 수치는 `ImgLotRepository.GetDailyItemSummary(line, station, date)` — 우측 칩은 좌측 날짜와 무관하게 금일 수치이고, 스캔 확정에 성공하면 좌측도 금일로 돌아간다. INJ 판과 같이 LOT 생성일 기준으로 `INPUT = FINAL + NG + 미확정` 이 성립한다. 우측 목록은 `GetTodayLots`(오늘 발행 LOT 전부, 최신순). dev DB 는 `dist/seed_md_bop_img_dev.sql` 로 ST-IMG-01 스테이션·데모 품번·BOP 를 채운다 (IMG 라인은 스키마에 스테이션이 없어 이 시드 없이는 로그인 자체가 안 된다).

#### RWK (재작업 스테이션) — 1화면
| 화면 ID | 파일 | 설명 |
|---------|------|------|
| REWORK | `Pages/ReworkMain.razor` | 독립 재작업 스테이션(`/rework`). 좌측 전 라인 재작업 대기열(`PR_DefectDetail.Disposition IS NULL`) + 우측 판정: LOT 스캔(INJ LotCode·IMG DataMatrix 모두 `ImgScanParser`) 또는 행 터치 → 원인코드(`MD_DefectCause`, 필수, 불량코드 `DefaultCauseCode` 기본 선택) → 조치 메모(선택) → **수리 완료 → 양품** / **폐기** |

LOT 상태 기계(`PR_InjLot`·`PR_ImgLot.ConfirmStatus`, 정본 `AMES.Data.Services.LotDefectRules`): `RAW → CONFIRMED`(스캔), `RAW/CONFIRMED/NG_BLOCKED → DEFECT`(라인 불량 팝업), `DEFECT → CONFIRMED`(수리) / `→ SCRAPPED`(폐기). `NG_CONFIRMED` 는 폐지됐다.
수리 양품은 `ReworkRepository.Rework` 가 원래 WO 에 `PR_ProductionResult` +1(`ProcessCode='RWK'`, `LineID='LINE-RWK-01'`) + `BumpStepCompleted(+1)` 로 확정한다. WO 는 등록 행의 `WoID` → 없으면 원래 라인 `FindOpenForItem` 순으로 해석하고, 둘 다 없으면 WO 없이 확정한다(`WoID` NULL, 단계 반영 없음 — WO 없는 생산분의 수리, 10-01). 폐기는 실적을 건드리지 않는다.
확정 후 LOT 의 불량 등록은 `PR_ProductionResult` 역분개 행(`GoodQty=-1`, `DefectFlag=0` — 불량은 `PR_DefectDetail` 에 있다, 보고서 이중 계상 방지) + 단계 `CompletedQty −1` 이며, `BumpStepCompleted` 는 음수일 때 단계 `Closed → In Progress` 를 되돌리고 `ActualEnd` 를 지운다; 헤더는 그 단계가 마지막 라인 단계일 때만 같이 되돌린다(헤더 `CompletedQty` 를 움직이는 건 그 단계뿐이다).
REWORK 로그인은 `LINE-RWK-01`(WC `WC-RWK`, ProcessCode `RWK`) 선택 — 마스터가 없으면 로그인 불가. `AppState.ModuleCode="RWK"` 라 `LabelDispatcher` 는 돌지 않는다. 대기열은 전 라인 공용이다.

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
`WorkOrder` / `LineSchedule` / `Calendar` / `PlanConfirm`
`Forecast`(PP-001, 「일별 구매계획」 탭 — SRM MM30011 업로드·API 수집) / `Delivery` / `Mrp` / `SupplyPlanImport` / `PurchaseReq`
`Oee` / `Downtime` / `DowntimeMonitor` / `ApsPlan`(PP-APS)

##### APS 생산계획 (PP-APS) — `Pp/ApsPlan.razor`, `/pp/aps-plan`

SEG APS(Seoyon E-Hwa Georgia 생산계획 웹앱, 재구축본 `Seg.Aps.Server`)의 순수 계산 엔진을 `AMES.Data.Aps` 로 이식한 것이다. **완제품 공급 자동계산 → 사출 소요 전개 → shot 환산 → 사출기 주야 배분 → 부하율·부족 신호등**을 내고, 저장된 실행에서 WO·라인 슬롯을 만들어 POP 까지 잇는다. PP-003 과는 대체가 아니라 앞뒤 관계다 — APS 가 만든 WO 도 PP-003 의 기발행 수량에 잡히고, PP-003 이 먼저 만든 WO 슬롯은 APS 화면에 잠긴 칸(회색 자물쇠)으로 들어온다. **완제품 품번으로 잡힌 사출 라인 슬롯**(코어 품번 WO 모델 — WO 는 완제품 품번이고 INJ 단계 슬롯도 그 품번)은 그 완제품의 **BOM 사출 자식 등록 계획**으로 읽는다: 자식의 사출 라인에 있는 부모 슬롯 수량 × 간선 QtyPer(스크랩 포함)를 자식마다 잠긴 칸에 더하고 부하에도 넣는다(10-02 사용자 결정 4-(a), `ApsRepository.ParentSlotsFor`, 정본 `ApsRepositoryTests.Parent_item_slot_…`). 부모가 자기 금형으로 직접 찍히는 품번(같은 품번 규칙)이면 그 슬롯은 부모 자신의 것이고, 자식 사출 라인이 아닌 라인의 부모 슬롯은 종전대로 "계획 라인이 아니라" 경고다.

- **엔진** `src/02_Data/AMES.Data/Aps/{Contracts,Domain}/`: REBUILD 의 클래스·필드 이름 그대로(`PlanBundle`·`Settings`·`PlanCalc`·`Autofill`·`InjectionScheduler`·`ShiftRules`·`StageRules`…), 수치 `double`, 경고·근거 문구 ko 리터럴, `Microsoft.Data.SqlClient` 참조 없음. 달력만 `IApsCalendar`(`SundayOffCalendar` = 골든, `WorkdayCalendarAdapter` = AMES `SYS_FactoryCalendar`)로 추상화. ORIG 규칙 보충은 `ApsOptions(PullForwardSupply, SafetyStockTerm, WarnStatus)` 플래그(기본 전부 false = 골든 경로). 정본 = 골든 픽스처 11개(`AMES.Data.Tests/TestData/Aps/`, `upload_current.json` 은 미반입 → `DemandBuilderTests` 2건 skip)와 `AMES.Data.Tests/Aps/*GoldenTests`·`ApsCalendarTests`·`ShiftRulesTests`·`ApsOptionsTests`·`SelfEdgeTests`.
- **어댑터** `AMES.Data.Aps.{DemandRules, ActualsRules, ShiftBands, ApsSettingsLoader.Parse, ApsPlanLines}`(순수) + `Repositories/ApsRepository`(`BuildBundle`·`ReadSettings`·`SaveRun`·`LoadRun`·`ListRuns`·`ListPlanLines`·`ListLineStages`·`SaveLineStage`·`DeleteLineStage`·`ListLines`). 라인 유형 = `MD_WorkCenter.ProcessCode` `INJ` → injection, `IMG`·`PNT` → assembly(개발 DB 실값, 스키마 시드 `INJECTION/WRAPPING/PAINTING` 은 `seed_aps_dev.sql` 이 고친다). 완제품 라인 = 라우팅의 라인 있는 단계 중 `MAX(StepSeq)` 의 라인, 능력 = `MD_Line.DailyCap`(NULL·0 이하는 "제한 없음" + 경고). **사출 라인 모드**(선택 라인이 injection)도 선행일·재고 규칙은 완제품 모드와 같게 적용되고(`PlanCalc.RootLine` = null → `MD_ApsLineStage` 공통 행 → `APS_SETTING.INJ_OFFSET_DAYS`, 음수는 경고 + 기본값), 완제품 공급 능력은 선택 라인이 아니라 부모 완제품 행마다 자기 라인의 `MD_Line.DailyCap` 이다(`Autofill` 이 행 라인별로 깎기·앞당기기·경고). **전체 라인 모드**(`ApsRepository.AllLines = "-"`, 필터 첫 항목 「전체 완제품 라인」)는 완제품 라인을 걸러내지 않고 IMG·PNT 라인 전부의 행을 **실행 1건**에 담는다 — 행마다 자기 라인이 남고 능력도 라인별(전체 표식에는 `LineShift`·DailyCap 경고 없음), `PP_ApsRun.LineID` 에 `-` 로 저장(FK 없음), 화면은 라인별로 묶어 보이고 칩·실행 이력은 「전체 완제품 라인」으로 표시한다. 사출 라인 모드는 라인별 그대로다(정본 `ApsRepositoryTests.BuildBundle_all_lines_…`). 사출 행은 ① 같은 품번 규칙(라우팅에 INJ 단계 + 활성 `MD_MoldItem`, `BomEdge(X,X,1)`) ② BOM 규칙(유효 APPROVED BOM 을 **다단계**로 내려가 활성 금형 품번을 찾는다 — 마스터 리스트(`260828 Master List.xlsx`)는 완제품 → 서브조립(S-품번·PNL ASSY·MODULE, 금형 없는 SUB) → 코어·레일(금형) 구조라 10-05 사용자 결정으로 1단계에서 다단계로 바꿨다. **사출품은 `MD_Item.InjFlag = 1` 로 가른다**(사용자 결정 10-05 — dev 의 `migrate_md_item_inj_flag.sql`, `migrate_aps.sql` §1 도 만든다): InjFlag 1 인 자식에서 멈추고(활성 `MD_MoldItem` 이 있어야 사출 행, 없으면 "활성 금형이 없어" 경고 + 행 없음), InjFlag 0 인 자식은 서브조립로 보고 그 BOM 을 내려가며 QtyPer(스크랩 포함)를 경로를 따라 곱한다(두 경로로 닿으면 합산). 금형은 있는데 InjFlag 0 이면 사출품으로 보지 않고 경고(품번당 1회). MATERIAL 은 내려가지 않으며 깊이 상한 `ApsRepository.BomWalkMaxDepth`(5)·순환 가드. 같은 품번 규칙 ① 은 InjFlag 를 보지 않는다(라우팅 INJ 단계 + 금형이 기준). **InjFlag 가 서브조립까지 1 이면 그 자리에서 멈춰 "활성 금형이 없어" 경고가 되고 그 아래 코어·레일은 안 보인다** — 사출품만 1 로 둘 것) — `PP_ApsPlanLine.SameItem` 이 ①을 표시한다. `Uph = MD_MoldLine.UPH × 품번 캐비티 ÷ MD_Mold.CavityCount`, 품번 캐비티 = `MD_Mold.CavityCount ÷ 패밀리 품번 수`(내림·최소 1 — `MD_MoldLine.UPH` 는 금형 전체 산출이라 품번이 여럿인 패밀리 금형만 나뉜다; 품번별 캐비티 컬럼 `PartCavityCount` 는 09-30 폐지). **패밀리(형제·동시 취출)의 단위는 금형이 아니라 금형 × 색상(`MD_MoldItem.Color`)** — 같은 금형의 다른 색상(NNB/YGU 등)은 따로 찍으므로 형제가 아니고 캐비티도 나누지 않으며, 엔진은 `InjectionRow.MoldColor` 를 받아 `MoldGroupKey()`(금형/색상, 색상 없으면 금형, 금형 없으면 품번)로 묶는다(10-02, 정본 `MoldColorGroupingTests`). "균등 분할" 경고는 캐비티가 패밀리 품번 수로 나누어떨어지지 않을 때만(LH/RH 2캐비티는 정상이라 조용하다). `MD_Item.ItemType = 'MATERIAL'`(구매 자재)은 수요·재고·등록 계획이 있어도 후보에서 조용히 뺀다(10-02 사용자 결정 — RoutingType 경고 대상도 아니다), `PackSize = MD_Item.BoxQty ?? 1`(사출 행. BoxQty 없는 완제품 전용 행은 `BoxQty ?? APS_SETTING.ROUND_TO` — 앞자리가 같은 다른 품번의 포장 단위를 물려받지 않게 항상 규칙을 둔다, +경고), 수요 = `PP_CustomerOrder` Confirmed(+Open 옵션) 잔량을 납기(휴무일은 직전 근무일, 지연분은 첫날)에 버킷 + `IncludeDailyPlan`(기본 켜짐)이면 PP-001 일별 구매계획(`PP_DemandPlan.ScheduledQty`, 같은 고객 필터, `PoQty` 는 무시, 휴무일 → 직전 근무일, 기준일 이전 계획은 버림 — 수주 지연분과 달리 첫날에 몰지 않는다, 미등록 품번은 경고 1건/품번)을 더해 셀에 합산(계획 몫은 `DemandRules.Result.PlanDemand` 로 따로 보존해 셀·drawer 표시용 — 수주 몫은 Demand − PlanDemand), 재고 = 현재 재고를 기준일 아침값으로 역산(현재 재고 = 통합재고 `WH_Inventory` 의 `PartNo` 별 `SUM(Qty)`(`Qty > 0`, **위치 무관** — 사용자 결정 09-30; 구 `FG_Inventory` 는 통합재고로 흡수돼 없다), 그날 출고 = `WH_InventoryTransaction` 의 `OUT` 거래(`QtyChange < 0`) 합 — 위치·사유 무관, 실적 = `SUM(GoodQty)` 역분개 포함, 불량 항 0), 교대 = `MD_LineTimeSegment`(패턴) × 공통코드 `WORK_SHIFT.SortOrder` 최소 = 주간·나머지 = 야간(`ShiftBands.Split`). **사출 라인의 패턴은 APS 설정이 정한다**(10-06 사용자 결정 (b)): `MD_ApsLineStage.PatternID`(라인 지정) → `APS_SETTING.DEFAULT_PATTERN`(기본, 전역 패턴) 순이고 PP-LSB 가 그 날 저장한 `PP_LineSchedule.PatternID`·라인 전용/전역 자동 해석(`ReadDayCapacity` 의 기본 경로)보다 **항상 우선**한다(`ReadDayCapacity(…, patternId)` 인자, PP-003·PP-LSB 는 종전대로). 둘 다 없거나 지정 패턴이 없어졌거나 ACTIVE 가 아닌 사출 라인이 번들에 나오면 **조회·WO 생성이 `ApsConfigurationException`(라인별 사유 목록)으로 막힌다**(정본 `ApsPatternResolver`, 화면은 배너 + 「설정 열기」). 패턴은 있는데 가동 구간이 0 이면 종전대로 0h 경고. `SHIFT_DAY_H/NIGHT_H` 는 **폐기**(10-06 사용자 결정 "패턴을 쓰면 필요 없다") — `migrate_aps.sql` §5-b 가 행을 지우고 `ApsSettingsLoader.RetiredKeys` 라 로더가 읽지 않으며 다이얼로그는 남은 행도 숨긴다. 엔진 `Settings.Shift` 는 SEG 골든 픽스처 재현용 기본값만 남는다. **기준일 앞으로 떨어지는 사출 소요는 첫 사출일에 몬다**(10-06 사용자 결정 (a) — 사출일 i 의 소요는 공급[i + 선행일]이라 기준일 당일 등 앞쪽 공급의 사출일이 계획판 밖이면 원본은 조용히 버렸고 그만큼 WO 가 빠졌다(실행 #1355·#555); `PlanCalc.RequirementsWithFolds` 가 `StageRules.FoldPreHorizon`(AMES 경로만 true, 골든 픽스처는 원본대로)일 때 사출일 0 에 더하고 `InjectionScheduler` 가 "기준일 이전에 찍었어야 할 소요 … 당겼습니다" 경고를 낸다, 정본 `HorizonFoldTests`). 모든 결손은 경고 문자열(품번·라인·수주번호 포함)로 배너에 보이고 조용히 0 으로 만들지 않는다. 규칙 정본 테스트: `DemandRulesTests`·`ActualsRulesTests`·`ShiftBandsTests`·`ApsSettingsLoaderTests`·`ApsPlanLinesTests`, DB 는 `ApsRepositoryTests`(AMES_DEV).
- **화면**: 필터(라인 — 첫 항목 「전체 완제품 라인」·기준일 `DbClock.Today`·일수 5(기본, 1~30)·고객·Open 포함·**일별 계획 포함**(`IncludeDailyPlan` 체크박스, 기본 켜짐)) → 「조회」 → KPI 5개 + 경고 배너(조회 직후 8건을 넘으면 접힌 채 시작, 건수는 헤더에) + 탭 4개(완제품·사출·사출기 부하·**요약** — 요약은 항상 보이던 하단 표였으나 10-05 사용자 요청으로 네 번째 탭; 각 품번 셀의 수요 줄은 일별 계획 몫이 0보다 크면 `· 계획 N` 을 덧붙이고, 그 아래 **「WO」 줄**은 그 칸의 등록 계획을 만든 이미 내려간 WO 수량 — `ApsBuild.RegisteredWos`(슬롯 단위, 저장 JSON 에는 없어 복원 실행도 현재 WO), 툴팁에 WO 번호 × 수량(부모 품번 슬롯을 자식 등록 계획으로 읽었으면 부모 품번 병기), WO 가 둘 이상이면 건수. 10-05 사용자 요청; 슬롯을 못 받은 APS WO(Shortfall, PP-LSB 미배치)는 `· 미배치 N` 으로 따로 보인다 — `ApsRepository.ReadUnplacedApsWos`: 사출 행은 `PP_ApsRunWo` 의 계획 행(품번·사출일), 완제품 행은 WO 의 `ProdDeadline`(= 공급일), 계획·부하에는 안 들어간다, 10-06 사용자 결정 (b)). **등록 계획 잠금(파란 🔒)도 풀 수 있다**(10-05 사용자 결정 — 전에는 고정): 풀면 자동 계산·재배분이 그 칸을 바꾸고, 값이 실제 WO 슬롯 수량과 다르면 빨간 `≠`(툴팁에 슬롯 수량) 가 붙는다. 현장(POP)은 여전히 `PP_LineSchedule` 슬롯대로 만들고 「WO 생성」도 기존 슬롯 수량을 빼므로 이중 발행은 없지만, 실제 슬롯을 바꾸는 곳은 PP-LSB·PP-003 이다 + 셀 클릭 근거 drawer(`AutofillTrace`/`Trace.Steps`, 계획 몫은 `(계획 N)` 으로 따로 보인다). 툴바(E): 「계획 자동 계산」(`Autofill.RunWithTraces` → `InjectionScheduler.RescheduleWithTraces` → `PlanCalc.Compute`) · 「사출 재배분」 · 「재계산」 · 「저장」(`PP_ApsRun` Status `Saved` + `IncludeDailyPlan` + JSON 3개 + `PP_ApsPlanLine` 정규화 사본, 감사 `PP-APS/SAVE`) · 「WO 생성」(저장된 실행, `Released` 전) · 「실행 이력」(최근 50, 열기 = 재계산 없이 재현 / 현재 데이터로 재계산 — `IncludeDailyPlan` 도 그 실행에 저장된 값 그대로 재사용) · 「설정」(`APS_SETTING` 값·`APS_COVER_TIER` 구간을 E 권한이면 인라인 편집 — `DEFAULT_PATTERN` 은 전역(`LineID NULL`) ACTIVE 패턴 콤보(비우면 라인 지정 없는 사출 라인은 조회 차단), 라인 행 표는 `RadzenDataGrid` 인라인 편집(`EditMode.Single`, 10-06 사용자 요청)이라 연필 → 행 안에서 선행일·재고 사용·가동 시간 패턴 콤보(그 라인 전용 + 전역 ACTIVE, 빈 값 = 기본 패턴 따름)·메모를 고치고 저장/취소하며, 「추가」는 라인 콤보가 열린 새 행을 맨 위에 넣는다(기존 행의 라인은 불변) — 10-06 사용자 요청, 전에는 읽기 전용. 공통코드 행을 `MasterDataRepository` 코드 항목 메서드로 직접 고치므로 MD-26 과 같은 데이터이고 감사 `PP-APS/MD_CodeItem`; 저장 전 검증은 엔진 `ApsSettingsLoader.ValidateSetting/ValidateTier`(플래그 1/0 체크박스, ROUND_TO ≥1, 선행일 ≥0, 교대 0~24h, 구간 = 0 이상 숫자 쌍·하한 중복 거부, 정본 `ApsSettingsLoaderTests`), 미등록 키는 `KnownSettings` 기본값으로 보였다가 저장 시 INSERT(CodeID `APS_SETTING_{키}`), 구간 추가는 CodeID `APS_COVER_TIER_{하한}`·SortOrder 는 하한 오름차순 순위로 재부여 + `MD_ApsLineStage` CRUD, 감사 `PP-APS/MD_ApsLineStage`). 편집 상태는 회로 메모리라 페이지를 떠나면 사라진다. `Released` 실행은 편집·재저장·재생성 불가(새 실행으로).
- **WO 생성** `PpRepository.CreateApsWorkOrders(runId, planLineIds, actor, dryRun)`(`Repositories/PpRepository.Aps.cs`, 한 트랜잭션, 정본 `ApsWoAllocatorTests`·`ApsWoCreateTests`(AMES_DEV)): 대상 = `Kind='INJ' AND PlanDay+PlanNight>0 AND WoID IS NULL AND SameItem=1`— `SameItem` 조건은 10-05 에 없앴다: **BOM 규칙 행(부모와 다른 사출품)도 대상**이고 WO 는 완제품(부모) 품번으로 나온다(사용자 결정 "Create WO 를 할 경우 IMG 와 INJ 다 생성", 스펙 `docs/superpowers/specs/2026-10-05-aps-bom-rule-wo-design.md`, 정본 `ApsWoCreateTests.Bom_rule_rows_create_parent_work_orders_…`). WO 단위 = (완제품, 사출 계획일): 같은 품번 규칙 행은 자식 = 자기 자신, BOM 규칙 행은 저장본 `BundleJson.Bom`(자기 간선 제외)의 부모마다 **자식 계획 수량을 부모 공급(계획일 + 선행일, `PP_ApsPlanLine` ASM Supply) × QtyPer 비율로 나눠**(전부 0 이면 균등, 자식 품번 기존 슬롯은 나누기 전에, 부모 품번 기존 슬롯은 나눈 뒤 부모 단위로 차감) 싣고, WO 수량 = 자식 몫(부모 단위) 중 최대를 **완제품 포장 단위(`MD_Item.BoxQty` ?? `APS_SETTING.ROUND_TO`)로 올림**(10-06 사용자 요청 — 비율 분배의 2~3개 자투리 방지, `PpRepository.RoundUpToPack`; 같은 품번 규칙만인 WO 는 종전대로 내림), INJ 단계에 자식마다 슬롯 1개(자식 수량 = 조각 × QtyPer, 주간 몫부터), `PP_WorkOrderRouting` INJ 라인 = 품번순 첫 자식의 라인, IMG 단계 배치량 = 모든 자식이 실은 몫 ÷ QtyPer 의 최소. `PP_ApsRunWo` 는 자식 행마다·조각마다 1행(Qty = 자식 단위), `PP_ApsPlanLine.WoID` = 품번순 첫 부모의 첫 WO, 자식에 간선이 없으면 `NoParent` 거부, 이미 `WoID` 가 있으면 `SkippedExisting`(`ApsWoResult.BomRuleExcluded` 는 항상 0, 호환용). 같은 품번 규칙에서 WO = 품번 × 사출 계획일, `OrderQty` 는 **납기순 Confirmed 수주 잔량(`OrderQty − ShippedQty − 기발행 WO`)에 FIFO 배정**(`AMES.Data.Aps.ApsWoAllocator`)하며 수주가 갈리면 WO 를 쪼개고(`SoID` 각각) 남는 양은 `SoID NULL` 재고 보충 WO 1건. **그 날·그 라인·그 품번에 이미 있는 WO 슬롯 수량(취소 제외)은 주/야로 나눠 뺀다**(이중 발행 방지, 다 덮이면 WO 없이 `SkippedExisting`). `ProdDeadline` = 사출일 + 그 사출 라인 선행일(`MD_ApsLineStage.OffsetDays` → `APS_SETTING.INJ_OFFSET_DAYS`) 근무일, `DueDate` = 배정 수주 납기 없으면 `ProdDeadline`, `Status` Draft → `ReleaseCore`(INJ 단계 = 계획 행 라인, 라인 있는 마지막 단계 = 같은 실행 ASM 행의 라인, QC/FG NULL), 채번은 `NextWoSeq` 공유(`WO-yyyyMMdd-NNN`). 금형은 `MoldResolver`(직전 금형 → 라인 배정 중 교체 최소 → MoldID 순), 후보 없음 → 그 계획 행 `NoMold` 거부, 고른 금형의 그 라인 `MD_MoldLine.UPH` 없음 → `NoUph` 거부(나머지는 계속). 직전 금형과 다르면 `EntryType='MC'` 행(RefID = 대표 첫 WO)을 앞에 둔다. **INJ 슬롯은 `DeadlinePacker` 가 아니라 `ShiftBands` 고정 배치**다 — 능력은 조회와 같은 APS 설정 패턴(라인 지정 → `DEFAULT_PATTERN`, 대상 행 라인에 없으면 쓰기 전에 `ApsConfigurationException` 으로 전체 거부; 완제품 단계(IMG/PNT) 라인도 라인 지정 → 기본 패턴 순이고 **둘 다 없을 때만** 자동 해석 — 10-06 사용자 지적, 전에는 완제품 라인이 기본 패턴을 건너뛰어 세그먼트 없는 전역 패턴을 잡아 IMG 단계가 전량 Shortfall 이었다), 분 = `ceil(수량 ÷ UPH × 60)`, 주간 수량은 주간 잔여 구간 앞에서부터, 야간은 야간 잔여 구간 앞에서부터, 모자라면 Shortfall(WO 는 Released 로 남아 PP-LSB 미배치 목록). **형제 품번**(같은 실행·날짜·금형·색상 — `MoldResolver.MoldCandidate.Color` = 그 품번의 `MD_MoldItem.Color`, 색상이 다르면 같은 금형이라도 각자 시간을 받는다)은 **다른 자식 품번끼리만** 형제다 — 같은 자식 품번이 여러 부모 WO 로 나뉜 조각(4색 완제품이 코어 하나를 쓰는 경우)은 같은 금형이 차례로 찍는 양이라 **합산**해 조각마다 실제 슬롯을 받는다(10-06 사용자 결정, 전에는 조각끼리 형제로 묶여 사출 부하가 조각 수만큼 과소). `ItemNo` 오름차순 첫 품번이 대표로 시간을 점유(분 = 품번별 합계 중 최대, `MoldID` 는 대표 슬롯에만)하고 나머지 품번은 `StartMin = EndMin = 대표 StartMin` 인 0분 슬롯에 `PlannedQty` 만 기록한다 — POP 은 수량만 읽고 `ReadDayCapacity` 는 `EndMin > StartMin` 만 세어 이중 계상이 없다. 완제품 단계(IMG/PNT)는 같은 WO 의 후속 단계로 `DeadlinePacker.Pack`(오늘 = 사출일, 시작 = INJ 마지막 슬롯 끝, 마감 = `ProdDeadline`) 슬롯 — 배치량은 그 조각이 사출에 실제로 실은 수량 상한(PP-003 과 같은 원칙, 못 실은 나머지는 그 단계 Shortfall). 슬롯은 전부 `DRAFT`, 게시는 PP-LSB 그대로. `PP_ApsRunWo` 에 조각마다 1행, `PP_ApsPlanLine.WoID` 에 첫 WO. 대상이 될 수 있는 행이 남지 않고 WO 가 연결된 행이 하나라도 있으면 `PP_ApsRun.Status='Released'`(거부·미선택 행이 남으면 `Saved` 유지 — 금형 등록 후 같은 실행으로 재실행). `dryRun` 은 같은 경로를 돌고 롤백(다이얼로그 미리보기, WO 번호는 잠정). 감사 `PP-APS/WO/PP_ApsRun`. Pop·Api·InjAgent 변경 없음 — POP 은 종전대로 `PP_LineSchedule` 의 WO 슬롯을 읽는다.
- **배포 순서**: ① `dist/migrate_aps.sql`(`MD_Item.BoxQty` 재생성 — 재생성 목록에 dev 의 `InjFlag`(`ItemCategory` 다음, 없으면 0 으로 생성)과 `ScanRequired`(`BoxQty` 다음, 제약 `DF_MD_Item_ScanRequired` 유지, 없으면 0 으로 생성 — 10-07 aps→dev 머지 때 추가; 기존 DB 에서 ScanRequired 가 끝에 붙어 있으면 재실행이 `BoxQty` 뒤로 옮긴다)도 들어 있다; **dev 의 `migrate_md_item_inj_flag.sql` 은 재생성 목록에 BoxQty 가 없어 migrate_aps 뒤에 돌리면 BoxQty 가 데이터째 사라진다**(10-05 복제본 실제 발생 — inj_flag 먼저, migrate_aps 나중, 또는 migrate_aps 재실행으로 컬럼 복구 후 `seed_aps_dev.sql` ③ 로 값 재시드) — 초판의 `MD_MoldItem.PartCavityCount` 는 있으면 삭제, `MD_ApsLineStage`(§3-b 가 10-06 `PatternID` + FK → `MD_LineTimePattern` 을 기존 테이블에 더한다)·`PP_ApsRun`·`PP_ApsPlanLine`(+`SameItem`)·`PP_ApsRunWo`, 공통코드 `APS_SETTING`(8 — `DEFAULT_PATTERN` 은 빈 값으로 생겨 **적용 직후 PP-APS 조회는 설정 전까지 막힌다**: 설정 다이얼로그에서 기본 패턴 또는 라인별 패턴을 지정할 것, dev 는 `seed_aps_dev.sql` ⑧ 이 라인 전용/전역 ACTIVE 패턴으로 채운다)·`APS_COVER_TIER` — 전제: `migrate_mold_master.sql`·`migrate_md_equipment_type_tonnage.sql`·`migrate_md_item_pallet_qty.sql`(`PalletQty`·`MaxPalletQty`·`ToteFlag`)·`migrate_pp_mrp_result.sql`(`LeadTimeDays`)·`migrate_md_item_mount_pos.sql`(`MountPos`) 적용 후, `AMES_Schema.sql` 로 새로 만든 DB 는 포함. 재생성 전 가드가 `MD_Item` 의 컬럼 구성이 재생성 목록과 다르거나 목록 밖 나가는 FK·인덱스가 있으면 `THROW` 로 중단한다) → ② `dist/migrate_pp_aps_screen.sql`(`SYS_Screen PP-APS` + Admin `REA`) → ③ `dist/migrate_demand_plan.sql`(PP-001 일별 구매계획·DPSYNC Worker 설정 — `PP_DemandPlan`·`PP_DemandPlanBatch`·`PP_ApsRun.IncludeDailyPlan`·공통코드 `SW_DPSYNC*`, `migrate_aps.sql`(`PP_ApsRun`) 뒤 전제) → ④ AMES.Web 게시(`tools\publish-web.ps1`). 셋 다 재실행 안전, `sqlcmd -f 65001 -I -b`. 롤백은 Web 구버전(새 테이블·컬럼은 구버전이 안 읽는다). **마이그레이션 없이 신 Web 을 올리면 PP-APS 진입뿐 아니라 MD-003 품목 목록이 매번 `Invalid column name`(`BoxQty`) 예외이고, PP-APS 조회·저장은 `IncludeDailyPlan` 컬럼이 없어, PP-001 일별 탭은 `PP_DemandPlan`·`PP_DemandPlanBatch` 테이블 자체가 없어(`Invalid object name`) 각각 실패한다**. dev 는 `dist/seed_aps_dev.sql`(운영 금지: `MD_WorkCenter.ProcessCode` 보정, `BoxQty`, `LINE-INJ-01` 전용 2교대 패턴 `LP-APS-INJ01`, `MD_ApsLineStage`, `APS-SO-*` 확정 수주, INJ·IMG BOP)와 `dist/seed_demand_plan_dev.sql`(운영 금지: DPSYNC 소스 SEMS, URL 자리표시자)을 마이그레이션 뒤에 적용한다.
- **알려진 한계**(후속 과제): 금형 교체(MC)의 "직전 금형"은 그 라인·그 날 마지막으로 점유된 금형(축 순서가 아니라 처리 순서 기준)이라, 하루 안에 금형이 여러 번 바뀌는 축의 "사이" 간격을 채우는 되돌림 MC 는 만들지 않는다 — 같은 라인·같은 날에 금형 그룹이 여럿이면 그룹당 MC 는 최대 1개. MC 는 창(주간 → 야간)마다 교체시간 + 1분(교체 뒤 생산 1분 이상)이 한 빈틈에 들어가는 첫 자리에만 두며, 어느 창에도 그런 자리가 없으면(교체시간조차 안 들어가는 경우 포함) 잘못된 금형으로 생산하지 않도록 **MC 도 생산 슬롯도 쓰지 않고 그 날 사출 수량 전량 Shortfall**(WO 는 Released 로 PP-LSB 미배치 목록)이다. PP-LSB 「적용」(`LineScheduleRepository.SaveSchedule`)은 그 날의 APS 형제 0분 행(`EntryType='WO'`·`WoID` 있음·`EndMin ≤ StartMin`·수량 > 0)을 지우기 전에 읽어 되살린다 — 보드는 0분 행을 싣지 않으므로, 보드가 그 WO 에 실제 슬롯을 준 경우에만 그 슬롯이 0분 행을 대신한다(되살린 행이 있으면 placeholder 행은 쓰지 않는다). BOM 규칙 자식의 재고도 같은 통합재고 규칙(`WH_Inventory` `PartNo` 별 `SUM(Qty)`, `Qty > 0`, 위치 무관)이며, PP-APS 는 **통합재고 마이그레이션**(`dev`·`pop` 의 `migrate_unify_inventory.sql`·`migrate_fg_inventory_consolidation.sql`·`migrate_fg_shipment_consolidation.sql`)이 적용된 DB 를 전제한다 — 구 형상(`FG_Inventory`·`WH_Inventory.OnHandQty`)에서는 조회가 `Invalid object name`/`Invalid column name` 으로 실패한다. 수량 0으로 덮인 행(`SkippedExisting`)은 `WoID` 가 남지 않아 같은 실행을 다시 「WO 생성」하면 대상에 다시 보인다(재생성은 아니다 — 이미 있는 슬롯 수량만큼 다시 빼므로 이중 발행은 없다). 저장 없이 화면을 편집한 뒤 「WO 생성」하면 저장본(마지막 「저장」) 기준으로 만들어진다(확인창이 안내).

#### MNT (설비보전) — 10화면
`Dashboard` / `EquipmentCard` / `WorkOrder` / `EquipPmSchedule`(MNT-005, PM_CLASS=EQUIP) / `MaintPmSchedule`(MNT-010, PM_CLASS=MAINT — 둘 다 `PmScheduleBoard` 공유, 테이블 `MNT_PMSchedule` 공용) / `Downtime`
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

### 시각 기준 — DbClock
- 기록 시각의 정본은 DB 서버 시각(`SYSDATETIME()`, 공장 현지시각 — 개발서버는 미국 동부시간)이다. 화면 기본값·"오늘" 필터·채번 접두어에 **`DateTime.Now`/`DateTime.Today` 를 쓰지 말고 `DbClock.Now`/`DbClock.Today`**(`AMES.Data.Services.DbClock`, Web 은 `GlobalUsings.cs` 의 전역 별칭)를 쓴다.
- `DbClock` 은 DB 시각 − 호스트 시각(Offset)을 한 번 읽어 두고 `DateTime.Now + Offset` 으로 계산한다(호출마다 DB 를 가지 않음, Kind=Unspecified). Web 은 기동 시 `Program.cs` 가 1회, 로그인(회로 시작) 때 `TopBar` 가 10분보다 오래됐을 때만 다시 맞춘다. PP-LSB 는 Now 선 때문에 1분 기준. 상태는 `GET /api/health` 의 `dbClock`(now·offsetMinutes·syncAgeSec)로 확인한다.
- `Configure` 하지 않은 프로세스(POP·API)는 Offset 0 = 호스트 시계 그대로다. 웹과 DB 가 같은 기계면 Offset 은 0 에 가깝고, 한국시간 PC 의 로컬 IIS + 개발 DB 조합에서는 −780분이 정상이다.

### Repository 패턴
- 모든 DB 접근은 `AMES.Data.Repositories.*Repository` 경유
- 각 메서드마다 `using var conn = _connFactory.OpenConnection()` (connection-per-method)
- `MapToDto()` static 헬퍼로 `SqlDataReader` → DTO 변환
- 향후 `dbo.SP_*` 스토어드 프로시저로 전환 예정 (현재 inline SQL)

### Pop 모듈 분기
로그인 화면에서 작업자가 Line/Station을 선택하면, 선택 라인의
`MD_Line.WCID → MD_WorkCenter.ProcessCode`(INJ/IMG/PNT/QC/RWK)로 모듈이 결정된다.
appsettings 의 `PopTerminal:ModuleCode`/`LineId`/`StationId` 는 제거됐다 — 매 로그인 선택.
모듈 코드는 `AppState.ModuleCode` 에 실리고, 라우팅과 라벨 디스패처 게이트가 이를 본다.

### 인증 흐름
- **Pop**: `PopAuthService` → `AuthRepository.FindByEmployeeNo()` → 없으면 `WorkerRepository.FindByEmployeeNo()` → `PinHasher` (PBKDF2) → `PopSessionRepository.CreateSession()`
  - POP 로그인은 두 곳을 본다: 웹 계정 작업자(`SYS_UserProfile` + `AspNetUsers`)와 POP 전용 작업자(`MD_Worker`). **사번이 겹치면 웹 계정이 이긴다.**
  - **POP·PDA 로그인에 라인 제한은 없다**(09-24) — 웹 계정의 라인 배정 컬럼 `SYS_UserProfile.AssignedLines` 와 SYS-001 라인 선택, `PopAuthService` 라인 검사를 없앴다(`dist/migrate_drop_user_assigned_lines.sql`, 신 Web·Pop·Api 배포 **후** 적용 — 구 바이너리는 이 컬럼을 SELECT 해서 먼저 적용하면 SYS-001 목록·POP 로그인이 `Invalid column name` 으로 실패). `AuthResult.LineNotAuthorized` 값은 과거 `PR_PopAuthLog` 해석용으로만 남아 있다.
  - `MD_Worker` 는 최소 구성이라 실패 카운터가 없다 — 워커는 **PIN 오류로 잠기지 않는다**.
  - **POP 이 기록하는 "누가" 는 전부 로그인 사번이다**(09-23) — 웹 계정도 GUID 가 아니라 `SYS_UserProfile.EmployeeNo`. `PopSessionRepository.CreateSession` 이 `PR_PopSession.OperatorID`·`PopSessionDto.OperatorId` 를 사번으로 채우므로 실적·LOT·PNT·QC·재작업의 `OperatorID`·`ConfirmedBy`·`InspectorID` 등 사람 컬럼과 `CreatedBy`·`ModifiedBy` 가 모두 사번이 된다(`AspNetUsers.Id` 는 `IsAdmin` 역할 조회에만 쓴다). PDA/Tablet 도 같은 `CreateSession` 을 쓰므로 API 토큰의 `OperatorId` 도 사번이다. 로그인 전 기록은 시도한 사번(`PR_PopAuthLog.CreatedBy`, PIN 실패 시 `SYS_UserProfile.ModifiedBy`), 안돈은 감사 컬럼 = 로그인 사번 · 배지를 찍은 사람은 `AckedBy`·`CalledBy`·`ArrivedNo`·`TechnicianID`·`ReportedBy` 에 남는다. 그 이전 행은 GUID 가 섞여 있다. 사번은 웹 계정·워커를 통틀어 전사 유일해야 한다(SYS-001 사용자 저장이 두 테이블과의 중복을 거부하지만 DB 제약은 없다).
  - `AMES.Api` 의 `/api/auth/login` 도 같은 서비스를 쓰므로 **PDA 도 워커 로그인을 받는다.**
  - POP 로그인 화면은 시리얼 스캐너(`ScannerService`)로 사원증 QR 을 받으면 `AuthMethod.Badge` 로 **PIN 없이 즉시 로그인**한다. 라인·스테이션 미선택, 픽커 열림, 로그인 진행 중에는 스캔을 무시한다.
  - 사원증 QR 발행 양식은 **`EOS*사번*이름`** 세 토큰이고 `AMES.Devices.BadgeScanParser` 가 정본이다(단위 테스트 `AMES.Pop.Tests/BadgeScanParserTests`). 이 양식이 아니면 스캔값 전체를 사번으로 본다 — 구 사번-only QR 과 웹 계정 배지가 계속 동작하게 하려는 것이며, 그 경로는 자동 등록 대상이 아니다.
  - **EOS 양식으로 읽히면 모르는 사번은 그 자리에서 `MD_Worker` 에 만들어진다**(`EmployeeName`=배지의 이름, 없으면 사번 / PIN 없음 / `CreatedBy`=본인 사번 — 09-23 이전 행은 `'POP-SCAN'`, MD-032 는 둘 다 배지 등록으로 표시). 로그인 화면 스캔에서만 동작하며 PDA/API 는 종전대로 등록된 사람만 받는다. 즉 **EOS 양식 QR 을 인쇄할 수 있으면 누구나 계정을 만들 수 있다** — 배지 발급을 통제할 것. INSERT 전용이라 `ActiveFlag=0` 인 행은 재스캔으로 되살아나지 않고, 관리자가 고친 이름도 덮이지 않는다.
  - **`PinHash` 가 없는 워커는 배지 로그인 직후 PIN 설정을 강제한다** — 4자리를 두 번 입력해 일치해야 저장(`ModifiedBy`=본인 사번)되고 작업 화면으로 넘어간다. 건너뛸 수 없다: PIN 이 없으면 스캐너가 죽었을 때 그 사람은 들어올 방법이 없다. 자동 등록분뿐 아니라 등록 화면에서 PIN 없이 만든 워커도 대상이다.
  - 화면이 이걸 판단하는 근거는 `PopSessionDto.IsWorker` · `HasPin` 이고 `PopSessionRepository.CreateSession` 이 채운다. **PIN 설정 전에 `PR_PopSession` 행은 이미 생긴다** — 오버레이 상태로 자리를 뜨면 열린 세션이 남고 만료시각으로만 정리된다.
- **Api**: `POST /api/auth/login` → `TokenStore.Issue()` → Bearer 헤더 검증 (`BearerAuth` 미들웨어)
- **Web**: ASP.NET Identity, `ApplicationDbContext` (EF Core, Identity 테이블 전용)
  - **화면 읽기 권한은 `MainLayout` 이 공통으로 검사한다**(09-29) — 라우터가 정적이라 화면 이동마다 레이아웃이 서버에서 먼저 돌고, 내부 계정이 `SYS_Screen` 에 등록된 화면(하위 경로 포함, PORTAL 제외)을 R 없이 열면 `/unauthorized`(`PermissionService.CanOpen`). 새 화면도 `SYS_Screen` 에 등록하면 자동으로 막히고, 미등록 경로는 막지 않는다. 화면별 `IsVisible` 검사는 남아 있어도 무방하며 E/A 버튼 게이트는 여전히 화면 몫이다.
  - `PermissionService` 는 **불러오기 전·첫 로드 실패 시 권한 없음**(구 "REA" 허용 폐지)이고, 첫 로드에 실패하면 `CanOpen` 도 홈·`/unauthorized`·오류·계정 화면만 연다. 다시 읽기는 **`EnsureAsync` 에서만** — `ScreenCatalogNotifier.Version` 이 바뀌었거나(SYS-003·SYS-004 저장이 `Notify()`) 60초가 지났을 때(다른 서버·DB 직접 수정분) 스레드 풀에서 읽어 한 번에 바꾸고, 실패하면 직전 값을 유지한다. `IsVisible`·`CanEdit` 같은 조회는 DB 를 부르지 않는다. 메뉴는 화면 이동 때 `EnsureAsync` 를 불러 갱신하며, 알림 한 번에 세션당 한 번만 다시 읽는다(10-07 — `ScreenCatalogNotifier.Notify` 는 구독자를 스레드 풀에서 부르고, 메뉴·홈·화면 제목은 DB 재조회를 회로 밖에서 한 뒤 다시 그리기만 회로에서 한다. 메뉴 카탈로그는 `MenuCatalog.ReloadFor(버전)` 으로 같은 세션의 메뉴·홈이 겹쳐도 한 번만 읽는다. 전에는 저장한 사람의 처리 안에서 모든 세션의 재조회가 차례로 돌았다)(`Reset()` 은 다음 EnsureAsync 에 다시 읽게 표시만 한다). 이미 열린 화면의 버튼 상태는 화면 이동 때 반영된다. 역할 배정(SYS-001) 변경은 보안 스탬프를 갱신하므로 재확인 주기(기본 5분) 안에 그 사용자 세션이 끊겨 재로그인 때 반영.
  - **열린 화면 끊기·DB 장애 안내(10-06)**: `MainLayout`·`PortalLayout` 이 대화형 `Layout/SessionGuard` 를 둔다 — ① 회로 재검증이 인증을 풀면(포탈 잠금·비활성·업체 변경, 내부 보안 스탬프 변경) 열린 화면도 로그인으로 보낸다(내부 `Account/Login?ReturnUrl=`, 포탈 `portal/login`). 라우터가 정적이라 이 구독이 없으면 인증이 풀려도 열린 화면이 그대로 남았다. 재검증 주기는 설정 `Auth:RevalidationMinutes`(기본 5 — 10-07 30 에서 줄임, 내부 쿠키의 보안 스탬프·상태 재확인도 같은 주기). ② DB 연결 장애 배너 — `AMES.Data.Connection.DbHealth` 가 `AmesConnectionFactory.OpenConnection` 의 성공·실패를 기록하고(마지막이 실패면 장애, EF Identity 연결은 `DbHealth.IsConnectionFailure(ex)` 로 판정 — 연결 오류 번호(서버 없음·시간 초과·소켓·DB 열기·앱 계정 로그인 실패)와 심각도 20 이상만 장애로 보고, 값 잘림·없는 컬럼·교착 같은 문장 오류는 장애가 아니라 일반 오류 화면으로 간다(10-07)), 배너는 3초마다 상태를 보고 장애 중 9초마다 스레드 풀에서 연결을 시험해 복구 시 저절로 사라진다(화면들이 DB 오류를 빈 목록으로 삼키므로 따로 알린다). 권한 읽기 실패의 원인이 DB 장애면 `MainLayout` 은 `/unauthorized` 대신 장애 안내로 본문을 대체하고, 내부·포탈 로그인은 "DB 연결 장애로 로그인할 수 없습니다", 오류 화면(`/Error`)도 장애 문구를 보인다(키 `App.DbDown.*`, 정본 테스트 `DbHealthTests`).
  - 회로 재검증(`IdentityRevalidatingAuthenticationStateProvider`)은 포탈(`AmesPortal`) 사용자를 Identity 가 아니라 `SCM_PortalVendorUser`(활성·잠금·업체 활성·업체 일치, 쿠키 검증과 같은 기준)로 본다 — 예전에는 포탈 사용자가 30분마다 인증이 풀려 같은 회로의 포탈 화면이 빈 목록이 됐다(09-30).
  - 행 클릭으로 수정 모달을 여는 화면은 **클릭 조건에도 `_canEdit` 를** 넣고, 저장·삭제 핸들러 첫 줄에서도 `_canEdit` 를 다시 본다(09-30 SYS-008·MD-004 BOM·PP-LSB 초기화에서 R 만으로 수정되던 것 수정).
  - 로그인 뒤 돌아갈 주소(ReturnUrl)는 `Services/LocalUrl` 규칙(같은 사이트 `/x` 만, `//host`·`/\host`·절대 URL 거부)으로만 검사한다 — `Uri.IsWellFormedUriString(…, Relative)` 는 `//host` 를 통과시키므로 쓰지 말 것.
  - SYS-009 활성 토글은 **활성 여부를 실제로 읽는 키(`Config.razor` `ActivationKeys` = 현재 `LANGUAGE_DEFAULT`)에만** 보인다 — 나머지 키는 코드가 `IsActive` 를 보지 않으므로 숨기고 값 편집도 잠그지 않는다. 새 키가 활성 여부를 읽게 되면 그 집합에 넣는다. 유형 `TIME` 은 `HH:mm` 로만 저장한다(`ProdCalendar.TryNormalizeCutoff`, `7:00`·`0700` 입력은 `07:00` 으로, 틀리면 전체 저장 거부).
  - SYS-009 비밀값(유형 `password` 또는 숫자가 아닌 `*PASSWORD*`·`SECRET`·`TOKEN`·`APIKEY`·`_KEY` 키)은 브라우저로 보내지 않는다 — 조회 전용은 `••••••`, 수정은 빈 입력란(비우면 유지). 언어 스위처는 `AppLanguageState.SupportedCultures` 에 있는 컬처만 링크로 만든다. `DetailedErrors`·Swagger 는 Development 에서만 켜진다.
  - **계정 등록·로그인(10-07, 사용자 결정)**: 내부 계정은 **SYS-001 에서 관리자가 직접 등록한 계정만** 쓴다 — 자기가입(`Register`)·비밀번호 찾기·재설정·이메일 인증·인증 메일 재발송·이메일 변경(`Manage/Email`)·외부 로그인(`ExternalLogin`·`Manage/ExternalLogins`) 화면과 메일 발송 코드(SMTP·`AccountMail`·`IdentityNoOpEmailSender`·MailKit 패키지·appsettings `Smtp`)를 모두 없앴다(M365 테넌트가 SMTP AUTH 를 막아 둔 것도 이유). 로그인은 **계정 상태(`SYS_UserProfile.AccountStatus`)가 ACTIVE 일 때만** 되고(`WebSignIn`, 잠김은 `Auth.Err.Locked`, 그 밖은 `Auth.Err.Inactive`) 이메일 인증 여부는 보지 않는다(`RequireConfirmedAccount=false`). 프로필이 없는 계정은 로그인할 수 없다(`AuthRepository.GetProfileStatus` → INACTIVE) — 기동 시드 admin 은 `EnsureActiveProfile` 로 활성 프로필을 같이 만들고, SYS-001 목록은 프로필 없는 계정을 "프로필 없음"으로 보이며(필터 가능) 그 계정을 수정 저장하면 고른 상태로 프로필을 만든다. 프로필 없는 계정은 MNT 고장 보고자·작업지시·PM 담당자 콤보와 SYS-008 알림 관리의 수신자 콤보에서 빠진다(`UserRow.IsApproved`·`UserSelectRow.IsApproved`, 이미 저장된 값은 수정 창에서 빈칸이 되지 않게 남긴다). 계정 상태 공통코드 `USER_STATUS` 는 ACTIVE·INACTIVE·SUSPENDED·LOCKED 뿐이다(PENDING·UNVERIFIED 삭제). 로그인 화면의 개발 계정 안내(`admin@ames.local / Dev2026!`)는 **Development 환경에서만** 보인다(IIS 는 Production 이라 안 보임).
  - **계정 보안 규칙(10-07, 사용자 결정)**: ① **잠금은 자동으로 풀리지 않는다** — 로그인 비밀번호와 PP-CAL 납기 변경 PIN 실패를 같은 카운터(`SYS_UserProfile.FailedLoginCount`)에 세어 5회면 LOCKED, SYS-001 에서 **Admin 만** 해제한다(Identity 자체 잠금은 쓰지 않는다). Admin 이 모두 잠기면 `dist/unlock_user.sql`(`-v Email=… Mode=ROLLBACK|COMMIT`)로 DB 에서 직접 푼다. ② 로그인(내부·외부)은 **비밀번호를 먼저** 확인한다 — 틀리면 계정 유무·상태와 관계없이 `Auth.Err.Invalid` 만 보이고 맞을 때만 잠김·비활성 사유를 보인다(없는 계정도 더미 해시 검증으로 시간을 맞춘다). 잠금이 일어나면 감사 로그 `ACCOUNT/LOCK`(대상·IP). ③ 로그인 POST(`/Account/Login*`·`/portal/login`)는 IP 당 분당 `Auth:LoginPerMinute`(기본 10)회 — 넘치면 `?throttled=1` 로 돌려보낸다(`Program.cs` `AddRateLimiter`, `PortalAuth.IsLoginPost`). ④ SYS-001 의 역할·상태·비밀번호·잠금 해제·삭제와 다른 사람의 PIN 은 화면 E 권한이 아니라 **Admin 역할만**(자기 PIN 은 누구나 SYS-001 에서 바꾼다 — 10-07 사용자 결정)(E 만 있는 사용자는 사번·이름·부서·공장·교대만 고치고 Admin 계정은 열지 못한다, 프로필 없는 계정을 저장하면 INACTIVE 로 만든다). Admin 은 **자기 자신을 제외한 모든 사용자를 삭제**할 수 있고(다른 Admin 포함) 자기 역할·상태는 바꾸지 못한다. ⑤ 상태·역할·비밀번호가 바뀌면 `UpdateSecurityStampAsync` — 그 사용자의 열린 화면·쿠키가 재확인 주기(`Auth:RevalidationMinutes` 기본 5분) 안에 끊긴다. 재확인은 스탬프 + 계정 상태(`Services/AuthRevalidation`, DB 를 직접 고친 비활성·정지·프로필 삭제도 끊음)이며 **LOCKED 는 세션을 끊지 않는다**(실패 잠금은 남이 일부러 일으킬 수 있어 일하던 사람을 내쫓지 않는다 — 새 로그인만 막는다). ⑨ **신뢰 기기(`Services/TrustedDevices`, 10-07)** — 내부 로그인에 성공한 브라우저는 그 계정 전용 쿠키 `ames.td.{계정 해시}`(Data Protection 서명·1년·경로 `/Account`)를 받는다. 계정이 LOCKED 여도 신뢰 기기에서 맞는 비밀번호면 로그인되고(감사 `ACCOUNT/LOGIN_TRUSTED`, 계정 잠금은 그대로라 새 기기는 Admin 해제 전까지 막힘), 신뢰 기기에서 틀린 횟수는 계정이 아니라 그 기기에 세어 5회면 그 기기만 신뢰 해제(`DEVICE_UNTRUST`, 사용자 결정 (a)). 기기별 실패 수·해제 목록은 메모리(재시작 시 초기화). 남이 다른 PC 에서 일부러 잠가도 평소 PC 는 계속 쓴다. ⑩ **포탈 비밀번호 버전** — 포탈 쿠키에 `ames:pwv`(비밀번호 해시 SHA-256 앞 16자, `PortalAuth.PasswordVersion`)를 넣고 쿠키 검증·회로 재검증이 현재 해시와 비교해 다르면 끊는다(본인 변경·SCM-004 재설정 → 다른 PC 는 다음 요청에 로그아웃). 변경한 본인은 1회용 표로 `GET /portal/refresh-signin` 에서 새 쿠키를 받는다. 이 클레임이 없는 배포 전 쿠키는 통과(다음 로그인부터 적용, 사용자 결정). ⑥ 자기 계정 삭제 화면(`Manage/DeletePersonalData`)은 없앴다. SYS-001 삭제는 `SysRepository.DeleteProfile` 이 프로필과 Identity 보조 행(AspNetUserRoles·Claims·Logins·Tokens)을 한 트랜잭션으로 먼저 지운다 — 개발·로컬 DB·스키마의 이 테이블들에 외래키가 없어 `UserManager.DeleteAsync` 만으로는 역할 행이 고아로 남았다(10-07 브라우저 검증에서 발견). ⑦ CSV 내보내기(PP-002·PP-001·PP-003)는 `AMES.Contracts.Formatting.CsvCell`(테스트 `CsvCellTests`)로 `= + - @`·탭으로 시작하는 값 앞에 `'` 를 붙인다(숫자 그대로인 값은 제외). ⑧ **상단바 열쇠 버튼(`Layout/MyCredentialsDialog`)으로 누구나 자기 비밀번호·PIN 을 바꾼다**(외부 포탈 상단바도 같은 열쇠 버튼 `Layout/PortalPasswordDialog` — 비밀번호만, 실패는 포탈 카운터·SCM-004 해제, 외부 쿠키는 해시를 보지 않아 재발급 없음, 감사 `PORTAL/PWD_CHANGE`) — 둘 다 현재 비밀번호로 다시 확인하고 틀리면 같은 실패 카운터에 쌓인다(LOCKED 면 변경 불가). 비밀번호를 바꾸면 스탬프가 바뀌므로 회로가 1회용 표(`Services/SignInRefreshTickets`, 60초)를 발급해 `GET /Account/RefreshSignIn?t=` 으로 이동하고 그 요청이 새 쿠키를 준다(ACTIVE 만, 로그인 유지 여부는 기존 쿠키 값). 감사 `ACCOUNT/PWD_CHANGE`·`PIN_SET`(note self). 대화상자는 `<header>` 밖에 둔다 — `.ames-topbar` 의 `backdrop-filter` 가 fixed 모달을 헤더 안에 가둔다.

---

## 코드 컨벤션

- **네임스페이스**: `AMES.<Project>.<Subfolder>` (e.g. `AMES.Pop.Pages`, `AMES.Data.Repositories`)
- **DTO**: `AMES.Contracts.Dto.*Dto` — 계산 프로퍼티 허용 (`ProgressPct`, `DaysToDue` 등)
- **Enum**: `AMES.Contracts.Enums.*` (`ItemType`, `AuthResult`, `AuthMethod`)
- **Pop 공통 컴포넌트**: `Common/` — `AppConfig`, `PopServices`, `ToastService`, `ConfirmService`, `HelpModal`
- **감사 로그(Web MD·SYS)**: 등록·수정·삭제 핸들러는 저장 성공 직후 `AuditLogger`(`@inject AMES.Web.Services.AuditLogger Audit`)를 한 줄 부른다 — `Audit.Created/Updated/Deleted(화면코드, 테이블, 키, 전, 후)`, 그 밖의 동작은 `Audit.Log`(APPROVE·COPY·PIN_SET·PIN_RESET·PWD_RESET·UNLOCK). 전/후는 선택 행·폼 모델을 그대로 넘기며 Password/Pin/Secret/Token/Hash 속성은 서비스가 뺀다. 감사 실패는 저장을 실패로 만들지 않는다(경고 로그). 값이 든 속성 이름만으로 비밀인지 알 수 없는 경우(공통코드 `SW_*_AUTH` 의 설명란 토큰 등)는 `AuditLogger.IsSecretKey(그룹·키)` 로 판단해 `AuditLogger.Redact(스냅샷, "Description")` 로 가린 JSON 을 넘긴다. 계정 관리 화면(Account/Manage 비밀번호 변경 등)은 `Audit.Log("ACCOUNT", …)` 로 남기며 모듈은 SYS 로 묶인다. 수정 모달을 열 때 폼 모델에 키 필드도 채워 둘 것 — 비워 두면 After 스냅샷의 키가 빈 값으로 남는다. 중괄호 없는 if/else 문장 뒤에 넣으면 분기 밖이 되므로 주의. 페이징 그리드는 `PagingSummaryFormat="@L["Pager.Summary"]" PageSizeText="@L["Pager.PageSize"]"` 를 붙인다
- **행위자 코드(CreatedBy·ModifiedBy·ApprovedBy·RequestedBy)**: 09-23 `dist/migrate_audit_actor_varchar20.sql` 이후 이 컬럼들은 전 테이블에서 **`varchar(20)`** 이다(원문은 `SYS_AuditActorMap` 에 보존). GUID·이메일을 그대로 넣으면 `String or binary data would be truncated` 로 저장·로그인이 500 난다. Web 은 `AMES.Web.Services.ActorCode.Of(user)` 만 쓴다 — 로그인 클레임 `ames:actor`(= `SYS_UserProfile.EmployeeNo`, `AmesClaimsPrincipalFactory` 가 채움) → 없으면 사용자명의 `@` 앞부분 → 20바이트(CP949) 절단. `auth.User.Identity?.Name` 을 행위자로 쓰지 말 것. `AuditLogger` 도 같은 규칙으로 줄인다. POP·Api 는 세션의 `EmployeeNo` 를 넘긴다 — 09-23 부터 세션 `OperatorId` 도 사번이다(위 인증 흐름 참조, 그 이전 행의 `OperatorID` 에는 GUID 가 섞여 있다). 리포지토리의 행위자 파라미터는 `SqlDbType.VarChar, 20`(컬럼과 같은 형 — 09-30 개발 DB 기준 정렬) 이며 SqlClient 는 더 긴 값을 **예외 없이 잘라** 보내므로 GUID·이메일을 넘기지 말 것. 단 사용자 ID 를 담는 `nvarchar(450)` 컬럼(`ReportedBy`·`LoggedBy`·`ReleasedBy` 등)은 그 컬럼 형을 따른다.
- **DB 매개변수 형식·길이 = 개발 DB 컬럼**(09-30): `cmd.Parameters.Add("@X", SqlDbType.T, N)` 의 T·N 은 비교·대입되는 개발 DB 컬럼과 같게 둔다 — varchar 컬럼에 `NVarChar`(암시적 변환), nvarchar 컬럼에 `VarChar`(한글 손실), 컬럼보다 짧은 N(저장 시 조용히 잘림)을 쓰지 말 것. 한 매개변수가 여러 컬럼에 쓰이면 가장 짧은 컬럼 길이, LIKE 검색 매개변수는 `%` 를 붙이므로 길어도 된다. 읽기도 컬럼 형 그대로 — `smallint` 를 `as int?`, `bigint` 를 `as int?`, `int` 를 `as string` 으로 읽으면 **예외 없이 null** 이 된다(09-30 `PP_LineSchedule.StartMin/EndMin`·`MD_Mold.CumulativeShots`·`PP_LineDowntimeLog.AndonID` 수정). 화면 입력칸 `MaxLength` 도 컬럼 길이와 같게. `RecordSuccessfulLogin`·`IncrementFailedCount` 는 넘긴 사번(POP) → 없으면 프로필 사번 → 없으면 UserID 앞 20자를 `ModifiedBy` 에 쓴다. 기존 쿠키는 재로그인해야 클레임이 생기고, 그 전에는 사용자명 폴백으로 동작한다. **표시**는 `AMES.Web.Services.ActorNames`(`@inject … Actors`, `Actors.Display(code)` = "이름 (사번)", 사전 = `AuthRepository.ListActorNames`: 사번·GUID·사용자명·`SYS_AuditActorMap` 별칭 → 이름, 5분 캐시)로 하며 원시 코드를 그대로 찍지 않는다(MD-032·MD-033·MD-004 승인란·PP-006·PP-DTL·SYS-007 적용). 담당자·신고자처럼 **사용자 ID(GUID)를 저장한 컬럼도 같은 `Actors.Display`** 로 보인다 — 사전이 GUID 를 사번으로 바꿔 "이름 (사번)"(`AuthRepository.ListUserEmployeeNos`, 09-28 MNT 담당자·보고자·교체자·기록자). 사람 선택 콤보는 `[사번] 이름`.
- **수량 표시 = 단위 자릿수**(09-25): 단위가 있는 수량은 `N0`·`N3`·`0.###` 를 하드코딩하지 않고 `MD_Uom.DecimalPrec` 로 표시한다. 규칙 정본은 `AMES.Contracts.Formatting.UomQty`(테스트 `AMES.Data.Tests/UomQtyTests`) — 자릿수 고정(KG 3 → `12.500`), 자릿수보다 긴 소수는 반올림해 숨기지 않고 최대 6자리까지 더 보이며, 모르는 단위(미등록·빈 값·NULL)는 필요한 자릿수만. Web 은 `@inject UomFormat UomFmt` → `UomFmt.Qty(값, 단위)` / `QtyUnit`(5분 캐시, MD-019 저장 시 `Invalidate`). PDA·Tablet 은 아직 적용하지 않았다(각 화면의 고정 서식 그대로). **단위는 기호로 보인다**(10-08 사용자 결정) — 화면·인쇄물(포탈 납품서·박스/케이스 라벨 포함)은 `UomFmt.Sym(코드)`(MD_Uom.Symbol 을 저장된 값 그대로, 대소문자 포함 — 기호가 비거나 모르는 단위면 코드), `UomFmt.QtyUnit` 도 기호를 붙인다. 저장값·검색·정렬·단위 선택 콤보(`[EA] Each`)·CSV/엑셀 내보내기는 단위 코드 그대로이고, MD-019 단위 관리 화면은 코드를 보인다. 웹만 적용(POP·PDA 미적용). 단위가 섞인 합계·단위를 모르는 값은 단위 없이 `UomFmt.Qty(값, null)`. 입력란(RadzenNumeric 등) 서식과 저장 시 반올림은 아직 적용하지 않았다
- **검색창 placeholder**(09-29): `A/B/C` — 슬래시로만 잇고 공백·`·`·쉼표·"검색"·"..." 를 붙이지 않는다(예 `설비ID/설비명`). 키는 `{모듈}.{화면}.Ph.Search`(보조 검색은 `Ph.TxnSearch` 등)로 resx 4파일에 두고, 인라인 `T()`·`PortalText` 나 `Btn.Search`("조회")·"—" 를 placeholder 로 쓰지 않는다. ko·en·es 세 언어가 입력칸 폭에 들어가는지 확인한다
- **라인 표시 순서**(10-01, Web 만): 라인으로 정렬하는 목록·카드·콤보는 **공정 순(사출 → 감싸기 → 도장 → 재작업) → 라인 ID**. 공정 순위 = 라인 WC 의 `ProcessCode` 를 공통코드 `PROCESS.SortOrder` 로 바꾼 값(순서 변경은 MD-26), 공정 모르는 라인은 맨 뒤. 정본 `AMES.Data.Services.LineOrder` — SQL 은 `ORDER BY {LineOrder.RankSql("x.LineID")}, x.LineID`, 화면은 `MD.LineProcessOrder()` 사전 + `LineOrder.Rank(사전, lineId)`. `MD.ListLines()` 를 쓰는 콤보는 자동 적용. POP(`ListPopLineOptions`)과 Api 공용 `ListEquipment` SQL 은 바꾸지 않았고, 값(OEE·건수) 순위 정렬 화면은 대상이 아니다
- **라이트(Day) 테마 색**(09-30): 다크가 기본이고 라이트는 `app.css` 의 `html[data-theme="light"]` 블록이 덮는다. Radzen 은 `App.razor` 가 `humanistic-dark-base.css`·`humanistic-base.css` 두 링크를 두고 `media` 로 하나만 켠다(`theme.js` 가 같은 id 로 전환, 쿠키 `ames.theme` 로 서버 렌더 — 테마는 브라우저 단위). 새 스타일에 **다크 전용 밝은 색(흰색·Tailwind 50~500)을 글자색으로, 어두운 단색을 배경으로 직접 쓰지 말고** `--ames-tx-*`(글자)·`--ames-sf-*`/`--ames-surface`(면)·`--ames-bd-*`(흰 반투명 테두리) 토큰을 쓴다 — 고정 색을 넣었으면 `node tools/theme-tokenize.js --write`(멱등, 인자 없으면 모의 실행)로 토큰화한다. 토큰 정의는 `app.css` 의 `THEME TOKENS (dark)`(다크 원값 = 다크 화면 불변)·`THEME TOKENS (light)`(같은 색상 700/800·`--ames-text`) 두 블록이며 스크립트가 다시 쓴다. C# 이 돌려주는 글자색도 `"var(--ames-tx-red-400)"` 처럼 토큰 문자열로(SVG `fill`/`stroke` 속성·막대 배경의 중간 톤은 그대로). 제외: 사출 현황판 `Display/InjShots`(다크 고정), 인쇄 CSS, 로그인 화면. 라이트의 Radzen 보정(`--rz-base-*` 아이보리, 색 버튼 `--rz-danger/success/warning/info/secondary` 진한 색)도 라이트 블록에 있다
- **처리 중 재클릭(10-05)**: Web 의 모든 컴포넌트는 `Components/_Imports.razor` 의 `@inherits AmesComponentBase`(`Components/AmesComponentBase.cs`, 레이아웃은 제외)를 상속한다 — 마우스 이벤트(버튼·행 클릭)에 한해 ① 같은 핸들러가 아직 실행 중(비동기 대기)이면 새 클릭을 버리고 ② 0.4초 이상 걸린 처리 직후 0.4초 안에 들어온 클릭(동기 처리 중 눌려 대기열에 쌓였던 것)도 버린다. Blazor Server 는 한 회로의 이벤트를 차례로 처리해, 동기 저장 중에 누른 클릭이 처리 뒤 그대로 다시 실행됐다(PP-LSB 적용 두 번 저장). 그 밖의 이벤트·다시 그리기 동작은 ComponentBase 와 같다. 새 화면에서 오래 걸리는 저장은 PP-LSB `SaveAndReload` 처럼 `async` + 상태 필드(`_busy`)로 버튼을 끄고 "…중"(`Msg.Saving`·`Msg.Applying`·`Msg.Publishing` 등)을 보이며 DB 호출은 `await Task.Run(...)` 으로 회로 밖에서 돌린다(동기 호출이면 `_busy` 를 켜도 처리가 끝날 때까지 화면에 반영되지 않는다). **처리 중 표시는 공통이다**(10-06) — `wwwroot/js/busy-buttons.js` 가 저장류 색 버튼(Primary·Danger·Success·Warning)을 누른 뒤 0.35초 안에 끝나지 않으면 그 버튼을 같은 크기로 고정하고 스피너 + "처리 중…"(ko/en/es, `html lang` 기준, 폭 72px 미만은 스피너만)으로 바꾸며, 끝은 `AmesComponentBase` 가 클릭 처리 완료(버린 클릭 포함) 때 `amesBusy.done` 으로 알린다(30초 안전 해제). 동기 처리 화면도 그대로 보이므로 화면마다 문구를 넣을 필요는 없다 — 버튼이 스스로 바뀌면(화면별 "적용 중…" 문구·비활성) 그 표시를 쓰고 덮지 않는다. 표시가 필요 없는 버튼은 조상 요소에 `data-no-busy`.
- **주석**: 비명확한 WHY에만 최소 작성, WHAT 설명 주석 금지
- **Pop 화면 파일명**: `{ModuleCode}{화면번호}{기능명}.razor` (e.g. `Inj04ProductionEntry.razor`)

---

## DB 스키마 영역

스키마 파일·개발 DB 176개 테이블(TEST_* 제외), 기능 접두사로 구분:

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

**품목·BOM 정본은 `docs/260828 BOM Master List.xlsx`** 이고(09-28), `tools/gen_md_item_bom_seed.py` 가 그 엑셀을 읽어 `dist/seed_md_item_bom_master_list.sql` 을 만든다 — 생성물이라 손으로 고치지 말고 엑셀·생성기를 고쳐 다시 만든다. 시드는 `MD_Item`·`MD_BomVersion`·`MD_Bom`·`SCM_ItemVendor` 를 **전부 지우고** 넣으며(SEMS 추출분 등 엑셀에 없는 품목은 사라진다 — 사용자 결정), 품목·BOM 을 **FK 로** 참조하는 행이 있으면 시작 전에 THROW 하므로(FG 테이블 구성이 DB 마다 달라 이름이 아니라 `sys.foreign_keys` 로 찾는다 — FK 없는 수주·WO·LOT·재고·반품 참조는 검사하지 않고 고아로 남는다. 마지막 FK `FK_FG_CustomerReturn_Item` 은 09-30 `dist/migrate_drop_fg_customer_return_item_fk.sql` 로 없앴고 스키마·`PDA_SCHEMA.sql` 도 더 이상 만들지 않는다) `rebuild_db.sh` 순서(PDA_SEED 이전, 구 `reseed_md_item_partmaster.sql` 자리)로 적용하거나, 운영 중인 DB 에는 `dist/run_seed_md_item_bom_master_list.sql`(저장소 루트에서 `sqlcmd … -I -b -v Mode=ROLLBACK|COMMIT`, 트랜잭션 하나)로 단독 적용한다. 규칙: ItemNo = 품번 + 3자리 색상(루트는 Assembly Color 마다 1행, 자식은 Material Color 가 조립색별로 다르면 같은 순번 색·단일 색이면 그 색·BK/공란이면 무접미) · RoutingType 은 Complete ASSY = `'A'`(코어 사출 → 완제품 IMG), 그 밖은 NULL · ItemType 은 Complete ASSY = ASSY / 공급사 `MIP.JACKSON`(자사) = SUB / `LP.`·`KD.` 구매 = MATERIAL(단위 G·SH 는 항상 MATERIAL) · ItemCategory ASSY→TRIM, SUB→SUB, MATERIAL→FABRIC(SH)·RESIN·CHEM·FASTENER·PART(뒤 4개는 시드가 공통코드로 추가) · CarType 은 `■` 헤더(NE1A W 는 NE1A, 공용 자재는 NULL) · PGN·ALC 는 엑셀에 없어 생성기에 박아 둔 09-24 개발 DB 스냅샷을 되살린다(IMG 라벨용, MountPos 는 없음) · Material Spec·중량·사이즈는 컬럼을 추가하지 않고 버린다(사용자 결정) · BOM 은 부모 품번마다 `V-{ItemNo}-01`(APPROVED, EffFrom 2026-08-28) 1건 + 직접 자식 Level 1, QtyPer = Usage Quantity 변형 열(BB=1번·BC=2번 루트), 구매 부품(MATERIAL) 아래 레진은 사급이 아니라 넣지 않는다(사용자 결정) · 공급사 8곳은 `MD_Vendor`(LP → LOCAL, KD → CKD) + `SCM_ItemVendor`, 단위 `SH` 는 `MD_Uom` 에 추가. 원본 보정(루트 오기 3건 `M3320-PI020`·`M2320-QI000`·`M2320-FC000`, `YUG→YGU`, LQ2 +CUR 루트 P8010 유도, 조지아 NQ5a·MV1a 의 품번 없는 행·`?` 수량·복사 잔재 제외)은 생성 SQL 머리말에 전부 나열된다. 구 시드 `reseed_md_item_partmaster.sql`·`seed_md_item_tailgate_door_trim.sql`·`seed_md_bom_tailgate_door_trim.sql`·`fix_md_item_seed_align.sql` 은 더 이상 쓰지 않는다.
사출 자동수집 테이블(`PR_InjLot` · `MD_InjCondItem` · `PR_InjCondLog` · `PR_RobotInspection`)은 `dist/migrate_inj_agent.sql`, 금형 마스터(`MD_MoldColor` · `MD_MoldItem` · `MD_MoldLine`)는 `dist/migrate_mold_master.sql` 참조.
라벨 발행 선점 컬럼(`PR_InjLot.PrintClaimTS` · `PrintClaimStation`)은 `dist/migrate_inj_lot_print_claim.sql` — `migrate_inj_agent.sql` 적용 후에 실행하며, 이게 없으면 Pop 의 `LabelDispatcher` 가 동작하지 않는다.
LotNo 채번 기반(`SYS_LotSeq` · `MD_Line.LotPrefix` · `tbl_Lot.LotCode` 유니크 인덱스)은 `dist/migrate_lotno_rule.sql` — INJ 원천 Lot 과 실적 배치 Lot(`ProductionRepository.RecordCycle`, IMG-03 등)은 9자리 신규칙(`[년1][월1][일1][라인코드2][순번4]`, 년=A~Z 26년 순환)으로 `LotNoGenerator` 가 채번하며, `LotPrefix` 미등록 라인은 채번이 예외로 막힌다 (스키마 시드: INJ I1~I9 / IMG W1~W5 / PNT P1·P2, 공장 `EOS-PLT-01` — 개발서버 MD_Line 기준).
INJ-MAIN 품번 패널·칩이 5초마다 세는 "라인의 오늘 LOT" 조회용 인덱스 `IX_tbl_Lot_Line_Created(LineID, CreatedTS)` 는 `dist/migrate_inj_lot_line_created.sql` — 스키마 변경 없이 인덱스만 추가하므로 순서 무관, 재실행 안전.
IMG 원천 LOT(`PR_ImgLot`)은 `dist/migrate_img_lot.sql` — `migrate_lotno_rule.sql` 뒤에 적용. 이게 없으면 IMG-MAIN 이 5초마다 조회 예외를 띄우고 라벨 발행이 안 된다. 완제품 라벨 장착위치 컬럼 `MD_Item.MountPos` 는 `dist/migrate_md_item_mount_pos.sql` — 순서 무관, 재실행 안전. 둘 다 없으면 `ImgLotRepository` 의 LOT 조회가 예외다. Core 1:1 보장 필터 유니크 인덱스 `UX_tbl_Lot_ImgParent`(`tbl_Lot.ParentLotID`, `ProcessCode='IMG'`)는 `dist/migrate_img_core_lot.sql` — 순서 무관, 재실행 안전, `sqlcmd -f 65001 -I`, 중복 연결이 있으면 중단. 없어도 Core 스캔은 동작한다(Core 행 잠금이 1차 방어).
품목 출하 포장 정보 `MD_Item.PalletQty`(적입수량 — 팔레트 1개당 제품 수) · `MaxPalletQty`(최대 적재 — 출하 차량당 최대 팔레트 수) · `ToteFlag`(팔레트 대신 토트 박스 출하, 켜면 MD-003 이 최대 적재를 비운다)는 `dist/migrate_md_item_pallet_qty.sql` — `DrawingNo` 바로 뒤에 두려고 **테이블을 재생성**하며, 들어오는 FK(개발 DB 는 `SCM_ItemVendor` 포함)와 컬럼 설명은 DB 에서 읽어 되살린다. 재실행 안전, `sqlcmd -f 65001 -I -b`. 이게 없으면 신 Web 의 MD-003 품목 목록이 매번 예외다.
품목 사출품 여부 `MD_Item.InjFlag`(BIT NOT NULL DEFAULT 0, `ItemCategory` 바로 뒤 — 이름은 사출 약어 `Inj`(`PR_InjLot`·`MD_Mold.AssyInjResultFlag`)와 bit 접미사 `Flag`(`ToteFlag`)를 따름)는 `dist/migrate_md_item_inj_flag.sql`(10-03) — 컬럼 순서 때문에 `migrate_md_item_pallet_qty.sql` 과 같은 방식으로 테이블을 재생성(들어오는 FK·컬럼 설명 복원), 재실행 안전, `sqlcmd -f 65001 -I -b`, 기존 행은 0(자동으로 채우지 않음). **품목 유형 SUB 만 1** 이 될 수 있다 — MD-003 목록은 체크 아이콘 열, 모달은 유형이 SUB 일 때만 체크박스가 켜지고(아니면 비활성 + "SUB 유형만 지정", 유형을 바꾸면 꺼짐) 저장 때 `MasterDataRepository.NormalizeInjFlag`(테스트 `ItemInjFlagRuleTests`)가 SUB 아닌 품목을 0 으로 저장한다. 아직 이 값을 읽는 다른 화면·로직은 없다(코어 판정은 여전히 `CoreItemResolver` 의 품명 `CORE` 규칙). 이 마이그레이션 없이 신 Web 을 올리면 MD-003 목록이 매번 예외다(`Invalid column name 'InjFlag'`).
품목 개별 바코드 스캔 필요 여부 `MD_Item.ScanRequired`(BIT NOT NULL DEFAULT 0, 제약 `DF_MD_Item_ScanRequired` — 10-06 Younghun, 케이스 입고 정책)는 `dist/migrate_md_item_case_receive.sql` — 재실행 안전(구 이름 `RequireBoxScanOnCaseReceive` 가 있으면 개명). PDA 케이스 입고(`WhEndpoints`·`Wh02PdaInbound`)에서 이 값이 1 인 품목의 박스는 개별 스캔해야 하고 0 이면 일괄 입고된다(품목 마스터에 없으면 스캔 필수로 본다). MD-003 은 목록 체크 아이콘 열과 등록·수정 모달의 TOTE 줄 다음 체크박스로 관리하며 표시 이름은 "개별 바코드 스캔 필요"(키 `MD.Items.ScanRequired`, 10-07 사용자 결정). 스키마 파일은 `ToteFlag` 바로 뒤에 두지만 마이그레이션은 `ALTER ADD` 라 기존 DB(개발·로컬)에서는 맨 뒤다. `MasterDataRepository.ListItems` 가 이 컬럼을 읽으므로 **이 마이그레이션 없이 신 Web 을 올리면 MD-003 뿐 아니라 `ListItems` 를 쓰는 BOM·BOP·WO·출하계획·SCM 화면도 매번 예외**다(`Invalid column name 'ScanRequired'`).
**외래키(FK)는 두지 않는 것이 원칙이다**(10-08 사용자 결정) — 프로그램에 문제가 있어도 생산라인은 절대 멈추면 안 되므로, FK 위반이 현장 저장을 막지 않게 정합성은 리포지토리 코드가 책임진다(삭제 때 관련 행을 같이 지우거나 거부). FK 부재를 문제로 보지 않는다. 기본키·유니크 키는 별개다 — `AspNetUserRoles` 는 DB 에서 기본키가 없던 유일한 테이블이라 `dist/migrate_aspnetuserroles_pk.sql`(10-08, 재실행 안전, 개발·로컬 적용 완료, 스키마 반영, `rebuild_db.sh` 끝)이 NULL·중복 정리 → 두 컬럼 NOT NULL → `PK_AspNetUserRoles (UserId, RoleId)` + `IX_AspNetUserRoles_RoleId` 를 건다(키 길이 900바이트 초과 경고는 GUID 값이라 무해). 이 테이블은 웹 관리 화면·시드만 쓰고 POP·PDA 는 읽기만 하므로 키가 현장 저장을 막지 않으며, 쓰는 시드 6개는 모두 `NOT EXISTS`/선행 DELETE 라 재실행해도 실패하지 않는다.
프로필 없는 내부 계정 보강은 `dist/migrate_user_profile_backfill.sql`(10-07) — 역할이 있는 계정은 ACTIVE, 없는 계정은 INACTIVE 로 `SYS_UserProfile` 을 채운다(`CreatedBy`=`PROFILE-BACKFILL`, 사번 비움, 재실행 안전, `sqlcmd -f 65001 -I -b`). **신 Web 보다 먼저 적용**한다 — 신 Web 은 프로필 없는 계정의 로그인을 거부하므로 순서를 바꾸면 그런 계정(개발 DB 는 WH Pick 계정 2개)이 로그인하지 못한다. 10-07 개발·로컬 DB 적용 완료(WH Pick 2계정 ACTIVE), `rebuild_db.sh` 맨 끝에도 넣었다. 계정 메일 폐지에 따른 상태 정리는 `dist/migrate_user_status_mail_cleanup.sql`(10-07) — `AccountStatus` UNVERIFIED·PENDING → ACTIVE, `AspNetUsers.EmailConfirmed` 전부 1, 공통코드 `USER_STATUS` 의 PENDING·UNVERIFIED 삭제(재실행 안전, 개발·로컬 적용 완료 — UNVERIFIED 10계정 ACTIVE, 이메일 12건 인증됨, `rebuild_db.sh` 에도 추가).
설비 유형 공통코드와 사출기 톤수(09-26)는 `dist/migrate_md_equipment_type_tonnage.sql` — 순서 무관, 재실행 안전, `sqlcmd -f 65001 -I -b`. ① 공통코드 `EQUIP_TYPE`(INJ 사출·WRAP 감싸기·PNT 도장) 신설 ② `MD_Equipment`·`MD_PmTemplate` 의 `EquipType` 기존 값 변환(INJ_MACHINE→INJ, IMG·WRAP_PRESS→WRAP, PNT_ROBOT·OVEN_UNIT·SPRAY_BOOTH→PNT — 소속 라인 공정 기준, 그 밖의 값은 보존) ③ `MD_Equipment.Tonnage`(INT NULL, `MD_Mold.Tonnage` 와 같은 형)를 `MoldCompatJSON` 바로 앞에 추가(테이블 재생성, 컬럼 설명 보존). MD-014 설비 기준정보·MD PM 템플릿은 유형을 공통코드 콤보로 받고, 톤수는 MD-014 에서 **INJ 일 때만** 입력된다(다른 유형으로 바꾸면 비우고, 저장 시에도 `MasterDataRepository.NormalizeEquipTonnage` 가 INJ 가 아니면 NULL 로 저장). 이 마이그레이션 없이 신 Web 을 올리면 설비 목록(MD-014·MNT 설비 선택 화면)이 매번 예외다(`Invalid column name 'Tonnage'`).
POP 전용 현장 작업자 마스터 `MD_Worker` 는 `dist/migrate_md_worker.sql` — 순서 무관, 재실행 안전. 이게 없으면 POP 로그인 화면의 사원 픽커에 워커가 안 뜨고(웹 계정만 나온다), 등록되지 않은 사번으로 로그인을 시도하면 예외가 난다. 신 Pop/Api 배포 전에 먼저 적용할 것. 등록 화면은 Web MD-032(`Md/Fd/Workers.razor`, `md/fd/workers`, 메뉴·권한은 `dist/migrate_md_worker_screen.sql`)이고 마이그레이션 자체에는 시드가 없으며, dev 는 `dist/seed_md_worker_dev.sql` 로 작업자 5명(W001~W005, PIN 전원 1234, W005 는 비활성)을 채운다 — PIN 해시가 고정 솔트 리터럴이라 **운영 금지**다. 사번을 W 로 두는 이유는 `seed_pop_users` 의 웹 계정(E/I/P/Q/S)과 겹치면 웹 계정이 이겨 워커가 로그인되지 않기 때문이다. **사번은 `SYS_UserProfile`·`MD_Worker` 두 테이블을 합쳐 유일해야 한다**(09-23) — SYS-001 등록·수정은 `SysRepository.EmployeeNoExists`(다른 웹 사용자)·`EmployeeNoUsedByWorker`(작업자), MD-032 등록은 `WorkerRepository.Exists`(작업자)·`UsedByWebUser`(웹 사용자)로 저장 전에 검사해 겹치면 모달 오류로 거부한다(대소문자·공백 무시).
실적의 전기일·교대 컬럼 `PR_ProductionResult.ProdDate` · `ShiftCode` 는 `dist/migrate_pr_result_prod_shift.sql` — 순서 무관, 재실행 안전, 기존 행은 `EntryAt` 기준 백필. POP 이 실적을 INSERT 하는 3곳(`ProductionRepository.RecordCycle` · `InjLotRepository.ConfirmByLotCode` · `ImgLotRepository.ConfirmByLotCode`)은 `AMES.Data.Services.ProdCalendar.ResolveNow` 로 **서버 시각**에 설정·공통코드를 읽어 채운다: 전기일 기준 시각 **`SYS_Config.DAY_CUTOFF_TIME`**(SYS-009 Operations, `HH:mm`, 이 시각 전은 전날 생산분 · 행이 없으면 자정 — 10-01 `dist/migrate_day_cutoff_config.sql` 이 구 공통코드 `DAY_CUTOFF/TIME` 값을 옮기고 그 그룹을 지웠다, 재실행 안전. 배포는 마이그레이션 → 신 Web·Pop·Api, 그 사이 구 바이너리의 ProdDate 는 자정 기준) + `WORK_SHIFT` Attribute1(`HHMM-HHMM`, SortOrder 순 첫 매치 · 2400=자정 · 자정 넘김 허용 · 어느 창에도 안 걸리면 NULL). 캐시가 없어 공통코드 화면에서 고치면 다음 실적부터 반영된다. POP 로그인 세션 `ShiftCode` 도 같은 판정(`MasterDataRepository.CurrentShiftCode`)으로 정하며 appsettings `DefaultShift` 는 판정 실패 시 폴백이다. **이 마이그레이션 없이 신 Pop 을 올리면 스캔 확정·수동 입력이 매번 예외**다. 판정 규칙 정본은 `AMES.Data.Tests/ProdCalendarTests`. RPT-010 보고서 빌더의 생산 실적 「교대」 차원도 같은 규칙을 SQL 로 옮긴 `ProdCalendar.ShiftCaseSql`(WORK_SHIFT Attribute1 창, SortOrder 순 첫 매치, 자정 넘김, 창 밖은 —)로 `EntryAt` 을 조회 시점에 판정한다(10-07 — 전에는 8–15시 A·16–23시 B·나머지 C 하드코딩, DB 검증 `RptAdhocShiftTests`). 공통코드를 고치면 지난 실적도 새 창으로 다시 묶인다. PNT-09 교대 보고(`PntRepository`)는 아직 하드코딩이다.
WO 공정 단계(`PP_WorkOrderRouting.CompletedQty` · 인덱스 · 백필)는 `dist/migrate_wo_step_line.sql` — `migrate_routing_step.sql` 다음에 적용. 이 뒤로 라인 배정·상태·완료수량의 정본은 단계 행이며 `PP_WorkOrder.LineID` 는 쓰지 않는다(컬럼만 잔존). Pop 은 단계 `LineID` 로 WO 를 받고, 실적은 `WorkOrderRepository.BumpStepCompleted` 한 곳으로만 반영된다.
WO 단계 생산 품번 `PP_WorkOrderRouting.ItemNo`(NULL = WO 품번)는 `dist/migrate_wo_step_item.sql` — `migrate_wo_step_line.sql` 다음, 재실행 안전, 백필 없음(구 WO 는 NULL 로 종전 동작). 이게 없으면 신 Web 의 WO 발행·PP-003 과 신 Pop 의 열린 WO 조회가 매번 `Invalid column name 'ItemNo'` 다. 개발 시드는 `dist/seed_inj_img_master_dev.sql`(코어 35종 가상 금형 `M-{코어품번}`·MoldItem·MoldLine + INJ 스테이션 BOP = 코어, IMG 스테이션 BOP = 완제품, 차종별 라인 NE1A→01·LQ2→02·LX3A→03·ME1A→04·MV1A→05·NQ5A/NX5A→06(IMG 는 05) — 구 `seed_md_bop_inj_dev.sql`·`seed_md_bop_img_dev.sql` 대체)과 `dist/seed_pp_customer_order_dev.sql`(ASSY 77 × 3회차 = 수주 231건 Confirmed, `SoNumber` `41001{회차}0{순번3}`). 구 품번 PP·PR 데이터 정리는 `dist/cleanup_legacy_item_data_dev.sql`(`-v Mode=ROLLBACK|COMMIT`, 창고·FG LOT 은 제외), 고객 마스터는 `dist/seed_md_customer_seoyon.sql`(Savannah·Georgia·Auburn 세 플랜트 `CUS-SAV`/`GEO`/`AUB` 만, 09-30 사용자 결정 — 데모 OEM 5곳 삭제).
BOM 버전 상태(`MD_BomVersion.Status`)는 공통코드 **`BOM_STATUS`**(DRAFT 초안·PENDING 승인 대기·APPROVED 승인·REJECTED 반려·EXPIRED 만료)이고 `dist/migrate_bom_status_code.sql`(10-02, 순서 무관·재실행 안전)이 만든다. 종료일(`EffTo`)이 오늘(DB 날짜)보다 지난 APPROVED 버전은 MD-004 가 목록을 읽을 때마다 `MasterDataRepository.ExpireBomVersions` 로 **EXPIRED** 로 바꾸고 감사 `EXPIRE` 를 남긴다(마이그레이션도 1회 같은 처리). 버전 규칙 정본은 `AMES.Data.Services.BomVersionRules`(대표 버전·승인 시 겹치는 기존 승인 버전 종료일을 새 시작일 전날로 마감·새 버전 기본 시작일 = 직전 버전 종료일 다음 날·버전 번호 `V{주}.{부}` 정규화, 테스트 `BomVersionRulesTests`) — MRP·코어 해석·BOP 상하위(`BomHierarchy`)는 기간으로 유효 버전을 고르므로 EXPIRED 와 무관하다. 마이그레이션 없이 신 Web 을 올리면 상태 콤보가 "전체" 뿐이고 배지는 코드값 그대로 보일 뿐 예외는 없다.
생산 마감일 컬럼 `PP_WorkOrder.ProdDeadline` 과 설정 `SYS_Config.PP_PROD_BUFFER_WORKDAYS`(기본 3, 1 이상만 유효 — 0·미등록·파싱 실패는 3 으로 본다) 는 `dist/migrate_wo_prod_deadline.sql` — 순서 무관, 재실행 안전. PP-003 이 WO 를 만들 때 `납기 − 버퍼 근무일`(`SYS_FactoryCalendar` 의 WORKDAY·SPECIAL 만 근무일, 행 없는 날은 토·일만 휴일) 을 박아 두고, `AMES.Data.Scheduling.DeadlinePacker` 가 오늘부터 마감일까지 단계별·날짜별로 수량을 쪼개 자동 배치한다(마감일 초과분은 납기일까지 `Late`, 그래도 남으면 `Shortfall`; 납기가 이미 지난 수주는 오늘부터 60일 안에 전량 `Late` 로 넣는다). 이 마이그레이션 없이 신 Web 을 올리면 PP-003 배치뿐 아니라 `WorkOrderRepository.ListAll` 을 쓰는 PP-004·PP-CAL 도 매번 예외다.
백필된 WO 중 헤더 라인이 마지막 라인 단계가 아닌 건(예: A 라우팅을 INJ 라인으로 발행)은 첫 후속 실적에서 헤더 `CompletedQty` 가 마지막 단계 값으로 내려갈 수 있다 — PP-04 진척률이 한 번 감소해 보인다.
PP-003 일괄 생성은 WO 생성 → Release(단계별 라인) → 단계마다 PP_LineSchedule 슬롯(DRAFT) 배치까지 **한 트랜잭션**으로 처리한다(PpRepository.CreateScheduledWorkOrders). 수량 기준은 순수요·수주량·**직접 입력** 세 가지이고, 순수요/수주량은 **기발행 WO 수량(취소 제외)을 뺀 잔량**이다 — 같은 수주에 WO 를 나눠 낼 수 있고(`PlanLineRow.IssuedQty/RemainQty`, 잔량 > 0 인 수주만 후보), 직접 입력은 `OrderPlan.Qty` 로 서버에 그대로 전달되며 수주량 초과도 허용한다(다이얼로그에 경고만). 수주 1건 = WO 1건 제약은 없어졌다. 배치 위치·날짜·수량은 AMES.Data.Scheduling.DeadlinePacker(순수 함수, 하루 안은 SlotPacker.FillDay)가 정하고, 하루 능력은 LineScheduleRepository.GetDayCapacity(패턴 해석: 그 날 저장 행 → 라인 전용 ACTIVE → 전역)로 읽는다. 자리가 없는 단계는 슬롯 없이 Released 로 남아 PP-LSB 보드의 미배치 목록에서 수동 배치한다. Pop INJ-MAIN 은 오늘 슬롯을 계획으로 읽으므로, 이 경로가 아니면 WO 가 Pop 에 안 뜬다.
예비품 마스터·입출고(`MD_SparePart` · `MNT_SparePartsTxn`)는 마이그레이션 3개를 **이 순서로** 적용한다: ① `dist/migrate_spare_part_category.sql`(공통코드 `SPAREPARTS_CATEGORY` A~K 1자리 교체 · `SPAREPARTS_EQUIP` 1~9 신설 · `Category` VARCHAR(1) · `ApplicableEquip` 추가 · `CompatEquipJSON` 삭제) → ② `dist/migrate_spare_part_no.sql`(`SparePartNo VARCHAR(16)` 첫 컬럼, 기존 행 채번 백필) → ③ `dist/migrate_spare_part_pk.sql`(**PK 를 `SparePartNo` 로**, `PartNo` 는 고유 인덱스, 현재고 `OnHandQty` 추가, `MNT_SparePartsTxn` 을 `SparePartNo` 논리 참조(**FK 없음**, 사용자 결정) + `BalanceBefore/BalanceAfter` 스냅샷만 남기는 구조로 재생성, 거래 단가(`UnitPrice`)도 제거 — 재고 금액은 마스터 `UnitCost` × 현재고). 셋 다 가드형·재실행 안전이며 컬럼 순서 때문에 테이블을 재생성하므로 확인용 SELECT 는 `GO` 뒤 별도 배치에 둔다. 예비품번호는 `MasterDataRepository.InsertSparePart` 가 `EOS-SP-{분류}{적용설비}-{yy}{순번4}` 로 트랜잭션 안에서 채번(분류·적용설비 필수, 등록 후 불변, **순번 9999 는 PDA 테스트 고정 데이터 `EOS-SP-K9-269999`(`dist/pda/PDA_SCHEMA.sql`) 몫이라 MAX 에서 제외, 9998 을 넘으면 예외** — 09-16 에 9999 를 포함해 채번한 10000 이 VARCHAR(16) 에서 잘려 `EOS-SP-K9-261000` 이 등록된 사고의 재발 방지)하고, 현재고는 `MntRepository.AdjustSparePartStock`(IN/OUT/ADJ, 마스터 갱신 + 이력 INSERT 한 트랜잭션, 재고 부족 출고 거부)로만 바뀐다. 이력이 있는 부품은 `DeleteSparePart` 가 이력 존재를 검사해 거부한다(FK 가 없으므로 고아 방지 책임이 리포지토리에 있다 — 비활성 처리로 안내). 입출고 전용 화면은 아직 없고(MNT-08 입고 보류 항목), MD-026 마스터 화면의 초기 재고·현재고 수정만 이 메서드를 통해 IN/ADJ 이력을 남긴다. 부품 이미지는 ④ `dist/migrate_spare_part_image.sql`(`SparePartImage VARBINARY(MAX)` 를 `UnitCost` 다음에 — 컬럼 순서 때문에 재생성, 재실행 안전) 로 두며, MD-026 이 `InputFile` + `RequestImageFileAsync` 로 **브라우저에서 320×180 이내(비율 유지)로 축소한 JPEG/PNG 바이트만** 저장한다(PNG 는 투명 유지). 목록 조회는 바이트를 싣지 않고 `HasImage` 만 읽어 체크 아이콘(BOP QC 열과 동일)으로 유무만 보이며, 이미지 자체는 수정 모달에서 본다. 이미지 교체/제거는 `MasterDataRepository.UpdateSparePartImage` 로 바뀐 경우에만 쓴다. 보관 구역·칸은 ⑤ `dist/migrate_spare_part_zone_slot.sql`(`ZoneCode VARCHAR(20)` · `Slot VARCHAR(5)` 를 `SparePartImage` 다음에 — `MD_Location` 과 같은 형, 재생성·재실행 안전, 공통코드 `MNT_ZONE`·`MNT_SLOT` 그룹/항목도 없으면 생성) 로 두며 MD-026 이 두 공통코드 콤보로 관리한다. 자유 입력이던 `StorageLoc` 컬럼은 같은 마이그레이션이 삭제한다(구역·칸이 대체). 제조사 `Maker NVARCHAR(100)`(자유 입력, `SupplierID` 앞)은 ⑥ `dist/migrate_spare_part_maker.sql`(재생성·재실행 안전). 자유 입력 위치 `ExtraLocation NVARCHAR(60)`(`Slot` 다음)은 ⑦ `dist/migrate_spare_part_extra_location.sql`(재생성·재실행 안전, 공통코드 `MNT_SLOT` 에 `EX`(Extra) 추가) — **구역 `SP_EXTRA` 는 칸이 `EX` 로 고정되고 `ExtraLocation` 은 그 구역에서만 쓴다**(정본 `MasterDataRepository.NormalizeSpareLocation`, 테스트 `SpareLocationRuleTests`; 다른 구역이면 `EX` 칸·자유 위치를 저장 직전에 지운다). MD-026 은 구역을 `SP_EXTRA` 로 고르면 칸을 `EX` 로 채워 비활성화하고 입력란을 보이며, 다른 구역에서는 `EX` 를 칸 옵션에서 뺀다. **PDA API 는 예비품 위치를 `MD_Location` 과 (`ZoneCode`, `Slot`) 으로 맞추므로** 같은 마이그레이션이 기존 `SP_EXTRA` 예비품과 `MD_Location` 의 `SP-EXTRA` 행 칸도 `EX` 로 맞추고, `dist/pda/PDA_SEED.sql`·`PDA_SCHEMA.sql`(테스트 리셋 프로시저)의 해당 값도 `EX` 다 — 한쪽만 `NULL` 로 남으면 PDA 에서 그 부품의 위치가 안 잡힌다. PDA 위치 이동(`WhEndpoints`)은 `SP_EXTRA` 밖으로 옮기면 `ExtraLocation` 을 지운다. 이 마이그레이션 없이 신 Web 을 올리면 MD-026 목록·저장이 매번 예외다(`Invalid column name 'ExtraLocation'`).
PM 실행 이력 스냅샷 컬럼(`MNT_PMExecution` 의 `PMScheduleID` 다음에 **`MNT_PMSchedule` 과 같은 순서로** `PMPlanNumber` · `EquipID` · `PMClass`(같은 형 VARCHAR(10)) · `PMType`, 이어서 `DueDate` · `LaborMinutes`, 인덱스 `IX_MNT_PMExecution_Class_Completed`)은 `dist/migrate_mnt_pm_execution_class.sql` — 순서 무관, 재실행 안전(컬럼이 없거나 순서가 다르면 재생성), 처음 적용 시 기존 행은 일정·작업지시로 백필(`DueDate` 만 NULL). `MntRepository.AdvancePm` 이 완료 때마다 스냅샷을 채우고, MNT-005/010 달력의 **완료 칩은 이 테이블**(`ListPmExecutions`, PMClass 로 분리)에서 나온다 — 일정이 지워져도 완료 칩은 남는다. 이력 없이 `LastPMDate` 만 있는 구 일정만 예전처럼 일정 값으로 완료 칩을 만든다. 완료 칩 클릭은 상세 내용 + 닫기만 있는 보기 모달(완료 처리·수정 없음)이고, 일정 상세 모달 하단에 최근 이력 5건(`ListPmExecutionsFor`)이 붙는다. 이 마이그레이션 없이 신 Web 을 올리면 PM 완료 처리와 달력 조회가 매번 예외다.
사번·이름 컬럼 통일 `MD_Worker.EmployeeNo`·`EmployeeName` · `MD_LineSupervisor.EmployeeNo`(구 `WorkerNo`·`WorkerName`, `SYS_UserProfile` 의 `EmployeeNo` VARCHAR(20)·`EmployeeName` NVARCHAR(50) 과 같은 이름·형)은 `dist/migrate_employee_no_rename.sql` — `migrate_md_worker.sql`·`migrate_andon_workflow.sql` 뒤, 재실행 안전, 데이터 변경 없음(고유 인덱스도 `UQ_MD_Worker_EmployeeNo` 로 개명). 이 마이그레이션 없이 신 Pop/Web/Api 를 올리면 POP 로그인·안돈 슈퍼바이저 검증·MD-032/033 이 매번 예외다. 라인 슈퍼바이저 등록 화면은 Web MD-033(`Md/Fd/LineSupervisors.razor`, `md/fd/line-supervisors`, 메뉴·권한은 `dist/migrate_md_line_supervisor_screen.sql`) — 라인은 `MD_Line` 활성 라인, 사번은 **Supervisor 역할** 웹 사용자(`SYS_UserProfile` × `AspNetUserRoles`)만 콤보에 나오며 현장 작업자(`MD_Worker`)는 후보가 아니다(막힌 계정 DISABLED/LOCKED/SUSPENDED/INACTIVE 제외, UNVERIFIED 허용)이며 키(라인·사번)는 고정이라 수정은 활성 여부뿐, 바꾸려면 삭제 후 재등록. `seed_andon_dev.sql` 은 이제 개발 편의용일 뿐이다.
DB 구조 정합용 마이그레이션 2개(09-17): `dist/migrate_md_codeitem_widen.sql`(`MD_CodeItem.Attribute1` 40→200 · `Description` 120→500 — 개발서버가 수동으로 넓혀 둔 값을 정본으로 스키마·로컬을 맞춤) · `dist/migrate_pr_imglot_column_order.sql`(초기 테이블에 `CustomerCode` 를 ALTER ADD 한 DB 의 컬럼 순서를 스키마와 같게 재생성, FK·인덱스 재작성). 둘 다 재실행 안전. 개발·로컬 DB 구조 서명이 동일한 상태가 기준이다(10-08 기준 176 테이블, 스키마 파일도 동일).
공통코드 두 번째 속성 `MD_CodeItem.Attribute2 NVARCHAR(200)`(Attribute1 바로 다음)은 `dist/migrate_md_codeitem_attribute2.sql`(10-02) — `migrate_md_codeitem_widen.sql` 다음, 컬럼 순서 때문에 테이블을 재생성하며 행·PK·기본값·컬럼 설명을 보존(재실행 안전, `sqlcmd -f 65001 -I -b`). MD-030 공통코드 화면이 목록 열·등록/수정 모달(상위 코드 옆 순서, Attribute1·Attribute2·설명 각 한 줄)로 입력하고, `MasterDataRepository.CodeItemRow.Attribute2` 는 위치 생성자 호출을 깨지 않으려고 레코드 **맨 뒤 선택 인자**다. 이 마이그레이션 없이 신 Web·Pop·Api 를 올리면 공통코드를 읽는 모든 화면·조회(`ListCodeItems`)가 `Invalid column name 'Attribute2'` 로 예외다.
교대 시각 모델(10-02 사용자 정의): 전기일 경계 = `SYS_Config.DAY_CUTOFF_TIME`(07:00) · `WORK_SHIFT.Attribute1` = 하루 24시간을 교대로 나눈 창(A 0700-1630 · B 1630-0200 자정 넘김 · C 0200-0700, 실적 교대 판정 `ProdCalendar`) · `WORK_SHIFT.Attribute2` = 실제 생산 시간(SYS-005 일정 생성 모달의 교대 기본 시각, 휴게 기본 A 65·B 65·C 0분) · `SHIFT_PATTERN.Attribute1` = 그 패턴의 가동 교대 목록(`A` / `A,B` / `A,B,C`). MD-028 라인 시간 패턴 편집기는 교대 창을 Attribute1 로 그리며 자정을 넘는 창은 "확장 분"(1440 초과)으로 편집하고, 저장(`MD_LineTimeSegment`, 하루 분 0~1440)은 자정에서 두 조각으로 나눈다 — 규칙 정본 `AMES.Data.Services.LineTimeLayout`(테스트 `LineTimeLayoutTests`). 편집기를 열 때 저장본이 현재 교대 창과 맞지 않으면(교대 시각 변경) 교대 기본 상태와 다른 "칠한" 구간만 하루 시각 그대로 보존해 새 창에 다시 배치하고 "교대 시간 변경 반영" 표시와 미저장 상태로 연다(저장해야 DB 반영). 기본 상태는 패턴 교대(`SHIFT_PATTERN.Attribute1`, 없으면 SHIFT1/2/3 이름의 교대 수)에 들고 **실제 생산 시간(`WORK_SHIFT.Attribute2`) 안**이면 OPERATING, 그 밖(예 A 07:00–07:30·B 01:30–02:00)과 가동하지 않는 교대는 IDLE 이다(`ShiftWindow.DefaultAt`, Attribute2 가 없거나 틀리면 창 전체) — 밴드 삭제·전체삭제·재배치가 이 기본값으로 채운다. 실행취소는 마지막 편집(칠하기·밴드 삭제·전체삭제) 직전 시간표로 여러 단계 복원하고, 미저장 여부는 저장본과 비교해 정한다(모두 되돌리면 저장 버튼이 꺼지고 닫을 때 확인하지 않는다). **시간표는 00:00–24:00 을 빈틈·겹침 없이 덮어야 저장된다**(`LineTimeLayout.CoverageIssues`) — `WORK_SHIFT` 교대 구간이 24시간을 덮지 않으면 MD-028 목록·편집기에 빈 시간·겹친 시간을 담은 경고가 뜨고 저장 버튼이 꺼지며, 편집기 저장 직전과 `MasterDataRepository.SaveLineTimeSegments`(DB 를 건드리기 전, `InvalidOperationException`)도 같은 규칙으로 거부한다. 세그먼트를 쓰는 길은 이 메서드 하나뿐이다(구간을 하나씩 바꾸던 구 메서드 3개는 10-05 삭제). 패턴 헤더 `OperatingFlag`·`SegmentFlag` 는 글자 위치 = 하루 분(0 = 00:00, PP-LSB 발행 스냅샷과 같은 기준)이다. 10-03 개발·로컬 DB 의 패턴은 전 라인 공통 **`LP-CMN-001` 공통 2교대**(전역·근무일·`SHIFT2_SCHEDULE`·유효기간 없음) 하나뿐이다(사용자 결정 — 전 공정 근무표가 같다): A 07:30–16:30 · B 16:30–01:30 가동, 점심 45분(12:00·21:00) · 휴식 10분 ×2(09:30·14:30 / 18:30·23:30), 교대 창의 나머지(07:00–07:30·01:30–02:00·C 전체)는 유휴 — 가동 950분. 구 패턴 LP-INJ-001·002·LP-IMG-001 은 삭제했고 그것을 가리키던 `PP_LineSchedule.PatternID` 1,025행은 LP-CMN-001 로 바꿨다(기존 슬롯 시각은 그대로라 새 휴식·식사 구간과 겹칠 수 있다). 교대 시각을 또 바꾸면 각 패턴을 MD-028 에서 열어 저장해야 PP-LSB·PP-003 능력(`GetDayCapacity`)이 새 시각을 따른다 — **저장본을 자동·일괄로 바꾸는 기능은 두지 않는다**(사용자 결정 10-03). 대신 MD-028 은 교대·상태·사유 기준정보(`WORK_SHIFT`·`SHIFT_PATTERN`·`SEGMENT_STATE`·사유코드)를 목록 조회(조회 버튼 포함)와 편집기 열기마다 다시 읽고, 저장본이 현재 교대 창·패턴 교대와 맞지 않는 패턴에 목록의 패턴명 옆 "교대 시각 변경됨" 표시를 띄운다(`LineTimeLayout.IsOutdated`, 읽기 전용). 패턴 교대 목록만 바꾼 경우(예 2교대에 C 추가)는 기존 C 구간을 사용자가 칠한 값으로 보기 때문에 표시되지 않는다.
공장 달력 순작업 시간 `SYS_FactoryCalendar.NetWorkHours` 는 **`decimal(4,2)`**(10-03 `dist/migrate_sys_factory_calendar_net_hours.sql`, ALTER COLUMN·재실행 안전, 기존 값은 다시 계산하지 않는다 — 개발·로컬 DB 의 1자리 값 22행은 10-05 에 시각·휴게로 다시 계산했다, `ModifiedBy` = `NETHRS-1005`). 계산은 SYS-005(`Sys/Calendar.razor` `CalcNet`, 반올림 AwayFromZero·표시 `0.00`)와 `SP_SYS_FactoryCalendar_Fill`(SQL `ROUND(…, 2)`)이 같은 값을 낸다(475분 = 7.92). 저장값을 화면에 보이거나 계산에 쓰는 코드는 없다 — SYS-005 는 시각·휴게로 매번 다시 계산해 보이고, PP-003 근무일 판정은 `DayType` 만 본다.
공휴일 마스터 `SYS_PublicHoliday`(국가·날짜 PK, `HolidayDate` = 쉬는 날 — 토·일이면 연방 규칙으로 금·월 대체, `ActualDate` = 원래 날짜, `ActiveFlag` 0 = 공장이 일하는 공휴일)와 공장 달력 자동 생성 프로시저 `dbo.SP_SYS_FactoryCalendar_Fill` 은 `dist/migrate_sys_public_holiday.sql`(10-03, 순서 무관·재실행 안전, `sqlcmd -f 65001 -I -b`) — **관리 화면은 없다**(사용자 결정, SQL 로 관리). 시드는 미국 연방 공휴일 11종 × 2026–2030(55건). 프로시저는 오늘(DB 날짜)부터 `@Months`(기본 3)개월 − 1일까지 **`SYS_FactoryCalendar` 에 행이 없는 날짜만** SYS-005 일정 생성 모달과 같은 규칙으로 채운다: 토·일 WEEKEND · 활성 공휴일 HOLIDAY(이름) · 그 밖 WORKDAY 교대별 1행(교대 = `SHIFT_PATTERN`(`@ShiftPattern` 기본 `SHIFT2_SCHEDULE`).Attribute1, 시각 = `WORK_SHIFT.Attribute2`, 휴게 A 65·B 65·그 밖 0, 공장 = `@PlantCode` 없으면 `PLANT` 첫 행, `CreatedBy` = `SQL-AGENT`). 범위 안 연도에 공휴일이 하나도 없으면 아무것도 넣지 않고 오류 50002 — 다음 해 공휴일은 그 전에 SQL 로 넣어야 한다(월 1회 실행 기준으로 다음 해 공휴일은 그해 11-01 실행 전까지 — 2031 년분은 2030-11-01 전). `@DryRun = 1` 은 넣을 행만 보여 준다. 자동 실행은 SQL Server 에이전트 작업 **`[AMES] Factory Calendar Fill`**(매월 1일 00:30 서버 시각, 실패 시 10분 간격 재시도 2번 = 최대 3번 시도, 단계는 등록할 때 `-d` 로 준 DB 에서 `@Months = 3`)이고 **`dist/setup_job_factory_calendar_fill.sql` 을 sysadmin 이 대상 DB 에서(`-d AMES_DEV`) 실행**해 등록한다(`ames_app` 은 msdb 권한 없음, 재실행 안전 — 구 이름 `AMES - Factory Calendar Fill` 도 지운다, 시스템 DB 에서 실행하거나 프로시저가 없으면 중단). 서버 PC 에서 `powershell -ExecutionPolicy Bypass -File dist\setup-factory-calendar-job.ps1 [-Instance MSSQLSERVER01] [-Database AMES_DEV]` 를 실행하면 관리자 승인을 받아 에이전트 서비스 자동 시작·시작과 작업 등록(Windows 인증)을 한 번에 한다. 10-03 로컬(`localhost\MSSQLSERVER01`)은 완료(에이전트 계정으로 미리보기 실행까지 확인), **개발서버는 원격 Windows 인증이 막혀 그 PC 에서 위 스크립트를 실행해야 한다.** 로컬 작업도 로컬 DB 를 따로 채우므로 그 행의 ID·작성 시각·기준 날짜(로컬 PC 시각)는 개발 DB 와 달라진다.
**새 서버를 꾸릴 때 이 작업은 저절로 생기지 않는다** — 에이전트 작업은 대상 DB 가 아니라 서버의 msdb 에 있어서 DB 백업 복원·sqlpackage 동기화·마이그레이션 적용으로는 옮겨지지 않고, DB 가 스스로 서버 작업을 만들 방법도 없다(앱 계정 `ames_app` 도 msdb 권한이 없다). `dist/rebuild_db.sh` 로 재구축하면 마지막 단계에서 sa 로 등록하지만(실패해도 재구축은 계속 — 컨테이너는 `MSSQL_AGENT_ENABLED=true` 여야 실제로 돈다), **그 밖의 방법으로 만든 서버는 반드시 `dist\setup-factory-calendar-job.ps1` 을 실행해야 한다.** 빠뜨리면 오류 없이 달력이 3개월 뒤부터 비기 시작하고, PP-003 생산 마감일 계산은 달력 행이 없는 날을 토·일만 휴일로 본다(공휴일이 근무일로 계산된다). 자동 실행은 SQL Server 에이전트 작업으로 유지하고 AMES.Api `ScheduledWorker` 로 옮기지 않는다(10-05 사용자 결정). Web·Pop·Api 코드는 이 테이블·프로시저를 읽지 않는다(SYS-005 생성 모달의 공휴일 목록은 여전히 수동 입력).
안돈 대응 워크플로(`MD_LineSupervisor` · `PR_AndonDeptCall` · `PR_AndonCall.SupervisorName` · 공통코드 `ANDON_DEPT`/`ANDON_CAUSE`)는 `dist/migrate_andon_workflow.sql` — 순서 무관, 재실행 안전. 이게 없으면 INJ-MAIN·IMG-MAIN 이 5초마다 진행중 안돈을 조회하다 예외를 내 좌측 헤더에 갱신 실패 스트립이 계속 뜨고 안돈 버튼도 예외 토스트를 띄운다 — 실적 확정 자체는 계속 동작한다. `ANDON_CAUSE.Attribute1` 이 기본 호출 부서이며 둘 다 MD-26 공통코드 화면에서 관리한다. 라인별 슈퍼바이저 등록은 Web MD-033 에서 하며, dev 는 `dist/seed_andon_dev.sql`(활성 라인 × Supervisor 역할 웹 사용자, 운영 금지)로 채울 수 있다.
LOT 단위 불량·재작업(`PR_DefectDetail` 컬럼 `CauseCode`·`DispositionBy`·`DispositionAt`·`PriorStatus`·`ReversalResultID`, 필터 유니크 인덱스 `UX_PR_DefectDetail_OpenLot`, `NG_CONFIRMED → SCRAPPED` 이관, 구 수량 행(`LotID` NULL 또는 0) `Disposition='LEGACY'` 봉인, RWK 마스터)은 `dist/migrate_lot_defect_rework.sql` — 순서 무관, 재실행 안전. 적용은 반드시 `sqlcmd -f 65001 -I` — 필터 유니크 인덱스 때문에 `-I` 가 없으면 Msg 1934 로 실패한다. 이게 없으면 불량 팝업 등록과 REWORK 대기열 조회가 매번 예외다(스캔 확정은 계속 동작). REWORK 판정은 `MD_DefectCause` 가 비어 있으면 버튼이 비활성이라 dev 는 `dist/seed_rework_dev.sql`(원인 4개 + dev 불량코드 기본 원인 연결)을 적용한다.
금형 교체 시간(`MD_Mold.MoldChangeMin` · `PP_LineSchedule.MoldID`)은 `dist/migrate_mold_change_plan.sql` — 순서 무관, 재실행 안전. 이게 없으면 MD-007 금형 목록·PP-003 배치·PP-LSB 능력 조회가 매번 예외다(구 Web 은 컬럼을 안 읽어 안전). PP-003 은 INJ 단계마다 `AMES.Data.Services.MoldResolver`(① 직전 금형 → ② 라인 배정 중 교체 최소 → ③ MoldID 순)로 금형을 정하고, 직전 금형(`LineScheduleRepository.LineLastMoldBefore`: 그 라인 이전 날짜 마지막 슬롯 → `MNT_EquipmentStatus.MountedMoldID`)과 다르면 `EntryType='MC'` 행(`WoID NULL`, `RefType='WO'`·`RefID`=WoID, `MoldID`=신금형)을 WO 슬롯 앞에 넣는다. 유효 교체 시간은 `COALESCE(MD_MoldLine.PrepTime, MD_Mold.MoldChangeMin, 0)`. **INJ 단계 품번에 활성 `MD_MoldItem` 이 없으면 그 수주는 거부**(`RejectedOrder.Reason='NoMold'`)되며 라인 미배정은 거부 사유가 아니다. POP 은 `EntryType='WO'` 로 계획을 읽어 MC 를 보지 않는다. PP-LSB 는 MC 를 표시·보존하고 클릭하면 시각·소요분 수정/삭제(적용·발행 때 반영)할 수 있으나 수동 배치 시 자동 삽입은 하지 않는다(후속). 보드에서 MC 는 PM 과 달리 가동을 깎지 않고 WO 처럼 부하로 센다. 정본 테스트는 `AMES.Data.Tests/MoldResolverTests`·`DeadlinePackerTests`·`MoldPlanningTests`.
PP-005 MRP 결과 스냅샷(`PP_MRPResult` · `PP_MRPResultWo`)과 품목 조달 리드타임 `MD_Item.LeadTimeDays` 는 `dist/migrate_pp_mrp_result.sql` — 순서 무관, 재실행 안전. 이게 없으면 PP-005 화면 진입·실행이 매번 예외다(구 Web 은 `PP_MRPLog` 만 읽어 안전). `PpRepository.RunMrp` 가 열린 WO(Completed·Closed·Stocked·Cancelled 제외) 잔량을 유효 APPROVED BOM(`MD_BomVersion` Status·EffFrom/EffTo, 부모 품번당 EffFrom 최신 버전 하나)으로 leaf 까지 분해하고, 재고 = `WH_Inventory` OnHand − Reserved 합, 발주중 = `WH_PurchaseOrder` Open/Partial 미입고분 + `SapPoNumber` 없는 Draft/Sent/Approved PR 수량, 부족 = 소요 − 재고 − 발주중(양수가 부족), 발주 기한 = 영향 WO 최단 납기 − 리드타임(부족일 때만·둘 중 하나라도 없으면 NULL)으로 계산해 `PP_MRPLog` 헤더 + 결과 두 테이블을 한 트랜잭션으로 남긴다. BOM 없는 WO 는 건너뛰고(`WosConsidered` 제외), BOM 순환은 `Status='Failed'` 로그만 남기고 `MrpCalculator.MrpCycleException`. 화면은 최근 Completed 실행만 보이며 야간 자동 실행·`PP_WorkOrder.IsBlocked` 는 없다 — "차단 WO" KPI 는 부족·PR 미생성 자재의 영향 WO 수를 화면에서 센다. 화면은 PP-003 과 같은 전체 높이 가상화 그리드(고정 체크박스 열, 부족·PR 미생성 행만 선택 가능, 헤더 체크박스로 표시분 전체 선택)이고 PR 생성(`CreateShortagePrs`)은 **체크한 행만** 확인 모달을 거쳐 일괄 처리한다. 모달에서 자재별 PR 수량을 조절할 수 있다(기본 부족량, 0 이하 거부, 초과 허용, 부족량 미만이면 결과 행에 잔여 부족이 남지만 같은 실행에서 두 번째 PR 은 못 만든다 — 재실행하면 다시 집계) — `PP_PurchaseRequest`(Draft, `PR-yyyyMMdd-NNNN` 일별 채번, 수량 = 입력 수량, 필요일 = 발주 기한 → 최단 WO 납기 → 오늘, `WoID` = 최단 납기 WO, `VendorID` NULL) 를 만들고 결과 행에 `PrID` 를 연결하며 입력 수량만큼 부족을 발주중으로 옮긴다(재생성 없음). 행 Detail 모달은 소요·가용·부족·L/T·영향 WO 와 함께 그 자재의 **진행 중 구매요청**(`ListOpenPrsForItem`: `SapPoNumber` 없는 Draft/Sent/Approved — 발주중에 세는 PR 과 같은 범위)을 나열한다. 그리드·모달의 PR 번호는 `pp/purchase-req?pr=PR-…` 링크이고 PP-006 은 `pr` 쿼리를 받으면 그 번호로 검색(최근 N 건 밖이면 500 건으로 재조회)한 뒤 상세 모달을 바로 연다. 다음 실행부터는 그 PR 이 발주중으로 잡힌다. 계산 규칙 정본은 순수 함수 `AMES.Data.Services.MrpCalculator`(`AMES.Data.Tests/MrpCalculatorTests`), DB 경로는 `MrpRepositoryTests`(AMES_DEV 필요). MD 품목 화면의 리드타임 입력란은 후속.
PP-006 구매요청의 SAP 전송 상태(`PP_PurchaseRequest.SapDocNum` · `SentAt` · `RetryCount` · `LastError`, 상태 어휘 Draft/Sent/Approved/Failed 로 이관 — 구 Pending → Draft, Rejected → Failed, PO 번호 보유 행 → Approved)는 `dist/migrate_pp_pr_send.sql` — 순서 무관, 재실행 안전. 이게 없으면 PP-006 목록·PP-005 상세의 구매요청 조회가 매번 예외다. **SAP B1 Service Layer 연동은 없다** — 전이는 화면이 확정하고(`Endpoint='MANUAL'`) `PP_PRSendLog` 에 전이마다 1행(AttemptNo = 그 PR 의 이력 수 + 1, Result = Sent/Failed/Approved, ResponsePayload = DocNum·사유·PO 번호)을 남긴다. 규칙 정본은 `AMES.Data.Services.PrStatusRules`(`PrStatusRulesTests`): Draft/Failed →(Send) Sent →(Approve, PO 번호 필수) Approved, Draft/Failed/Sent →(Fail, 사유 필수, RetryCount +1) Failed, Approved 는 종결. 리포지토리 `SendPrs`(선택 행 일괄, 전송 불가 상태는 건너뜀, 단건은 DocNum 선택 입력) · `FailPr` · `ApprovePr` · `UpdatePrVendor`(Draft/Failed 만) · `ListPrSendLog` 는 행 잠금 + 한 트랜잭션(`PrRepositoryTests`, AMES_DEV 필요). 화면은 PP-005 와 같은 전체 높이 가상화 그리드(고정 체크박스 열 — Draft/Failed 만 선택, 헤더 체크박스로 표시분 전체 선택), KPI Draft·Sent·Approved·Failed, 필터 상태·거래처·필요일 이후·검색, 툴바 "선택 SAP 전송" 확인 모달(DocNum 없이 Sent 확정), 상세 모달(정보 타일 · 거래처 지정 · 연결 WO — 그리드에는 WO 열이 없고 상세·내보내기·검색에만 있다 · `pp/mrp?item=` 링크 · 전송 이력 · 상태별 액션: 전송/재전송 + DocNum, 실패 기록 + 사유, PO 승인 + PO 번호). `?pr=` 진입은 그 번호로 검색 후 상세를 연다. 거래처는 필수가 아니다. MRP 발주중과 상세 "구매 요청중" 은 `SapPoNumber` 없는 Draft/Sent/Approved 를 센다(Failed 제외). PO 전환 뒤 입고 예정은 `WH_PurchaseOrder` 로 잡히는 구조라, PO 번호를 승인해도 `WH_PurchaseOrder` 행이 없으면 다음 MRP 에서 그 수량이 발주중에서 빠진다(SAP 동기화 후속).
APS 생산계획(PP-APS)의 마스터 gap 컬럼·결과 테이블·설정(`MD_Item.BoxQty`(`ToteFlag` 다음) — 컬럼 순서 때문에 `migrate_md_item_pallet_qty.sql` 방식으로 **재생성**(초판이 만든 `MD_MoldItem.PartCavityCount` 는 09-30 폐지 — 있으면 §2 가 떨어뜨린다) · `MD_ApsLineStage`(라인별 선행일·재고 사용·가동 시간 패턴 `PatternID`(10-06, FK → `MD_LineTimePattern`), PP-APS 설정 다이얼로그에서 관리) · `PP_ApsRun`(실행 헤더 + `SettingsJson`·`BundleJson`·`ResultJson` = 재현 정본, Status `Saved`/`Released`) · `PP_ApsPlanLine`(품번×일자 정규화 사본, `Kind` ASM/INJ, `SameItem` = 같은 품번 규칙 사출 행, `WoID` = 첫 연결 WO, 유니크 `(RunID, Kind, ItemNo, PlanDate)`) · `PP_ApsRunWo`(계획 행 ↔ WO ↔ SoID 조각) · 공통코드 `APS_SETTING`(`DEFAULT_COVER`·`ROUND_TO`·`TRIM_LAST_DAY`·`INJ_OFFSET_DAYS`·`PULL_FORWARD`·`SAFETY_TERM`·`WARN_STATUS`·`DEFAULT_PATTERN`(10-06, 사출 라인 기본 가동 시간 패턴 = 전역 패턴 ID, 빈 값이면 라인 지정 없는 사출 라인은 PP-APS 조회 차단))·`APS_COVER_TIER`(CodeValue = 일수요 하한, Attribute1 = 커버일수, CodeValue 내림차순 첫 매치 — SortOrder 는 참고용, 실제 정렬은 CodeValue 기준) — 그룹 이름 `APS · …`, `SW_` 접두어 아님)은 `dist/migrate_aps.sql` — `migrate_mold_master.sql`·`migrate_md_equipment_type_tonnage.sql`·`migrate_md_item_pallet_qty.sql`(`PalletQty`·`MaxPalletQty`·`ToteFlag`)·`migrate_pp_mrp_result.sql`(`LeadTimeDays`)·`migrate_md_item_mount_pos.sql`(`MountPos`) 뒤(`AMES_Schema.sql` 로 새로 만든 DB 는 이미 포함 — 빠지면 §1 의 `MD_Item` 컬럼 대조가 `THROW` 로 중단, PK 밖 나가는 FK·인덱스가 있어도 중단), 재실행 안전, `sqlcmd -f 65001 -I -b`. 화면 등록은 `dist/migrate_pp_aps_screen.sql`(`SYS_Screen PP-APS`, `pp/aps-plan`, PP SortOrder 7, Admin `REA`; `MenuCatalog.FallbackItems`·`EsByHref` 항목은 소스). dev 시드는 `dist/seed_aps_dev.sql`(운영 금지). 이 마이그레이션 없이 신 Web 을 올리면 PP-APS·MD-003 이 매번 예외다(`Invalid column name 'BoxQty'`). `rebuild_db.sh` `FILES` 에는 `migrate_md_equipment_type_tonnage.sql` 바로 뒤에 `migrate_aps.sql` → `migrate_pp_aps_screen.sql`, 그 뒤 `#   → 개발용` 주석으로 `seed_aps_dev.sql` 이 등록돼 있다.
PP-001 일별 구매계획(고객사 SRM MM30011)의 저장 테이블·APS 실행 플래그·자동수집 설정(`PP_DemandPlan`(고객×품번×일자 예정량, `ScheduledQty`·`PoQty`·`PackQty`, 유니크 `(CustomerID, ItemNo, PlanDate)`) · `PP_DemandPlanBatch`(업로드/수집 1건 헤더 — 예약어라 대괄호 필수인 `[RowCount]` 포함) · `PP_ApsRun.IncludeDailyPlan`(bit, 기존 실행은 0) · 공통코드 `SW_DPSYNC`·`SW_DPSYNC_SOURCE`·`SW_DPSYNC_URL`·`SW_DPSYNC_AUTH`(PO Sync 와 같은 형식, `WINDOW` 없음))은 `dist/migrate_demand_plan.sql` — `migrate_aps.sql`(`PP_ApsRun`) 뒤 전제, 재실행 안전, `sqlcmd -f 65001 -I -b`. 이 마이그레이션 없이 신 Web 을 올리면 PP-APS 는 `Invalid column name 'IncludeDailyPlan'`, PP-001 일별 탭은 `PP_DemandPlan`·`PP_DemandPlanBatch` 테이블이 없어 `Invalid object name` 으로 매번 예외다. dev 는 `dist/seed_demand_plan_dev.sql`(운영 금지: DPSYNC 소스 `SEMS`, URL 자리표시자 — MD-26 에서 `SW_DPSYNC_URL`·`SW_DPSYNC_AUTH` 를 실제 값으로 바꿔야 호출된다)을 `seed_aps_dev.sql` 뒤에 적용한다.

---

## 알림 채널 — PUSH (10-08, 사용자 결정)

메일·SMS 발송이 불가능해 알림은 **내부 앱의 PUSH** 로 간다(앱은 별도 개발 예정). 단 나중에 다시 쓸 수 있게 **채널은 지우지 않고 공통코드 `NOTIFICATION_CHANNEL` 의 사용 여부(UseFlag)로만 켜고 끈다** — EMAIL·SMS 행은 사용 안 함으로 남기고 PUSH 만 사용. `dist/migrate_notification_push.sql`(재실행 안전, 개발·로컬 적용 완료, `rebuild_db.sh` 끝)이 코드를 이렇게 맞추고(이전 이름 APP 이면 PUSH 로 개명) 알림 규칙 `SYS_NotificationRule.ChannelsJSON` 에 PUSH 를 **더한다**(EMAIL·SMS 는 그대로). 사용자별 채널(`SYS_NotificationChannel`)·발송 이력(`SYS_NotificationHistory`)·안돈 기록(`PR_AndonPush`)의 과거 EMAIL·SMS 값은 건드리지 않는다.
- **발송 원칙**: 규칙에 적힌 채널 중 **사용 중인 채널로만** 보낸다(앞으로 만들 발송기의 규칙). 메일·SMS 를 다시 쓰려면 MD-030 에서 UseFlag 를 켜고 발송 설정만 추가하면 기존 규칙·주소가 그대로 살아난다.
- **SYS-008 알림 관리**(`Sys/Notifications.razor`, 채널 공통코드를 쓰는 유일한 화면): 새로 고르는 채널 콤보는 사용 중인 채널만, 규칙 수정 창은 모든 채널을 보이되 사용 안 함 채널에 「비활성」 표시(선택돼 있으면 해제 가능, 새로 체크는 불가), 목록·이력의 사용 안 함 채널 칩은 흐림+취소선, 이력 채널 필터는 사용 안 함 채널까지 고를 수 있다. 사용 중 채널이 없을 때 EMAIL/SMS/KAKAO/SLACK 을 보이던 고정 대체 목록은 없앴다.
- **실제 발송 기능은 아직 없다**(규칙·이력 화면만). POP 의 안돈 발동은 `AndonRepository.RecordPush` 로 `PR_AndonPush` 에 수신자·채널을 고정값(`SUPERVISOR/PDA`·`LINE-LEAD/EMAIL`)으로 기록하는 임시 코드다 — POP 담당 전달사항.

## 외부 API 연동 Worker — AMES.Api

외부 시스템을 주기적으로 호출하는 연동(가져오기·내보내기)은 **API 마다 Worker 하나**를 두고, 모두 `AMES.Api/Workers/ScheduledWorker<TTarget, TResult>` 를 상속한다(첫 Worker 는 아래 PO Sync). 베이스가 가진 공통 규칙(정본 `AMES.Api.Tests/ScheduledWorkerTests`):

- 틱마다 `LoadPlan()` 을 다시 읽고(캐시 없음) 대상별 주기(`IntervalMinOf`, 0 이하는 그 대상만 중지)가 지난 것만 순차 실행. `ScheduledPlan.Enabled=false` 면 스케줄러 전체 중지(수동 실행은 허용).
- **시간 설정은 2단 — Worker 마다 다른 값은 공통코드, 공통 기본값은 appsettings**(정본 `ScheduledWorkerSettings`): 틱 간격·시작 지연은 `ScheduledPlan.TickSec`/`StartupDelaySec`(Worker 가 자기 공통코드에서 읽어 채움) → appsettings `ScheduledWorker:TickSec`(기본 60) · `ScheduledWorker:StartupDelaySec`(기본 10) 순, HTTP 타임아웃도 같은 방식으로 `ScheduledWorker:TimeoutSec`(기본 60). 범위 밖 값(틱·타임아웃 1 미만, 지연 음수)은 없는 것으로 본다. 고정 타이머가 아니라 **틱이 끝날 때마다 그 틱에서 읽은 계획으로 다음 대기**를 정하므로 틱 간격 변경도 다음 틱부터 반영되고, 틱 시작 간격은 실행 시간 + 틱 간격이다(계획 로드 실패 시 직전 값). 시작 지연을 정하려고 기동 때 계획을 한 번 읽으며, 실패하면 공통 기본값으로 기다린다.
- 마지막 실행 시각은 메모리라 Api 재시작 직후 모든 대상이 한 번 돈다. 대상별 잠금으로 틱과 수동 실행이 겹치지 않는다.
- 수동 실행 `RunNowAsync(key)`: 대상별 60초 쿨다운(틱 실행도 시계를 채운다). 단건은 미등록 키 404 · 진행 중/쿨다운 409, 전체 실행은 해당 대상을 결과 안에 `busy`/`cooldown` 으로 담고 나머지는 계속. 엔드포인트는 자기 경로·쿼리 이름을 유지하고 `ScheduledWorkerEndpoints.RunAsync` 한 줄로 이 HTTP 규칙을 쓴다(세션 없으면 401). POP 세션이 없는 AMES.Web 이 부를 길은 **서비스 키**다 — 엔드포인트가 `RunAsync` 에 키 공급자(`Func<string?>`)를 넘길 때만 열리고, 헤더 `X-AMES-Service-Key` 가 공급자 값과 일치(`FixedTimeEquals`)해야 통과한다. 공급자가 null·빈 값을 주거나 예외를 내면 닫힌 채(401)이고, 헤더가 없거나 세션이 있으면 공급자를 부르지 않는다(정본 `ScheduledWorkerEndpointsTests`).
- 계획 로드 자체가 실패(DB 접속 등)하면 모니터 행 `{Code}-CONFIG` 에 남기고 예외를 다시 던진다. 대상 모니터 행 `{Code}-{키}` 는 20자(`SYS_InterfaceMonitor.InterfaceCode`)를 넘지 않게 Worker 가 키 길이를 제한한다.
- **공통코드 그룹 이름 규칙**: Worker 설정 그룹은 모두 **`SW_{Code}`** 로 시작한다(`SW_{Code}` 전역 · `SW_{Code}_SOURCE` 등) — MD-26 그룹 검색(GroupCode·이름 부분 일치)에 `SW_` 를 넣으면 모든 Worker 설정이 나온다. 그룹 이름(한·영)에는 접두어를 붙이지 않는다(예 `PO 자동수집 설정` / `PO Sync Settings` — 10-05 사용자 결정, 구 규칙 `ScheduledWorker · ` 접두어는 폐지). `MD_CodeGroup.GroupCode` 가 VARCHAR(20) 이라 `_SOURCE` 를 붙이는 Worker 는 `Code` 를 10자 이내로 둔다. 그룹 이름 상수는 도메인 설정 클래스 한 곳에 두고 `"SW_" + Code` 에서 파생한다(PoSync: `PoSyncConfig.GroupGlobal` 등).
- **새 API 추가**: `Workers/{이름}/{이름}Worker` 가 베이스를 상속해 `Code`·`Name`·`LoadPlan`·`KeyOf`·`IntervalMinOf`·`RunTargetAsync`·`RecordConfigError`·`Skipped`·`IsOk` 를 구현 → `Program.cs` 에 `AddSingleton<X>()` + `AddHostedService(sp => sp.GetRequiredService<X>())`(엔드포인트가 같은 인스턴스를 주입받아야 한다) → 엔드포인트 한 줄. 도메인 로직(설정 해석·매핑·실행기)은 `AMES.Data.Services.{이름}` 에 두고 `AMES.Data.Tests` 에서 테스트한다.
- Worker 마다 타이머가 따로 돌고 **Worker 간 실행 순서 조율은 없다**(API 마다 Worker 를 두기로 한 결정의 대가).

## PO 자동 수집 (PO Sync) — AMES.Api

고객사 SRM 의 MM31006 `INQUERY` 결과를 REST 로 받아 PP-002 와 같은 `PpRepository.UpsertCustomerOrders` 로 `PP_CustomerOrder` 에 업서트한다(actor `PO-SYNC`, 새 행 `Open`, 확정은 PP-002 에서). `AMES.Api/Workers/PoSync/PoSyncWorker`(위 `ScheduledWorker` 상속, `Code=POSYNC`)가 돌리며 설정은 전부 **공통코드(MD-26)** 다 — 주기·고객사·URL·인증 토큰까지. 마이그레이션 `dist/migrate_po_sync.sql` 은 그룹 4개와 전역 기본값만 만든다(재실행 안전). 09-17 에 그룹을 `PO_SYNC*` → `SW_POSYNC*` 로 바꿨고, 구 이름으로 적용된 DB 는 같은 스크립트를 다시 돌리면 항목을 새 그룹으로 옮기고(CodeID 접두어 포함) 빈 구 그룹을 지운다. URL·토큰이 긴 값이라 공용 `dist/migrate_md_codeitem_widen.sql`(`Attribute1` 200 · `Description` 500)이 먼저 적용돼 있어야 MD-26 에서 저장된다. 이게 없으면 워커는 소스 0개로 돌 뿐 예외는 없다.

| 그룹 | CodeValue | Attribute1 | Description |
|---|---|---|---|
| `SW_POSYNC` | `INTERVAL` | 수집 주기(분) — 전 소스 공통, 0 = 전체 중지 | |
| `SW_POSYNC` | `WINDOW` | `-60,0` (발주일 `PO_DATE` 오늘−60 ~ 오늘) | |
| `SW_POSYNC` | `TICK_SEC` · `STARTUP_DELAY_SEC` · `TIMEOUT_SEC` | 초 — 선택, 없으면 appsettings `ScheduledWorker` 기본값 (시드 안 함) | |
| `SW_POSYNC_SOURCE` | 소스 키(≤13자) | 귀속 `MD_Customer.CustomerID` | `CORCD=;BIZCD=;VENDCD=;PURC_ORG=` 필수, `WINDOW=` 선택(그 밖의 키는 무시) |
| `SW_POSYNC_URL` | 소스 키 | | 엔드포인트 URL (쿼리 없이) |
| `SW_POSYNC_AUTH` | 소스 키 | `Query:{이름}` / `Bearer` / `Basic` / `Header:{이름}` | 키 / 토큰 / `user:pw` / 헤더값 |
| `SW_POSYNC_AUTH` | `AMES_SERVICE_KEY` (예약) | 참고용 `Header:X-AMES-Service-Key` (코드는 안 읽는다) | AMES.Web → Api 수동 실행 서비스 키 (16자 이상) |

- 소스마다 순차 실행, 한 소스 실패는 다른 소스를 막지 않는다. 결과는 `SYS_InterfaceMonitor` 의 `POSYNC-{키}` 행(SYS-Interfaces 화면, Direction `INBOUND` — 공통코드 `IF_DIRECTION` 값, 09-17 이전 행의 `IN`·`ERR` 은 `migrate_po_sync.sql` §4 가 보정)에만 남는다 — 성공 `OK`·건수·시각, 실패 `ERROR`(공통코드 `IF_STATUS` 값 — 화면 장애 KPI 는 `DOWN`/`ERROR`/`FAULT` 를 세며 `ERR` 는 어디에도 안 잡힌다)·메시지·RetryCount(마지막 성공 시각은 유지). 설정이 깨진 소스(URL·CustomerID·필수 파라미터 누락)도 `ERROR` 로 보인다. 공통코드 로딩 자체가 실패(DB 접속 등)하면 `POSYNC-CONFIG` 행 하나로 남는다(베이스 규칙).
- 마지막 실행 시각은 메모리라 **Api 재시작 직후 모든 소스가 한 번 돈다.** 주기 변경은 다음 틱부터. 주기는 전역 `SW_POSYNC.INTERVAL` 하나뿐이다(소스별 주기 없음, 설명란의 `INTERVAL=` 은 무시) — `0` 이면 스케줄러 전체를 멈춘다(수동 실행은 그대로 동작). 소스 하나만 멈추려면 그 `SW_POSYNC_SOURCE` 행의 `UseFlag=0`.
- 매핑(`AMES.Data.Services.PoSync.SrmPoMapper`, 정본 `SrmPoMapperTests`): `PONO` `4100172316-10` → SoNumber/SoLineNo, `PO_QTY`→OrderQty, `DELI_QTY`→ShippedQty, `PO_DATE`/`PO_DELI_DATE`. `PO_QTY<0`·`LOEKZ='L'`·`PARTNO` 빈 값·20자 초과는 제외, `ELIKZ='X'`(납품완료)는 포함. 매핑 0행은 정상. `SW_POSYNC_SOURCE`·`SW_POSYNC_URL`·`SW_POSYNC_AUTH` 에 같은 키 행이 2개 이상이면 그 소스는 `ERROR` 로 빠지고, 전역 `SW_POSYNC` 의 중복 행은 마지막 행이 이긴다(`PoSyncConfig.Resolve`, PK 가 `CodeID` 라 MD-26 에서 키 중복 등록이 가능하다).
- 원격 요청(`AMES.Api/Workers/PoSync/HttpPoSource`, 정본 `AMES.Api.Tests/HttpPoSourceTests`, 09-17 테스트 서버 `WEBSRV_INQUERY_PO.ashx` 실측): **GET + 쿼리 `CORCD`·`BIZCD`·`PURC_ORG`·`VENDCD`·`PO_DATE_BEG`·`PO_DATE_TO`(yyyy-MM-dd)** — 6개 모두 필수이고 **그 밖의 매개변수는 400**(POST 는 405)이라 다른 값을 붙이지 않는다. 날짜 창은 납기일이 아니라 **발주일(`PO_DATE`) 기준**이다. 테스트 서버 인증은 쿼리 `APIKEY` 뿐(헤더로 보내면 401) → `SW_POSYNC_AUTH` = `Query:APIKEY`. 응답은 루트 배열(첫 배열 프로퍼티도 허용). HttpClient 로그는 쿼리를 `?*` 로 가려 키가 남지 않는다.
- 수동 실행 `POST /api/pp/po-sync/run?source=SEMS`(Bearer, 생략 시 전부) — 상태 코드·busy·cooldown 규칙은 위 베이스 그대로이고, 결과 행에서는 `ok=false, error="busy"`/`"cooldown"` 으로 보인다. Bearer 세션 대신 서비스 키 헤더로도 부를 수 있다(아래).
- **PP-002 "API 가져오기"**(E 권한, `Pp/SupplyPlanImportApiDialog.razor`): 호출 대상을 **`SW_POSYNC_URL` 의 활성 행**에서 하나 골라(이름은 `SW_POSYNC_SOURCE` 행 → URL 행 순, URL·토큰은 화면에 안 낸다, 1개면 자동 선택, 전체 실행 없음) `AMES.Web.Services.PoSyncClient` 가 `{Services:ApiBaseUrl}/api/pp/po-sync/run?source={키}` 를 서비스 키 헤더로 부른다. Web 이 직접 수집하지 않는 이유는 Worker 의 대상별 잠금·60초 쿨다운·모니터 기록을 스케줄 틱과 공유해야 해서다. 다이얼로그는 그 소스의 마지막 동기화(`SYS_InterfaceMonitor` `POSYNC-{키}`)와 실행 결과(발주일 창·조회·매핑·제외·신규·갱신, 실패 시 오류)를 보여 주고, 닫으면 목록을 다시 읽는다. 누가 눌렀는지는 `SYS_AuditLog`(`PP-002` · `RUN` · `PoSync` · 소스 키)에만 남는다 — Worker actor 는 항상 `PO-SYNC` 다. 클라이언트 타임아웃은 150초(원격 `TIMEOUT_SEC` 60 + 업서트)이며 넘기면 "수집은 계속 진행 중일 수 있음" 으로 안내한다. 실패 안내: 키 행 없음/무효 → 공통코드 확인, 401 → Api 가 구버전이거나 다른 DB, 404 → `SW_POSYNC_SOURCE` 에 그 키가 없거나 비활성(URL 행만 있는 경우), 409 → 실행 중·쿨다운.
- **서비스 키**는 `dist/migrate_po_sync_service_key.sql`(`migrate_po_sync.sql` 뒤, 재실행 안전)이 `SW_POSYNC_AUTH` 의 예약 행 `AMES_SERVICE_KEY` 로 만든다 — 값은 적용 시점에 `CRYPT_GEN_RANDOM(32)` 64 hex 로 생성해 리포지토리에 비밀값이 없고 DB 마다 다르며, 이미 있으면 건드리지 않는다(교체는 MD-26). CodeValue 가 16자라 소스 키(≤13자)와 겹칠 수 없고 `PoSyncConfig.Resolve` 는 소스 키로만 인증 행을 찾아 이 행을 무시한다. Web·Api 가 같은 DB 에서 요청마다 읽으므로(캐시 없음, `PoSyncConfig.LoadServiceKey`) 배포본 설정을 맞출 필요가 없다. 행 없음·`UseFlag=0`·16자 미만·**같은 키 행 2개 이상**이면 서비스 키 경로가 닫힌다(정본 `PoSyncConfigTests`). MD-26 을 볼 수 있는 사용자에게는 값이 보이고(토큰과 같은 사용자 결정), 감사 로그에서는 `SW_*_AUTH` 규칙으로 가려진다. 이 키를 아는 호출자는 세션 없이 **`POST /api/pp/po-sync/run`(PO 수집)과 `POST /api/pp/demand-plan-sync/run`(일별 구매계획 수집) 수동 실행만** 할 수 있다(다른 엔드포인트는 키를 받지 않는다). 배포 순서: ① 마이그레이션 ② AMES.Api ③ AMES.Web — 마이그레이션이나 신 Api 없이 신 Web 만 올리면 버튼이 안내 메시지만 띄울 뿐 기존 기능은 그대로다.
- appsettings 에는 PoSync 전용 섹션이 없다 — 틱·시작 지연·타임아웃의 공통 기본값만 `ScheduledWorker` 섹션에 있고, PoSync 만 다르게 줄 값은 위 `SW_POSYNC` 공통코드다. 타임아웃은 요청마다 해석한다.
- dev: `dist/seed_po_sync_dev.sql`(소스 `SEMS` = Seoyon E-Hwa Manufacturing Savannah, URL 은 자리표시자 — MD-26 에서 `SW_POSYNC_URL`·`SW_POSYNC_AUTH` 를 실제 값으로 바꿔야 호출된다. 개발 DB 는 09-17 에 고객사 테스트 서버 `http://192.168.1.68:5220/Service/WEBSRV_INQUERY_PO.ashx` + `Query:APIKEY` 로 설정돼 있다). `migrate_po_sync.sql` 의 MERGE 는 없는 행만 넣으므로 이미 적용된 DB 의 `WINDOW`·그룹 설명은 MD-26 에서 직접 고친다. 토큰은 MD-26 을 볼 수 있는 모든 사용자에게 보인다(사용자 결정). 응답 샘플 `src/02_Data/AMES.Data.Tests/TestData/PO_7700_310471_EN_data.json` 은 매핑 테스트(`SrmPoMapperTests`) 전용이다.
- **정합성 미비(후속 과제, Important #2)**: 상대 SRM 에서 취소·삭제된 PO 라인은 조회 결과에서 사라질 뿐 AMES 에는 반영되지 않아 `Open` 으로 남고, 이미 `Confirmed` 인 수주도 수량·납기가 바뀌면 매 주기 덮어써진다(감사 행 없음) — PP-002 수동 업로드와 같은 동작이며, 정합 처리(미조회 라인 플래그·확정분 변경 경고)는 후속 과제다.

## 일별 구매계획 자동 수집 (DPSYNC) — AMES.Api

고객사 SRM MM30011(일별 구매계획, JIT) 응답을 받아 PP-001 업로드와 같은 `PpRepository.ReplaceDemandPlan`(고객×날짜 창 교체)으로 `PP_DemandPlan` 에 반영한다(actor `DP-SYNC`). `AMES.Api/Workers/DemandPlanSync/DemandPlanSyncWorker`(`ScheduledWorker` 상속, `Code=DPSYNC`)가 돌리며, 공통코드 4그룹·해석 규칙(전역 last-wins, 소스/URL/AUTH 중복 키 오류, 키 길이, 필수 파라미터)은 PO Sync 와 `AMES.Data.Services.WorkerSourceConfig` 를 공유한다 — **`WINDOW` 는 없다**: 날짜 창은 요청 매개변수가 아니라 응답의 `dates` 봉투(`D{k}` → `yyyy-MM-dd`)가 정한다. 마이그레이션 `dist/migrate_demand_plan.sql` 은 그룹 4개와 전역 `INTERVAL=60` 만 만든다(재실행 안전). **고객(Attribute1 의 CustomerID)당 활성 소스는 1개뿐이다** — WINDOW 가 없어 창은 응답이 정하므로 같은 고객을 가리키는 소스가 둘 이상이면 서로 최근 응답으로 덮어써 저장이 흔들린다; `DemandPlanConfig.Resolve` 가 이를 감지해 그 키들을 전부 설정 오류로 빼고(`SW_DPSYNC_SOURCE` 는 유효해도) 모니터에 `ERROR` 행만 남긴다(정본 `DemandPlanConfigTests`).

| 그룹 | CodeValue | Attribute1 | Description |
|---|---|---|---|
| `SW_DPSYNC` | `INTERVAL` | 수집 주기(분) — 전 소스 공통, 0 = 전체 중지(기본 60) | |
| `SW_DPSYNC` | `TICK_SEC` · `STARTUP_DELAY_SEC` · `TIMEOUT_SEC` | 초 — 선택, 없으면 appsettings `ScheduledWorker` 기본값 | |
| `SW_DPSYNC_SOURCE` | 소스 키(≤13자) | 귀속 `MD_Customer.CustomerID` | `CORCD=;BIZCD=;VENDCD=;PURC_ORG=` 필수 |
| `SW_DPSYNC_URL` | 소스 키 | | 엔드포인트 URL |
| `SW_DPSYNC_AUTH` | 소스 키 | `Query:{이름}` / `Bearer` / `Basic` / `Header:{이름}` | 키 / 토큰 / `user:pw` / 헤더값 |

- 원격 요청(`AMES.Api/Workers/DemandPlanSync/HttpDemandPlanSource`, 인증은 PO Sync 와 공유하는 `SrmAuth`): **GET {URL}?CORCD&BIZCD&PURC_ORG&VENDCD&PLAN_DATE=yyyy-MM-dd**(잠정 계약 — 매개변수 이름 `PLAN_DATE` 는 `HttpDemandPlanSource.PlanDateParam` 상수 하나에만 있어 실제 SRM 이름으로 바뀌면 거기만 고친다). 응답은 `AMES.Data.Services.DemandPlan.SrmMipMapper`(정본 `SrmMipMapperTests`) 가 처리하는 JSON 봉투 — `dates` 로 날짜 열을 정하고, 행의 `D{k}_PR_QTY` 를 `ScheduledQty`, `D{k}_PO_QTY` 를 참고용 `PoQty` 로 매핑한다. `PARTNO` 빈 값·20자 초과 행과 음수 `PR_QTY` 칸은 경고와 함께 제외.
- **빈 응답 보호**(`DemandPlanRunner.RunAsync`): 매핑 결과 **셀 수(`SrmMipMapResult.Cells.Count`)가 0** 이면 `PP_DemandPlan` 을 건드리지 않고 `OK`·0건으로 모니터에 기록한다 — 응답 자체가 비었든(`Rows==0`), 행은 있어도 전부 걸러졌든(잘못된 PARTNO·전 날짜 0/음수) 마찬가지다. `Rows==0` 만 보면 후자를 놓쳐 창을 잘못 비울 수 있어 **셀 수만 본다**. `DemandPlanRunResult` 에는 매퍼 경고(`Warnings`, dates 파싱 실패·PARTNO 제외·음수 등)가 같이 오는데, OK 인 실행이라 모니터 `error` 컬럼에는 넣지 않고 `DemandPlanSyncWorker.LogResult` 가 Warning 레벨 로그로만 남긴다.
- 수동 실행 `POST /api/pp/demand-plan-sync/run?source=`(Bearer, 생략 시 전부) — **서비스 키는 새로 만들지 않고 PO Sync 것(`SW_POSYNC_AUTH`/`AMES_SERVICE_KEY`)을 그대로 쓴다**(엔드포인트가 `PoSyncConfig.LoadServiceKey` 를 공급자로 넘긴다). PP-001 일별 탭의 "API 가져오기"(`DemandPlanSyncDialog.razor`)가 `AMES.Web.Services.DemandPlanSyncClient` → PoSync·DemandPlanSync 공용 `WorkerRunClient`(HTTP·서비스 키·상태 코드 분기 공유) 로 소스 하나만 즉시 실행시킨다. 클라이언트 타임아웃 150초.
- dev: `dist/seed_demand_plan_dev.sql`(소스 `SEMS`, URL 은 자리표시자 — MD-26 에서 `SW_DPSYNC_URL`·`SW_DPSYNC_AUTH` 를 실제 값으로 바꿔야 호출된다. 운영 금지). 응답 샘플은 매핑 테스트(`SrmMipMapperTests`) 전용.

---

## POP 시리얼 스캐너 (Zebra DS3678, USB CDC)

INJ-MAIN 은 HID(키보드 웨지) 외에 시리얼 스캔도 받는다. 호스트(`PopBlazorForm`)가 앱 기동 시 `SerialScannerReader` 로 COM 포트를 열고, `ScannerService.OnScan` 으로 화면에 전달하며, INJ-MAIN 이 구독해 HID 와 같은 `ConfirmScan` 경로로 확정한다. 팝업이 열려 있으면 시리얼 스캔은 무시된다 — 단 안돈 팝업은 예외로, 직접 구독해 모든 스캔을 사원증으로 해석한다.

```json
"PopTerminal": { "Scanner": { "PortName": "COM5", "ReconnectMs": 3000 } }
```

- `PortName` 이 비어 있으면 비활성(HID 만). 리포지토리 기본은 빈 값 — 터미널마다 장치 관리자에서 COM 번호를 확인해 넣는다.
- 보레이트·패리티 등은 설정에 없다. USB CDC 는 이 값을 무시하며 코드가 9600/8/N/1 + DTR/RTS on 으로 고정한다.
- 연결 실패는 토스트 없이 `{Printer:OutputDir}/scanner-YYYYMMDD.log` + 재시도. 상단바 `🔌 스캐너` 칩이 회색이면 포트를 못 잡은 것이다.

**스캐너 사전 설정 (크래들 CR8178 에 걸린다 — 페어링된 스캐너로 설정 바코드를 찍는다):**

| 항목 | 값 | 주의 |
|---|---|---|
| 호스트 인터페이스 | **USB CDC Host** | "SSI over USB CDC"·"SNAPI" 는 바이너리 프로토콜이라 텍스트가 안 온다 |
| Scan Suffix 1 | CR (Enter) | 파서는 CR/LF 둘 다 종결로 받는다 |
| PC 드라이버 | Zebra CDC 드라이버 권장, Windows 10/11 내장 `usbser` 도 동작 | 장치 관리자에 "Zebra CDC Scanner" 또는 "Symbol Bar Code Scanner::CDC" 로 보이면 정상 |

---

## LOT 불량·REWORK — 배포 순서

```
1. dist/migrate_lot_defect_rework.sql   (sqlcmd -I 필수 — 컬럼·인덱스·상태 이관·RWK 마스터)
2. MD-013 에서 불량원인 등록 (없으면 REWORK 판정 불가)
3. AMES.Pop 신버전                       (INJ·IMG·REWORK — 한 바이너리)
```

| 잘못된 상태 | 결과 |
|---|---|
| 마이그레이션 없이 신 Pop | 불량 팝업 등록·REWORK 대기열 조회가 매번 예외. 스캔 확정은 계속 동작 |
| 신 DB + 구 Pop | 구 팝업이 `LotID NULL` 행을 넣는다. 대기열 쿼리는 `LotID IS NOT NULL` 이라 안 보이며 마이그레이션 §2 재실행으로 `LEGACY` 봉인 — 안전한 실패 쪽 |
| RWK 라인 미등록 | REWORK 로그인 불가. 라인 불량 등록은 정상, 대기열만 쌓인다 |

롤백은 Pop 구버전으로만. 구버전은 `DEFECT`/`SCRAPPED` 를 몰라 그 LOT 스캔이 `ConfirmFailed` 로 떨어진다. InjAgent 변경 없음.

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

단계 품번 컬럼(`migrate_wo_step_item.sql`)도 같은 순서(마이그레이션 → Pop → Web)다. 구 Pop + 신 DB 는 컬럼을 안 읽어 안전하고, 마이그레이션 없는 신 바이너리는 발행·조회가 매번 예외다.

**순서를 뒤집으면 안 되는 이유:**

| 잘못된 상태 | 결과 |
|---|---|
| 구 Pop + 신 Web | 신 Web 이 발행한 WO 는 헤더 `LineID` 가 NULL 이라 **구 Pop 의 WO 목록에 아예 안 보인다.** 구 Pop 이 올린 실적은 헤더 `CompletedQty` 만 올리고 단계 행은 그대로라, 신 Pop 배포 후 단계 진척이 0 에서 다시 시작한다 |
| 마이그레이션 없이 신 바이너리 | `PP_WorkOrderRouting.CompletedQty`·`TerminalLock` 컬럼이 없어 **PP-04 라인 로드·Pop WO 목록 조회가 매번 예외.** 배포 전 컬럼 존재를 반드시 확인할 것 |
| 구 Web + 신 DB | 라벨 순서와 달리 **안전한 실패 쪽이다.** 구 Web 은 헤더 `LineID` 에 기록하고 단계는 `Pending` 으로 남으며, 마이그레이션 §3(백필)을 다시 돌리면 단계 행이 정리된다 |

**INJ 스테이션마다 `MD_Bop`(StationCode) 등록이 선행돼야 한다.** 비어 있으면 INJ-MAIN 좌측 패널이 비고(당일 실적 있는 품번만 "미등록"으로 뜸) 수동 라벨 생성·불량 팝업이 동작하지 않는다 — 스캔 확정은 LOT 품번으로 WO를 찾으므로 계속 동작한다. dev 는 `dist/seed_md_bop_inj_dev.sql`, 운영은 MD-005 화면에서 등록.

---

## POP 안돈 — 운영 전제

안돈 흐름: 확인창 → `OPEN`(슈퍼바이저 호출) → 슈퍼바이저 배지 스캔 `SUP_ACKED` → 원인(`ANDON_CAUSE`)·부서(`ANDON_DEPT`) 호출 `DEPT_CALLED` → 담당자 배지 도착 → ACK → 전 부서 ACK 시 `RESOLVED`. 부서 호출 없이 슈퍼바이저가 자체 해결 종료할 수도 있다. 호출 수단은 DB 기록 + POP 화면뿐이다(알림 채널 없음, Web 대시보드는 후속).

- **라인마다 `MD_LineSupervisor` 에 슈퍼바이저 사번이 등록돼 있어야 한다.** 없으면 발동된 안돈이 `OPEN` 에서 못 벗어난다 — 스캔마다 "이 라인의 슈퍼바이저가 아닙니다" 만 뜬다. 등록만이 탈출구다.
- 부서 담당자 스캔은 소속을 검증하지 않지만 EOS 양식 배지이거나 등록된 사번이어야 한다. 그 안에서는 누가 찍든 그 사람이 도착자로 기록된다.
- 안돈 팝업이 열려 있는 동안 스캔은 전부 배지로 해석된다. LOT 라벨을 찍으면 거부 토스트가 뜬다 — 실적 확정은 팝업을 닫고 한다. 팝업을 닫아도 안돈은 DB 에 남고 상단바 `🚨 안돈 진행중` 칩으로 다시 연다.
- 배포 순서: ① `dist/migrate_andon_workflow.sql` ② 슈퍼바이저 등록 ③ AMES.Pop 신버전. 롤백은 Pop 구버전으로만(새 테이블·컬럼은 구버전이 안 쓴다).
- **심각도는 필수**다. 슈퍼바이저가 `DEFECT_SEVERITY`(MINOR/MAJOR/CRITICAL) 중 하나를 골라야 부서 호출·자체 해결 종료가 되고, 값은 `PR_AndonCall.Severity` 에 남는다(발동 직후는 NULL).
- **연동 테이블**: 발동 시 `PP_LineDowntimeLog`(ReasonCode `ANDON`, `AndonID`) 1행이 열리고 종료 시 `EndTS`·`DurationMin` 이 닫힌다(자체 해결 포함). 보전(`ANDON_DEPT` 코드 `MAINT`, `AndonDeptCodes.Maint`)이 호출되면 `MNT_FailureRegister` 1행(`Source='ANDON'`, `AndonRefID`=안돈 ID, `FailureNumber`=`FAIL-yyMM-NNN`, `Severity`=선택값, `FailureType`=원인 코드)이 생기고 도착·ACK 가 `MNT_FailureAction`(ARRIVED/ACK) 으로, 안돈 종료가 `Status='RESOLVED'` 로 이어진다(10-06 부터 Web MNT-002 상세 모달 "대응 이력"이 발생 → 보전 도착 → ACK → 수리 완료(REPAIRED)/해결을 시각·담당자·발생 후 분으로, MNT-009 실시간 고장 알림이 행마다 대응 단계(대기·도착·조치 중·해결)와 경과/해결 소요를 보이며 행을 누르면 `mnt/failure?id=` 로 그 상세가 열린다. MNT-007 작업지시 상세도 연결된 고장(`MNT_FailureRegister.WorkOrderID`, `ListFailuresForWo`)마다 같은 이력을 보이며 고장 번호가 MNT-002 상세 링크다 — 표시는 공용 `Pages/Mnt/FailureTimelinePanel.razor`, 조회 `MntRepository.ListFailureActions`, 규칙 정본 `AMES.Data.Services.FailureTimeline`·`FailureTimelineTests`. 안돈 종료는 조치 행 없이 등록 행만 닫으므로 REPAIRED 가 없는 해결은 등록 행 `ResolvedAt` 으로 보인다). 품질·자재 호출은 고장을 만들지 않는다. Web PP-DTL(`Pp/Downtime.razor`)은 이 안돈 행(`AndonID` 있음, 출처 "POP")을 **원인·비고만** 고치게 하고(`PpRepository.UpdateDowntimeCauseComment` — 시각·사유는 안돈 기록과 묶여 잠근다), 웹 등록(`InsertDowntime`, 종료 비우면 진행 중)·웹 행 수정(`UpdateDowntimeEntry`, 라인 고정, `AndonID IS NULL` 행만)은 같은 라인 기존 비가동(진행 중 포함)과 겹치면 거부한다(OEE 이중 계상 방지, 규칙 정본 `AMES.Data.Services.DowntimeEntryRules`·`DowntimeEntryRulesTests` — 미래 시각 거부). 사유는 이력 DISTINCT(ANDON 제외), 원인은 MD-025 사유 코드(`MD_ReasonCode`, `ReasonType='DOWNTIME'`) 콤보이며 `RequiresComment` 원인은 비고 필수다(10-05). PP-ODM(`DowntimeMonitor`)은 **조회 전용**이다(10-08 사용자 결정 — 사유 수정 모달·`PpRepository.UpdateDowntimeReason` 삭제, 안내 문구와 PP-DTL 링크만). 비가동 사유·원인·비고 수정은 PP-DTL 에서만 한다. `MNT_FailureRegister.Severity` 컬럼은 `dev` 의 `migrate_mnt_failure_severity.sql` 이 만든다 — 그 마이그레이션 없는 DB 에서는 보전 호출이 예외 토스트로 실패한다.

---

## 외부 개방 화면 (Partner Portal, `/portal`) — AMES.Web

같은 AMES.Web 안에서 외부 사용자에게 특정 화면만 여는 구조(09-23). 정본은 `Services/PortalAuth.cs`, 화면은 `Components/Pages/Portal/`.

- **외부 사용자** = SCM-004 외부 사용자 관리(`Pages/Scm/PortalUsers.razor`, `scm/portal-users`)에서 등록한 **`SCM_PortalVendorUser`** 행(09-25) — **AspNet 테이블과 무관**하다. 이메일(`UserID`, 소문자 저장)이 로그인 ID, 비밀번호는 `PortalAuth.HashPassword`(ASP.NET `PasswordHasher` V3 형식, 규칙은 내부 사용자와 같은 Identity 검증기), 행 하나가 협력업체(`VendorID` → `MD_Vendor`) 하나에 묶인다. 이름(`UserName`)은 선택. 로그인 5회 실패(`ScmRepository.PortalMaxFailedLogins`)면 `LockedFlag=1` 이고 **내부 사용자가 SCM-004 에서 잠금 해제**한다(비밀번호를 바꿔도 풀린다). 자가 가입·비밀번호 변경 화면은 없다. 로그인 ID 는 내부·외부를 합쳐 유일해야 한다 — SCM-004 등록은 `EmailUsedByWebUser`, SYS-001 등록은 `FindPortalUser` 로 서로 검사한다. 구 방식(Identity 계정 + 역할 `ExternalCustomer`)의 외부 계정·역할·포탈 RBAC 행은 `dist/migrate_portal_legacy_cleanup.sql`(09-25, 재실행 안전)이 지웠고, `migrate_portal.sql`·`migrate_scm_portal_screens.sql` 은 더 이상 그 역할과 포탈 권한을 만들지 않는다 — 다른 스크립트가 다시 만들면 정리 스크립트를 다시 돌린다.
- **로그인은 완전히 분리**(`Services/WebSignIn.cs`, 09-26): 내부 `Account/Login`(`SignInInternalAsync`)은 내부 Identity 계정만, 외부 `/portal/login`(`SignInPortalAsync`)은 `SCM_PortalVendorUser` 만 받는다 — 반대쪽 계정은 조회하지 않고 일반 실패(`Auth.Err.Invalid`)로 거부해 어느 쪽 계정이 있는지 드러내지 않는다. 내부 로그인은 계정 상태(ACTIVE)·5회 잠금(관리자 해제)·ID 기억 쿠키 규칙 그대로이고 `ReturnUrl` 이 `/portal` 이면 `/` 로 보낸다. **한 브라우저에서 내부·외부에 동시에 로그인할 수 있다**(10-07) — 로그인은 반대쪽 쿠키를 건드리지 않고, 로그아웃도 자기 쿠키만 지운다. 두 로그인 경로는 `Program.cs` 미들웨어가 요청 사용자를 익명으로 바꿔 처리한다 — 위조 방지 토큰이 화면을 연 시점의 사용자에 묶여, 화면을 열어 둔 채 다른 탭에서 로그인하면 첫 POST 가 400(→ `?expired=1`)이 돼 버튼을 두 번 눌러야 했다(09-24). 외부 로그인은 `SCM_PortalVendorUser` 의 해시·잠금으로 검증하고(`ActiveFlag=0`·업체 비활성은 로그인 불가) **`AmesPortal` 스킴 쿠키 `.AMES.Portal`(경로 `/`, 클레임: NameIdentifier·Name = 이메일, `ames:actor` = 이메일 @ 앞 20바이트, `ames:vendor` = VendorID, 역할 없음)** 을 발급한다. 쿠키 검증(`OnValidatePrincipal`, 정적 파일 경로 제외)이 요청마다 행을 다시 읽어 **비활성·잠금·업체 변경·삭제면 즉시 쿠키를 끊는다**. 로그아웃은 `GET /portal/logout`(외부 쿠키만 삭제 → 외부 로그인). 내부 화면은 외부 쿠키를 보지 않으므로 외부 사용자만 로그인한 브라우저에서 내부 화면을 열면 내부 로그인으로 간다. 외부 스킴의 권한 거부(`AccessDeniedPath`)도 외부 로그인이고, `/unauthorized` 는 내부 전용이다(외부 호스트에서는 막히는 경로라서). 계정 전환 흐름(구 `to=internal`)은 없다.
- **스킴 선택**(`Program.cs` `AddPolicyScheme(AmesDynamic)`): `/portal` 경로는 **항상 `AmesPortal`** — 내부 쿠키가 있어도 외부 인증으로만 판단하므로 내부 로그인 상태로 포탈 화면을 열면 `/portal/login` 으로 간다. 그 밖의 경로는 **항상 Identity**(반대쪽 쿠키는 보지 않는다). Blazor 회로도 경로로 갈린다 — 회로의 사용자는 연결 요청의 인증으로 정해지므로 외부 화면은 별도 허브 **`/portal/_blazor`**(`Program.cs` `MapBlazorHub(PortalAuth.HubPath)`, `App.razor` 가 `/portal` 화면에서만 `blazor.web.js` 를 `autostart="false"` 로 받아 `Blazor.start({ circuit: { configureSignalR: b => b.withUrl(…) } })` 로 주소를 바꾼다), 내부 화면은 기본 `/_blazor` 다(10-07). `_blazor/disconnect`·`_blazor/initializers` 는 인증과 무관해 기본 주소를 같이 쓴다. 향상된 탐색은 회로를 유지하므로 **내부↔외부 화면을 오가는 링크는 `forceLoad`** 로 새로 불러와야 한다(현재 두 영역 사이 링크는 없고, `SessionGuard`·`RedirectToLogin` 은 이미 `forceLoad`).
- **인가**: 기본 정책(DefaultPolicy)이 "내부 Identity 스킴으로 인증된 사용자" 라서 `[Authorize]` 만 붙은 내부 화면 전부와 `AuthorizeView` 가 외부 쿠키를 거부한다(→ `/unauthorized`). 외부 화면은 폴더 `_Imports.razor` 의 `[Authorize(Policy = PortalAccess)]` = **외부 쿠키 사용자만**(`PortalAuth.AllowsPortalAccess` = `IsPortalUser`, 내부 계정은 Admin 이라도 불가). **포탈 화면은 RBAC 대상이 아니다**(09-25) — `SYS_Screen`(ProcessCode `PORTAL`)에 등록만 하고 `SYS_RolePermission` 행은 두지 않으며(마이그레이션이 지운다) SYS-004 RBAC 화면도 PORTAL 화면을 보이지 않는다. 포탈 메뉴·홈은 등록된 PORTAL 화면을 모두 보인다. 데이터 범위는 협력업체로 걸린다 — `ScmRepository.ListPurchaseOrders` 가 `SCM_PortalVendorUser.UserID = 로그인 이메일` 인 행의 `VendorID` 발주만 준다(브라우저가 보낸 업체값은 쓰지 않음). 내부 계정의 관리자 미리보기·대리 수주 확인·대리 납품 처리는 09-26 에 없앴다 — SCM 리포지토리의 관리자 우회 파라미터(`adminPreview`·`adminOnBehalf`·`@Admin`)도 제거됐다.
- **레이아웃** `Layout/PortalLayout.razor`(정적 셸) + 대화형 `PortalTopBar`·`PortalNavMenu`(09-25 — 레이아웃이 정적이면 테마 버튼·메뉴 검색·접기 클릭이 서버에 닿지 않는다. 내부 MainLayout 의 TopBar·NavMenu 와 같은 구성). 포탈 홈 `/portal` 은 내부 홈과 같은 형태(KPI 줄 = 로그인 업체 발주 건수·확인 대기·납품 진행·납기 지연 + 사이트맵 타일·화면 칩): 외부 사용자는 **내부 사이트와 분리된 외부 셸**(내부 셸과 같은 구조 — 상단바 + 좌측 메뉴 — 이지만 청록 테마, 메뉴에는 권한 있는 외부 화면 PORTAL-* 만)을 본다. 상단바는 협력업체·로그아웃만 보인다("내부 사이트로" 링크 없음). **내부 메뉴(NavMenu)·검색·즐겨찾기에는 `portal/…` 화면이 나오지 않는다**(`NavMenu.V()` 가 접두어로 제외, `MenuCatalog.Sections` 에도 없음) — 내부 사용자는 포탈 화면을 열 수 없다(09-26). `Routes.razor` 의 `RedirectToLogin` 은 `/portal` 경로면 외부 로그인으로 보낸다.
- **오류·400**: `/portal` 아래에서 난 예외는 `UseWhen` 분기의 예외 처리기가 맨몸 `/portal/error`(내부 메뉴 없음)로 보내고, 외부 로그인 폼의 위조 방지 토큰 만료 400 은 `/Account` 와 같이 GET 으로 되돌린다. 외부 셸·로그인은 청록 액센트(`.portal-auth`·`.portal-shell`)로 내부 사이트와 인상을 분리한다. 좌측 메뉴(`PortalNavMenu`)는 10-06 부터 내부 `NavMenu` 와 같은 구성(주/보조 언어 두 줄 표기·즐겨찾기 ☆(저장 키 `ames.portal.nav.*`)·접힘 기억)이고 색은 청록 그대로다. 섹션 머리글은 `Portal.Sub`(외부 포탈 / PARTNER PORTAL)만 보인다. PORTAL-001 의 수주 확인 열은 상태 열처럼 배지다(확인 완료 `ok`·확인 대기 `warn`, 해당 없음 —).
- **외부 노출**: appsettings `Portal:ExternalHosts`(기본 `[]`)에 외부 호스트명을 적으면 그 호스트로 들어온 요청은 `/portal/*`·`/_blazor`·`/_framework`·정적 파일·`/keep-alive` 만 허용하고 나머지는 403(`/` 는 `/portal/login` 으로). IIS URL Rewrite 없이 소스로 관리한다. HTTPS 바인딩·외부 DNS 는 별도.
- **DB**: `dist/migrate_portal.sql`(`PROCESS/PORTAL` 코드, 구 출하 계획 화면 정리) → SCM 마이그레이션들(`migrate_scm_portal_screens.sql` 등, `rebuild_db.sh` 순서) → **`dist/migrate_scm_portal_user_login.sql`** → `dist/migrate_portal_legacy_cleanup.sql`(구 역할·계정·포탈 RBAC 정리)(09-25, `SCM_PortalVendorUser` 를 로그인 구조로 — 구 구조(AspNetUsers FK)에 행이 있으면 중단, 포탈 RBAC 행 삭제, SCM-004 화면·Admin REA, 재실행 안전) → `dist/seed_portal_dev.sql`(개발 전용 `adminext@ames.local` → V1007, 비밀번호는 admin 과 동일 — 해시 복사). 구 `migrate_scm_portal_vendor_user.sql` 은 새 구조면 PK 를 건드리지 않게 가드했다. **이 마이그레이션 없이 신 Web 을 올리면 포탈 로그인·SCM-004 가 예외**이고, 마이그레이션만 먼저 적용하면 구 Web 의 포탈 화면은 RBAC 행이 없어져 막힌다 — Web 과 함께 반영한다.
- 포탈 화면은 SCM 발주 화면과 같은 공용 컴포넌트 `Components/Shared/ScmPreview.razor`(`IsPortal=true`)다: PORTAL-001 발주 조회·수주 확인 · 002 납기별 · 003 납품서 등록 · 004 납품서 조회·수정 · 005 입고·검수. 납품서·검수는 아직 회로 메모리의 미리보기(`ScmPreviewStore`)라 DB 에 저장되지 않는다. 외부 로그인 후 첫 화면은 포탈 홈 `/portal`(`PortalAuth.HomePath`). 09-25 SCM·포탈 화면 디자인을 표준 화면과 맞췄다 — 카드 헤더 필터(Radzen)·No. 열·25행 고정 페이저·행 클릭 상세 모달(정보 타일)·발주 작성 모달, 섹션 클래스 `ames-sec-scm`·`ames-sec-portal`.

## 데모 화면참조 문서 — `AMES_Office_Web_화면참조.md` (참고용)

리포 루트의 `AMES_Office_Web_화면참조.md` 는 데모 사이트(https://mes-taehee.github.io/ames-docs/DEMO_Office_Web.html, `DEMO_*.html`, 상세 설계 `VOLxx_*.html`)에서 추출한 Office Web 74화면 정의다(PP 13 · MNT 9 · RPT 10 · SYS 8 · MD 29 + MD L3 설계서 5). 화면마다 개요·필터·KPI·그리드 컬럼·버튼·모달 입력 필드가 정리돼 있다.

**사용 규칙**
- **구속력 없음.** 데모 정의대로 개발할 의무는 없다. 화면 ID(`PP-01` vs 소스 `PP-001`, MD 번호는 전혀 다름)·라우트(`/pp/wo` vs `/pp/work-order`, MD 는 `/md/fd/…` 서브그룹 경로)·영문 컬럼명은 **현행 소스와 `SYS_Screen` 이 정본**이며, 문서에 맞춰 바꾸지 않는다(권한·메뉴가 HRef 키라 라우트를 바꾸면 깨진다). 데모 샘플 데이터(SAV Detroit·GEO Birmingham·2026-05·`$`)는 무시.
- **기능 범위와 화면 간 관계의 참고 기준으로 쓴다.** 화면을 구현·수정할 때 같은 화면의 데모 정의를 먼저 읽고, 소스에 없는 기능·흐름·모달이 있거나 소스가 데모의 관계와 다르게 가고 있으면 **먼저 사용자에게 개발 방향을 제시한다**(예: "데모 MNT-07 은 발행→배정→완료 모달과 완료 시 결과·소요시간·사용부품을 받습니다. 지금은 조회 전용인데 이 흐름으로 갈까요?"). 사용자가 결정하기 전에 문서대로 구현하지 않는다.
- 데모에만 있고 소스에 없는 대표 기능: MNT-07 작업지시 **수동 발행** 모달(배정·착수·완료는 09-12 구현됨 — 완료 시 고장 RESOLVED·PM 실시일/예정일 갱신·`MNT_PMExecution` 기록·기간 주기 PM 은 다음 WO 자동 발행, MNT-02 수리 완료·MNT-05/010 PM 완료 처리도 같은 `MntRepository.CompleteWoCore` 경로), MNT-04 Shot 갱신·금형 정비 등록, MNT-08 입고(Receive Stock), PP-DTL 비가동 등록/종료, PP-07 릴리스 게이트 검증, SYS-06 알림 테스트 발송. 소스에만 있는 것: MNT-010 보전 PM, SYS 역할·화면 레지스트리, MD 스테이션·위치·라우팅·현장 작업자, WH/FG 웹 화면.

## 다음 개발 항목

- `AMES.Pda`: 추가 모듈 (PP, MNT 등) MAUI 화면
- `AMES.Data`: inline SQL → `dbo.SP_*` 스토어드 프로시저 전환
- `AMES.Web`: 실제 데이터 바인딩 완성 (일부 화면 scaffold 상태)
- `AMES.Pop`: 추가 공정 모듈 (필요 시)
