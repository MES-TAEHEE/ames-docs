# 사출 라벨 발행 주체 이전 — InjAgent → AMES.Pop

**작성일**: 2026-08-03
**상태**: 승인됨 (구현 대기)

## 배경

현재 사출 반제품 라벨은 `AMES.InjAgent`가 발행한다. 에이전트는 사출기 샷을 감지해 RAW LOT을 만든 직후, 같은 코드 경로에서 ZPL을 프린터로 내보낸다 (`MachinePoller.cs`의 `_printer.PrintLabel`).

이 배치에는 두 가지 문제가 있다.

1. **역할 경계가 흐리다.** 에이전트는 PLC 수집 상주 프로세스인데 현장 장치(프린터) 제어까지 겸한다.
2. **라인별 프린터 라우팅이 불가능하다.** `Agent:Machines`는 배열이고 각 항목이 자기 `LineId`를 갖지만, `Agent:Printer`는 단일 객체다. 한 에이전트가 2개 라인을 수집하면 두 라인의 라벨이 같은 프린터로 쏟아진다. 반면 Pop 터미널은 태생적으로 라인별이며 각자 프린터 설정을 갖는다.

## 목표

라벨 발행 책임을 Pop으로 옮긴다. InjAgent는 **수집과 LOT 생성만** 담당한다.

### 비목표

- 발행 시점을 작업자 통제로 바꾸지 않는다 (자동 발행 유지).
- ZPL 레이아웃은 건드리지 않는다.
- Pop이 꺼져 있던 동안 쌓인 과거 미출력분을 자동으로 소급 발행하지 않는다.

## 결정 사항

| 항목 | 결정 | 근거 |
|---|---|---|
| 발행 시점 | 자동, 라벨 전용 1초 폴링 | 역할만 옮기고 동작은 유지. 화면 갱신(5초)과 분리해 지연 최소화 |
| 과거 미출력분 | 소급 발행 안 함 (수동 재출력만) | 교대 복귀 시 수백 장 일괄 분출 방지 |
| InjAgent 발행 기능 | 완전 제거 | 역할 분리 목적에 부합. 플래그로 남기면 이중 발행 사고 여지 |
| 발행 위치 | 세션 백그라운드 서비스 | 대시보드·팝업 중에도 발행 지속 |
| NG_BLOCKED LOT | 발행함 | 현행 동작 유지. NG 부품도 물리적 식별 필요 |
| 중복 방지 | DB 원자적 클레임 | 같은 라인에 Pop 터미널이 여러 대 돌 수 있음 |

## 아키텍처

```
[InjAgent]  PLC 폴링 → 샷 감지 → RAW LOT 생성          ← 프린터 모름
     │
     │ (DB: tbl_Lot + PR_InjLot)
     ▼
[Pop 터미널]  LabelDispatcher → 클레임 → ZPL 발행 → 카운트 확정
```

`LabelDispatcher`는 Pop DI 싱글톤이다. `AppState.OnChange`를 구독해 로그인 시 시작하고 로그아웃 시 정지한다. 화면 수명과 무관하므로 어느 화면에 있든 발행이 계속된다.

## 스키마 변경

`PrintedCount`를 선점 플래그로 재사용하지 않는다. 출력 **전에** 카운트를 올리면 프린터 장애 시 "카운트는 1인데 실물 라벨은 없는" 거짓 상태가 남기 때문이다. 전용 컬럼 2개를 추가한다.

```sql
ALTER TABLE dbo.PR_InjLot ADD
  [PrintClaimTS]      DATETIME2   NULL,   -- 선점 시각 (NULL = 미선점)
  [PrintClaimStation] VARCHAR(20) NULL;   -- 선점한 터미널 StationId
```

정본은 `dist/migrate_inj_agent.sql`의 `CREATE TABLE`에 반영하고 (해당 스크립트는 `DROP TABLE` 후 재생성 구조), 기존 DB용 `ALTER`는 `dist/migrate_inj_lot_print_claim.sql`로 따로 낸다.

## 데이터 흐름

서비스 시작 시 워터마크를 한 번 잡고, **세션 동안 절대 전진시키지 않는다.**

```sql
SELECT ISNULL(MAX(LotID), 0) FROM dbo.tbl_Lot WHERE LineID = @line AND ProcessCode = 'INJ'
```

`ProcessCode` 필터가 없으면 같은 라인의 비-INJ LOT이 워터마크를 최신 INJ LOT 너머로 밀어올려, 그 사이 미출력분이 영구 제외된다. 클레임 쪽은 `PR_InjLot` 조인이 같은 역할을 하므로 필터가 필요 없다 — 의도된 비대칭이다.

워터마크를 전진시키면 안 되는 이유: 출력에 실패해 클레임을 반납한 LOT이 `LotID > watermark` 조건에서 영구 제외되어 재시도가 불가능해진다. 워터마크는 "이 세션 시작 경계"라는 고정 의미만 갖고, **재시도 대기열 판정은 전적으로 `PrintedCount = 0`과 `PrintClaimTS IS NULL`이 담당한다.**

- 출력 성공분 → `PrintedCount = 1` → 자동 제외
- 출력 실패분 → 클레임 반납으로 `PrintClaimTS = NULL` → 다음 틱에 다시 선점됨
- 세션 이전 LOT → `LotID <= watermark` → 영구 제외 (소급 발행 안 함)

`LotID > watermark` 는 **하드 컷오프**다. 워터마크 이하 LOT 은 `PrintedCount` 가 0 이어도 자동 발행 대상이 아니며, 재시도 판정은 워터마크 위쪽에만 적용된다.

**워터마크 획득은 첫 성공 틱에 일어난다.** 로그인 시점에 동기로 잡으면 그때의 일시적 DB 장애 한 번이 교대 내내 라벨을 죽인다 — 재시도도 알림도 없이. 그래서 획득을 `Tick()` 으로 옮겨 실패 시 다음 틱에 재시도한다. 대가는 획득이 지연되는 동안 생성된 LOT 이 결국 잡힌 워터마크 아래로 들어가 자동 발행에서 빠지는 것이다. 정상 상황에서 그 창은 1틱(1초)이고, 해당 LOT 들은 세션 이전 미출력분과 같은 취급 — 미확정 목록의 🖨 버튼으로 수동 발행한다.

매 틱(1초):

```
ClaimForPrint(lineId, watermark, stationId)   -- UPDATE…OUTPUT, 원자적
  ↓ 선점된 LOT 목록 (없으면 즉시 종료)
각 LOT: ZplLabelBuilder.Build → ZplPrinter.Print
  ├ 성공 → IncrementPrintedCount(lotId)
  └ 실패 → ReleasePrintClaim(lotId, stationId)   -- 다음 틱 재시도
```

클레임 조건 (한 번에 `top`건, 기본 5건):

```sql
UPDATE TOP (@top) dbo.PR_InjLot
SET    PrintClaimTS = SYSDATETIME(), PrintClaimStation = @station
OUTPUT INSERTED.LotID INTO @claimed
WHERE  LotID > @watermark
  AND  PrintedCount = 0
  AND  (PrintClaimTS IS NULL OR PrintClaimTS < DATEADD(second, -@stale, SYSDATETIME()));
```

스테일 조건이 크래시 복구를 담당한다. 선점 직후 터미널이 죽어도 `staleSeconds` 뒤 다른 터미널이 회수하므로 라벨이 영구 유실되지 않는다. `PrintedCount = 0` 조건이 함께 있어 이미 출력된 건은 재선점되지 않는다.

**불변식: `staleSeconds > top × 프린터 최악 지연`.** `ZplPrinter`는 라벨 1장당 연결 2초 + 송신 3초 = 최악 5초다. 이 관계가 깨지면 첫 터미널이 아직 배치를 처리하는 중에 다른 터미널이 뒷부분을 정당하게 스테일 회수해 같은 라벨이 두 장 나간다. 현재 값은 `60 > 5 × 5 = 25`.

**반납은 소유자만 한다.** `ReleasePrintClaim`은 `AND PrintClaimStation = @station`으로 소유권을 검증한다. 이게 없으면, 지연된 터미널의 뒤늦은 실패 보고가 스테일 회수로 정당하게 넘어간 다른 터미널의 클레임을 지워 3장째 라벨이 나온다.

선점 후 라벨 데이터 조회는 같은 배치에서 기존 `SelectLotView`에 `WHERE l.LotID IN (SELECT LotID FROM @claimed)`를 붙여 수행한다. 라벨 조립에 `tbl_Lot.LotCode`와 `MD_Item.ItemName`이 필요한데 `OUTPUT` 절은 갱신 대상 테이블(`PR_InjLot`) 컬럼만 낼 수 있기 때문이다.

## 발행 경로 일원화

`ManualEntryPopup`은 현재 LOT 생성 직후 인라인으로 라벨을 뽑는다. 그 LOT들도 `LotID > watermark AND PrintedCount = 0` 조건에 걸리므로 디스패처가 같은 라벨을 한 번 더 뽑는다.

→ **인라인 발행을 제거하고 디스패처에 일임한다.** 발행 경로가 하나로 정리되고 팝업에서 per-LOT try/catch와 `printError` 누적 코드가 사라진다. 대가는 최대 1초 지연이다.

재출력 버튼(`InjMain`, `Inj04ProductionEntry`)은 유지한다. 클레임 쿼리가 `PrintedCount = 0`만 대상으로 하므로 이미 출력된 LOT과 충돌하지 않는다.

**알려진 허용 동작**: 디스패처가 방금 선점했으나 아직 뽑지 못한 1초 이내의 LOT을 작업자가 재출력 버튼으로 누르면 2장이 나온다. 재출력 버튼의 의미상 허용 가능한 것으로 본다.

## 에러 처리

폴링 주기(1초)가 `ZplPrinter`의 TCP 연결 타임아웃(2초)보다 짧다. 프린터 장애 시 이전 틱이 끝나기 전에 다음 틱이 겹치므로 **재진입 금지가 필수**다. `Interlocked.Exchange` 게이트로 실행 중이면 해당 틱을 건너뛴다.

| 상황 | 처리 |
|---|---|
| 프린터 출력 실패 | 클레임 반납 → 다음 틱 재시도. 연속 `MaxFailures`회 실패 시 자동 발행 정지 + 토스트 알림 |
| 정지 후 복구 | 재출력 버튼 성공 시 실패 카운터를 0으로 리셋하고 타이머 재개 |
| DB 조회 실패 | 로그만 남기고 다음 틱 (일시적 장애로 간주, 정지 안 함) |
| 로그아웃 / 세션 없음 | 타이머 정지, 워터마크 폐기 → 재로그인 시 새 워터마크 |

**실패 카운터 규칙**: 라벨 1장 출력 실패마다 +1, 1장이라도 성공하면 0으로 리셋. 틱 단위가 아니라 라벨 단위로 센다.

프린터가 꺼져 있는데 1초마다 2초짜리 TCP 연결을 무한 반복하는 것을 막는 것이 핵심이다. 작업자에게 원인을 알리지 않고 조용히 실패하는 상태도 함께 방지한다.

**정지 중 누적분**: 자동 발행이 정지된 동안에도 에이전트는 계속 LOT을 만든다. 워터마크가 고정이므로 복구 시 그동안 쌓인 미출력분이 한꺼번에 나간다. 이는 의도된 동작이다 — 해당 LOT들은 이번 세션에 실제로 생산된 부품이며 라벨이 필요하다. 다만 프린터를 오래 방치했다가 고치면 분출이 발생할 수 있음을 운영자가 알아야 한다.

## 설정

```jsonc
"PopTerminal": {
  "Printer": {
    "Mode": "File",        // 기존
    "Host": "127.0.0.1",   // 기존
    "Port": 9100,          // 기존
    "OutputDir": "labels", // 기존
    "PollMs": 1000,        // 신설 — 라벨 전용 폴링 주기
    "MaxFailures": 3       // 신설 — 연속 실패 시 자동 정지 임계값
  }
}
```

`AgentConfig.Printer`와 `appsettings.json`의 `Agent:Printer` 섹션은 제거한다.

## 테스트

`LabelDispatcher`를 타이머·DI에서 분리해 순수 로직으로 테스트한다. 저장소는 `IInjLotClaimStore`(클레임·반납·카운트), 프린터는 `ILabelSink`로 주입받는다. `AMES.InjAgent.Tests`가 exe 프로젝트를 참조하는 것과 같은 패턴으로 `AMES.Pop.Tests`(xUnit)를 신설한다.

| 테스트 | 검증 |
|---|---|
| 워터마크 이전 LOT은 선점하지 않는다 | 소급 발행 안 함 |
| 성공 시 PrintedCount +1, 클레임 유지 | 정상 경로 |
| 실패 시 클레임 반납, PrintedCount 불변 | 재시도 가능 상태 보존 |
| 실패한 LOT이 다음 틱에 다시 선점된다 | 워터마크 고정 (전진 시 재시도 불가 회귀 방지) |
| 연속 `MaxFailures`회 실패 → 정지, 이후 틱은 무동작 | 무한 루프 방지 |
| 이전 틱 실행 중이면 새 틱 스킵 | 재진입 금지 |
| NG_BLOCKED LOT도 발행 대상 | 현행 동작 유지 |
| 로그아웃 시 정지 | 세션 수명 |

DB 레벨(원자적 클레임, 스테일 회수, 반납 소유권 검증)은 단위 테스트로 검증할 수 없다. 마이그레이션 적용 후 두 세션에서 동시에 클레임을 실행해 한쪽만 행을 가져가는지 수동 확인한다.

## 영향 범위

**신설**
- `src/03_Pop/AMES.Pop/Services/LabelDispatcher.cs`
- `dist/migrate_inj_lot_print_claim.sql`
- `src/03_Pop/AMES.Pop.Tests/` (xUnit)

**수정**
- `AMES.Data/Repositories/InjLotRepository.cs` — `GetMaxLotId`, `ClaimForPrint`, `ReleasePrintClaim` 추가
- `dist/migrate_inj_lot_print_claim.sql` — 클레임 컬럼 2개 + `IX_PR_InjLot_PrintClaim` + `IX_tbl_Lot_Line`

`InjLotDto`와 `SelectLotView`는 변경하지 않는다. 클레임 컬럼은 디스패처 내부 상태일 뿐 화면에 노출되지 않으므로 DTO에 실을 이유가 없다.
- `dist/AMES_Schema.sql`, `dist/migrate_inj_agent.sql` — `PR_InjLot` 정본에 컬럼 2개
- `AMES.Pop/Forms/PopBlazorForm.cs` — DI 등록
- `AMES.Pop/Common/AppConfig.cs` — `PollMs`, `MaxFailures`
- `AMES.Pop/Pages/InjPopups/ManualEntryPopup.razor` — 인라인 발행 제거
- `AMES.Pop/appsettings.json` — 설정 2개

**삭제**
- `AMES.InjAgent/Core/ZplLabelPrinter.cs`
- `AMES.InjAgent/Core/Interfaces.cs` — `ILabelPrinter`, `IInjAgentStore.MarkLabelPrinted`
- `AMES.InjAgent/Core/MachinePoller.cs` — `_printer` 필드·생성자 인자·발행 호출부
- `AMES.InjAgent/Core/AgentConfig.cs` — `Printer` 속성
- `AMES.InjAgent/Program.cs` — 프린터 생성·주입
- `AMES.InjAgent/appsettings.json` — `Agent:Printer` 섹션
- `AMES.InjAgent.Tests` — `FakePrinter`, `NopPrinter`

## 위험

- **Pop이 꺼져 있으면 라벨이 나오지 않는다.** 에이전트 폴백을 두지 않기로 했으므로, LOT은 DB에 쌓이되 라벨은 없다. 복구는 미확정 LOT 목록의 재출력 버튼(수동)으로 한다. 이 트레이드오프는 역할 분리를 위해 의도적으로 수용한다.
- **DB 재구축 순서에 의존한다.** `dist/migrate_inj_agent.sql`이 `PR_InjLot`을 `DROP` 후 재생성하므로, 정본 수정과 별도 `ALTER` 스크립트가 둘 다 일관되게 유지되어야 한다.
