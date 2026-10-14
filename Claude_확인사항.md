# Claude 확인사항 — 담당자별 미해결·보류·문제점

> **이 파일이 미해결·보류·문제점·다음 개발 항목의 유일한 정본입니다**(10-14 사용자 결정). CLAUDE.md·다른 문서·작업 메모에는 목록을 두지 않습니다. 항목을 해결하면 같은 커밋에서 여기 항목을 지우고, 새로 찾은 문제는 담당자 절에 더합니다.

- **작성**: 2026-10-14, Claude Code(이승욱 세션)
- **기준 소스**: `dev` 브랜치. 9b564a28 전체 점검 결과에, 이 파일과 함께 올린 커밋(MNT 부분 완료 결함 2건 수정)을 반영했습니다.
- **점검 방법**
  - 5영역(POP·InjAgent·QC·PNT / PP·APS·MRP / Api·WH·FG·SCM·Portal / SYS·MD·MNT·RPT / DB 스크립트·스키마·공통 규칙)을 소스로 다시 확인했습니다.
  - 테이블 175개의 읽기·쓰기 사용처를 전수 조사했습니다. 테스트 코드는 빼고, 실제로 호출되는 DB 프로시저는 포함했습니다.
- **줄 번호**는 위 기준 소스 기준입니다. **행 수**는 개발 DB(192.168.0.132) 기준입니다.
- **심각도**
  - **상**: 데이터 손실·현장 정지·보안
  - **중**: 잘못된 집계·동시성·권한
  - **하**: 표시·정리·드문 경우
- **분류**
  - **미해결**: 이미 알려졌고 방향이 정해진 결함
  - **보류**: 결정·일정 대기
  - **테이블**: 읽기만 있거나 아무도 쓰지 않는 테이블(보류 성격 — 만들지·지울지 결정)
  - **문제점**: 점검에서 새로 찾은 결함
- 담당 범위(10-10 지정): 심재현 = POP(QC 포함)·InjAgent·Devices + Web PP-001~006·PP-APS / 안영훈 = PDA·Api + Web WH·FG·SCM·Portal + 재구축 스크립트 / 이승욱 = 나머지 Web(SYS·MD·MNT·RPT·PP-CAL·LSB·OEE·DTL·ODM·계정·공통) + 공통 마이그레이션·설정 / 오세영 = 알림(PUSH) 앱·발송.
- **결함으로 보지 않는 것(사용자 결정)**: FK(외래키)를 두지 않는 것, 열린 WO 없이 생산 허용(WoID NULL), 배지 EOS QR 자동 등록, 현장 작업자 PIN 잠금 없음, 쓰지 않는 SYS_Config 키, 2FA 미사용, 공장 하나(SYS-005 공장 구분 없음), 스페인어 화면의 공통코드 이름 영어 표시, 예비품 수량 조정은 PDA 에서만, 개발서버 배포는 zip 이 아닌 폴더, 공휴일은 SQL 로 관리(`SYS_PublicHoliday`).

---

## 0. 요약

| 담당 | 미해결 | 보류 | 테이블 | 문제점 | 가장 먼저 볼 것 |
|---|---|---|---|---|---|
| 심재현 | 20 | 7 | 7묶음 | 31 | [상] WO 번호 인덱스 없음 → POP 확정 지연 · [상] APS WO 생성 전체 실패 · [상] POP·InjAgent 배포 설정이 로컬 DB |
| 안영훈 | 2 | 6 | 3 | 29 | [상] PDA 테스트 계정 PIN 0000 · [상] 배지 로그인 API 가 PIN 없이 토큰 발급 · 재구축 스크립트 세 곳에서 중단 |
| 이승욱 | 0 | 6 | 6 | 6 | PP-OEE 가동률이 시연 데이터로 계산됨 · 부분 완료한 원래 작업지시에서 고장 연결이 안 보임 |
| 오세영 | 5 | 2 | — | — | 발송기가 없어 SYS-008 발송 이력이 시연 데이터 |

### 지금 결정이 필요한 것(이승욱 작업분)
1. **개발서버 반영**: 개발 DB 에는 PP-LSB 발행 폐지 마이그레이션(`dist/migrate_lsb_publish_removal.sql`)이 이미 적용돼 있습니다. 새 Web·Api 를 올리기 전까지 개발서버의 PP-LSB 는 열리지 않습니다(구 Web 이 삭제된 `PublishedAt` 컬럼을 읽음). 최신 패키지는 `publish\AMES.Web_20261014-054436.zip`·`publish\AMES.Api_20261014-054545.zip` 입니다.
2. **PP-OEE 가동률 원천**: 지금은 시연 데이터(`PP_LineStateLog`)로 계산됩니다. 실제로 쌓이는 비가동 기록(`PP_LineDowntimeLog`)으로 바꿀지 정해야 합니다(이승욱 테이블 2).
3. **개발 DB 테스트 행**: 테스트가 남긴 `PP_LineSchedule` PM 행 120개(`EntryType='PM' AND CreatedBy='ITEST'`, 지금 DRAFT)를 지울지.
4. **화면 저장 검증**: 10-14 감사 로그(MNT·PP-OEE)·MNT 부분 완료·PP-CAL 거부를 화면에서 실제로 저장해 보지는 않았습니다(DB 테스트로만 확인).
5. **문서 표기**: CLAUDE.md 의 "SQL Server 2022"(실제는 개발 2019 RTM 15.0.2000.5, 로컬 2025 17.0.1135.8), "176개 테이블"(실제 175) 등(이승욱 문제점 5).

---

## 1. 심재현 — POP(QC 포함)·InjAgent·Devices + Web PP-001~006·PP-APS

### 1-1. 미해결 (20)

1. **[상] POP·InjAgent 배포 설정이 로컬 DB 를 가리킴**
   - 위치: Pop `appsettings.Production.json:3`, `AppConfig.cs:48·84-85` / InjAgent `appsettings.json:3`, `AgentConfig.cs:56-57`
   - 내용
     - POP 배포 설정이 비상용 `localhost\MSSQLSERVER01` 이고, 기본 설정(개발서버)을 Production 파일이 덮어씁니다. 그래서 현장 터미널이 비상용 DB 에 쓰거나 접속에 실패합니다.
     - InjAgent 는 `sa@localhost` 에 Connect Timeout 이 없고 재시도도 없어, 샷 LOT 이 유실될 수 있습니다.
     - 둘 다 환경변수 오버라이드(`AddEnvironmentVariables`)를 읽지 않습니다.
   - 제안: 배포 설정을 개발서버 문자열로 바꾸고 환경변수 오버라이드를 추가합니다. InjAgent 는 전용 계정과 `Connect Timeout=30` 을 씁니다.
2. **[중] 스캔 확정 뒤에 온 로봇 NG 판정이 버려짐**
   - 위치: `InjLotRepository.cs:614-620`(MarkNgBlocked)
   - 내용: 로봇 검사 결과가 작업자 스캔 확정보다 늦게 오면, LOT 이 RAW 가 아니라는 이유로 NG 표시를 무시합니다. 불량품이 양품으로 집계됩니다.
   - 제안: 확정된 LOT 에 늦은 NG 가 오면 불량 등록(역분개) 경로로 넘기거나, 최소한 경고를 남깁니다.
3. **[중] DB 가 끊기면 POP 화면이 최대 30초 멈춤**
   - 위치: `InjMain.razor:272`, `ImgMain.razor:276`
   - 내용: 5초 타이머가 화면 스레드 안에서 동기 조회를 7~8번 합니다. 접속 대기 30초 동안 화면과 스캔 입력이 모두 멈춥니다.
   - 제안: 조회를 백그라운드로 돌리고, 실패하면 갱신 실패 표시만 합니다.
4. **[중] APS 등록 계획 단위 불일치**
   - 위치: `ApsRepository.cs:324-326·409-414·733·738`, `PpRepository.Aps.cs:960`
   - 내용: BOM 규칙 WO 의 사출 슬롯을 부모 품번으로 읽고, 이미 자식 단위인 수량에 QtyPer 를 한 번 더 곱합니다. 잠긴 칸·부하가 부풀고, WO 생성 때 기존 슬롯 차감도 자식 슬롯을 놓칩니다(문제점 7 과 같은 원인).
   - 제안: 슬롯 품번을 단계 품번 기준으로 읽고 재곱을 없앱니다.
5. **[중] 수동 라벨이 두 번 생성됨**
   - 위치: `InjMain.razor:478-494·439`, `ImgMain.razor:363-374·491`
   - 내용: 처리 중 표시가 동기 처리라 화면에 반영되기 전에 두 번째 터치가 쌓여, LOT 2개·라벨 2장이 나옵니다. 재출력 버튼에는 가드가 없습니다.
   - 제안: 비동기 처리로 바꾸고 처리 중 버튼을 끕니다. 재출력에도 같은 가드를 둡니다.
6. **[중] 안돈이 동시에 2건 발동됨**
   - 위치: `AndonWorkflow.cs:91-92`, `AndonRepository.cs:21-40`
   - 내용: 같은 라인의 두 터미널이 동시에 안돈을 누르면, 확인과 INSERT 사이에 잠금이 없어 2건이 생깁니다.
   - 제안: 라인 단위로 잠근 뒤 확인하고 INSERT 합니다.
7. **[중] InjAgent 수집 오류 묶음**
   - 위치: `MachinePoller.cs:76-77·117-157·160-175·202-221`, `FEnetClient.cs:140-149`
   - 내용
     - 카운터 리셋이나 금형 변경만으로 LOT 이 생깁니다.
     - 샷이 2 이상 건너뛰면 1건만 생깁니다.
     - 로봇 쓰기 실패를 무시해 판정이 다른 LOT 에 붙습니다.
     - 검사 이력이 재시도 때 중복됩니다.
     - FEnet 응답을 한 번만 받아 잘린 응답을 그대로 씁니다.
   - 제안: 샷 증가분만큼 LOT 을 만들고, 쓰기 실패는 재시도하며, 응답 길이만큼 반복해서 받습니다.
8. **[중] 재작업이 취소된 WO 에 실적을 귀속**
   - 위치: `ReworkRepository.cs:193`, `WorkOrderRepository.cs:452·457-466`
   - 내용: 수리한 양품이 취소 WO 로 들어갑니다. 마지막 단계면 Cancelled 가 Closed 로 바뀌고 완료 수량도 바뀝니다.
   - 제안: 취소 WO 면 열린 WO 를 다시 찾고, 없으면 WO 없이 확정합니다.
9. **[중] QC 홀드 해제 조건 없음·QC 번호 중복**
   - 위치: `QcRepository.cs:220-225·127·173`
   - 내용: 이미 해제된 홀드도 다시 해제됩니다. 검사·NCR 번호가 분 단위(yyMMddHHmm)라 같은 분에 두 건이면 번호가 겹치고, 유니크 제약도 없습니다.
   - 제안: 상태 조건을 추가합니다. 번호는 범위 잠금으로 채번합니다(MNT `ClaimNumber` 방식).
10. **[중] inj_flag 마이그레이션이 컬럼을 데이터째 지움**
    - 위치: `dist/migrate_md_item_inj_flag.sql:63-111`
    - 내용: 테이블 재생성 목록에 BoxQty·ScanRequired 가 없고, 기본값 제약·인덱스도 되살리지 않습니다. 구성이 다르면 멈추는 가드도 없어, 다시 돌리면 두 컬럼이 사라집니다.
    - 제안: 재생성 목록을 보강하고, `migrate_aps.sql` 처럼 컬럼 대조 가드를 둡니다.
11. **[하] PIN 키패드 기본 사번 E001**
    - 위치: `Login.razor:401`
    - 내용: 사원을 고르지 않고 PIN 을 치면 E001 로 시도해 E001 실패 횟수가 쌓입니다.
    - 제안: 사원을 고르기 전에는 입력을 막습니다.
12. **[하] POP 로그인 화면 예외 처리**
    - 위치: `Login.razor:463·283-284·298`
    - 내용: DB 장애 때 예외를 다시 던지고, 배지 등록·초기 목록 조회가 try 밖이라 로그인 화면 자체가 깨집니다.
13. **[하] 로그·라벨 폴더가 실행 위치 기준**
    - 위치: `AppConfig.cs:57`, `PopLog.cs:15`
    - 내용: 바로가기의 작업 폴더에 따라 파일 위치가 달라집니다.
    - 제안: 실행 파일 폴더 기준으로 바꿉니다.
14. **[하] 라벨 선점 순서·출력 후 카운트 실패 시 중복**
    - 위치: `InjLotRepository.cs:404`, `InjMain.razor:443-454`, `ImgMain.razor:381-388·495`
    - 내용: 선점 UPDATE 에 순서가 없습니다. 출력은 됐는데 카운트 갱신이 실패하면 "실패" 로 안내해, 작업자가 다시 출력합니다.
15. **[하] 로그아웃 뒤 확인창이 남음**
    - 위치: `ConfirmService.cs:15-30`, `AppState.cs:46-57`
16. **[하] 안돈 기록 오류 묶음**
    - 위치: `AndonPopup.razor:246`, `AndonRepository.cs:316·411-413·52-53`
    - 내용
      - 기존 안돈을 복원만 해도 설비 STOP 기록이 중복됩니다.
      - 원인코드가 15자로 잘립니다.
      - 종료 때 CLOSED 를 RESOLVED 로 덮습니다.
      - 알림 수신자·채널이 고정값입니다(오세영 PUSH 와 연계).
17. **[하] PNT 단위 EA 고정·불량코드 버림**
    - 위치: Pnt01·02·03·06·07·08·09 화면과 도움말, `PntRepository.cs:421-436`
18. **[하] 마이그레이션 FK 6개·그룹 이름 옛 접두어**
    - 위치: `migrate_aps.sql:242·243·268·325·353`, `migrate_aps_plan_line_shift.sql:21`, `migrate_demand_plan.sql:69·92-98`
    - 내용: FK 를 두지 않는 원칙과 달리 FK 6개를 새로 만듭니다. DPSYNC 공통코드 그룹 이름에 폐지된 `ScheduledWorker · ` 접두어가 있습니다.
19. **[하] 테스트 코드에 접속 비밀번호·외부 IP**
    - 위치: `AmesDevDb.cs:11` 외 `AMES.Data.Tests` 8파일(`MoldPlanningTests:20`, `PlanScheduleTests:21` 은 98.95.142.192 이고 Connect Timeout 도 없음)
    - 제안: 모두 `AMES_TEST_CONN` 하나로 바꿉니다.
20. **[하] POP PLAN 기준 결정**
    - 위치: `InjLotRepository.cs:750-758`, `ImgLotRepository.cs:537-546`
    - 내용: PP-LSB 「발행」이 폐지돼(10-14), 「적용」만 해도 현장 PLAN 에 바로 반영됩니다. 어떤 기준으로 할지 POP 담당이 정합니다.

### 1-2. 보류 (7)
- **APS 금형 교체(MC) 한계**(CLAUDE.md APS 「알려진 한계」에서 옮김): "직전 금형"을 그 라인·그 날 처리 순서상 마지막 금형으로 보므로, 하루 안에 금형이 여러 번 바뀌면 그 사이 되돌림 MC 를 만들지 않습니다(같은 라인·같은 날 금형 그룹당 MC 최대 1개). 교체 자리가 없을 때 억지로 둔 MC 는 휴게·점유 구간과 겹칠 수 있습니다. 조정은 PP-LSB 에서 합니다.
- **B2 IMG 라벨 교대 글자가 항상 C**(`LabelPrinter.cs:27-32`): DAY/NIGHT 만 A/B 로 바꾸는데, 실제 교대 코드는 A/B/C 라 항상 C 가 찍힙니다. 교대 코드를 그대로 쓸지 답을 기다립니다.
- **B11 POP·InjAgent 시각을 DB 서버 기준으로**(AMES.Pop 30개 파일 78곳): DbClock 을 맞추지 않아 PC 시계를 씁니다. 결정은 됐고 아직 구현하지 않았습니다.
- **B15 PNT-09 교대 하드코딩**(`PntRepository.cs:443·451`): 8–15시/16–23시로 박혀 있어 공통코드 교대 시각과 다릅니다.
- **B3 POP 의 "오늘" 이 자정 기준**
  - 위치: `InjLotRepository.cs:769·120`, `QcRepository.cs:324·326·378`, `PntRepository.cs:39·109·449`
  - 내용: 웹 보고서는 07시 전기일인데, INJ/IMG 좌측 수치·QC-07·PNT·금형 일 사용량은 자정 기준입니다.
  - 쓰지 않는 POP 차트 `HourlyBarChart`·`HourlyOutputDto` 도 정리 대상입니다.
- **B23 유휴 로그아웃 뒤 라벨 자동 발행 누락**
  - 위치: `PopBlazorForm.cs:87-99`, `LabelDispatcher.cs:112-117`
  - 내용: 로그인할 때마다 발행 기준점을 새로 잡아, 로그아웃 동안 생긴 LOT 은 자동 발행에서 영구히 빠집니다.
- **라우팅 B·시드 변경으로 실패하는 테스트 10건**: PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1.

### 1-3. 테이블 (10-14 전수 조사)
읽기만 있는 테이블입니다. POP 화면이 조회만 하고, 데이터는 시연 행뿐입니다.

| 테이블(행) | 용도 | 읽는 곳 | 상태 |
|---|---|---|---|
| `QC_InspectionStd`(3) | 검사 기준서(버전·AQL·항목) | POP QC-08(`QcRepository.cs:405`), 웹 MD 고객 삭제 사용처 | 등록·개정·승인 화면이 없음. QC-01~03 검사도 이 기준을 쓰지 않음 |
| `QC_CAPA`(2)·`QC_CAPA_Action`(5) | 시정·예방 조치 | POP QC-06·QC-07(`QcRepository.cs:261-282·363`) | 등록·진행 기능 없음 |
| `PNT_DailyPlan`(2) | 도장 일일 계획 | PNT-01·03·09, QC 집계(`PntRepository.cs:36·106·448`) | 계획을 만드는 곳 없음(PP 계획과 연결 안 됨) |
| `PNT_OvenTempSample`(120) | 오븐 5초 온도 | PNT-05(`PntRepository.cs:281-316`) | 온도 수집 장치·에이전트 없음 |

읽기·쓰기 모두 없는 테이블입니다. 소스에는 주석으로만 이름이 남아 있습니다.
- **PNT_ 17개**: DailyReport, JigBindingLog, JigUnload, LabelPrintJob, LabelScanLog, LotLabel, OvenDeviationLog, OvenLog(1행), OvenSpikeLog, PartLossLog, QcQueue, SeqAllocator, ShiftReport, ShiftReportAudit, ShiftReportLineItem, StationStatsCache, TagFailureLog. 도장 공정 설계 때 만든 상세 기록 테이블입니다.
- **PR_ 7개**: BondSetupAudit(3행), CycleAnomalyLog, DashTileCache, DefectAutoLink, DefectRateCache, PlcInterlock, ShiftHandover.
- **QC_ 2개**: QC_Disposition(부적합 처분), QC_NCR_Action(NCR 처리 이력). QC-04·05 가 처분·이력을 남기지 않는다는 뜻이기도 합니다.
- **죽은 코드만 쓰는 5개**: `BondRepository`·`FabricRepository` 는 어디서도 생성되지 않습니다(IMG 는 원단·본딩을 다루지 않기로 함). 대상은 PR_BondSetup(2), PR_BondCycleLog(14), PR_FabricIssue(1), PR_FabricDeductionLog(14), PR_FabricIssueAttempt(0) 입니다.

**결정 필요**
- 읽기만 있는 테이블: 등록 화면·수집기를 만들지, 화면을 「시연용」으로 표시하거나 숨길지
- 아무도 안 쓰는 테이블: DROP 할지(두 리포지토리 삭제 포함). FK 가 없어 지워도 다른 테이블에는 영향이 없습니다.

### 1-4. 문제점 (31)
1. **[상] `PP_WorkOrder.WoNumber` 인덱스가 없어 POP 확정이 막힘**
   - 위치: `PpRepository.cs:931-940`(NextWoSeq), `WorkOrderRepository.cs:428`. 개발·로컬 DB 둘 다 확인
   - 내용: WO 번호를 매길 때 잠금이 테이블 전체에 걸립니다. APS WO 미리보기(읽기 권한 사용자도 가능)나 PP-003 이 도는 동안 POP 스캔 확정이 기다리다, 30초를 넘으면 실패합니다. `dist/migrate_wonumber_unique.sql` 은 있지만 스키마·재구축 목록에 없고 어느 DB 에도 적용되지 않았습니다.
   - 제안: 인덱스를 추가하고(중복 정리 후 유니크 또는 일반), 스키마·`rebuild_db.sh` 에도 반영합니다.
2. **[상] APS 「WO 생성」이 한 품번 때문에 전체 실패**
   - 위치: `PpRepository.Aps.cs:297·326` → `WorkOrderRepository.cs:579`
   - 내용: 대상 중 한 완제품이라도 코어를 정할 수 없으면(사출 자식이 레일뿐인 품번 등) 예외로 트랜잭션 전체가 롤백돼, 정상 행의 WO 도 0건입니다. PP-003 처럼 행 단위로 거부하지 않습니다.
   - 제안: 그 행만 `NoCore` 로 거부하고 나머지는 진행합니다. 이승욱 보류 B9(코어 판정 기준 통일)와 함께 봐야 합니다.
3. **[중] APS 재고 역산에서 WO 없이 만든 생산이 빠짐**
   - 위치: `ApsRepository.cs:905-910·955-970`
   - 내용: 당일 실적을 WO 품번으로 묶어, WO 없는 생산과 BOM 자식 사출 실적이 빠집니다. 시작 재고가 부풀고 사출 소요가 적게 잡힙니다.
   - 제안: 실적 품번을 LOT 품번 → 단계 품번 → WO 품번 순으로(보고서와 같은 규칙) 정합니다.
4. **[중] PP-004 릴리스·취소가 승인 권한만 있는 역할에서 무반응**
   - 위치: `WorkOrder.razor:290·313`
   - 내용: 버튼은 승인 권한으로 보이는데, 처리 첫 줄은 수정 권한을 봅니다. 읽기+승인 역할은 눌러도 아무 일이 없고 안내도 없습니다.
   - 제안: 처리 첫 줄을 `_canApprove` 로 바꿉니다.
5. **[중] MRP 에서 같은 부족분 PR 이 두 번 생김**
   - 위치: `PpRepository.cs:1172-1243`, `Mrp.razor:399`
   - 내용: PR 을 만들 때 그 MRP 실행이 최신인지, 같은 품번의 열린 PR 이 있는지 보지 않습니다.
6. **[중] APS 「WO 생성」 수주 초과 발행 경합**
   - 위치: `PpRepository.Aps.cs:202·924-945·252·314`
   - 내용: 수주 잔량과 기존 슬롯을 잠금 없이 읽은 뒤에야 채번 잠금을 잡아, 두 실행이 겹치면 같은 수주를 초과 배정합니다.
7. **[중] WO 미리보기가 읽기 사용자에게도 쓰기 트랜잭션을 돌림**
   - 위치: `ApsWoPreview.razor:434-479`
   - 내용: 들어가자마자 dryRun(실제 INSERT 후 롤백)이 돌아 잠금을 잡고 번호를 소모합니다. 문제점 1 과 겹치면 POP 확정까지 막힙니다. 「미리보기 갱신」에도 권한 검사가 없습니다.
8. **[중] 이미 IMG 에 쓰인 코어를 INJ 불량으로 등록할 수 있음**
   - 위치: `InjLotRepository.cs:640-680`
   - 내용: 완제품은 양품인데 그 코어는 폐기되는 모순이 생깁니다.
   - 제안: 자식 IMG LOT 이 있으면 거부합니다.
9. **[중] PNT 상태 전이 검사 없음**
   - 위치: `PntRepository.cs:181-238·339-379·382-394`
   - 내용: 지그 사용 여부·LOT 상태를 보지 않아, 두 터미널이 같은 지그를 쓰고 하차 확정이 이미 라벨 붙은 LOT 을 되돌립니다.
10. **[중] PNT 하차 확정이 누적 불량을 지움**
    - 위치: `PntRepository.cs:345-352`
    - 내용: 하차 확정이 불량 수량을 덮어써 PNT-08 에서 쌓은 불량이 사라집니다. 확정 뒤에도 PNT-08 이 불량을 더해 양품+불량이 투입을 넘습니다.
11. **[중] 실적 테이블에 LotID 인덱스 없음**
    - 위치: `LotDefectWriter.cs:25-29`, `InjLotRepository.cs:517`
    - 내용: 확정 LOT 의 불량 등록이 LOT 잠금을 쥔 채 실적 전체를 훑습니다. 운영 기간에 비례해 느려집니다.
12. **[중] POP PLAN 과 INPUT/FINAL 의 하루 기준이 다름**
    - 위치: `InjLotRepository.cs:756 vs 769`(IMG 543 vs 556)
    - 내용: PLAN 은 07시 근무일, INPUT/FINAL 은 자정 기준이라 00~07시에는 수치가 어긋납니다.
13. **[하] 불량 역분개가 마감된 WO 를 다시 엶**
    - 위치: `WorkOrderRepository.cs:431-436·457·265`
    - 내용: 다음 코어 스캔이 Released WO 대신 마감 WO 로 들어갑니다.
14. **[하] APS BOM 규칙 WO 의 다른 자식은 POP 에서 "WO 없음"**
    - 위치: `InjLotRepository.cs:750`, `WorkOrderRepository.cs:279`
    - 내용: 단계 품번이 첫 자식 하나라, 레일 등은 PLAN 0·WO 없음으로 보이고 WO 없이 확정됩니다. 보고서의 계획 품번도 첫 자식으로 몰립니다.
    - 제안: 슬롯 품번 칸 추가를 검토합니다.
15. **[하] 확정은 됐는데 "확정 실패" 로 표시**
    - 위치: `InjMain.razor:403-418`, `ImgJudgePopup.razor:117-122`
    - 내용: 커밋 뒤의 WO 조회가 같은 try 안이라, 순간 끊김이면 실패로 보입니다.
16. **[하] QC 검사 품번이 상수**
    - 위치: `Qc01Incoming:147`, `Qc02InProcess:138`, `Qc03Final:138`
    - 내용: 모든 검사가 "DR-TRM-LH-A1" 로 저장되고 LOT·WO·라인이 없습니다.
17. **[하] POP 로그인이 웹 계정 잠금을 무력화**
    - 위치: `PopAuthService.cs:83` → `AuthRepository.cs:258-267`
    - 내용: POP 로그인 성공(배지 포함)이 웹 비밀번호·PP-CAL PIN 실패 횟수를 0 으로 되돌립니다.
18. **[하] PP-001 주간 탭이 조회 오류를 숨김**
    - 위치: `Forecast.razor:366·389·476-479`
19. **[하] PP-002 일괄 확정이 건마다 따로 저장**
    - 위치: `SupplyPlanImport.razor:297-299`
    - 내용: 중간에 실패하면 앞 건만 확정되고, 몇 건이 반영됐는지 안내가 없습니다.
20. **[하] PP-002~006 승인·생성 동작에 감사 로그 없음**
    - 내용: 수주 확정, WO 생성·릴리스·취소, PR 생성·전송·승인이 SYS-007 에 남지 않습니다.
21. **[하] 단위 EA 고정·WO CSV 품번 누락·MRP 상태 색**
    - 위치: `WorkOrder.razor:162·367`, `PlanConfirmBatchDialog.razor:31`, `Mrp.razor:170·377`
22. **[하] 행위자 매개변수 형식이 컬럼과 다름**
    - 위치: `PpRepository.Aps.cs:1012·1039·1057`, `ApsRepository.cs:1041·1214`, `PpRepository.cs:307·318`, `WorkOrderRepository.cs:596·628`, Inj/ImgLotRepository `@Op`
    - 내용: varchar(20) 컬럼에 NVarChar 로 넘깁니다(기능 오류는 아님).
23. **[하] APS WO 를 취소해도 계획 연결이 남음**
    - 위치: `WorkOrderRepository.cs:687-718`
    - 내용: 취소한 계획 행이 계속 '기발행' 으로 남아 다시 만들 수 없습니다.
24. **[하] IMG 라벨과 실적의 WO 가 다를 수 있음**
    - 위치: `ImgLotRepository.cs:144·200-215·247·438-449`
25. **[하] QC 최종 판정 덮어쓰기**
    - 위치: `QcRepository.cs:81-87`
26. **[하] POP 창을 닫아도 세션이 열린 채 남음**
    - 위치: `PopBlazorForm.cs:63`
27. **[하] InjAgent 정지가 2초를 넘으면 상태 경합**
    - 위치: `PollerRunner.cs:56-69`
28. **[하] 수동 LOT 번호가 PC 시계 기준**
    - 위치: `InjLotRepository.cs:209·66`, `ImgLotRepository.cs:238`
    - 내용: B11 과 같은 원인입니다.
29. **[하] QC-07 이 실적 없는 라인의 LOT 불량을 빠뜨림**
    - 위치: `QcRepository.cs:313-327`
30. **[하] 금형 시드가 일부 참조만 정리**
    - 위치: `dist/seed_md_mold_master.sql:23-24·138-146`
    - 내용: 다시 돌리면 MNT_MoldShotCount·PR_InjLot·PP_WorkOrder 에 옛 금형을 가리키는 행이 남습니다.
31. **[하] 안돈 고장번호가 한 달 999건을 넘으면 반복**
    - 위치: `AndonRepository.cs:255·261`
    - 내용: RIGHT 3자리로 읽어 1000번째부터 같은 번호가 되풀이되고, 웹 번호와 겹칠 수 있습니다.
    - 제안: 웹 `ClaimNumber` 와 같은 방식으로 맞춥니다.

---

## 2. 안영훈 — PDA·Api + Web WH·FG·SCM·Portal + 재구축 스크립트

### 2-1. 미해결 (2)
1. **[중] API 앱풀이 유휴 상태에서 종료돼 Worker 가 멈춤**
   - 위치: `tools/publish-api.ps1:110-126`
   - 내용: AlwaysRunning·유휴 시간·미리 로드 설정이 없어, 요청이 없으면 앱풀이 내려가고 PO·일별 계획 수집이 멈춥니다.
2. **[중] 재구축 스크립트가 지금 세 곳에서 멈춤**
   - ① `dist/migrate_aps.sql:59-77`: BOX 내용 컬럼(BoxContentUnit·BoxContentQty)을 몰라 빈 DB 에서 오류로 중단되고, `set -e` 때문에 재구축 전체가 정지합니다.
   - ② `migrate_scm_portal_screens.sql:31-41`: PORTAL-004 번호를 바꾸다 오류가 납니다.
   - ③ `seed_portal_dev.sql:17`: 업체 V1007 을 만드는 `PDA_SEED` 보다 먼저 돌아 실패합니다.
   - 제안: 세 스크립트를 고친 뒤 실제 재구축으로 확인합니다.

### 2-2. 보류 (6)
- **PO 자동 수집 정합성**(CLAUDE.md PO Sync 「정합성 미비, Important #2」에서 옮김): 고객 SRM 에서 취소·삭제된 PO 라인은 조회에서 사라질 뿐 AMES 에는 `Open` 으로 남고, 이미 `Confirmed` 인 수주도 수량·납기가 바뀌면 매 주기 감사 행 없이 덮어써집니다. 미조회 라인 표시·확정분 변경 경고가 필요합니다.
- **B12 Worker 가 호스트 시각을 씀**
  - 위치: `ScheduledWorker.cs:64`, `PoSyncWorker.cs:42`, `PoSyncRunner.cs:24·52`, `DemandPlanRunner.cs:29`
  - 내용: Api 는 DbClock 을 맞추지 않아 발주일 창·일별 계획 날짜가 PC 시계 기준입니다.
- **B3 Api 일별 생산이 자정 기준**(`RptRepository.cs:149-171`, `RptEndpoints.cs:15`): 웹 보고서는 07시 전기일입니다.
- **CORS 전체 허용**(`Program.cs:51-52`): 아래 [상] 배지 로그인 문제와 겹쳐 위험이 커집니다. 보안 방향 결정을 기다립니다.
- **PO 자동 수집 실패**: 고객 테스트 서버 환경 문제로 09-17 부터 계속 실패합니다.
- **개발 완료 시 정리**: appsettings 비밀번호 평문을 접속문자열 환경변수로 옮깁니다.

### 2-3. 테이블 (10-14 전수 조사)
| 테이블(행) | 상태 | 내용 |
|---|---|---|
| `PP_SupplyPlan`(3)·`PP_SupplyPlanDetail`(9) | 읽기만 | 옛 공급 계획입니다. Api `GET /api/pp/supply-plans`(`PpEndpoints.cs:33` → `PpRepository.cs:545`)만 읽고 쓰는 곳이 없어, 늘 시연 3건을 돌려줍니다. PP-002 는 고객 수주 업로드로 바뀌었습니다. PDA·외부에서 부르지 않으면 엔드포인트와 테이블을 정리합니다. |
| `WH_ReleaseSchedule`(0) | 아무도 안 씀 | 출고 일정 테이블입니다. 소스 참조가 0 이고, 옛 `migrate_wh_core_tables.sql` 이 지우는 테이블이기도 합니다. DROP 후보입니다. |
| `PP_MaterialReservation`(0) | 아무도 안 씀 | WO 자재 예약입니다. 읽는 `WarehouseRepository.ListPickingSlipCandidates`(1290행)를 아무도 부르지 않고, 예약을 만드는 코드도 없습니다. 죽은 메서드와 함께 정리하거나 자재 예약 기능을 설계합니다. |

### 2-4. 문제점 (29)
1. **[상] PDA 테스트 계정이 PIN 0000 으로 살아 있음**
   - 위치: `dist/pda/PDA_SEED.sql:41-130`, `PdaScenarioUsers.cs`. 개발·로컬 DB 확인
   - 내용: SCTEST1·SCTEST2(관리자)·PTEST 가 PIN 0000·활성이고, 시드를 다시 돌릴 때마다 0000 으로 되돌립니다. 운영 차단은 Api 에만, 그것도 SCTEST1/2 만이라 POP 에서 SCTEST2/0000 으로 관리자 로그인이 됩니다.
   - 제안: 운영 DB 에는 시드를 막는 가드를 둡니다. 차단을 POP·Api 공통 로그인 서비스로 옮기고 PTEST 도 포함합니다.
2. **[상] `barcode-login` 이 PIN 없이 토큰을 발급**
   - 위치: `AuthEndpoints.cs:41-62`, `PopAuthService.cs:56-57`
   - 내용: EOS 양식이 아니면 스캔값을 사번으로 보므로, 관리자 사번 문자열만 보내도 관리자 토큰이 나옵니다. CORS 가 전체 허용이라 LAN 의 웹 페이지에서도 부를 수 있습니다.
3. **[중] WH 직접 출고가 FG 완제품까지 출고**
   - 위치: `WhEndpoints.cs:2545-2560·2711-2716`
   - 내용: 다른 WH 조회가 쓰는 창고 박스 조건(FG 구역·라인 위치·컨테이너 제외)을 적용하지 않습니다. PDA 직접 출고로 FG 구역 완제품을 SCRAP 처리하면 출하 없이 재고가 0 이 됩니다.
4. **[중] 닫힌 피킹 줄의 품목이 다시 출고됨**
   - 위치: `WhEndpoints.cs:2657-2661 vs 2685-2717`
   - 내용: 수요 확인은 열린 줄만 보는데 LOT 조회는 줄 상태를 무시합니다.
5. **[중] Api 수동 수집에 권한 검사 없음**
   - 위치: `ScheduledWorkerEndpoints.cs:20`, `PpEndpoints.cs:67-82`
   - 내용: 세션만 있으면(작업자 배지 토큰 포함) 일별 구매계획 창을 교체할 수 있고, 감사도 남지 않습니다.
6. **[중] 포탈 포장수량 저장이 행위자 칸에 이메일을 넣음**
   - 위치: `ScmPackingQuantities.cs:73·91`
   - 제안: `ames:actor` 클레임을 씁니다.
7. **[중] WH 행위자 'web' 고정 14곳**
   - 위치: `WarehouseRepository.cs:394·399·544·548·612·619·719·766·800·884·893·938·969·1051`
8. **[중] PDA 토큰이 계정 변경을 반영하지 않음**
   - 위치: `PopSessionRepository.cs:17`, `TokenStore.cs:42-61`, `BearerAuth.cs:24`
   - 내용: 12시간 동안 발급 때 정보 그대로라, 비활성·잠금·역할 변경·삭제 뒤에도 유효합니다.
9. **[중] 운영 환경에서도 Swagger 가 켜짐**
   - 위치: `Program.cs:96-103`
10. **[중] 로그인 횟수 제한 없음**
    - 위치: `AuthEndpoints.cs:24`
    - 내용: 현장 작업자는 잠기지 않아 4자리 PIN 을 무제한 시도할 수 있습니다.
11. **[중] WH 출고 잠금 범위가 넓어 교착**
    - 위치: `WhEndpoints.cs:2522·2550-2552·2652-2661·2685-2689`
    - 내용: Serializable + 범위 잠금 + 인덱스를 못 타는 UPPER 비교 때문에 PDA 끼리, 그리고 보충 Worker 와 막힙니다.
12. **[중] 위치 맵 쓰기에 감사 없음**
    - 위치: `LocationMapExplorer.razor:275·318·324`, `WarehouseRepository.cs:719`
    - 내용: 창고·구역·층 삭제가 위치 비활성·공통코드 삭제까지 하는데 기록이 남지 않습니다.
13. **[하] 납품서 발행·포장수량 감사 없음**
    - 위치: `ScmPreview.razor:644`, `PackingQuantities.razor:117-119·150`
14. **[하] SCM-004 저장 처리에 권한 재확인 없음**
    - 위치: `PortalUsers.razor:317·344·373·390`
15. **[하] 고정 색 남음**
    - 위치: `ShipmentTrailerDialog.razor.css`, `DeliveryCases.razor.css:50`
16. **[하] 로그인 TerminalId·LineId 길이 검사 없음**
    - 위치: `AuthEndpoints.cs:13·24-36`
    - 내용: 넘치면 조용히 잘리고, 비어 있으면 500 이 납니다.
17. **[하] LineReplenishmentWorker 실패가 모니터에 안 남음**
    - 위치: `Program.cs:35`, `LineReplenishmentWorker.cs:7-33`
18. **[하] PDA 가 403 을 처리하지 않음**
    - 위치: `SessionExpiryHandler.cs:11`, `AdjustmentEndpoints.cs:25`
19. **[하] 옛 출고 피킹 대체 경로가 실패를 성공으로 처리**
    - 위치: `WhEndpoints.cs:1056-1096`
    - 내용: 0행 UPDATE 도 성공으로 처리되고 재고도 줄이지 않습니다.
20. **[하] 같은 품목이 두 줄인 피킹 지시서의 수요 유실**
    - 위치: `WhEndpoints.cs:2655-2672`
21. **[하] FG 조정 검사 부족**
    - 위치: `FgEndpoints.cs:37·156·1251-1271`
    - 내용: 기대 수량·위치·정수 검사가 없습니다.
22. **[하] WH 프로시저가 없으면 빈 목록**
    - 위치: `WhEndpoints.cs:1768-1769`
    - 내용: 마이그레이션 누락이 "데이터 없음" 으로 가려집니다.
23. **[하] 예비품 이력 API 에 기간·건수 제한 없음**
    - 위치: `WhEndpoints.cs:455-458·2375-2397`
24. **[하] 납품서 조회가 잠금 트랜잭션**
    - 위치: `ScmDeliveryNotes.cs:28-39`
    - 내용: 보기·재출력이 출하·취소를 막습니다.
25. **[하] 수동 실행 중 DB 장애가 나면 500**
    - 위치: `ScheduledWorkerEndpoints.cs:21-28`
26. **[하] 토큰 저장이 실패하면 세션 행만 남음**
    - 위치: `TokenStore.cs:42-48`, `AuthEndpoints.cs:87`
27. **[하] `PR_PopSession` 새 컬럼의 위치·스키마 미반영**
    - 위치: `migrate_pda_bearer_session.sql:6-9`
    - 내용: 감사 컬럼 뒤에 붙었고 스키마 파일·CLAUDE.md 배포 순서에 없습니다. 이 마이그레이션이 빠진 DB 에서는 PDA 로그인이 전부 500 입니다.
28. **[하] BOX 내용 컬럼 순서·시험 흔적 컬럼**
    - 내용: BOX 내용 컬럼이 DB 에서는 감사 컬럼 뒤, 스키마 파일에서는 앞에 있습니다. 개발 DB 에만 쓰이지 않는 `SCM_ItemVendor.PackingUnitCode` 가 있습니다. 감사 컬럼 규칙을 어긴 테이블은 개발 DB 4개, 로컬 DB 3개입니다.
29. **[하] 납품서 인쇄 화면이 영어**
    - 위치: `Portal/DeliveryNote.razor:16·20`
    - 내용: "Back to Delivery Management"·"Print" 가 하드코딩돼 있습니다. 의도인지 확인이 필요합니다.

---

## 3. 이승욱 — Web SYS·MD·MNT·RPT·PP-CAL/LSB/OEE/DTL/ODM·계정·공통

### 3-1. 미해결
없습니다. 10-13~14 에 모두 해결했습니다: 확정 전 LOT 불량 집계, MNT 완료 직렬화·채번·고장 삭제, PP-LSB 발행 폐지, RPT 일정 준수·보고서 품번, MNT·PP-OEE 감사 로그, RPT-010 불량 라인, PP-CAL 끝난 WO 납기 차단, MNT 부분·후속 완료.

### 3-2. 보류 (6)
- **B4 MNT-007 확장**
  - 위치: `MntRepository.cs:643·835·1298·1436·965-971·1464`, `WorkOrder.razor:252-263`
  - ① 수동 발행: 고장·PM 밖의 작업(개선·PdM)을 기록할 수 없습니다.
  - ② 체크리스트: 템플릿 단계를 작업지시로 복사하지 않고, 단계별 결과 입력이 없습니다.
  - ③ 부품 차감: 사용 부품이 자유 텍스트라 재고가 줄지 않습니다. "예비품 조정은 PDA 에서만" 결정과 충돌해 방향을 정해야 합니다.
  - 권장 순서는 ③ → ② → ① 입니다.
- **B5 보전 상태 설계**(`MntCodes.cs:33-40`, `MntRepository.cs:1002`): ASSIGNED 상태가 없고(배정해도 ISSUED), CLOSED 마감 경로가 없으며, OPEN 동의어·PdM 유형이 시연 데이터에만 있습니다.
- **B7 MNT-004 금형 정비 등록**(`MoldRepository.cs:53·81`, 호출처 없음): 정비 유형·작업자·샷 리셋·정비일을 남길 곳이 없습니다. 금형 교체를 기록하던 메서드도 옛 POP 화면을 지우면서 끊겼습니다.
- **B8 PP-ODM 라인 상태 기록**: `PP_LineStateLog` 에 쓰는 곳은 `tools/seed_pp_demo` 뿐입니다(아래 테이블 2).
- **B9 코어 판정이 품명 기준**(`CoreItemResolver.cs:20-21·55`)
  - 품명이 CORE 로 시작하는지로 판정합니다. APS 는 InjFlag 로 판정해 둘이 다릅니다.
  - 심재현 문제점 2(APS 전체 실패)의 원인이기도 해서, InjFlag 기준으로 통일하는 일은 심재현과 함께 진행합니다.
- **개발 완료 시 정리**: 운영 admin 시드 차단, appsettings 비밀번호·AllowedHosts·접속문자열 환경변수(Web).

### 3-3. 테이블 — 웹 화면이 읽지만 쓰는 곳이 없음 (10-14 전수 조사)
1. **`MNT_OEELog`(224행) — 설비 OEE 기록**
   - 읽는 곳: MNT-008 OEE 분석, RPT-004 설비 OEE, RPT-008 월간 KPI 가용률, RPT-010 OEE 원본, MNT 대시보드 「오늘 평균 OEE」, Api `/api/mnt/oee`(`MntRepository.cs:294·320·1632`, `RptRepository.cs:472·521·611·932`)
   - 내용: 이 테이블을 채우는 코드가 없습니다. 224행은 시연 데이터라 위 화면들은 실제 생산과 무관한 고정 숫자를 보여 줍니다. PP-OEE 는 별도 테이블 `PP_LineOEE` 를 저장해 서로 연결되지 않습니다.
   - 제안(권장): 화면들이 `PP_LineOEE`(라인 단위)를 읽도록 바꾸고 이 테이블은 폐기합니다. 다른 길은 PP-OEE 를 설비 단위로 확장하는 것입니다.
2. **`PP_LineStateLog`(720) — 라인 상태 분 단위 기록**
   - 읽는 곳: PP-ODM 비가동 모니터(`PpRepository.cs:1759-1815`), PP-OEE 계산(`OeeRepository.cs:36·199·234`), Api
   - 내용: 기록 코드가 없어 PP-ODM 현황뿐 아니라 **PP-OEE 가동률도 시연 데이터로 계산**됩니다(B8).
   - 제안: 가장 작은 해결은 PP-OEE 가동률을 실제로 쌓이는 `PP_LineDowntimeLog`(비가동 기록)로 계산하는 것입니다. 큰 해결은 InjAgent 신호(심재현 연계)나 Api Worker 로 상태를 기록하는 것입니다.
3. **`MNT_WorkOrderTask`(24) — 정비 점검 항목**
   - 읽는 곳: MNT-007 상세 체크리스트, 목록의 「작업 n/m」(`MntRepository.cs:971`), Api
   - 내용: 발행 때 템플릿 단계를 복사하지 않고 결과 입력도 없어, 진행 수치가 늘 시연 값입니다(B4②).
4. **`PR_MoldChange`(3) — 금형 교체 이력**
   - 읽는 곳: MNT-004 「교체 이력」(`MntRepository.cs:365-386`)
   - 내용: 기록 메서드를 아무도 부르지 않아 시연 3행만 보입니다(B7). 실제 교체는 PP-LSB 금형 교체(MC) 슬롯으로 계획만 있습니다.
   - 제안: POP 교체 시작·종료 버튼(심재현) 또는 B7 정비 등록과 함께 기록합니다.
5. **`MNT_MoldShotCount`(0) — 금형 샷 누적**
   - 읽는 곳: MNT-004 금형 목록(`MntRepository.cs:346`), MD 금형·설비 삭제 사용처
   - 내용: 샷을 쌓는 곳이 없어(InjAgent 는 `PR_ShotCount` 에만 씀) 소진율이 비어 있고 정비 한계 알림이 동작하지 않습니다.
   - 제안: InjAgent 가 샷을 기록할 때 이 카운터도 올립니다(심재현 연계).
6. **`MD_InjCondItem`(2) — 사출 조건 항목 마스터**
   - 읽는 곳: InjAgent(`InjCondRepository.cs:18`), MD 라인 삭제 사용처(`MasterDataRepository.cs:2513`)
   - 내용: 등록 화면이 없어 라인·항목을 늘리려면 SQL 로 넣어야 합니다.
   - 제안: MD 화면을 추가하거나 SQL 관리로 결정합니다(심재현과 요구사항 확인).

### 3-4. 문제점 (6)
1. **[하] 부분 완료한 원래 작업지시에서 고장 연결이 안 보임**
   - 위치: `MntRepository.CarryFailuresToFollowUp`, `ListFailuresForWo`
   - 내용: 고장이 후속 작업지시로 옮겨져, 원래 작업지시 상세의 대응 이력이 비어 보입니다.
   - 제안: 원래 작업지시는 SourceRefID 로도 고장을 찾습니다.
2. **[하] 이름 사전 실패를 5분간 캐시**
   - 위치: `Services/ActorNames.cs:25-28`
   - 내용: DB 가 잠깐 끊긴 뒤 5분 동안 모든 화면이 이름 대신 사번·GUID 를 보입니다.
3. **[하] 쓰지 않는 `PlaceWo` 는 호출되면 즉시 실패**
   - 위치: `LineScheduleRepository.cs:68-84`
   - 제안: 삭제합니다.
4. **[하] 행위자 자르기 길이 불일치**
   - 위치: `MntRepository.cs:1627`
   - 내용: 50자로 자르는데 매개변수는 20자입니다.
5. **[하] 문서·스키마 표기가 현재와 다름**
   - CLAUDE.md: "176개 테이블"(실제 175), "SQL Server 2022"(실제 개발 2019 RTM·로컬 2025), MD-032 메서드 이름 2개(`EmployeeNoExists`·`EmployeeNoUsedByWebUser`), 없는 설계 문서 경로, 재구축 설명, 엑셀 이름
   - 스키마 파일: 머리말 숫자, 앞부분 DROP 목록
   - `rebuild_db.sh`: `migrate_mnt_number_index.sql` 누락, attribute2/widen 순서(둘 다 실제 영향은 없음)
6. **[하] 로컬 DB 에만 폐지 화면 WH-05 잔재**
   - 내용: `SYS_Screen`·`SYS_RolePermission` 에 남아 있습니다(로컬 DB 만).

---

## 4. 오세영 — 알림(PUSH) 앱·발송

### 4-1. 미해결 (5)
1. **[중] PUSH 발송 기능이 없음**
   - 알림 채널은 PUSH 만 쓰기로 했지만(EMAIL·SMS 는 사용 안 함으로 보존), 규칙·이력 화면(SYS-008)만 있고 실제 발송이 없습니다.
   - 테이블 점검에서 `SYS_NotificationHistory`(40행)는 SYS-008·Api·상태 화면이 읽기만 하고 쓰는 코드가 없음을 확인했습니다. 지금 보이는 이력·건수는 시연 데이터입니다.
   - 발송기를 만들면 결과(SENT/FAILED·재시도·오류)를 이 테이블에 씁니다.
2. **전송 방식 결정**: FCM(인터넷 필요, 키는 공통코드 `SW_PUSH_*`) 또는 사내망 상시 연결(SignalR/WebSocket). PDA 앱이 수신을 겸할지도 정해야 합니다.
3. **앱 기능**: 기존 `POST /api/auth/login`(사번+PIN)으로 로그인, 푸시 토큰 등록·해제, 알림 목록·읽음 처리, 누르면 해당 건(고장 번호·안돈 ID 등)으로 이동, 방해 금지 시간.
4. **서버(AMES.Api, 안영훈과 함께)**
   - 기기 등록 API(`SYS_NotificationChannel` upsert)
   - 발송기: 규칙 → 역할 ID 수신자 → 그 사람의 PUSH 행 → 발송 → 이력
   - 반복 알림 15분(SYS-009 `ALERT_REPEAT_MINUTES`)
   - 이벤트 9종: 고장 등록, 안돈 발동, QC 홀드, PM 지연, 재고 부족, 납기 위험, 연동 실패, PLC 정지, 일일 요약
5. **지킬 규칙**: 사용 중인 채널로만 발송, 시각은 DB 서버 시각, 행위자는 20자(예 `PUSH-SVC`), 비밀값은 공통코드 `SW_*_AUTH`(감사 로그에서 가려짐).

### 4-2. 보류 (2)
- 전송 방식 결정 대기(FCM / 사내망, PDA 겸용 여부)
- 일정·착수 시점 미정

### 4-3. 연계
- POP 안돈의 알림 기록 고정값(`SUPERVISOR/PDA`·`LINE-LEAD/EMAIL`)을 PUSH·규칙 수신자로 바꾸는 일은 심재현 미해결 16 입니다.

---

## 5. 공통 — 다음 개발 항목 (CLAUDE.md 에서 옮김)
- `AMES.Pda`: 추가 모듈(PP·MNT 등) MAUI 화면 — 안영훈
- `AMES.Data`: inline SQL → `dbo.SP_*` 스토어드 프로시저 전환 — 공통
- `AMES.Web`: 실제 데이터 바인딩 완성(일부 화면 scaffold 상태) — 각 화면 담당
- `AMES.Pop`: 추가 공정 모듈(필요 시) — 심재현
- 참고: 옛 `AMES_DB_CRUD_커버리지_20260917.md`(172 테이블 기준 CRUD 분석)는 10-14 전수 조사(각 담당자 「테이블」 절)로 대체돼 삭제했습니다.

## 6. 참고 — 검증 기준선과 배포 메모

- **테스트(2026-10-14, Data 는 로컬 DB)**
  - Pop 149, Api 64, InjAgent 54 모두 통과
  - Data 1,007건 중 989 통과, 10 실패, 8 건너뜀
  - 실패 10건은 모두 기존 항목입니다(PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1).
  - 병렬로 돌 때 테스트끼리 교착이 가끔 1~2건 더 나는데, 따로 돌리면 통과합니다.
- **두 DB 구조**: 개발·로컬·스키마 파일 모두 175개 테이블이고 컬럼 형식 차이가 없습니다. 예외는 이 문서에 적은 컬럼 순서 3건과 `PackingUnitCode` 입니다. 공통코드(430행)·화면 등록도 같고, 로컬의 WH-05 만 다릅니다.
- **제품 코드 규칙**: `AddWithValue` 0곳, Web 의 `DateTime.Now/Today` 0곳, resx 4개 파일 키셋 동일(3,528키).
- **10-14 PP-LSB 발행 폐지 배포 순서**: 신 Web·Api 먼저 → `dist/migrate_lsb_publish_removal.sql`. 개발·로컬 DB 에는 이미 적용됐으므로, 개발서버는 Web·Api 만 덮어쓰면 됩니다.
- 이 문서는 점검 시점의 스냅샷입니다. 항목을 해결하면 커밋에 함께 이 문서의 해당 줄을 지우거나 "해결(날짜·커밋)" 으로 고쳐 주세요.
