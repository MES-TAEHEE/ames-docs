# 프로젝트 개요·솔루션 구조·기술 스택·빌드

> `CLAUDE.md` 에서 분리한 상세 문서입니다(원문 그대로, 11-18 토큰 절감 정리). 필요한 주제만 `grep -rn 키워드 docs/claude/` 로 찾아 해당 줄 범위만 읽으세요.

## 프로젝트 개요

자동차 부품 제조 현장을 위한 MES(Manufacturing Execution System).
공장 터미널(POP), 핸디 스캐너(PDA), 사무실 포탈(Web), REST API로 구성된 다중 클라이언트 시스템.

- **회사**: Seyon (한국 자동차 부품사)
- **운영 DB(10-29 사용자 지정)**: `172.29.3.21\MES` / `AMES`(SQL Server 2025 Standard, 서버·DB 콜레이션 `SQL_Latin1_General_CP1_CI_AS`) — **POP·InjAgent의 appsettings는 여기를 가리키며, Web·API는 `ConnectionStrings__AMES` 환경변수로 연결한다**(로컬 `AMES` 를 그대로 옮겨 만듦, `ames_app` 은 같은 SID·비밀번호, SQL Server 에이전트 작업 5개 등록). 아래 개발서버·비상용 로컬 설명은 그 전 기준이다. 비상용 로컬 전환(`launchSettings` 「로컬 DB 비상」·`publish-*.ps1 -DbTarget Local`)과 데모 시드 도구(`tools/seed_*`)는 운영 DB 를 가리키지 않게 그대로 둔다.
- **콜레이션은 모든 DB 에서 `SQL_Latin1_General_CP1_CI_AS` 로 통일한다**(10-29 사용자 결정 — 전에는 `Korean_Wansung_CI_AS`). varchar·char 는 CP1252 라 **한글은 nvarchar 컬럼에만** 들어간다(varchar 에 넣으면 `?`). `dist/create_database.sql`·`rebuild_db.sh` 는 Latin1 로 DB 를 만들고, 스키마 파일·마이그레이션의 컬럼은 `COLLATE DATABASE_DEFAULT`(DB 기본 콜레이션을 따름 — `tools/schema_from_db` 도 원본 DB 기본 콜레이션을 이렇게 바꿔 뜬다), 새 SQL 의 한글 상수는 `N'…'`. 저장 검사 `ColumnParams` 는 컬럼 콜레이션의 코드 페이지(Latin1 = 1252, Korean = 949)로 바이트·문자를 검사한다(정본 `ColumnParamsTests`). 개발서버 `AMES_DEV`·로컬 `AMES_DEV` 는 아직 Korean 이다.
- **DB(개발)**: `AMES_DEV` @ SQL Server 2019(개발서버 15.0.2000.5 RTM — 비상용 로컬은 2025), mixed-mode auth, user `ames_app` — 콜레이션 `Korean_Wansung_CI_AS`(10-29 통일 전)
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
| DB 접근 | ADO.NET (raw SqlCommand, `Microsoft.Data.SqlClient` 7.1.1 — 10-15 6.1.6 에서 올림, 전이 의존성 `System.Security.Cryptography.Pkcs` 는 취약성 때문에 AMES.Data 에서 10.0.13 으로 고정) — ORM 없음 (operational data) |
| ORM | EF Core 10.0.13 — AMES.Web Identity 테이블 전용 |
| WinForms UI | `Microsoft.AspNetCore.Components.WebView.WindowsForms` 10.0.110 |
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

**DB 전제조건**: ① `dist/create_database.sql`로 `AMES_DEV`를 **`COLLATE SQL_Latin1_General_CP1_CI_AS`**로 생성(10-29 통일) → ② `dist/AMES_Schema.sql`(159개 테이블 — 10-29 읽기·쓰기 코드가 모두 없는 20개(옛 `PNT_*` 10·`PR_*` 8·`QC_Disposition`·`QC_NCR_Action`, 목록은 파일 머리말)를 뺐다, 개발 DB 에는 남아 있어 `tools/schema_from_db` 로 다시 뜨면 다시 들어간다·프로시저 164개(POP `SP_POP_*` 포함, 옛 PDA 프로시저 `WH_PDA_`·`FG_PDA_`·`SP_PDA_` 제외 — 개발·로컬 DB 에서도 10-23·10-24 지움)·뷰 1개(`VW_MaterialLotUsage`)·시퀀스 1개(시작값은 파일 값을 잇는다 — DB 의 현재 번호를 뜨지 않음) — 10-29 새 개발 DB `172.29.3.21\MES`·`AMES` 에서 SMO 로 다시 뜬 최종 구조, 삭제 승인 대기인 `WH_ReleaseSchedule` 은 생성기가 뺀다(10-23 안영훈, `dist/retire_wh_release_schedule.sql`). 빈 DB 에 돌리면 그 테이블 말고는 개발 DB 와 테이블·컬럼 순서·인덱스·CHECK·외래키·프로시저·시퀀스·컬럼 설명이 같다) 적용 → ③ `dist/migrate_*.sql` 후 실행(스키마가 이미 최종 상태라 대부분 건너뛴다). DB를 Latin1 콜레이션(`SQL_Latin1_General_CP1_CI_AS`)으로 먼저 만들어야 한다.
**스키마 파일 재생성**(10-08): `tools/schema_from_db`(`dotnet run -- "<개발 DB 연결문자열>" dist/AMES_Schema.sql <출력>`)가 개발 DB 의 테이블·키·인덱스·기본값·CHECK·외래키·컬럼 설명·프로시저를 SMO 로 뜨고(`TEST_*`·SSMS 다이어그램 객체 제외), 기존 파일의 저장소 시드(`-- Repository sample seeds` 부터 — 옛 파일이면 `-- Persistent box labels.` 직전까지)를 뒤에 붙이며 `SYS_Screen` 시드는 개발 DB 현재 값으로 다시 뜬다. DROP 목록은 개발 DB 에 있는 테이블만이다(10-14 — 옛 WH 테이블 9개·트리거 DROP 을 생성기에서 뺐다). 시퀀스도 뜬다(10-24 — `SCM_CaseNumberSequence`). 예전 파일 꼬리에 이어 붙어 있던 마이그레이션 블록(SCM 박스·케이스, FG 출하·재고 통합 등)은 개발 DB 에 이미 반영돼 버렸다. 확인은 빈 DB 에 새 파일을 돌린 뒤 개발 DB 와 테이블·컬럼(순서 등수·형식·NULL·기본값·콜레이션)·키·인덱스·외래키·CHECK·프로시저 정의를 비교해 차이 0 이어야 한다. **재구축 주의**: `rebuild_db.sh` 는 `set -e` + `sqlcmd -b` 라 **첫 실패에서 멈춘다**. 옛 WH/FG 업그레이드 마이그레이션(`migrate_wh_core_tables.sql`·`migrate_fg_inventory_consolidation.sql`)은 스키마 파일의 최종 구조를 덮지 않게 목록에서 뺐다. 지금 막히는 지점과 할 일은 `Claude_확인사항.md` 안영훈 미해결 2 가 정본이다(실제 재구축으로는 10-08 이후 확인하지 않음).

**DB 통합 테스트는 환경변수 `AMES_TEST_CONN` 으로만 DB 를 정한다**(10-28 — 소스에 접속 정보를 두지 않는다, 없으면 DB 테스트는 건너뛴다. Data·Pop 테스트 공통, 쓰기 테스트는 로컬 DB 권장).

**솔루션 전체 빌드는 6~16분 걸린다**(MAUI: Pda, Tablet). **두 개를 동시에 돌리면 `NETSDK1047`·`MSB3061` 가짜 실패**가 나므로 순차 실행할 것.

---
