# AMES — A-MES Manufacturing Execution System
# Claude Code 가이드

> **이 파일은 핵심 규칙과 색인만 담는다**(11-18 토큰 절감 정리 — 360KB → 약 12KB). 기능별 상세 설명은 `docs/claude/` 로 **원문 그대로** 옮겼다.
> 필요한 주제만 `grep -rn 키워드 docs/claude/` 로 찾아 해당 줄 범위만 읽을 것(파일 통째로 읽지 않는다). 아래 「문서 색인」 참고.
> **새 기능·변경 설명은 이 파일이 아니라 `docs/claude/` 의 알맞은 파일에 더한다**(이 파일을 다시 키우지 말 것).

## 프로젝트 개요

자동차 부품 제조 현장을 위한 MES(Manufacturing Execution System) — 공장 터미널(POP), 핸디 스캐너(PDA), 사무실 포탈(Web), REST API 로 구성된 다중 클라이언트 시스템. 회사: Seyon(한국 자동차 부품사).

- **DB**: 개발·운영 모두 `172.29.3.21\MES` / `AMES`(SQL Server 2025, 콜레이션 `SQL_Latin1_General_CP1_CI_AS`) — POP·InjAgent appsettings 가 여기를 가리키고 Web·Api 는 환경변수 `ConnectionStrings__AMES`. 이 PC 로컬 테스트 DB 는 `localhost,1433` `AMES_DEV`.
  자세한 접속·전환·IP 이력은 `docs/claude/01_overview_build.md`.
- **솔루션**: `src/AMES.sln`(Visual Studio 2022) — 13개 프로젝트(.NET 10).

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
| DB 접근 | ADO.NET (raw SqlCommand, `Microsoft.Data.SqlClient` 7.1.1 — 10-15 6.1.6 에서 올림, 전이 의존성 `System.Security.Cryptography.Pkcs` 는 취약성 때문에 AMES.Data 에서 10.0.13 으로 고정) — ORM 없음 (operational data) |
| ORM | EF Core 10.0.13 — AMES.Web Identity 테이블 전용 |
| WinForms UI | `Microsoft.AspNetCore.Components.WebView.WindowsForms` 10.0.110 |
| API | ASP.NET Minimal API (`MapGroup` 패턴) |
| MAUI | `Microsoft.Maui.Controls` + `Microsoft.AspNetCore.Components.WebView.Maui` |
| 인증 | Pop: PIN 기반 커스텀 / Web: ASP.NET Identity 쿠키 / API: Bearer Token (`TokenStore`) |

---


## 빌드 및 실행

```powershell
dotnet build src\AMES.sln                                              # 전체 빌드(6~16분 — MAUI: Pda·Tablet)
dotnet run --project src\03_Pop\AMES.Pop\AMES.Pop.csproj               # 공장 터미널
dotnet run --project src\04_Api\AMES.Api\AMES.Api.csproj               # REST API(PDA 서버)
dotnet run --project src\06_Web\AMES.Web\AMES.Web.csproj               # 사무실 웹
dotnet run --project src\07_Etc\AMES.InjAgent\AMES.InjAgent.csproj     # 사출 PLC 수집 에이전트
tools\publish-pop.ps1 [-Zip]                                            # POP 설치 패키지(실행 파일 + 설정 + wwwroot)
```

- **솔루션 전체 빌드를 두 개 동시에 돌리지 말 것**(`NETSDK1047`·`MSB3061` 가짜 실패) — 순차 실행.
- **DB 통합 테스트는 환경변수 `AMES_TEST_CONN` 이 있어야 돈다**(없으면 건너뜀, 쓰기 테스트는 로컬 DB 권장).
- Web 을 **실행 중에 `-o` 없이 빌드하지 말 것**(obj 압축 자산을 덮어 실행 중 CSS 가 깨진다) — 확인용 빌드는 `-o 임시폴더`.
- DB 전제조건·스키마 재생성·재구축 절차는 `docs/claude/01_overview_build.md`(빌드 및 실행)와 `docs/claude/09_db_schema_*.md`.

## 반드시 지킬 핵심 규칙

**작업 방식**
- **커밋·푸시는 사용자가 요청할 때만.** 푸시는 사용자가 말하기 전에는 하지 않는다. Web·Api 는 푸시하면 Jenkins 가 개발서버에 배포한다. DB 마이그레이션은 사용자가 요청한 DB 에만 적용한다.
- 커밋에는 로컬 전용 파일(`appsettings*.json`·`.vscode`·`src/07_Etc/test`·PDA 변경 등 사용자가 보류한 것)을 넣지 않는다 — `git add` 는 파일을 하나씩 명시한다. 수정 커밋 메시지에는 증상·원인·수정·확인을 자세히 적는다.
- 미해결·보류·문제점·다음 개발 항목은 **저장소 루트 `Claude_확인사항.md` 에서만** 관리한다(담당자별). 해결하면 같은 커밋에서 그 항목을 지운다.
- 데모 화면참조 문서(`AMES_Office_Web_화면참조.md`)는 구속력 없는 참고용 — 소스·`SYS_Screen` 이 정본(`docs/claude/12_portal_misc.md`).
- 화면 확인용 임시 서버·브라우저는 한 번에 몰아서 쓰고(로그인은 첫 시도가 실패하니 비밀번호 칸을 비우고 다시 입력), 끝나면 서버·탭을 닫는다.

**데이터·DB**
- **외래키(FK)는 두지 않는다**(생산 라인이 멈추면 안 됨) — 정합성은 리포지토리 코드가 책임진다. 삭제는 사용 중이면 거부.
- 마이그레이션은 **재실행 안전**하게 쓰고(`sqlcmd -f 65001 -I -b`), 스키마 파일(`dist/AMES_Schema.sql`)·`dist/rebuild_db.sh` 에도 반영한다. 새 컬럼을 `ALTER ADD` 하면 감사 컬럼 뒤로 가므로 `dist/migrate_audit_columns_last.sql` 로 정리. **마이그레이션 → 신 바이너리 순서**(없으면 신 코드가 `Invalid column name` 으로 실패).
- **콜레이션은 전 DB `SQL_Latin1_General_CP1_CI_AS`**: varchar 는 CP1252 라 **한글은 nvarchar 에만**, 새 SQL 의 한글 상수는 `N'…'`, 컬럼 정의는 `COLLATE DATABASE_DEFAULT`.
- DB 매개변수는 **개발 DB 컬럼 형식·길이와 같게**: 저장은 `ColumnParams.Add`·공용 헬퍼의 `("@Name","테이블.컬럼",값)`, 조회는 `ColumnParams.AddFilter`. **`AddWithValue` 금지.**
- 기록 시각은 DB 서버 시각 — `DateTime.Now`/`Today` 대신 **`DbClock.Now`/`DbClock.Today`**.
- 행위자 컬럼(`CreatedBy`·`ModifiedBy` 등 사람을 기록하는 `…By`)은 **사번 `varchar(20)`** — Web 은 `ActorCode.Of(user)`, GUID·이메일 금지. 감사 컬럼 순서는 `CreatedBy → CreatedTS → ModifiedBy → ModifiedTS`(맨 뒤).
- 인증 비밀번호·DB 비밀번호를 appsettings·launchSettings·로그·답변에 넣지 않는다(환경변수 `ConnectionStrings__AMES`).

**화면(Web)**
- 화면 읽기 권한은 `MainLayout` 이 `SYS_Screen`(HRef 키) 기준으로 검사한다. 권한은 **R·E·A 3자리**(수정=E, 승인=A) — 새 화면은 SYS-003 에서 기본 권한을 정하고, 저장·삭제 핸들러 첫 줄에서도 권한을 다시 확인한다.
- 등록·수정·삭제 성공 직후 `AuditLogger` 한 줄(`Audit.Created/Updated/Deleted/Log`). 날짜 표시는 `DateFmt`, 수량 표시는 `UomFmt`, 새 문구는 resx 4개 파일(`SharedResources`·`.ko`·`.en`·`.es`)에.
- 품번 글자에 `data-item="@품번"` 을 붙이면 BOM 미리보기가 뜬다. WO·수주·출하 오더 번호는 `WoLink`·`SoLink`·`ShipOrderLink` 컴포넌트로.
- 다크가 기본 테마 — 새 스타일은 고정 색 대신 `--ames-*` 토큰을 쓴다. 팝업은 모든 화면 공통 `modal-drag.js`(끌어 이동·배경 클릭 닫기 가드)를 따른다.

**POP·현장**
- 생산이 불가능하면 안 된다 — DB 가 끊겨도 INJ·IMG·PNT 는 오프라인으로 계속 일한다(`docs/claude/11_pop_ops.md`). 현장 장비(POP·InjAgent)는 아직 가동 전이라 이 PC 에서 로컬 테스트로만 돌린다.

## 문서 색인 (`docs/claude/` — 필요할 때만 grep 으로 찾아 읽기)

| 파일 | 담은 내용 | 읽을 때 |
|---|---|---|
| `01_overview_build.md` | 프로젝트 개요 전문, DB 접속·콜레이션·IP 이력, 빌드·DB 전제조건·스키마 재생성 | DB 접속·재구축·빌드 문제 |
| `02_web_deploy.md` | IIS 배포·접속문자열·CORS·앱풀·watchdog·스마트 앱 컨트롤 | 배포·서버 장애 |
| `03_pop_screens.md` | POP INJ·IMG·RWK·PNT·QC 화면, LOT 상태 기계, BOM 자동 차감, 팔레트 적재, 재작업 | POP 화면·LOT·재고 차감·불량 |
| `04_pda_fg_api.md` | PDA WH·FG 시나리오 기준, AMES.Api 그룹 | PDA·FG 출하·API |
| `05_web_pp.md` | PP-002 자동 확정, PP-APS(계획·WO 생성·슬롯), PP-003·LSB | 생산계획·APS·WO |
| `06_web_mnt_rpt_sys.md` | MNT·RPT·SYS 화면 목록, SYS-001 탭 | 화면 목록 확인 |
| `07_architecture_auth.md` | DbClock·Repository 패턴·모듈 분기, 인증(POP·PDA·Web)·권한(R/E/A)·계정 보안·역할 | 로그인·권한·역할·계정 |
| `08_conventions.md` | 감사 로그·행위자 코드·DB 매개변수 형식·수량/날짜/검색창 표시·라이트 테마·메뉴 | 코드 컨벤션 상세 |
| `09_db_schema_1~5.md` | 테이블 접두사, 품목·BOM 시드, 마이그레이션별 노트(MD·MNT·PP·SYS·FG·SAP 등) 전부 | 특정 테이블·마이그레이션·MD/MNT 규칙 |
| `10_workers_integrations.md` | 알림 PUSH(FCM)·ScheduledWorker·PO Sync·일별 구매계획 수집 | 외부 연동·워커 |
| `11_pop_ops.md` | POP 오프라인, 시리얼 스캐너, LOT 불량·라벨 발행·WO 단계 배포 순서, 안돈 | POP 운영·배포 순서 |
| `12_portal_misc.md` | 외부 개방 포탈, 데모 화면참조 사용 규칙, 테스트 시나리오·결과서 작성 규칙 | 포탈·테스트 문서 |

예) 「출하 오더」→ `grep -rn "출하 오더\|FG_ShippingOrder" docs/claude/`, 「마이그레이션 X」→ `grep -rn "migrate_x" docs/claude/`.

## 미해결·보류·문제점·다음 개발 항목 — `Claude_확인사항.md` 에서만 관리 (10-14 사용자 결정)

미해결 결함·결정 대기(보류)·점검에서 찾은 문제점·쓰지 않는 테이블·다음 개발 항목은 **저장소 루트의 `Claude_확인사항.md` 한 파일에서만** 담당자별로 관리한다. 이 CLAUDE.md 나 다른 문서·메모에 목록을 따로 두지 않는다 — 기능 설명 문단의 "알려진 한계"·"후속 과제" 는 현재 동작 설명이고, 그 처리 여부는 확인사항 파일의 해당 항목이 정본이다. 항목을 해결하면 같은 커밋에서 그 파일의 항목을 지우고, 새로 찾은 문제는 담당자 절에 더한다.
