# POP 오프라인·시리얼 스캐너·LOT 불량·라벨 발행·WO 공정 단계·안돈

> `CLAUDE.md` 에서 분리한 상세 문서입니다(원문 그대로, 11-18 토큰 절감 정리). 필요한 주제만 `grep -rn 키워드 docs/claude/` 로 찾아 해당 줄 범위만 읽으세요.

## POP 오프라인 동작 (10-23, 사용자 결정 — "생산이 불가능하면 안 된다")

DB 가 끊겨도 INJ·IMG·PNT 는 로그인·스캔 확정·불량 등록·수동 라벨 발행을 계속하고, 복구되면 서버가 다시 판정한다. 원칙은 **일단 받아들이고 복구 뒤 서버가 검증**하며, 로컬 판정은 온라인과 같은 순수 규칙(`LotDefectRules`·`PalletLoadRules`·`ProdCalendar`)을 쓴다.

- **연결 상태**: `Common/PopDb` 가 프로그램 시작 때부터 화면과 무관하게(`PopDb.Start`, `PopBlazorForm`) 검사 주기 `PopTerminal:Db:ProbeIntervalSeconds`(기본 3초)마다 대기 `ProbeTimeoutSeconds`(기본 3초)짜리 전용 연결로 `SELECT 1` 을 실행해 `DbHealth` 를 갱신한다(복구 확인 겸용). **상태가 바뀔 때마다 `db-health.bin` 에 남기고 다음 실행은 지난번이 끊김이면 바로 오프라인으로 시작한다**(10-29 — 없으면 DB 가 끊긴 채 켰을 때 로그인 화면 첫 조회가 업무 연결 대기만큼(실측 11초) 비었다; DB 가 살아 있으면 첫 검사가 0.3초 안에 되돌린다). 로그인 화면 첫 조회와 로그인은 먼저 `PopDb.Verify` 로 확인해 끊겼으면 접속 대기 없이 오프라인 경로로 간다. **검사 주기·대기와 동기화 주기(`Offline:RefreshMinutes`)는 POP 을 다시 켜지 않아도 바뀐다** — `AppConfig` 가 설정 파일 변경을 감지해(`reloadOnChange`) 이 세 값만 매번 다시 읽고, 다시 읽기가 실패하면(저장 도중·오타) POP 을 멈추지 않고 기본값(3초·3초·30분)으로 돈다. 나머지 설정은 시작 때 한 번. 업무 연결의 `Connect Timeout` 은 설정 `PopTerminal:Db:ConnectTimeoutSeconds`(0 = 접속문자열 그대로). 프로세스 첫 연결 실패는 설정과 무관하게 24~26초 걸린다.
- **저장**: `%LocalAppData%\AMES\Pop`(설정 `PopTerminal:Offline:Dir` 로 변경) 의 DPAPI(현재 사용자) 암호화 파일 — `auth-cache.bin`(직원·PIN 해시·라인·스테이션) · `auth-journal.bin`(오프라인 로그인 기록) · `work-state.bin`(작업 사본) · `work-ops.bin`(미반영 작업 + 충돌 목록). 코드 `Services/OfflineStore·OfflineAuth·OfflineWork`.
- **받기**: 직원·라인 사본은 로그인 화면·로그아웃·동기화 주기 `PopTerminal:Offline:RefreshMinutes`(기본 30분, 작업 사본과 같은 값 — 10-29 전에는 12시간 고정)마다(로그인 화면은 사본이 있어도 **DB 가 정상이면 라인·스테이션을 DB 에서 읽고**, 끊겼거나 연결 오류면 사본 — 스테이션은 감시와 같은 3초 대기 연결 `PopDb.QuickMaster` 로 읽고 로그인 직전에도 활성인지 다시 확인한다, 10-29 LOGIN-TC-029), 작업 사본은 라인·스테이션 선택·로그인 중 `RefreshMinutes`(기본 30분, 10-29 전에는 10분)·로그아웃 때(앞 `PastDays`~뒤 `FutureDays` 일 현황, LOT, 열린 WO, 불량코드, 팔레트, LOT 번호 구간). 미반영 큐가 있으면 사본을 덮지 않는다. 사본은 `CacheMaxAgeDays` 가 지나면 쓰지 않는다. 설정은 `PopTerminal:Offline:*`(`PastDays`·`FutureDays`·`CacheMaxAgeDays`·`RefreshMinutes`·`LotBlockSize`·`TomorrowBlockFromHour`·`NotFoundRetryMinutes`).
- **오프라인 LOT 번호 구간 방식 A**(10-27 김현진 결정): 온라인일 때 `LotNoGenerator.Reserve`(`SYS_LotSeq` 를 구간만큼 올림)로 헤더(년월일+라인)마다 구간을 받아 두고, 오프라인 번호는 그 구간에서만 받는다. POP `OfflineWork` 는 `LotBlocks`(200개, 남은 50 아래에서 갱신 — `RefreshState`), InjAgent `QueuingInjAgentStore` 는 `LotBlock`(500개, 남은 20 아래에서 `Sync` 가 갱신). 두 쪽 구간은 DB 가 따로 떼어 주므로 겹치지 않고 복구 순서와 무관하다. 구간 밖 번호는 없으므로 9999·구간 소진은 `OfflineLotNumbersExhaustedException`(POP)·`InvalidOperationException`(InjAgent)으로 막힌다. 결번은 생길 수 있다. 복구 때 POP replay 는 `LotNoGenerator.EnsureAtLeast` 로 `SYS_LotSeq` 를 끌어올린다(INJ·IMG·PNT). 테스트·시나리오: `tools/test_lot_flow/시나리오.md`, 생산계획 테스트 도구 `src/07_Etc/AMES.TestHarness`(로컬 DB 전용).
- **InjAgent 오프라인 큐**(10-27): DB 연결 실패(`DbHealth.IsConnectionFailure`)면 샷 LOT 생성·검사 저장·NG 차단·조건 이력을 `%LocalAppData%\AMES\InjAgent\offline.bin`(DPAPI 암호화 JSON, `OfflineStateFile`)의 큐에 쌓는다. 장애 뒤 10초는 DB 를 부르지 않고(연결 대기 방지), `Program.cs` 의 5초 주기 `Sync` 가 밀린 작업을 **순서대로** 넣는다(LOT 은 `FindLotId` 로 이미 있는지 보고 `CreateRawLotAt` 로 오프라인 번호·샷 시각 그대로). 오프라인 LOT 의 임시 LotID(음수)는 `TempMap` 으로 실제 번호에 잇는다. 금형·조건 마스터는 마지막으로 읽은 캐시를 쓴다. 정본 테스트 `QueuingInjAgentStoreTests`(가짜 DB), DB `InjLotReplayDbTests`(`AMES_TEST_CONN` 로컬). InjAgent 는 김현진 담당이며 DB 가 죽은 동안 PLC 샷 감지는 계속된다.
- **복구**(5초 틱, `OfflineAuth.Tick` → `OfflineWork.Tick`): 인증 기록을 먼저 올려 임시 세션(음수 ID)을 실제 세션으로 바꾼 뒤, 작업 큐를 시간순으로 같은 저장소 메서드(`occurredAt` 인자)에 반영해 전기일·교대·불량일이 오프라인에서 일한 시각이 된다. 「LOT 없음」·코어 미확정은 `NotFoundRetryMinutes` 동안 재시도, 이미 확정·재작업은 무해, 서버가 거부하면 충돌 목록(상단 「⚠ 오프라인 작업 충돌 N건」). 왼쪽 표는 사본 현황 + 큐의 효과이며 온라인과 같이 LOT 생성일 기준이다.
- **안돈·재작업·QC·WO 목록도 오프라인으로 쓴다**(10-28 사용자 결정): 화면 조회는 온라인일 때 읽은 결과를 키별로 남겨 둔 사본(`Services/OfflineReads`, `read-cache.bin`)이고, 아직 열지 않은 화면도 보이게 RWK·QC 로그인 중에는 `RefreshMinutes` 마다 미리 읽는다(INJ·IMG·PNT 는 작업 사본을 받을 때 안돈 코드·라인 슈퍼바이저·진행 중 안돈도 함께). 쓰기는 같은 작업 큐(`work-ops.bin`, `WorkOp.Ref`·`Args`)에 올라가 같은 순서·같은 충돌 목록으로 반영된다. ① **안돈**(`Services/OfflineAndon` + `OfflineAwareAndonStore` — 워크플로 `AndonWorkflow` 는 그대로): 진행 중 안돈 사본 위에 큐의 안돈 작업을 차례로 얹은 상태를 보이고, 오프라인 발동은 임시(음수) 번호·오프라인 부서 호출은 임시 행 ID 로 시작해 반영 때 실제 번호로 바뀐다(부서 행은 안돈 + 부서 코드로 찾는다 `SP_POP_ANDON_FindDeptCall`). 슈퍼바이저 배지는 사본 명단(`SP_POP_ANDON_ListLineSupervisors`)으로 확인하고 명단을 받은 적이 없으면 받아들인다, 이름은 오프라인 로그인 사본. 그 라인에 반영 못 한 안돈 작업이 남아 있으면 온라인이어도 로컬 상태를 보인다. 오프라인 동안 설비 상태(STOP/RUN) 기록은 남기지 않는다 ② **재작업**(`Services/OfflineRework`): 대기열·오늘 판정·칩·원인은 사본, 수리·폐기는 큐(대기열에서 빠지고 오늘 판정에 바로 보임) — 다른 터미널이 오프라인 동안 등록한 불량은 사본에 없어 판정할 수 없다 ③ **QC**(`Services/OfflineQc`): QC-01~03 검사 시작(스캔 값 그대로 — 대상 확인은 복구 때 서버, 없으면 충돌)·판정, QC-04 NCR 등록, QC-05 홀드 해제는 큐, 오프라인에서 만든 검사·NCR 은 `OFFLINE n` 으로 보인다. QC-06·07·08 은 사본 조회만, QC-TRC 는 이 PC 가 온라인으로 본 LOT 만 ④ **WO 목록 팝업**: 작업 사본의 열린 WO. 반영은 같은 저장소 메서드에 발생 시각을 넘긴다 — 안돈·재작업 폐기·QC 프로시저의 선택 매개변수 `@At`(없으면 서버 시각, 구 바이너리는 그대로), 재작업 수리 양품의 전기일·교대도 그 시각. **배포**: `migrate_pop_stored_procedures.sql` 을 신 Pop 보다 먼저(`@At`·새 프로시저 2개). 정본 테스트 `OfflineModulesTests`(DB 없이)·`OfflineModulesReplayDbTests`(`AMES_TEST_CONN` — 안돈·QC 를 원래 시각으로 반영).
- **범위 밖**: InjAgent(DB 가 끊기면 디스크 큐 — 위 InjAgent 오프라인 큐), 자동 라벨 발행(LOT 은 InjAgent 가 DB 에 만든다 — 오프라인에서는 수동 발행만). 복구 때 서버가 거부한 작업의 처리 정책은 `Claude_확인사항.md` 김현진 보류 「오프라인 작업 충돌 처리 정책」. 정본 테스트 `AMES.Pop.Tests/Offline*`·`AMES.Data.Tests/Offline*`(DB 테스트는 `AMES_TEST_CONN` 또는 개발 DB 가 닿을 때).

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
- **Pop 로그아웃·재로그인은 라인 워터마크를 이어 쓴다**(10-26, `LabelWatermarkStore`): 로그아웃 때 저장한 최대 LotID 를 다음 로그인이 잇는다. 로그아웃 동안 생긴 LOT 은 재로그인 뒤 발행된다. 로그아웃 직전에 미출력이었던 LOT 은 워터마크 아래라 재출력 버튼으로 복구한다. 교대 인수인계 시 유의.
- **자동 발행은 카운트 선행이다**(10-26, `LabelDispatcher`): 출력 전에 `SP_POP_INJ_IncrementPrintedCount` 로 카운트를 올리고, 출력이 실패하면 `SP_POP_INJ_DecrementPrintedCount` 로 되돌린 뒤 선점을 반납한다. 카운트를 못 올리면 출력하지 않는다. 이 PC 가 출력한 LOT 은 발행 원장(`LabelPrintLedger`, `label-ledger.bin`, 30일)에 남아 다시 뽑지 않는다. 출력 기록은 `print-YYYYMMDD.log` 에 출력 모드와 함께 남아 모드와 무관하게 확인할 수 있다.

장애 추적은 `{PopTerminal:Printer:OutputDir}/dispatch-YYYYMMDD.log` — 무인 루프라 토스트로 알릴 수 없는 실패가 여기에만 남는다. `OutputDir` 를 비우면(기본) 실행 파일 옆 `labels` 폴더를 쓰며(작업 폴더와 무관), 상대 경로는 실행 파일 폴더 기준, 절대 경로·`%VAR%` 가능, 폴더는 자동 생성된다(`AppConfig.ResolveOutputDir`, 테스트 `AppConfigOutputDirTests`).

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
- **연동 테이블**: 발동 시 `PP_LineDowntimeLog`(ReasonCode `ANDON`, `AndonID`) 1행이 열리고(10-27 부터 같은 라인에 이미 열린 비가동이 있으면 열지 않는다 — 겹친 시간을 두 번 세지 않게, 그 행은 주인이 닫는다) 종료 시 `EndTS`·`DurationMin` 이 닫힌다(자체 해결 포함). 보전(`ANDON_DEPT` 코드 `MAINT`, `AndonDeptCodes.Maint`)이 호출되면 `MNT_FailureRegister` 1행(`Source='ANDON'`, `AndonRefID`=안돈 ID, `FailureNumber`=`FAIL-yyMM-NNN`, `Severity`=선택값, `FailureType`=원인 코드)과 **보전 작업지시(CM, 출처 ANDON·SourceRefID 고장번호·우선순위 = 심각도·ISSUED, `MWO-yyMM-nnn` 카운터 채번 — 웹 `IssueFailureWorkOrder` 와 같은 값, 10-28)** 가 같은 트랜잭션에서 생기고(안돈 종료 때 그 작업지시가 끝나지 않았으면 결과 OK 로 같이 완료) 도착·ACK 가 `MNT_FailureAction`(ARRIVED/ACK) 으로, 안돈 종료가 `Status='RESOLVED'` 로 이어진다(MNT-007 「종료」로 이미 CLOSED 인 고장은 덮지 않는다 — 10-28; `FailureType` 은 원인 코드라 varchar(20), `dist/migrate_mnt_failure_type_widen.sql`; 10-06 부터 Web MNT-002 상세 모달 "대응 이력"이 발생 → 보전 도착 → ACK → 수리 완료(REPAIRED)/해결을 시각·담당자·발생 후 분으로, MNT-009 실시간 고장 알림이 행마다 대응 단계(대기·도착·조치 중·해결)와 경과/해결 소요를 보이며 행을 누르면 `mnt/failure?id=` 로 그 상세가 열린다. MNT-007 작업지시 상세도 연결된 고장(`MNT_FailureRegister.WorkOrderID`, `ListFailuresForWo`)마다 같은 이력을 보이며 고장 번호가 MNT-002 상세 링크다 — 표시는 공용 `Pages/Mnt/FailureTimelinePanel.razor`, 조회 `MntRepository.ListFailureActions`, 규칙 정본 `AMES.Data.Services.FailureTimeline`·`FailureTimelineTests`. 안돈 종료는 조치 행 없이 등록 행만 닫으므로 REPAIRED 가 없는 해결은 등록 행 `ResolvedAt` 으로 보인다). 품질·자재 호출은 고장을 만들지 않는다. **고장 상태·해결일시는 등록·수정 창에서 바꾸지 않는다**(10-10 사용자 결정) — 등록은 항상 `OPEN` + CM 작업지시 발행, 「진행 중」은 MNT-007 착수, 해결은 MNT-002 「수리 완료」·MNT-007 「완료」·안돈 종료로만(원인·조치·작업시간과 함께 고장·WO 가 같이 닫힌다), 수리 없이 닫는 것은 MNT-007 「종료」(고장 CLOSED, 아래 MNT 상태 규칙); 수정 창은 둘을 읽기 전용으로 보이고 `MntRepository.InsertFailure/UpdateFailure` 도 상태·해결일시를 받지 않는다(정본 테스트 `FailureStatusFlowTests`). 이미 끝난 고장을 뒤늦게 입력할 때는 등록 창의 「등록 후 바로 수리 완료」(승인 권한)로 저장 직후 수리 완료 창이 이어서 열린다. 예전에 어긋난 데이터 정리는 `dist/fix_mnt_failure_wo_status.sql`(`-v Mode=ROLLBACK|COMMIT`, 10-10 개발·로컬 적용 — 미해결+WO 완료 1건). Web PP-DTL(`Pp/Downtime.razor`)은 이 안돈 행(`AndonID` 있음, 출처 "POP")을 **원인·비고만** 고치게 하고(`PpRepository.UpdateDowntimeCauseComment` — 시각·사유는 안돈 기록과 묶여 잠근다), 웹 등록(`InsertDowntime`, 종료 비우면 진행 중)·웹 행 수정(`UpdateDowntimeEntry`, 라인 고정, `AndonID IS NULL` 행만)은 같은 라인 기존 비가동(진행 중 포함)과 겹치면 거부한다(OEE 이중 계상 방지, 규칙 정본 `AMES.Data.Services.DowntimeEntryRules`·`DowntimeEntryRulesTests` — 미래 시각 거부). 사유는 공통코드 **`DOWNTIME_REASON`**(10-29 `dist/migrate_pp_downtime_reason_code.sql` — MACHINE·CHANGEOVER·MATERIAL·QUALITY·MANPOWER·PLANNED·OTHER·ANDON, 웹 콤보는 ANDON 제외·사용 중 코드만, 이력에만 있는 옛 값은 필터와 그 행 수정 때만 보임 — 전에는 이력 DISTINCT 라 이력이 없는 DB 에서 웹 등록이 안 됐다), 작업지시 열은 WO 번호, 원인은 MD-025 사유 코드(`MD_ReasonCode`, `ReasonType='DOWNTIME'`) 콤보이며 `RequiresComment` 원인은 비고 필수다(10-05). PP-DTL·PP-ODM 은 **재작업 라인(작업장 공정 RWK)을 보이지 않는다**(10-26 사용자 요청 — 라인 콤보·모니터 카드에서 빼고 비가동 조회 SQL 도 `PpRepository.NotReworkLine` 으로 제외, 판정 `MasterDataRepository.ReworkLineIds`; 라인 상태 분류 작업과 같은 기준). PP-ODM(`DowntimeMonitor`)은 **조회 전용**이다(10-08 사용자 결정 — 사유 수정 모달·`PpRepository.UpdateDowntimeReason` 삭제, 안내 문구와 PP-DTL 링크만). 비가동 사유·원인·비고 수정은 PP-DTL 에서만 한다. `MNT_FailureRegister.Severity` 컬럼은 `dev` 의 `migrate_mnt_failure_severity.sql` 이 만든다 — 그 마이그레이션 없는 DB 에서는 보전 호출이 예외 토스트로 실패한다.

---
