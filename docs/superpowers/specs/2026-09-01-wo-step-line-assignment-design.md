# 작업지시 공정 단계별 라인 배정·실적 — 설계

- 날짜: 2026-09-01
- 상태: 설계 확정 (사용자 승인)
- 적용 범위: AMES.Data · AMES.Web PP-04/PP-07 · AMES.Pop INJ/IMG 조회·실적 · DB 마이그레이션
- 선행 설계: `2026-09-01-wo-routing-required-design.md` (RoutingType 없는 품목 WO 생성 불가)

## 배경

`PP_WorkOrder.LineID` 는 라우팅 도입 이전의 단일 라인 모델이 남긴 컬럼이다.
라우팅 A 품목(INJ → IMG)은 두 라인에서 작업되는데 헤더에 라인 하나만 있어 다음 문제가 있다.

- WO 생성 시 `LineID` 는 NULL 이고, Release 다이얼로그에서 사람이 고른 한 라인만 기록된다.
  BOP(`MD_Bop` → `MD_Station.LineID`)에 라인이 등록되어 있어도 참조하지 않는다.
- Release 시 `PP_WorkOrderRouting` 에 단계 행이 생기지만 **아무도 읽지 않는다.**
  Pop 터미널은 헤더 `LineID` 로만 WO 를 찾으므로 INJ 라인으로 Release 한 WO 는 IMG 터미널에 보이지 않는다.
- 실적은 헤더 `CompletedQty` 단일 카운터에 쌓인다. 사출 LOT 확정(+1)과 IMG-03 배치 실적이
  같은 숫자를 더하므로 사출만으로 100% → 자동 Closed 가 될 수 있다.
- 단계별 라인 배정 규칙("WO 라인과 공정이 같으면 그 라인, 아니면 첫 활성 라인")은 BOP 를 무시하고
  조용히 정렬 첫 라인을 잡는다.

2026-09-01 기준 AMES_DEV 실태:

| 항목 | 값 |
|---|---|
| 활성 라인 | INJ 2 (`LINE-INJ-01/02`), IMG 1, PNT 2. QC 1 (INACTIVE), FG 없음 |
| BOP 스테이션 | `ST-INJ-01 → LINE-INJ-01`, `ST-IMG-01 → LINE-IMG-01` |
| 라우팅 템플릿 | A: INJ→IMG / B: INJ→PNT→QC→FG / C: INJ |
| `PP_WorkOrderRouting` 행 | 0 |
| Released·In Progress WO | 9건 (그중 RoutingType NULL 4건, IMG 라인 직접 Release 2건) |

## 결정 사항

| 항목 | 결정 |
|---|---|
| 모델 | 헤더(`PP_WorkOrder`) + 공정 단계 행(`PP_WorkOrderRouting`). 단계 행이 라인·상태·완료수량의 정본 |
| 헤더 `LineID` | 쓰기 중단, 컬럼 유지(스키마·기존 데이터·테스트 보존). 어떤 코드도 새로 읽거나 쓰지 않는다 |
| 단계 라인 출처 | BOP 스테이션 라인이 기본값. Release 다이얼로그에서 단계별 수정 가능 |
| 라인 필수 규칙 | 해당 공정에 **활성 라인이 하나라도 있으면** 라인 필수. 없으면(QC·FG) `LineID NULL` 로 생성하고 터미널·실적 대상에서 제외 |
| 폴백 | 없음. 필수 단계에 라인이 비면 Release 차단. "첫 활성 라인" 규칙은 백필 스크립트에만 남긴다 |
| 단계 개방 | Release 시 전 단계 동시 `Released`. 후속 단계는 앞 단계 실적을 기다리지 않는다 |
| 단계 수량 상한 | 헤더 `OrderQty` 만. 앞 단계 산출량으로 제한하지 않는다 |
| 헤더 `CompletedQty` | **라인이 있는 마지막 단계**의 `CompletedQty` 와 동기화 |
| 헤더 `Status` | Release → `Released`. 어느 단계든 접수되면 `In Progress`. 라인이 있는 마지막 단계가 닫히면 `Closed` |
| `MoldID`·`RecipeID`·`TerminalLock` | 헤더에 그대로 둔다(현재 INJ 만 사용) |
| Release 경로 | `PpRepository.ReleaseWo` 삭제. PP-04·PP-07 모두 `WorkOrderRepository.ReleaseWo` 사용 |
| 백필 | 단계 행 없는 Released·In Progress·Closed WO 에 단계 행 생성. 헤더 `CompletedQty` 는 손대지 않는다 |

## 1. 데이터 모델

### `PP_WorkOrderRouting` (단계 행)

- 컬럼 추가: `CompletedQty DECIMAL(14,3) NOT NULL DEFAULT 0`.
- 인덱스 추가: 유니크 `(WoID, StepSeq)`, 조회 `(LineID, Status)`.
- `Status` 어휘를 헤더와 통일: `Released` / `In Progress` / `Closed`. 기존 생성값 `Pending` 은 폐기.
- `LineID NULL` 허용 유지. 활성 라인이 없는 공정의 단계가 여기 해당한다.
- 기존 컬럼 `ActualStart` / `ActualEnd` / `StdCycleSec` / `StdYieldPct` 는 그대로 사용.

"라인이 있는 마지막 단계" = 같은 WO 의 단계 중 `LineID IS NOT NULL` 인 행 가운데 `StepSeq` 최대.
B 라우팅에서는 PNT 단계가 된다(QC·FG 는 라인 없음).

### `PP_WorkOrder` (헤더)

- 품목·수량·납기·RoutingType·전체 상태·SAP 참조를 가진다.
- `CompletedQty` 는 라인이 있는 마지막 단계의 값. 그 단계 실적이 오를 때 같은 트랜잭션에서 동기화.
- `ActualStart` 는 첫 접수 시, `ActualEnd` 는 헤더 Closed 시.

## 2. Release 흐름 (AMES.Data)

### `WorkOrderRepository.PreviewRouting(int woId)` 신설

반환: `List<RoutingStepPreview>`

```
record RoutingStepPreview(
    int StepSeq, string ProcessCode,
    string? BopLineId,            // MD_Bop(ItemNo, RoutingType) → MD_Station.LineID 중 공정 일치 첫 행(StepSeq 순)
    bool LineRequired,            // 그 공정에 활성 라인(ISNULL(Status,'ACTIVE') <> 'INACTIVE')이 하나라도 있으면 true
    IReadOnlyList<LineOption> Candidates)  // 그 공정의 활성 라인 (LineID, LineName)
```

- 소스는 `MD_RoutingStep(RoutingType = WO.RoutingType, ActiveFlag=1)` 정렬 `StepSeq`.
- BOP 스테이션이 있어도 그 스테이션의 라인이 INACTIVE 면 `BopLineId` 는 null.
- WO 가 Draft/Planned 가 아니거나 RoutingType 이 NULL 이면 빈 목록.

### `WorkOrderRepository.ReleaseWo(int woId, IReadOnlyList<StepLineChoice> steps, string actor)` 시그니처 변경

```
record StepLineChoice(int StepSeq, string? LineId)
```

한 트랜잭션에서:

1. `PreviewRouting` 과 같은 기준으로 템플릿 단계를 다시 계산해 **서버 측 검증**.
   - 템플릿 단계와 `steps` 의 `StepSeq` 집합이 다르면 예외.
   - `LineRequired` 단계에 `LineId` 가 비었거나, 후보(활성 라인, 공정 일치)에 없는 라인이면 예외.
   - `LineRequired=false` 단계는 `LineId` 를 무시하고 NULL 로 저장.
   - 예외 시 아무것도 바뀌지 않는다.
2. 헤더 UPDATE: `Status='Released'`, `ReleasedAt`, `ReleasedBy`, `ModifiedTS/By`. `LineID` 는 건드리지 않는다.
   `WHERE Status IN ('Draft','Planned')`. 영향 행 0 이면 0 반환하고 롤백.
3. 기존 단계 행 DELETE 후 INSERT: `StepSeq, ProcessCode, LineID, StdCycleSec(BOP StdCycleTime, 공정 일치 첫 행), Status='Released', CompletedQty=0`.

`GenerateWoRouting` 의 첫 활성 라인 폴백은 삭제한다.
`PpRepository.ReleaseWo` 는 삭제한다.

### 예외 메시지

검증 실패는 `InvalidOperationException` 에 리소스 키가 아닌 단계 정보를 담는다
(예: `"Step 2 IMG: line required"`). 화면은 이를 그대로 토스트로 보여준다.
정상 경로에서는 다이얼로그가 먼저 막으므로 사용자가 이 메시지를 볼 일은 드물다.

## 3. Release 다이얼로그 (AMES.Web `WoReleaseLineDialog` 개편)

- 파라미터: `WoId`. 열릴 때 `PreviewRouting(WoId)` 호출.
- 단계 목록을 행으로 표시: 순번 · 공정 · 라인 선택 · 출처 배지.

| 상황 | 라인 컨트롤 | 배지 |
|---|---|---|
| `LineRequired` + `BopLineId` 있음 | 드롭다운(후보 라인), 기본 = BOP 라인 | `BOP` (기본값 그대로일 때) / `변경` (바꿨을 때) |
| `LineRequired` + `BopLineId` 없음 | 드롭다운, 기본 = 빈 선택 | `BOP 미등록` |
| `LineRequired=false` | 컨트롤 없음, 텍스트 `라인 없음 · 터미널 미배정` | 없음 |

- 필수 단계에 빈 선택이 하나라도 있으면 Release 버튼 비활성.
- 결과: `Result(IReadOnlyList<StepLineChoice> Steps)`.
- 단계가 0개(RoutingType NULL 등)면 안내 문구와 함께 Release 버튼 비활성. 선행 설계로 신규 WO 에는 없는 경우.
- PP-04 `WorkOrder.razor` 와 PP-07 `WoRelease.razor` 가 같은 다이얼로그·같은 Repository 메서드를 쓴다.
- 문자열은 `SharedResources.resx / .en.resx` 키로 추가한다
  (`PP.WoRelease.Dlg.Step`, `PP.WoRelease.Badge.Bop`, `PP.WoRelease.Badge.Changed`,
  `PP.WoRelease.Badge.NoBop`, `PP.WoRelease.NoLineProcess`, `PP.WoRelease.Err.NoSteps`).

## 4. Pop 조회·실적 (단계 기준)

### `WorkOrderDto` 확장 (AMES.Contracts)

추가 필드(모두 nullable): `RoutingLineId`, `StepSeq`, `ProcessCode`, `RouteLines`.

**필드 의미 규칙** — DTO 주석에 명시한다.

- **라인 범위 조회**(`ListForLine`, `GetActiveForTerminal`): `LineId`·`Status`·`CompletedQty` 에 **단계 값**을 채우고
  `RoutingLineId`·`StepSeq`·`ProcessCode` 를 채운다. Pop 은 이 DTO 로 단계 진행률을 그대로 표시한다.
- **헤더 조회**(Web 목록 등): `LineId` 는 빈 문자열, `Status`·`CompletedQty` 는 헤더 값,
  `RoutingLineId`·`StepSeq`·`ProcessCode` 는 null, `RouteLines` 는 단계 라인 문자열.

### 조회

- `ListForLine(lineId)`:
  `PP_WorkOrderRouting r JOIN PP_WorkOrder w` , `WHERE r.LineID=@LineID AND r.Status IN ('Released','In Progress')`.
  정렬은 기존과 동일(`In Progress` 우선 → Priority → DueDate → WoID). 단, 상태 기준은 단계 `Status`.
- `GetActiveForTerminal(lineId, terminalId)`: 단계 `Status='In Progress'` + 헤더 `TerminalLock` 조건.
  정렬 기준(`PR_WoAcceptance.AcceptedAt`)은 기존과 동일.
- `LineScheduleRepository.ListLineWos(lineId)`: 단계 테이블 조인으로 변경.
- `PpRepository.ListAllWo(lineId, …)`: 라인 필터를 `EXISTS (단계 행 LineID=@LineID)` 로 변경. `WoLite.LineId` 는 `RouteLines` 문자열로 대체.
- `OeeRepository.ListLines()`: `PP_WorkOrder.LineID` 대신 `PP_WorkOrderRouting.LineID` UNION.

### 접수

`AcceptWo(int routingLineId, string terminalId, string operatorId, string employeeNo, string checkResultsJson)`

- 단계: `Status='In Progress'`, `ActualStart=ISNULL(ActualStart, SYSDATETIME())`.
- 헤더: `Status='In Progress'`, `TerminalLock=@TerminalID`, `ActualStart=ISNULL(...)`.
- `PR_WoAcceptance` 는 기존대로 `WoID` 로 기록(단계 ID 컬럼 추가는 범위 밖).
- 호출측(INJ-MAIN `WoConfirmPopup`, IMG 화면)은 DTO 의 `RoutingLineId` 를 넘긴다.

### 실적 반영 — 단일 진입점

```
internal static decimal WorkOrderRepository.BumpStepCompleted(
    SqlConnection conn, SqlTransaction tx, int routingLineId, decimal qty, string actor)
```

한 트랜잭션 안에서(호출측 트랜잭션에 참여):

1. 단계 `CompletedQty += qty`, `ModifiedTS/By`.
   `CompletedQty >= 헤더 OrderQty` 가 되면 단계 `Status='Closed'`, `ActualEnd=SYSDATETIME()`.
2. 이 단계가 **라인이 있는 마지막 단계**면 헤더 `CompletedQty = 단계 CompletedQty` 로 동기화.
   단계가 방금 닫혔으면 헤더 `Status='Closed'`, `ActualEnd=SYSDATETIME()`.
3. 새 단계 `CompletedQty` 반환.

호출측:

- `InjLotRepository.ConfirmByLotCode`: WO 선택 쿼리를 `PP_WorkOrderRouting r WITH (UPDLOCK, ROWLOCK) JOIN PP_WorkOrder w`
  에 `r.LineID=@Line AND w.ItemNo=@Item AND r.Status IN ('Released','In Progress')` 로 바꾸고 `RoutingLineID` 를 얻는다.
  기존의 헤더 직접 UPDATE 블록은 `BumpStepCompleted(…, 1, …)` 호출로 교체.
  `PR_ProductionResult.WoID` 기록은 그대로.
- `ProductionRepository.RecordCycle(woId, itemNo, lineId, …)`: `(WoID, LineID)` 로 단계 행을 찾아 `BumpStepCompleted(…, goodQty, …)`.
  기존 헤더 UPDATE 블록 제거. 단계를 못 찾으면 예외(현재 IMG 화면은 `ListForLine` 으로 받은 WO 만 넘기므로 정상 경로에서는 발생하지 않는다).
- `WorkOrderRepository.AddCompletedQty` 는 호출처가 없으므로 삭제.

## 5. Web PP-04 화면 (`WorkOrder.razor`)

- LINE 컬럼: `RouteLines` 표시. 형식 `LINE-INJ-01 → LINE-IMG-01`, 라인 없는 단계는 `QC(—)`.
  Draft 는 단계 행이 없으므로 `—`. SQL 은 `STRING_AGG(ISNULL(r.LineID, r.ProcessCode + '(—)'), ' → ') WITHIN GROUP (ORDER BY r.StepSeq)`.
- 상세 펼침의 공정 칩: 진행률을 단계 수로 나눈 근사를 버리고 **단계 행의 실제 `Status`·`CompletedQty`** 로 표시.
  `Closed`→`done`, `In Progress`→`cur`, `Released`→`todo`. 칩 툴팁에 `라인 · 완료수량/지시수량`.
  Draft 는 템플릿 공정 코드만 `todo` 로 표시(현재 동작 유지).
- 이를 위해 `WorkOrderRepository.ListSteps(woId)` 신설(`RoutingLineId, StepSeq, ProcessCode, LineId, Status, CompletedQty`).
  펼칠 때만 호출한다(목록 조회에 N+1 을 넣지 않는다).
- PROGRESS 컬럼·KPI 카드·CSV 내보내기는 헤더 값 그대로. CSV 의 LINE 열도 `RouteLines`.

## 6. 마이그레이션 `dist/migrate_wo_step_line.sql` (멱등)

적용 순서: `migrate_routing_step.sql` 다음. README 빠른 시작 목록에 추가.

1. `PP_WorkOrderRouting.CompletedQty` 추가(없을 때만), 인덱스 2개 추가(없을 때만).
2. 기존 단계 행 `Status='Pending'` → 헤더 `Status` 가 `Released` 면 `Released`, `In Progress` 면 헤더 라인 단계만 `In Progress` 나머지 `Released`, `Closed` 면 전부 `Closed`.
   (2026-09-01 현재 0행이라 실질 영향 없음. 정본성 유지 목적.)
3. 백필: `Status IN ('Released','In Progress','Closed')` 이고 단계 행이 없는 WO.

| 구분 | 단계 생성 | 라인 | 단계 Status | 단계 CompletedQty |
|---|---|---|---|---|
| RoutingType 있음 | 템플릿 활성 단계 전부 | 헤더 `LineID` 의 공정과 같은 단계 = 헤더 라인 / 다른 공정 = 그 공정의 첫 활성 라인(LineID 정렬) / 활성 라인 없음 = NULL | 헤더 라인 단계 = 헤더 Status, 나머지 라인 있는 단계 = `Released`, Closed WO 는 전부 `Closed` | 헤더 라인 단계에 헤더 값 복사, 나머지 0 |
| RoutingType NULL | 단계 1개, `ProcessCode` = 헤더 라인의 WC 공정 | 헤더 라인 | 헤더 Status | 헤더 값 복사 |

   헤더 `LineID` 가 NULL 인 Released/In Progress WO 는 백필하지 않고 스크립트가 WoID 를 PRINT 한다(수동 처리).
   **헤더 `CompletedQty` 는 손대지 않는다.** 동기화는 다음 실적부터 시작된다.
4. `dist/AMES_Schema.sql` 정본에 컬럼·인덱스 반영.

백필 규칙은 이 스크립트에만 존재한다. 코드에는 폴백이 없다.

## 7. 테스트 (`AMES.InjAgent.Tests`, AMES_DEV 통합, DB 미기동 시 skip)

각 테스트는 자체 품목·BOP·WO 픽스처를 만들고 트랜잭션 롤백 또는 정리로 흔적을 남기지 않는다.

- `PreviewRouting`
  - BOP 스테이션이 있는 공정 → `BopLineId` 채워짐. 없는 공정 → null.
  - 활성 라인 없는 공정(QC) → `LineRequired=false`, `Candidates` 비어 있음.
- `ReleaseWo`
  - 필수 단계 라인 누락 → 예외, 헤더·단계 무변경.
  - 후보에 없는 라인 → 예외.
  - 정상 → 단계 행 `Released`, 헤더 `Released`, 헤더 `LineID` 변경 없음(NULL 유지).
  - `LineRequired=false` 단계는 `LineID NULL` 로 저장.
- `ListForLine`: A 품목 Release 후 INJ 라인·IMG 라인 양쪽에서 같은 WO 가 조회되고 각각 자기 단계 값을 가진다.
- `BumpStepCompleted`
  - INJ 단계 가산 → 헤더 `CompletedQty` 불변.
  - IMG(라인 있는 마지막 단계) 가산 → 헤더 동기화. `OrderQty` 도달 시 단계·헤더 `Closed`, `ActualEnd` 설정.
  - B 라우팅: PNT 단계가 마지막 라인 단계로 취급됨.
- `ConfirmByLotCode`: INJ 단계 `CompletedQty` +1, 헤더 불변.
- `RecordCycle`: IMG 단계 가산, 헤더 동기화.
- `AcceptWo`: 단계·헤더 `In Progress`, `TerminalLock`.
- 기존 `WorkOrderRepositoryTests` 의 Release 호출을 새 시그니처로 수정.

## 8. 영향 받는 파일

| 프로젝트 | 파일 | 변경 |
|---|---|---|
| Contracts | `Dto/WorkOrderDto.cs` | 필드 4개 추가, 의미 규칙 주석 |
| Data | `Repositories/WorkOrderRepository.cs` | `PreviewRouting`·`ListSteps` 신설, `ReleaseWo` 시그니처, `ListForLine`·`GetActiveForTerminal`·`AcceptWo` 단계 기준, `BumpStepCompleted` 신설, `GenerateWoRouting` 폴백 제거, `AddCompletedQty` 삭제 |
| Data | `Repositories/PpRepository.cs` | `ReleaseWo` 삭제, `ListAllWo`·`ListReleasable` `RouteLines` |
| Data | `Repositories/InjLotRepository.cs` | `ConfirmByLotCode` 단계 선택·`BumpStepCompleted` |
| Data | `Repositories/ProductionRepository.cs` | `RecordCycle` 단계 찾기·`BumpStepCompleted` |
| Data | `Repositories/LineScheduleRepository.cs`, `OeeRepository.cs` | 단계 테이블 기준 |
| Web | `Pages/Pp/WoReleaseLineDialog.razor` | 단계별 라인 다이얼로그로 개편 |
| Web | `Pages/Pp/WorkOrder.razor`, `WoRelease.razor` | 새 다이얼로그·Repository, LINE 컬럼, 상세 칩 |
| Web | `Resources/SharedResources.resx / .en.resx` | 키 추가 |
| Pop | `Pages/InjPopups/WoConfirmPopup.razor`, IMG 화면·Forms | `AcceptWo(routingLineId, …)` |
| dist | `migrate_wo_step_line.sql`, `AMES_Schema.sql`, README | 마이그레이션·정본·순서 |
| Tests | `AMES.InjAgent.Tests/WorkOrderRepositoryTests.cs` 외 | 7절 |

## 9. 범위 밖

- 후속 단계 수량을 앞 단계 산출량으로 제한하는 정합성.
- QC·FG 실적을 WO 단계에 붙이는 것. `PR_WoAcceptance` 에 단계 ID 추가.
- `MD_RoutingStep` 에 라인 필수 플래그 추가.
- `MD_Bop` 마스터 정비(STD 사이클타임 등).
- PP-03 일괄 생성·PP-04 수동 생성 시점의 라인 배정(Release 시점에만 배정한다).
- 헤더 `PP_WorkOrder.LineID` 컬럼 제거.

## 검토했지만 뺀 대안

- **공정별 자식 WO(ParentWoID)**: Pop 무변경이 장점이지만 WO 번호가 공정 수만큼 늘고, 단계 간 수량 정합을 따로 관리해야 하며, `PP_WorkOrderRouting` 이 죽은 테이블이 된다.
- **헤더 `LineID` 에 BOP 1단계 라인 자동 채움**: 다중 라인 문제에 답이 없다.
- **후속 단계를 앞 단계 첫 실적 후 개방**: 실적 UPDATE 마다 상태 전이가 끼어들어 복잡해지고, 사출→래핑 파이프라인 흐름에는 동시 개방이 맞다.
- **폴백(첫 활성 라인) 유지**: IMG 라인이 늘어나면 BOP 누락 시 조용히 엉뚱한 라인에 배정된다.
