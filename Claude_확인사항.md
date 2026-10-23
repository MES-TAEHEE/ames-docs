# Claude 확인사항 — 담당자별 미해결·보류·문제점

> **이 파일이 미해결·보류·문제점·다음 개발 항목의 유일한 정본입니다**(10-14 사용자 결정). CLAUDE.md·다른 문서·작업 메모에는 목록을 두지 않습니다. 항목을 해결하면 같은 커밋에서 여기 항목을 지우고, 새로 찾은 문제는 담당자 절에 더합니다.

> **재검토 원칙**(10-16 사용자 확인): 이 문서의 지적·제안 자체가 구현 원칙이나 수정 지시는 아닙니다. 현재 호출 경로·합의된 업무 방향과 대조해 실제 결함인지 먼저 확인합니다. 지적이 틀렸거나 방향이 바뀌었다면 코드·DB를 억지로 바꾸지 않고, 이 문서에 해당하지 않는 이유·근거·현재 방향을 정정합니다. 운영 결함, 배포·검증 대기, 정책 결정, 선택적 정리를 구분하며 과거 DB 점검값을 현재 값으로 단정하지 않습니다.

- **작성**: 2026-10-14, Claude Code(이승욱 세션). 그 뒤 원격 병합·작업 때마다 해결된 항목을 지우고 남은 것만 둡니다(지난 판은 커밋 이력). **10-22 재정리**: 기준 소스를 `566893cd` 로 올리고 10-17(ba968b99) 이후 바뀐 파일을 가리키는 줄 번호를 다시 맞췄습니다(AppConfig·LineStock·스키마 파일·PP→POP 초기화 스크립트·MasterDataRepository — 나머지 참조 파일은 그 뒤 바뀌지 않음). 해결된 항목(안영훈 2-5 3 — 두 DB·스키마의 `FG_CustomerReturn.PartName` 위치 차이)을 지우고, 이승욱 10-21~22 작업(SAPFlag·로케이션 2단계)에 맞춰 안영훈 2-1 8·공통 SAP 항목·검증 기준선을 고쳤습니다. 다른 담당자 항목의 판정·문장은 바꾸지 않았습니다. **10-22 2차 재정리**: 기준 소스를 `2de82e9c` 로 올렸습니다. 김현진 POP 인라인 SQL → `dbo.SP_POP_*` 145개 전환(c10a16ee)과 심재현 팔레트 적재(dabed559·41f99dca)로 바뀐 POP 저장소·화면(Inj/ImgLotRepository·WorkOrderRepository·AndonRepository·QcRepository·PntRepository·ReworkRepository·AuthRepository·BackflushRepository·LotDefectWriter·MoldRepository·InjMain·ImgMain·ImgJudgePopup)의 줄 번호를 다시 맞췄고, SQL 이 프로시저로 옮겨 간 곳은 C# 메서드 범위와 `dist/migrate_pop_stored_procedures.sql` 의 줄을 함께 적었습니다. 지적 내용은 모두 그대로 남아 있음을 새 코드로 확인했습니다. 안영훈 2-1 의 번호 중복(8 두 개)을 9 로 고치고 공통 「SQL 위치」·검증 기준선을 고쳤습니다. **10-22 흐름 전수 점검**(사용자 요청 — 웹 마스터 → 계획 → POP 사출·감싸기·도장 → PDA 자재·완제품 입출고를 5구간으로 나눠 소스 읽기·로컬 복제 DB 조회만, 소스 변경 없음): 새로 찾은 것을 심재현 문제점 39~51·안영훈 2-4 1~11(2-1 8 세부 포함)·이승욱 3-4 1~2 로 더했습니다. 기존 항목과 겹치는 것은 넣지 않고 관련 항목 번호만 적었습니다. 로컬 DB 에 아직 팔레트 적재 데이터가 없어 팔레트 관련 판단은 코드 기준입니다. **10-22 3차·4차(심재현)**: 심재현 절의 줄 번호를 `7cd85fa4` 기준으로 다시 맞췄습니다(PNT-MAIN 전환으로 바뀐 저장 프로시저·스키마·Backflush·Rework·PalletStock·ImgJudgePopup). 해결·소멸한 항목은 지우고 뒤 번호를 당겼으며, 무엇을 지웠는지는 커밋 이력에만 둡니다.
- **기준 소스**: `dev` 10-23(이 문서를 고친 커밋 — 심재현 PNT-MAIN·APS 거부 규칙·WO 번호 유니크·팔레트 불량 재고 제거, 김현진 POP PIN 키패드 수정, 이승욱 BOM 단위·LOT PREFIX 규칙까지 포함). 심재현·이승욱 절의 줄 번호는 이 커밋 기준이고, 안영훈 절은 10-23 `young`에서 현재 호출 경로와 사용자 정책을 재검토해 수정·정정했습니다. 해당 절은 고정된 옛 줄 번호 대신 클래스/메서드와 검증 상태를 기준으로 봅니다.
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

| 담당  | 미해결                  | 보류  | 테이블 | 문제점 | 가장 먼저 볼 것                                                                                                                   |
| --- | -------------------- | --- | --- | --- | --------------------------------------------------------------------------------------------------------------------------- |
| 심재현 | 21                   | 8   | 7묶음 | 43  | [상] POP·InjAgent 배포 설정이 로컬 DB(미해결 1) · [중] BOM 자동 차감이 서브조립 아래 자재를 빼지 않음(문제점 28) · 안돈 보전 호출 작업지시(요청 20) |
| 안영훈 | 10(배포·검증·요청) | 7 | 3 | 1 | FG 라벨·미배정/수리 재고·백플러시·입고 취소·팔렛/출하 수량 보호 수정의 배포 및 실제 쓰기/동시성 검증(2-1 10) · 상위 위치 차단/기존 Area 부모 선택(2-1 8) · 생성기 패키지 보안 경고(2-4 1) · 정책/비결함 정정은 2-2·2-5 |
| 이승욱 | 1(적용·검증 대기)        | 3   | 3   | 2   | 라인 상태 기록 운영 마무리 — 06:00 재분류 작업 등록·임계값·보존 정리(3-1 1) · 쓰인 LOT PREFIX 변경 허용 — 안영훈과 결정(3-4 1) · 나사·레진 품목 단위가 정본과 다름 — 되돌릴지 결정 대기(3-4 2) · 품목 불출율·MRP 여유율 반영 방식 결정(보류 3) |
| 오세영 | 5                    | 2   | —   | —   | 발송기가 없어 SYS-008 발송 이력이 시연 데이터                                                                                               |

---

## 1. 심재현 — POP(QC 포함)·InjAgent·Devices + Web PP-001~006·PP-APS

### 1-1. 미해결 (21)

1. **[상] POP·InjAgent 배포 설정이 로컬 DB 를 가리킴**
   - 위치: Pop `appsettings.Production.json:3`, `Common/AppConfig.cs:51-52·87-91` / InjAgent `appsettings.json:3`, `Core/AgentConfig.cs:56-57`, LOT 생성 실패 재시도 없음 `Core/MachinePoller.cs:144-154`
   - 내용
     - POP 배포 설정이 비상용 `localhost\MSSQLSERVER01` 이고, 기본 설정(개발서버)을 Production 파일이 덮어씁니다. 그래서 현장 터미널이 비상용 DB 에 쓰거나 접속에 실패합니다.
     - InjAgent 는 `sa@localhost` 에 Connect Timeout 이 없고 재시도도 없어, 샷 LOT 이 유실될 수 있습니다.
     - 둘 다 환경변수 오버라이드(`AddEnvironmentVariables`)를 읽지 않습니다.
   - 제안: 배포 설정을 개발서버 문자열로 바꾸고 환경변수 오버라이드를 추가합니다. InjAgent 는 전용 계정과 `Connect Timeout=30` 을 씁니다.
2. **[중] 스캔 확정 뒤에 온 로봇 NG 판정이 버려짐**
   - 위치: `InjLotRepository.cs:551-568`(MarkNgBlocked), 호출 `MachinePoller.cs:206-210`
   - 내용: 로봇 검사 결과가 작업자 스캔 확정보다 늦게 오면, LOT 이 RAW 가 아니라는 이유로 NG 표시를 무시합니다. 불량품이 양품으로 집계됩니다.
   - 제안: 확정된 LOT 에 늦은 NG 가 오면 불량 등록(역분개) 경로로 넘기거나, 최소한 경고를 남깁니다.
3. **[중] DB 가 끊기면 POP 화면이 최대 30초 멈춤**
   - 위치: `InjMain.razor:275`, `ImgMain.razor:278`
   - 내용: 5초 타이머가 화면 스레드 안에서 동기 조회를 7~8번 합니다. 접속 대기 30초 동안 화면과 스캔 입력이 모두 멈춥니다.
   - 제안: 조회를 백그라운드로 돌리고, 실패하면 갱신 실패 표시만 합니다.
4. **[중] APS 등록 계획 단위 불일치**
   - 위치: `ApsRepository.cs:324-326·409-414·733·738`, `PpRepository.Aps.cs:976`
   - 내용: BOM 규칙 WO 의 사출 슬롯을 부모 품번으로 읽고, 이미 자식 단위인 수량에 QtyPer 를 한 번 더 곱합니다. 잠긴 칸·부하가 부풀고, WO 생성 때 기존 슬롯 차감도 자식 슬롯을 놓칩니다(문제점 5 와 같은 원인).
   - 제안: 슬롯 품번을 단계 품번 기준으로 읽고 재곱을 없앱니다.
5. **[중] 수동 라벨이 두 번 생성됨**
   - 위치: `InjMain.razor:532-548·493`, `ImgMain.razor:365-376·521`
   - 내용: 처리 중 표시가 동기 처리라 화면에 반영되기 전에 두 번째 터치가 쌓여, LOT 2개·라벨 2장이 나옵니다. 재출력 버튼에는 가드가 없습니다.
   - 제안: 비동기 처리로 바꾸고 처리 중 버튼을 끕니다. 재출력에도 같은 가드를 둡니다.
6. **[중] 안돈이 동시에 2건 발동됨**
   - 위치: `AndonWorkflow.cs:91-92`, `AndonRepository.cs:21-39`(Raise → `SP_POP_ANDON_Raise`, `migrate_pop_stored_procedures.sql:422-449`)
   - 내용: 같은 라인의 두 터미널이 동시에 안돈을 누르면, 확인과 INSERT 사이에 잠금이 없어 2건이 생깁니다.
   - 제안: 라인 단위로 잠근 뒤 확인하고 INSERT 합니다.
7. **[중] InjAgent 수집 오류 묶음**
   - 위치: `MachinePoller.cs:76-77·117-157·160-175·202-221`, `Plc/FEnetClient.cs:140-149`
   - 내용
     - 카운터 리셋이나 금형 변경만으로 LOT 이 생깁니다.
     - 샷이 2 이상 건너뛰면 1건만 생깁니다.
     - 로봇 쓰기 실패를 무시해 판정이 다른 LOT 에 붙습니다.
     - 검사 이력이 재시도 때 중복됩니다.
     - FEnet 응답을 한 번만 받아 잘린 응답을 그대로 씁니다.
   - 제안: 샷 증가분만큼 LOT 을 만들고, 쓰기 실패는 재시도하며, 응답 길이만큼 반복해서 받습니다.
8. **[중] 재작업이 취소된 WO 에 실적을 귀속**
   - 위치: `ReworkRepository.cs:160`, `WorkOrderRepository.cs:402-412`(BumpStepCompleted → `migrate_pop_stored_procedures.sql:304-307·313-322`)
   - 내용: 수리한 양품이 취소 WO 로 들어갑니다. 마지막 단계면 Cancelled 가 Closed 로 바뀌고 완료 수량도 바뀝니다.
   - 제안: 취소 WO 면 열린 WO 를 다시 찾고, 없으면 WO 없이 확정합니다.
9. **[중] QC 홀드 해제 조건 없음·QC 번호 중복**
   - 위치: `QcRepository.cs:151-178`(ReleaseHold → `migrate_pop_stored_procedures.sql:1173`), 번호 `migrate_pop_stored_procedures.sql:1100·1141`(CreateInspection·CreateNcr)
   - 내용: 이미 해제된 홀드도 다시 해제됩니다. 검사·NCR 번호가 분 단위(yyMMddHHmm)라 같은 분에 두 건이면 번호가 겹치고, 유니크 제약도 없습니다.
   - 제안: 상태 조건을 추가합니다. 번호는 범위 잠금으로 채번합니다(MNT `ClaimNumber` 방식).
10. **[중] inj_flag 마이그레이션이 컬럼을 데이터째 지움**
    - 위치: `dist/migrate_md_item_inj_flag.sql:63-111`
    - 내용: 재생성 목록(63-96행)에 `ScanRequired`·`SAPFlag`(10-16 `SAPTransFlag`, 10-21 개명) 가 없고 감사 컬럼 순서도 지금 DB(맨 뒤)와 다르며 기본값 제약(`DF_MD_Item_ScanRequired`)·인덱스도 되살리지 않으며, 컬럼 구성 대조 가드가 없습니다. 18-24행 가드 덕분에 `InjFlag` 가 이미 `ItemCategory` 바로 뒤에 있는 DB(개발·로컬)는 재실행해도 건너뛰지만, `InjFlag` 가 없거나 자리가 다른 DB 에 적용하면 뒤 컬럼이 데이터째 사라집니다.
    - 제안: 재생성 목록을 현재 `MD_Item` 구성으로 보강하고 컬럼 대조 가드를 둡니다. 이미 적용된 DB 가 대부분이면 "컬럼이 있으면 건너뜀" 만 남기는 것도 방법입니다.
11. **[하] POP 로그인 화면 예외 처리**
    - 위치: `Login.razor:467·285-286·300`
    - 내용: DB 장애 때 예외를 다시 던지고, 배지 등록·초기 목록 조회가 try 밖이라 로그인 화면 자체가 깨집니다.
12. **[하] 로그·라벨 폴더가 실행 위치 기준**
    - 위치: `AppConfig.cs:62`, `PopLog.cs:15`, `Common/BackflushNotice.cs:13`(10-16 백플러시 로그도 같은 `PopLog`)
    - 내용: 바로가기의 작업 폴더에 따라 파일 위치가 달라집니다.
    - 제안: 실행 파일 폴더 기준으로 바꿉니다.
13. **[하] 라벨 선점 순서·출력 후 카운트 실패 시 중복**
    - 위치: `InjLotRepository.cs:359-376`(ClaimForPrint → `migrate_pop_stored_procedures.sql:1516`), `InjMain.razor:497-508`, `ImgMain.razor:383-390·525`
    - 내용: 선점 UPDATE 에 순서가 없습니다. 출력은 됐는데 카운트 갱신이 실패하면 "실패" 로 안내해, 작업자가 다시 출력합니다.
14. **[하] 로그아웃 뒤 확인창이 남음**
    - 위치: `Services/ConfirmService.cs:15-30`, `Services/AppState.cs:46-57`
15. **[하] 안돈 기록 오류 묶음**
    - 위치: `AndonPopup.razor:246-250`, `AndonRepository.cs:228·36-37`, 종료 `migrate_pop_stored_procedures.sql:698-700`(SP_POP_ANDON_Resolve)
    - 내용
      - 기존 안돈을 복원만 해도 설비 STOP 기록이 중복됩니다.
      - 원인코드 매개변수가 `VarChar 15`(공통코드 값은 20자까지 — 지금 최대 8자라 잠재 결함)입니다.
      - 종료 때 CLOSED 를 RESOLVED 로 덮습니다.
      - 알림 수신자·채널이 고정값입니다(오세영 PUSH 와 연계).
16. **[하] 마이그레이션 FK 6개·그룹 이름 옛 접두어**
    - 위치: `migrate_aps.sql:74·75·100·157·185`, `migrate_aps_plan_line_shift.sql:21`, `migrate_demand_plan.sql:69·92-96`
    - 내용: FK 를 두지 않는 원칙과 달리 FK 6개를 새로 만듭니다. DPSYNC 공통코드 그룹 이름에 폐지된 `ScheduledWorker · ` 접두어가 있습니다.
17. **[하] 테스트 코드에 접속 비밀번호·외부 IP**
    - 위치: `AmesDevDb.cs:15` 외 `AMES.Data.Tests` 7파일(총 8파일, 192.168.0.132 를 쓰는 6파일은 Connect Timeout=10, `MoldPlanningTests:20`, `PlanScheduleTests:21` 은 98.95.142.192 이고 Connect Timeout 도 없음)
    - 제안: 모두 `AMES_TEST_CONN` 하나로 바꿉니다.
18. **[하] POP PLAN 기준 결정**
    - 위치: `migrate_pop_stored_procedures.sql:1713-1721`(SP_POP_INJ_GetDailyItemSummary)·`2082-2090`(SP_POP_IMG_GetDailyItemSummary)·`985-993`(SP_POP_PNT_GetDailyItemSummary)
    - 내용: PP-LSB 「발행」이 폐지돼(10-14), 「적용」만 해도 현장 PLAN 에 바로 반영됩니다. 어떤 기준으로 할지 POP 담당이 정합니다.
19. **[하] POP IMG 코어 스캔 검사를 InjFlag 기준으로**
    - 위치: `ImgLotRepository.cs:128`(CreateFromCore → `CoreItemResolver.Read`)
    - 내용: 웹 WO 발행은 10-14 부터 PP-APS 와 같은 InjFlag 기준(`CoreItemResolver.ReadByInjFlag`)으로 코어를 정하는데, IMG 코어 스캔의 `CoreMismatch` 검사는 아직 품명 CORE 규칙입니다. 개발 DB 완제품 77개는 두 규칙의 결과가 같지만, 레일만 있는 품번처럼 둘이 갈리면 WO 의 사출 단계 품번과 IMG 검사가 다른 코어를 봅니다.
    - 제안: `ReadByInjFlag` 로 바꿉니다(POP 담당 작업 예정).
20. **[중] [요청] 안돈 보전 호출에 CM 작업지시 발행·안돈 종료 때 함께 닫기**(10-16 사용자 결정 — 이승욱 요청)
    - 위치: `AndonRepository.cs:223-236`(보전 호출 → `SP_POP_ANDON_CallDepts_InsMNT_FailureRegister` `migrate_pop_stored_procedures.sql:574-604` — `MNT_FailureRegister` 만 생성, `WorkOrderID` NULL) · `AndonRepository.cs:269-281`(안돈 종료 → `SP_POP_ANDON_Resolve` `migrate_pop_stored_procedures.sql:698-700` — 고장만 RESOLVED)
    - 내용: 안돈 고장에는 작업지시가 없어 MNT-007 배정·착수·점검·PDA 부품 출고를 쓸 수 없습니다(개발 DB 작업지시 없는 열린 고장 10-17 기준 3건). 웹은 10-16 부터 MNT-002 상세의 「작업지시 발행」(`MntRepository.IssueFailureWorkOrder` — CM, 출처 ANDON, SourceRefID 고장번호)으로 수동 연결합니다. 그런데 그렇게 연결한 고장이 안돈 종료로 RESOLVED 가 되면 작업지시는 열린 채 남아 MNT-007 에서 따로 완료·종료해야 합니다.
    - 요청: ① 보전 호출 때 같은 트랜잭션에서 CM 작업지시를 발행해 `WorkOrderID` 를 채웁니다(웹과 같은 규칙 — `MntRepository.IssueFailureWorkOrder` 를 그대로 쓰거나 같은 값: WoType CM·SourceType ANDON·SourceRefID 고장번호·우선순위는 심각도·상태 ISSUED, 번호는 `MWO-yyMM-nnn` 범위 잠금 채번). ② 안돈 종료 때 연결된 작업지시가 끝나지 않았으면 같이 완료합니다(결과 OK — CM 작업지시는 점검 항목이 없어 완료 차단 규칙 `MwoChecklistRules` 에 걸리지 않습니다).
21. **[하] [요청] 금형 샷 누적 기록 — 사이클 PM 자동화의 전제**(10-16 이승욱 요청)
    - 위치: InjAgent(샷은 `PR_ShotCount` 에만 기록), `MNT_MoldShotCount`(0행 — 이승욱 테이블 3)
    - 내용: 사이클(CYCLE) 주기 PM 은 지금 완료할 때 사람이 다음 예정일을 넣습니다(10-16 사용자 결정 — 샷 누적 연동 전까지, `PmRules`). 샷이 쌓이지 않아 금형 소진율·정비 한계 알림도 비어 있습니다.
    - 요청: InjAgent 가 샷을 기록할 때 `MNT_MoldShotCount` 도 올립니다. 그 뒤 이승욱이 사이클 PM 의 다음 회차를 샷 기준으로 자동 발행하도록 바꿉니다.

### 1-2. 보류 (8)
- **PNT 확정의 코어 재고 처리 vs PP-APS 가용재고**(10-22 검토에서 발견): PNT 확정은 BOM 사출품 자식을 PNT 라인 위치의 자리표시 `BF-P{n}-{코어}` 음수로 뺀다(사용자 결정 10-22). APS 가용재고는 `WH_Inventory.Qty > 0` 만 더해 음수 자리표시를 보지 않으므로, 도장으로 소비된 코어의 팔레트 행이 창고 라인 보충 전까지 APS 코어 재고에 그대로 잡힌다(IMG 는 `ConsumeCore` 가 코어 재고 행을 지움). **10-22 사용자 결정: 지금은 라인 재고 차감 그대로 둔다.** 후속으로 사출품을 라인 재고로 옮기는 처리(팔레트의 코어 LOT 행 → 그 라인 위치의 품번 수량 행으로 전환)를 만들면 그 수량이 자리표시 음수를 메우고 팔레트 행이 사라져 APS 과대 계상도 함께 풀린다. 그때까지 APS 코어 재고는 도장 소비분만큼 과대하다(IMG 는 영향 없음).
- **POP 팔레트 적재(10-21) 의 범위 밖**: ① 재작업 스테이션의 수리 양품은 팔레트 스캔이 없어 `WH_Inventory` 에 넣지 않는다(INJ·IMG 확정만 싣는다) — 재작업에도 팔레트 스캔을 둘지. ② 현재 팔레트는 화면 메모리뿐이라 재로그인·재시작하면 다시 스캔한다 — 마지막으로 실은 팔레트를 DB(그 라인의 마지막 `PROD_CONFIRM` 거래)에서 되살릴지. ③ 팔레트 바코드는 `EOS-PLT-` 접두어로만 LOT 과 가르므로 다른 양식의 외부 팔레트는 받지 않는다. ④ LH/RH 쌍 팔레트의 `MD_Item.PalletQty` 는 "그 품번이 쌍 팔레트에 실리는 개수" 기준이다(10-21 결정 — 품번별로 센다) — 값이 있는 NEAFUC 쌍(D0111-PI010·D0121-PI010 32/32)이 "LH 32 + RH 32 = 64개" 인지 "16쌍" 인지 현장 적재 기준을 확인해 맞춰야 한다(값을 바꾸면 APS 계획 올림 단위도 같이 바뀐다).
- **APS 금형 교체(MC) 한계**: "직전 금형"을 그 라인·그 날 처리 순서상 마지막 금형으로 보므로, 하루 안에 금형이 여러 번 바뀌면 그 사이 되돌림 MC 를 만들지 않습니다(같은 라인·같은 날 금형 그룹당 MC 최대 1개). 교체 자리가 없을 때 억지로 둔 MC 는 휴게·점유 구간과 겹칠 수 있습니다. 조정은 PP-LSB 에서 합니다.
- **B2 IMG 라벨 교대 글자가 항상 C**(`Common/LabelPrinter.cs:27-32`): DAY/NIGHT 만 A/B 로 바꾸는데, 실제 교대 코드는 A/B/C 라 항상 C 가 찍힙니다. 교대 코드를 그대로 쓸지 답을 기다립니다.
- **B11 InjAgent 시각을 DB 서버 기준으로**: POP 은 10-16(eb5ace39) 로그인 때 `DbClock` 을 DB 시각에 맞추도록 바뀌었습니다(호스트 시계는 유휴·로그·토스트에만 남음). InjAgent 는 아직 `DbClock.Configure` 를 하지 않아, 자동 LOT 채번(`InjLotRepository.cs:66`, `MachinePoller.cs:144` 에서 호출)의 LotNo 날짜 글자가 PC 시계 기준입니다.
- **B3 POP 의 "오늘" 이 자정 기준**
  - 위치: `InjLotRepository.cs:120`, `migrate_pop_stored_procedures.sql:1732`(INJ 좌측)·`2101`(IMG 좌측)·`1004`(PNT 좌측 — 10-22 `SP_POP_PNT_GetDailyItemSummary`, 구 PNT 프로시저와 같은 자정 기준)·`1243·1245·1277`(QC-07)
  - 내용: 웹 보고서는 07시 전기일인데, INJ/IMG 좌측 수치·QC-07·PNT·금형 일 사용량은 자정 기준입니다. 10-16 부터 POP 의 "오늘" 은 DB 날짜(B11)이지만 경계는 여전히 자정입니다.
  - 쓰지 않는 POP 차트 `HourlyBarChart`·`HourlyOutputDto` 도 정리 대상입니다.
- **B23 유휴 로그아웃 뒤 라벨 자동 발행 누락**
  - 위치: `Forms/PopBlazorForm.cs:87-100`, `Services/LabelDispatcher.cs:111-123`
  - 내용: 로그인할 때마다 발행 기준점을 새로 잡아, 로그아웃 동안 생긴 LOT 은 자동 발행에서 영구히 빠집니다.
- **라우팅 B·시드 변경으로 실패하는 테스트 10건**: PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1(10-17 설비 재시드 병합 뒤에도 같은 10건). 10-22 로컬 DB 를 개발 DB 로 복제한 뒤 `MoldPlanningTests.LineLastMoldBefore_falls_back_to_mounted_mold_with_min_date` 1건이 더 실패합니다 — 테스트는 `LINE-INJ-01` 에 이전 날짜의 금형 슬롯이 없다고 보고 설비 장착 금형으로 돌아가길 기대하는데, 개발 데이터에 10-16~20 슬롯(금형 `NEAFUC--`)이 있어 그 금형을 돌려줍니다(코드 결함 아님 — 데이터 가정, 이승욱 10-22 확인).

### 1-3. 테이블 (10-14 전수 조사)
읽기만 있는 테이블입니다. POP 화면이 조회만 하고, 데이터는 시연 행뿐입니다.

| 테이블(행) | 용도 | 읽는 곳 | 상태 |
|---|---|---|---|
| `QC_InspectionStd`(3) | 검사 기준서(버전·AQL·항목) | POP QC-08(`QcRepository.cs:267-291` → `migrate_pop_stored_procedures.sql:1291`), 웹 MD 고객 삭제 사용처 | 등록·개정·승인 화면이 없음. QC-01~03 검사도 이 기준을 쓰지 않음 |
| `QC_CAPA`(2)·`QC_CAPA_Action`(5) | 시정·예방 조치 | POP QC-06·QC-07(`QcRepository.cs:181-218·238-246` → `migrate_pop_stored_procedures.sql:1197-1227·1264`) | 등록·진행 기능 없음 |

읽기·쓰기 모두 없는 테이블입니다. 소스에는 주석으로만 이름이 남아 있습니다.
- **PNT_ 22개**: DailyPlan(2행), DailyReport, JigBindingLog, JigLoad(1행), JigUnload, LabelPrintJob, LabelScanLog, LineEvent(12행), LotLabel, OvenDeviationLog, OvenLog(1행), OvenSpikeLog, OvenTempSample(120행), PartLossLog, QcQueue, SeqAllocator, ShiftReport, ShiftReportAudit, ShiftReportLineItem, StationStatsCache, TagFailureLog, VirtualLot(7행). 구 도장 모듈(가상 LOT·지그·오븐)의 테이블 — **10-22 PNT-MAIN 전환으로 읽는 코드·프로시저가 없다**(PNT 실적은 `tbl_Lot`/`PR_PntLot`, QC-07 도 실적 테이블로 센다). 행은 시연 데이터뿐. DROP 여부 결정 대기.
- **PR_ 7개**: BondSetupAudit(3행), CycleAnomalyLog, DashTileCache, DefectAutoLink, DefectRateCache, PlcInterlock, ShiftHandover.
- **QC_ 2개**: QC_Disposition(부적합 처분), QC_NCR_Action(NCR 처리 이력). QC-04·05 가 처분·이력을 남기지 않는다는 뜻이기도 합니다.
- **죽은 코드만 쓰는 5개**: `BondRepository`·`FabricRepository` 는 어디서도 생성되지 않습니다(IMG 는 원단·본딩을 다루지 않기로 함). 대상은 PR_BondSetup(2), PR_BondCycleLog(14), PR_FabricIssue(1), PR_FabricDeductionLog(14), PR_FabricIssueAttempt(0) 입니다.

**결정 필요**
- 읽기만 있는 테이블: 등록 화면·수집기를 만들지, 화면을 「시연용」으로 표시하거나 숨길지
- 아무도 안 쓰는 테이블: DROP 할지(두 리포지토리 삭제 포함). FK 가 없어 지워도 다른 테이블에는 영향이 없습니다.

### 1-4. 문제점 (43)
1. **[중] APS 재고 역산에서 WO 없이 만든 생산이 빠짐**
   - 위치: `ApsRepository.cs:900-923`(ReadProduced)`·953-976`(ReadWipStock)
   - 내용: 당일 실적을 WO 품번으로 묶어, WO 없는 생산과 BOM 자식 사출 실적이 빠집니다. 시작 재고가 부풀고 사출 소요가 적게 잡힙니다.
   - 제안: 실적 품번을 LOT 품번 → 단계 품번 → WO 품번 순으로(보고서와 같은 규칙) 정합니다.
2. **[중] PP-004 릴리스·취소가 승인 권한만 있는 역할에서 무반응**
   - 위치: `WorkOrder.razor:290·313`
   - 내용: 버튼은 승인 권한으로 보이는데, 처리 첫 줄은 수정 권한을 봅니다. 읽기+승인 역할은 눌러도 아무 일이 없고 안내도 없습니다.
   - 제안: 처리 첫 줄을 `_canApprove` 로 바꿉니다.
3. **[중] MRP 에서 같은 부족분 PR 이 두 번 생김**
   - 위치: `PpRepository.cs:1187-1259`, `Mrp.razor:399`
   - 내용: PR 을 만들 때 그 MRP 실행이 최신인지, 같은 품번의 열린 PR 이 있는지 보지 않습니다.
4. **[중] APS 「WO 생성」 수주 초과 발행 경합**
   - 위치: `PpRepository.Aps.cs:216·940-961·268·330·813-818`
   - 내용: 수주 잔량과 기존 슬롯을 잠금 없이 읽은 뒤에야 채번 잠금을 잡아, 두 실행이 겹치면 같은 수주를 초과 배정합니다.
5. **[중] WO 미리보기가 읽기 사용자에게도 쓰기 트랜잭션을 돌림**
   - 위치: `ApsWoPreview.razor:434-441·463-479`(갱신 버튼 `:62`)
   - 내용: 들어가자마자 dryRun(실제 INSERT 후 롤백)이 돌아 잠금을 잡고 WoID(IDENTITY)를 소모합니다(채번 범위 잠금은 `UX_PP_WorkOrder_WoNumber` 덕에 그 날 접두어 범위뿐이라 POP 확정은 막지 않는다). 「미리보기 갱신」에도 권한 검사가 없습니다.
6. **[중] 이미 IMG 에 쓰인 코어를 INJ 불량으로 등록할 수 있음**
   - 위치: `InjLotRepository.cs:577-625`(상태 검사 601-603)
   - 내용: 완제품은 양품인데 그 코어는 폐기되는 모순이 생깁니다.
   - 제안: 자식 IMG LOT 이 있으면 거부합니다.
7. **[중] 실적 테이블에 LotID 인덱스 없음**
    - 위치: `LotDefectWriter.cs:25`(→ `SP_POP_LOT_ReverseConfirmedResult_GetProductionResult`)(`InjLotRepository.cs:607`·`ImgLotRepository.cs:311` 에서 호출)
    - 내용: 확정 LOT 의 불량 등록이 LOT 잠금을 쥔 채 실적 전체를 훑습니다. 운영 기간에 비례해 느려집니다.
8. **[중] POP PLAN 과 INPUT/FINAL 의 하루 기준이 다름**
    - 위치: `migrate_pop_stored_procedures.sql:1719 vs 1732`(IMG 2088 vs 2101, PNT 991 vs 1004)
    - 내용: PLAN 은 07시 근무일, INPUT/FINAL 은 자정 기준이라 00~07시에는 수치가 어긋납니다.
9. **[하] 불량 역분개가 마감된 WO 를 다시 엶**
    - 위치: `WorkOrderRepository.cs:402-412`(BumpStepCompleted → `migrate_pop_stored_procedures.sql:273-328`, 재개방 292-293·315-316), `WorkOrderRepository.cs:287-299`(OpenStepOrder)
    - 내용: 다음 코어 스캔이 Released WO 대신 마감 WO 로 들어갑니다. CLAUDE.md 는 단계를 되돌리는 것을 의도된 동작으로 적고 있어, 다음 스캔이 그 WO 로 가는 것까지 결함으로 볼지는 결정이 필요합니다.
10. **[하] APS BOM 규칙 WO 의 다른 자식은 POP 에서 "WO 없음"**
    - 위치: `migrate_pop_stored_procedures.sql:1713-1714`(INJ PLAN), `WorkOrderRepository.cs:295-299`
    - 내용: 단계 품번이 첫 자식 하나라, 레일 등은 PLAN 0·WO 없음으로 보이고 WO 없이 확정됩니다. 보고서의 계획 품번도 첫 자식으로 몰립니다.
    - 제안: 슬롯 품번 칸 추가를 검토합니다.
11. **[하] 확정은 됐는데 "확정 실패" 로 표시**
    - 위치: `InjMain.razor:447-451`(catch 476-483), `ImgJudgePopup.razor:120-137`(catch 150)
    - 내용: 커밋 뒤의 WO 조회가 같은 try 안이라, 순간 끊김이면 실패로 보입니다. IMG 는 팝업이 남아 다시 OK 를 누르면 AlreadyConfirmed 가 됩니다.
12. **[하] QC 검사 품번이 상수**
    - 위치: `Qc01Incoming:147`, `Qc02InProcess:138`, `Qc03Final:138`, `Qc04Ncr:108`(기본값), `QcRepository.cs:93-104`(CreateInspection — LOT·WO·라인 인자 없음)
    - 내용: 모든 검사가 "DR-TRM-LH-A1" 로 저장되고 LOT·WO·라인이 없습니다.
13. **[하] POP 로그인이 웹 계정 잠금을 무력화**
    - 위치: `PopAuthService.cs:89` → `AuthRepository.cs:229-238`(RecordSuccessfulLogin → `migrate_pop_stored_procedures.sql:71`)
    - 내용: POP 로그인 성공(배지 포함)이 웹 비밀번호·PP-CAL PIN 실패 횟수를 0 으로 되돌립니다.
14. **[하] PP-001 주간 탭이 조회 오류를 숨김**
    - 위치: `Forecast.razor:484`(주간)`·371`(이력)`·394`(일별 이력)`·361`(고객) — 일별 조회(`:380`)만 오류를 안내
15. **[하] PP-002 일괄 확정이 건마다 따로 저장**
    - 위치: `SupplyPlanImport.razor:344-361`(355)
    - 내용: 중간에 실패하면 앞 건만 확정되고, 몇 건이 반영됐는지 안내가 없습니다.
16. **[하] PP-002~006 승인·생성 동작에 감사 로그 없음**
    - 내용: 수주 확정, WO 생성·릴리스·취소, PR 생성·전송·승인이 SYS-007 에 남지 않습니다. PP-002 「자동 확정」 스위치를 켤 때 일괄 확정되는 수주도 마찬가지입니다(스위치 켜고 끄기만 기록됨).
17. **[하] 단위 EA 고정·WO CSV 품번 누락·MRP 상태 색**
    - 위치: `WorkOrder.razor:162·368`, `PlanConfirmBatchDialog.razor:31`, `Mrp.razor:195·376`(PR 상태 색이 옛 상태값 Pending/Rejected 기준)
18. **[하] 행위자 매개변수 형식이 컬럼과 다름**
    - 위치: `PpRepository.Aps.cs:1028·1055·1073`, `ApsRepository.cs:1041·1214`, `WorkOrderRepository.cs:558`, `QcRepository.cs:67`(FinalizeInspection `@A` → ApproverID·ModifiedBy)(10-15 사람 컬럼 사번 전환 때 주간 구매계획·WO 릴리스·INJ/IMG 확정 매개변수는 고침)
    - 내용: varchar(20) 컬럼에 NVarChar 로 넘깁니다(기능 오류는 아님). `PpRepository.CreateShortagePrs` 는 `@By` 를 `VarChar, 20` 으로 선언하고 값은 `Trunc(by, 50)` 으로 자릅니다(`:1230` — 행위자가 20자 이하라 지금은 영향 없음).
19. **[하] APS WO 를 취소해도 계획 연결이 남음**
    - 위치: `WorkOrderRepository.cs:619-649`, 대상 선정 `PpRepository.Aps.cs:208`
    - 내용: 취소한 계획 행이 계속 '기발행' 으로 남아 다시 만들 수 없습니다.
20. **[하] IMG 라벨과 실적의 WO 가 다를 수 있음**
    - 위치: `ImgLotRepository.cs:111-120`(코어 스캔 WO)`·186-199`(라벨 수주처)`·212`(LOT WoID NULL — `migrate_pop_stored_procedures.sql:1887`)`·388-396`(판정 때 다시 해석)
21. **[하] QC 최종 판정 덮어쓰기**
    - 위치: `QcRepository.cs:60-69`(FinalizeInspection → `migrate_pop_stored_procedures.sql:1079-1082` UPDATE — `Verdict IS NULL` 가드 없음)
22. **[하] POP 창을 닫아도 세션이 열린 채 남음**
    - 위치: `Forms/PopBlazorForm.cs:63`(세션 닫기는 `AppRoot.razor:144` 로그아웃 경로뿐)
23. **[하] InjAgent 정지가 2초를 넘으면 상태 경합**
    - 위치: `Core/PollerRunner.cs:48-75`(69-70)
24. **[하] QC-07 이 실적 없는 라인의 LOT 불량을 빠뜨림**
    - 위치: `QcRepository.cs:221-236`(GetModuleDefectRates → `migrate_pop_stored_procedures.sql:1228-1257`)
25. **[하] 금형 시드가 일부 참조만 정리**
    - 위치: `dist/seed_md_mold_master.sql:22-24·137-146`
    - 내용: 다시 돌리면 MNT_MoldShotCount·PR_InjLot·PP_WorkOrder 에 옛 금형을 가리키는 행이 남습니다. 머리말은 `MNT_MoldShotCount` 를 "이력" 이라 남기지만 실제로는 현재 상태(CurrentShots·MountedEquipID) 테이블이고, `PP_WorkOrder.MoldID` 는 언급이 없습니다.
26. **[하] 안돈 고장번호가 한 달 999건을 넘으면 반복**
    - 위치: `migrate_pop_stored_procedures.sql:585-588`(SP_POP_ANDON_CallDepts_InsMNT_FailureRegister)
    - 내용: RIGHT 3자리로 읽어 1000번째는 `-000` 이 되고, 그 뒤 MAX 가 계속 999 라 `-000` 이 되풀이되며 웹 번호와 겹칠 수 있습니다.
    - 제안: 웹 `ClaimNumber` 와 같은 방식으로 맞춥니다.
27. **[중] 안돈 발동이 같은 라인의 열린 비가동을 보지 않고 새 행을 엶**
    - 위치: `migrate_pop_stored_procedures.sql:441-443`(SP_POP_ANDON_Raise 의 PP_LineDowntimeLog INSERT)
    - 내용: 같은 라인에 이미 열린 비가동(PP-DTL 웹 등록, 10-16 부터 웹 고장의 「라인 정지」)이 있어도 안돈이 또 하나를 열어, 두 행이 겹친 시간만큼 OEE·비가동 분석이 두 번 셉니다. 웹 쪽(PP-DTL 등록, MNT-002 라인 정지)은 겹침을 막거나 열린 행에 연결합니다.
    - 제안: 열린 행이 있으면 그 행의 `AndonID` 를 채워 연결하거나(종료는 안돈 종료 시각과 그 행 소유자 중 늦은 쪽), 최소한 안돈 행 시작 시각에 기존 열린 행을 닫습니다 — POP 코드 변경이라 이승욱과 방식을 맞춥니다.
28. **[중] BOM 자동 차감(백플러시)이 직접 자식만 봐서 서브조립 아래 자재가 빠지지 않음**(10-16 a6bbd59c)
    - 위치: `BackflushRepository.cs:100-111`(ReadBomRows → `migrate_pop_stored_procedures.sql:2460` — `ParentItemNo = @Item` 1단계), `BackflushRules.cs:31-49`
    - 내용: 완제품 → 서브조립(SUB, InjFlag 0) → 자재 구조에서 서브조립은 LOT 확정 단계가 없어, 그 아래 MATERIAL 은 어느 확정에서도 차감되지 않습니다(개발 DB 완제품 36개·자재 간선 88개). CLAUDE.md 는 "SUB 자식 = 코어" 로 전제하지만 InjFlag 0 인 SUB 는 코어가 아닙니다.
    - 제안: InjFlag 0 인 SUB 는 QtyPer 를 곱하며 내려가고 InjFlag 1 인 코어에서 멈춥니다(`CoreItemResolver.ReadByInjFlag`·APS BOM 다단계와 같은 규칙).
29. **[하] 확정된 적 없이 폐기된 LOT 의 자재가 차감되지 않음**(10-16 백플러시)
    - 위치: `ReworkRepository.cs:194-196`(Rework 만 차감)`·213-236`(Scrap 차감 없음), `LotDefectRules.cs:57`(WasNeverConfirmed)
    - 내용: 로봇 NG 샷처럼 RAW/NG_BLOCKED → DEFECT → SCRAPPED 로 끝난 LOT 은 레진을 실제로 썼는데 차감 기록이 없어 라인 재고가 폐기율만큼 부풉니다. CLAUDE.md 는 "되돌리지 않는다" 만 적고 이 경우는 정하지 않았습니다.
    - 제안: 폐기 때 차감할지 사용자 결정을 받습니다.
30. **[하] 백플러시 멱등 인덱스 정의가 스키마 파일과 두 DB 에서 다름**
    - 위치: `dist/AMES_Schema.sql:9344-9348`(`IX_WH_InventoryTransaction_Source` — SourceType·SourceID 두 열)·`:12031-12032`(같은 이름의 넓은 정의 — 건너뜀), `migrate_wh_inventory_backflush_placeholder.sql:32-34`(두 열)
    - 내용: 개발·로컬 DB 는 SourceType·SourceID·TransactionType + 포함 열 5개입니다. 스키마로 새로 만든 DB 는 구조 서명이 개발 DB 와 어긋납니다.
    - 제안: 마이그레이션이 넓은 정의로 만들게 바꾸고 스키마 파일을 다시 뜹니다.
31. **[하] CLAUDE.md 백플러시 문단의 MRP 설명이 코드와 다름**
    - 위치: CLAUDE.md "MRP 가용재고 … 음수 자리표시까지 더한 순수량"(백플러시 문단), 실제 `PpRepository.cs:1021-1027`
    - 내용: MRP 재고는 `Qty>0 AND AreaCode='MAT_AREA'` 라 라인 위치(I1·W1 등)와 음수 자리표시가 빠집니다. 라인이 불출보다 더 쓴 양은 MRP 에 보이지 않습니다(WH-AUTO 라인 보충 `WarehouseRepository.LineStock.cs:147` 은 음수를 포함해 맞음).
    - 제안: 문구를 코드에 맞추고, MRP 반영이 필요한지 정합니다.
32. **[하] Pop 개발 설정에 개인 장치값이 커밋됨**(10-16 e528cbca)
    - 위치: `src/03_Pop/AMES.Pop/appsettings.Development.json:9-19`(Printer `Mode: Tcp`·`192.168.10.20:9100`, Scanner `COM5`)
    - 내용: 어느 PC 에서든 `dotnet run` 하면 실제 네트워크 프린터로 ZPL 을 보내고 COM5 를 엽니다. CLAUDE.md 의 "포트 리포지토리 기본은 빈 값" 과도 어긋납니다(비밀번호는 기본 파일에 이미 있던 값이라 새 노출은 아님).
    - 제안: 저장소에는 File 모드·빈 포트를 두고 장치값은 커밋하지 않는 로컬 파일이나 환경변수로 줍니다.
33. **[하] PP→POP 시험 초기화 스크립트가 다른 데이터와 어긋나게 지움**(개발 전용, 10-16 943cdf24)
    - 위치: `tools/test_pp_pop/test_pp_pop_reset_dev.sql:374`
    - 내용: 10-17 기준 끊긴 `DowntimeID` 는 0건이라 실제 피해는 아직 없습니다. `PP_LineDowntimeLog` 를 전부 지워 고장(라인 정지)의 `DowntimeID` 가 없는 행을 가리키고, LOT 은 지우면서 BACKFLUSH 거래·`BF-` 자리표시 재고는 남기며, 공용 개발서버의 다른 개발자 PP 데이터도 통째로 지웁니다.
      안돈 고장(`MNT_FailureRegister` Source='ANDON')·조치 이력은 지우면서 `MNT_WorkOrder` 는 다루지 않아, 10-16 부터 MNT-002 「작업지시 발행」으로 만든 작업지시가 지워진 고장번호(`SourceRefID`)를 가리킨 채 남습니다(`:376-379`).
    - 제안: 비가동은 안돈 행만 지우거나 고장의 `DowntimeID` 도 비우고, 그 고장의 작업지시도 같이 지우며, 백플러시 거래·자리표시를 같이 되돌리고, 실행 전 서버 확인 가드를 둡니다.
34. **[중] 감싸기가 코어만 소모해 두 번째 사출품(레일 등) 재고가 줄지 않음**(10-22 흐름 점검)
    - 위치: `ImgLotRepository.cs:134-136`(코어 하나만 연결·`ConsumeCore`), `PalletStockRepository.cs:87-103`, `BackflushRules.cs:38`(MATERIAL 만)
    - 내용: 완제품 77개 중 31개가 사출품 2개(코어 + 레일)입니다(로컬 DB 다단계 BOM). 레일도 INJ 확정 때 팔레트 재고가 되지만 IMG 는 코어만 0 으로 내리고 백플러시는 SUB 자식을 빼므로 레일은 쌓이기만 합니다 → APS 가 레일 재고가 충분하다고 보고 레일 사출을 줄입니다.
    - 제안: IMG LOT 생성(또는 판정 OK) 때 코어가 아닌 InjFlag 1 자식을 QtyPer 만큼 FIFO 로 소모합니다(위치 규칙은 안영훈 2-4 4 와 함께).
35. **[중] 헤더가 Closed 인 WO 의 열린 사출 단계로 실적이 들어감**(10-22 흐름 점검)
    - 위치: `WorkOrderRepository.cs:281-292`(OpenStepWhere·OpenStepOrder), `migrate_pop_stored_procedures.sql:214-237`·`1561-1577`, `BumpStepCompleted` `migrate_pop_stored_procedures.sql:313-322`
    - 내용: 한 코어를 여러 색상 완제품 WO(A·B)가 쓰면, 사출 스캔은 In Progress 인 WO-A 로 몰리고 감싸기는 WO-B 의 IMG 단계를 끝내 WO-B 헤더가 Closed 가 됩니다(INJ 단계는 Released 로 남음). 열린 WO 조건은 Cancelled 만 빼므로 WO-A 가 차면 다음 스캔이 마감된 WO-B 로 가고, 그 단계가 In Progress 가 돼 계속 먼저 받습니다. 문제점 9(불량 역분개 재개방)과 원인이 다릅니다.
    - 제안: 열린 WO 조건에서 헤더 Closed 를 빼거나, 헤더가 Closed 될 때 남은 단계도 닫습니다.
36. **[중] In Progress WO 의 취소·강제 마감이 없고 끝난 WO 의 남은 슬롯이 정리되지 않음**(10-22 흐름 점검)
    - 위치: `WorkOrderRepository.cs:619-649`(취소는 Draft·Planned·Released 만, 단계 행 잠금 없음), `WorkOrder.razor:120-131`, `LineScheduleRepository.cs:353-357`(능력이 WO 상태와 무관하게 슬롯을 셈)
    - 내용: 실적이 일부 오른 WO 를 멈출 방법이 없어 남은 슬롯이 POP PLAN·PP-003/APS 능력·기발행 수량에 계속 잡힙니다. 계획보다 일찍 Closed 된 WO 의 미래 슬롯도 남습니다. 취소가 단계 행을 잠그지 않아, POP 확정이 단계를 찾은 직후 취소가 커밋되면 실적이 취소 WO 에 붙습니다(라우팅 C 는 마지막 단계라 Cancelled → Closed). 미해결 8(재작업이 취소 WO 에 귀속)과 같은 계열.
    - 제안: 「강제 마감」 전이를 두고 마감·취소 때 오늘 이후 슬롯을 정리하며 단계 행도 잠급니다.
37. **[중] `PP_WorkOrder.MoldID` 를 어떤 계획 경로도 채우지 않는데 POP 이 이 값을 씀**(10-22 흐름 점검)
    - 위치: WO INSERT `PpRepository.cs:894-912`·`PpRepository.Aps.cs:101-104`(금형은 슬롯 `PP_LineSchedule.MoldID` 에만), 사용처 `InjMain.razor:316-327·541-542`, `InjLotRepository.cs:164-180`
    - 내용: 로컬 DB WO 13건이 모두 MoldID NULL — INJ-MAIN 금형 샷 칩이 늘 「—」, 수동 라벨 LOT 의 금형·캐비티가 NULL, 확정 실적의 금형이 비고 캐비티 기준 사이클이 0.
    - 제안: POP 이 그 라인 오늘 슬롯의 금형(없으면 설비 장착 금형)을 씁니다.
38. **[중] PP-003 이 LH/RH 를 동시에 찍는 금형을 따로 계산해 사출 시간이 두 배**(10-22 흐름 점검)
    - 위치: `Scheduling/DeadlinePacker.cs:218-222`(분/EA = BOP 사이클), `PpRepository.cs:724-837`(형제 금형 처리 없음) ↔ APS `PpRepository.Aps.cs:355-370`
    - 내용: 2캐비티 금형의 LH·RH 수주 각 100개를 PP-003 으로 만들면 100분 + 100분 슬롯이 생깁니다(실제 100분) → Late·Shortfall 과다.
    - 제안: PP-003 도 같은 금형·색상 형제를 0분 행으로 묶습니다(APS 규칙과 같게).
39. **[중] 팔레트에 「닫힘」이 없고 적입수량이 거의 비어 있어 계속 실림**(10-22 흐름 점검) — 보류 「팔레트 적재 범위 밖」 ②·④ 관련
    - 위치: `PalletStockRepository.cs:55-80`(`ReadState` `:107-131`), `PalletLoadRules.cs:65`(PalletQty NULL = 제한 없음)
    - 내용: 이미 FG 위치를 받았거나 출하 배차에 실린 팔레트에도 POP 이 계속 실어, 새로 생긴 위치 없는 행 때문에 그 팔레트 출고가 52006·배차 완료가 「contents changed」 로 막힙니다. 로컬 DB 사출품 56개 중 53개·완제품 77개 중 30개가 `PalletQty` 가 없어 「가득 참」·「새 팔레트를 스캔하세요」가 나오지 않습니다(APS 포장 단위도 1).
    - 제안: 자식 행에 위치가 있거나 배차에 실린 팔레트는 적재 거부. 적입수량 등록은 MD-003 데이터 작업.
40. **[중] PP-003·APS·MRP 의 재고 기준이 서로 다름**(10-22 흐름 점검)
    - 위치: PP-003 `PpRepository.cs:585-589`·`895-899`(FG 구역 위치만) ↔ APS `ApsRepository.cs:789-794`(위치 무관), MRP `PpRepository.cs:1019-1025`(`MD_Location` INNER JOIN — 위치 NULL 제외)
    - 내용: POP 이 실었지만 아직 입고하지 않은 완제품을 PP-003 순수요는 재고로 보지 않고 APS 는 봐서 두 화면의 WO 수량이 다릅니다(PP-003 쪽 중복 WO). 입고만 하고 적재 전(위치 NULL)인 자재(로컬 15행·501)는 재고에도 발주중에도 없어 MRP 가 가짜 부족·중복 PR 을 냅니다. 문제점 31(MRP 가 라인 위치 제외)와 별개.
    - 제안: 위치 미배정 재고를 재고로 볼지 정하고 세 계산을 맞춥니다.
41. **[중] 사출 확정마다 그 설비의 LOT 이력 전체를 훑어 사이클을 구함**(10-22 흐름 점검)
    - 위치: `SP_POP_INJ_ConfirmByLotCode_GetLot2`(`migrate_pop_stored_procedures.sql:1591-1601`), 호출 `InjLotRepository.cs:466-474`
    - 내용: `MAX(CreatedTS) WHERE EquipID·CavityPos·CreatedTS < @ThisTs` — 로컬 실행계획에서 설비로 seek 한 뒤 그 설비 LOT 마다 `tbl_Lot` PK 조회. LOT·팔레트·단계 잠금을 쥔 채 돌아, 1년이면 설비당 약 150만 건(추정)을 매 스캔마다 읽어 확정이 느려지다 30초 제한에 걸릴 수 있습니다. 문제점 7(실적 LotID 인덱스)과 별개.
    - 제안: `pe.LotID < @ThisLot ORDER BY pe.LotID DESC` TOP 1 로 읽습니다.
42. **[중] 감싸기에서 잘못 발행한 LOT 을 취소할 수 없어 코어가 영구 소모됨**(10-22 흐름 점검)
    - 위치: `ImgLotRepository.cs:85-147`, `ImgMain.razor:303·493`, `SP_POP_IMG_FindCoreUser`(`migrate_pop_stored_procedures.sql:1811-1819` — DEFECT·SCRAPPED 자식도 사용으로 셈)
    - 내용: 좌측 기본 선택이 다른 색이면 그 색으로 라벨이 나가고 코어 재고는 0 이 됩니다. 남는 길은 NG 등록 → 폐기뿐이라 멀쩡한 코어·제품이 불량·폐기로 집계됩니다.
    - 제안: 판정 전(RAW) IMG LOT 의 발행 취소(LOT VOID·코어 연결 해제·`IMG_CORE` 역거래)를 둡니다.
43. **[하] 흐름 점검 묶음**(10-22 흐름 점검)
    - LOT 순번이 라인·일 9999 에서 막힘(`LotNoGenerator.cs:57-58` — 2캐비티 약 17초 이하 사이클이면 도달, 에이전트는 재시도 없음 — 미해결 1·7 과 겹침).
    - 불량 등록 뒤에도 `tbl_Lot.Status` 가 CONFIRMED(`migrate_pop_stored_procedures.sql:1670-1682` 은 QualityFlag 만) — QC 추적성이 CONFIRMED 로 표시.
    - `ConsumeCore` OUT 거래에 위치가 남지 않음(`PalletStockRepository.cs:87-103`).
    - 팔레트 적재가 수주처·고객 전용 팔레트(`MD_Pallet.CustomerID`)·내부용을 보지 않음 — 수주처가 다른 LOT 이 섞이면 팔레트 출고가 52018 로 거부. 정책 결정 필요.
    - 이미 출하된 LOT 도 POP 불량 등록이 됨(실적 −1·재작업 대기열).
    - 잠재: APS BOM 규칙 WO 의 INJ 단계 라인은 품번순 첫 자식(레일), 단계 품번은 코어라 라인이 갈리면 코어 사출이 WO 를 못 찾음(`PpRepository.Aps.cs:169·334-341`), 완제품 단계 라인이 비면 생성 전체 롤백. 단계 마감 기준이 모든 단계에서 헤더 OrderQty 라 QtyPer ≠ 1 자식이 생기면 일찍 Closed(`migrate_pop_stored_procedures.sql:292-293`).
    - WO 취소가 지난 날짜 슬롯까지 지우고(일정 준수 보고서에서 사라짐) 단계는 Released 로 남김(`WorkOrderRepository.cs:629-631`).
    - 열린 WO 규칙이 C# 상수(쓰는 곳은 테스트용 `ListForLine` 뿐)와 SP 7곳(`migrate_pop_stored_procedures.sql:214·1572·1776·1830·1994`·일별 요약 2곳)에 복사돼 하나만 고치면 화면의 활성 WO 와 실적이 붙는 WO 가 어긋남.
    - 마스터 미비를 조용히 넘김(로컬 실측): 사출품 19개에 INJ 스테이션 BOP 없음, `83314-P8000`·`P8010` 은 BOP 라인(INJ-01)과 금형 라인(INJ-02)이 다르고 금형 후보 조회(`MasterDataRepository.cs:2199-2222`)가 라인 미배정 금형도 돌려줘 UPH 없는 라인에 배치될 수 있음, PP-004 수동 릴리스는 금형·BOP 를 확인하지 않음.
    - 추정: 라우팅 B 에서 도장품 BOM 에 SUB 가 없으면 INJ 단계 품번이 도장 완제품(ASSY)이 돼 도장 전 부품이 완제품으로 FG 입고·출하될 수 있음(`WorkOrderRepository.cs:81-90`) — 10-22 PNT-MAIN 으로 B 경로(사출 → 도장 라벨 발행·판정·확정)는 이어졌으므로 도장품 BOM 에는 사출품(InjFlag 1) 자식을 반드시 둘 것(없으면 코어 해석이 완제품 자신을 돌려준다).

---

## 2. 안영훈 — PDA·Api + Web WH·FG·SCM·Portal + 재구축 스크립트

### 2-1. 적용·검증 대기·요청 (10)
1. **재구축 스크립트 — 파일 수정 완료, 전체 재구축 검증 대기**
   - 10-23: `AMES_Schema.sql`의 자동 생성 `DF__...__8자리hex` 기본값 이름 37개를 익명 DEFAULT로 바꾸고, `UX_PR_PopSession_BearerHash` 중복 생성을 제거했습니다. `tools/schema_from_db`도 같은 기본값 이름과 은퇴한 WH/FG/SP PDA 업무 프로시저를 재출력하지 않도록 수정했습니다(개발 TEST_RESET은 유지).
   - 화면 마이그레이션은 현재 라우트에 맞췄습니다. Portal의 `due-orders`·숨겨진 `delivery-notes`/`receipts` 별칭과 기존 PORTAL-001~006 식별자를 보존하고, SCM-003 Delivery Progress·004 Purchase Items·005 Portal Users로 등록합니다. 화면 표시용 Packing Quantities 코드는 기존처럼 PORTAL-004입니다. 기존 ScreenID·역할 권한을 임시 번호로 이동하는 보호는 유지했습니다.
   - 최신 스키마의 사출 4개 테이블·인덱스를 DROP 후 옛 구조로 덮던 `migrate_inj_agent.sql`을 **재구축 실행 목록에서 제외**했습니다. 정본 `AMES_Schema.sql`에 해당 정의가 모두 있습니다. 구형 파일 자체를 운영 DB에 실행하지 않습니다.
   - 개발 시드는 기본 제외이며 기존 opt-in을 유지합니다. `migrate_mnt_failure_downtime.sql`의 컬럼 부재 오류 수정은 기존 이승욱 변경을 유지합니다.
   - 생성기 빌드·정적 회귀 검사는 확인하되, 사용자 지시대로 DB 삭제·재생성·시드·전체 재구축은 실행하지 않습니다. 백플러시 멱등 인덱스 등 다른 담당자 검증 항목은 해당 절을 따릅니다. 생성기의 기존 전이 의존성 보안 경고는 2-4에 별도로 남깁니다.
2. **동시 출고·보충 Worker 부하 검증 대기**
   - 출고의 Serializable을 ReadCommitted로 낮추고 LOT 비교의 UPPER를 제거했습니다. 재고 행 변경 잠금과 피킹 지시 잠금은 유지합니다.
   - 동일 품목 여러 줄의 수요 분배 SQL은 읽기 전용 회귀 검사로 확인했습니다. 실제 PDA 여러 대와 보충 Worker를 겹쳐 실행하는 검증은 아직 하지 않았습니다.
3. **PDA 예비품 출고(SP-07) 보전 작업지시 연결 — API/PDA 배포·실출고 검증 대기**
   - `GET /api/wh/sp/work-orders`는 미완료 작업지시만 DB에서 조회하고 `PmRules.PlannedWoSql`로 아직 먼 예정 PM을 제외합니다. CHECK·ANDON 등 출처는 제한하지 않습니다.
   - PDA 출고에서 작업지시를 선택할 수 있고, 스캔한 예비품의 설비와 일치하는 것을 위에 보입니다. 선택은 연속 출고·Clear 동안 유지하며 선택 해제로 일반 출고가 가능합니다.
   - 요청의 `WorkOrderNo`는 선택입니다. 미선택은 기존 `RefType='PDA'`, 선택은 `RefType='MWO'`·`RefID`=작업지시 번호로 기록하며 출고·감사 메모에 번호를 포함합니다. 웹의 기존 `ListWoPartReleases`가 그대로 읽습니다.
   - 애플리케이션 SQL에서 작업지시를 재고보다 먼저 잠그고 존재·미완료·예정 여부를 확인합니다. 연결 출고의 취소도 완료·종료된 작업지시는 거부하고 원거래를 다시 확인합니다.
   - PM·CHECK만 받는 보전 작업 화면 API와 별개로 SP 전용 목록을 사용하므로 CM·ANDON 작업지시도 선택할 수 있습니다. 시리얼 없는 기존 테스트 재고는 변환하지 않습니다.
   - 프로시저를 추가·변경하지 않습니다. 배포 순서는 API(Contracts/Data 포함) → PDA이며 구 PDA의 작업지시 없는 출고도 호환합니다. 실제 재고 출고·취소 및 동시에 완료하는 테스트는 아직 실행하지 않았습니다.
4. **[요청] 재구축 목록에 MNT 마이그레이션 3개 추가됨 — 전체 재구축 검증에 포함**(10-16 이승욱)
   - `dist/rebuild_db.sh` 에 `migrate_mnt_pm_checklist.sql` → `migrate_mnt_failure_downtime.sql` → `migrate_mnt_pm_lead_days.sql` 순서로 넣었습니다(모두 재실행 안전, 스키마 파일은 이미 적용 뒤 구조라 대부분 건너뜁니다). 위 1(전체 재구축 검증) 때 같이 확인합니다.
   - `migrate_mnt_failure_downtime.sql` 은 `MNT_WorkOrder.DowntimeID` 를 지우고, 값이 있으면 THROW 합니다(개발 DB 는 10-17 기준 이미 컬럼이 없음 — 이미 적용된 상태). 목록에 있다는 이유만으로 재구축하거나 이 스크립트를 실행하지 않습니다.
5. **[알림·코드 반영] AMES.Api 응답 필드·집계 규칙 — 새 개발 요청 아님**(10-16 이승욱)
   - `/api/mnt` 설비 목록·대시보드: 설비 상태를 고장·PM 작업지시로 계산합니다(중지 = 안돈·CRITICAL·라인 정지 고장, 예방보전 = 착수된 설비 PM — 보전 PM 제외). 열린 작업지시 수에서 「예정」 PM 작업지시는 뺍니다. 대시보드 KPI 에 `PmDueEquip`·`PmDueMaint` 가 붙습니다.
   - 작업지시 행(`MwoRow`)에 `PmClass`·`PmDueDate`·`Planned`·`TaskFail`·`PmNeedsNextDue` 가 붙습니다(뒤에 더한 필드라 기존 PDA 는 영향 없음).
   - 현재 PDA의 `/api/mnt/pda/*`는 `MntRepository`의 작업지시·완료 규칙을 이미 사용합니다. 별도 동일 기능을 만들지 않으며 서버 반영 여부만 게시본으로 확인합니다(`PdaMaintenanceEndpoints.cs`, `PdaMaintenanceRepository.cs`).
6. **PDA 일반 업무 직접 SQL 전환·구형 정의 정리 — 서버 최종 배포·통합 검증 대기**
   - PDA WH·FG·SP의 조회·입고·적재·출고·조정·반품·예비품 라벨 생성은 애플리케이션의 매개변수 SQL로 처리합니다. 케이스 일괄 입고의 공통 저장소도 같은 경로를 사용합니다.
   - 사용자가 API 게시본을 교체했고 응답을 확인했습니다. 서버에 어느 수정까지 설치됐는지는 DLL 버전·해시를 아직 대조하지 않았으므로 추가 교체가 필요하다고 단정하지 않습니다. `/api/health`의 `ok`는 접속·DB 확인이지 최신 커밋 확인이 아닙니다. 웹의 공통 케이스 입고 경로도 설치된 AMES.Data를 확인한 뒤 필요한 게시본만 교체합니다. API 응답 형식은 유지했습니다.
   - 개발용 테스트 초기화 프로시저, SYS 공장 달력·SQL Server 잠금 기능은 변경하지 않았습니다. 기존 DB 프로시저 삭제·재구축은 하지 않았습니다.
   - 신규·구형 스키마/마이그레이션 7개에서 구형 PDA 업무 프로시저 정의 96개(중복 포함)를 제거하고, 테스트는 직접 SQL 기준으로 전환했습니다. 초기화 프로시저 6종은 유지합니다. 실제 DB 프로시저 삭제는 전체 API·Web 배포 확인 전까지 보류합니다.
   - 세션 만료 알럿은 WH/FG 다크 모달과 통일했습니다. 공통 PDA 주소를 개발 PC(.133)에서 서버 Tailscale 주소로 복구하고 Honeywell을 재설치했습니다.
   - 0수량 재고는 최근 입고 취소 이력이 확인될 때만 재사용하고, 출고/소진된 LOT 재입고는 차단합니다. 취소 시 PO·납품 누적 입고량은 조정 후 잔량이 아닌 원래 박스 입고량으로 차감합니다.
   - BOX-56 취소·재입고의 누적 입고량 오차는 사용자 승인으로 PoID 522·DeliveryLineID 30만 175→170 보정하고 감사 기록을 남겼습니다. 재고·재고 거래 이력은 변경하지 않았습니다.
   - 10-16 예비품 연동 추가 후 SQL 27개 컴파일(NOEXEC), 열린 작업지시 목록 읽기, API 214건·관련 Data 47건·SP 소스 검증과 API/PDA 빌드를 확인했습니다. 이는 개발 코드의 검증이며 서버 최신 배포나 재고 쓰기 통합 검증을 대신하지 않습니다. FG 반품·동시 PDA 쓰기 검증은 남아 있습니다.
   - WH 입고 예정 조회·공통 팔렛 입고의 구 프로시저 매개변수 태그는 명시 형식으로 대체했습니다. 일반 LOT 취소는 원입고 거래량만 PO에서 되돌리고 현재 잔량만 제거하며, 출고·소비 이력이나 재고/PO 누적량 불일치는 차단합니다. 30입고→25조정→취소→재입고 회귀 테스트를 별도 SQL opt-in 환경에 추가했으나 실제 DB 쓰기는 실행하지 않았습니다.
   - `FG_Shipment.LotNo` 및 출하 오더·DCR·라인 위치 구조는 `f6530138`·`5ecef7aa`·`300d78f1`에 포함되어 더 이상 저장소 미반영 결함이 아닙니다. RPT-003·FG 출하는 기존 Radzen·공통 카드·다국어·단위별 합계로 맞췄습니다. 서버의 설치 DLL과 웹/PDA 실제 화면 확인은 별개로 남아 있습니다.
7. **빈 팔렛 회수 — API/PDA 배포·실물 검증 대기**
   - 사용자 결정: 제품 반품과 빈 팔렛 회수는 별개입니다. PDA `FG → Empty Pallet Return`에서 물리 팔렛 바코드를 스캔·확인하고, `MD_Pallet.ShippedFlag=0`·DB 시각 `ReturnedAt`을 기록합니다. 부품 재고·고객 반품은 변경하지 않습니다.
   - 미출하·비활성·중복 회수, 스캔 이후 출하 시각 변경, 팔렛에 연결된 양수 재고가 남은 경우는 차단합니다. 따로 반품돼 연결이 해제된 부품의 과거 PalletNo만으로는 빈 팔렛 회수를 막지 않습니다. 회수 상태와 SYS 감사 기록은 같은 트랜잭션으로 저장하며 다음 출하 때 ReturnedAt은 비웁니다.
   - 기존 MD_Pallet 컬럼을 사용합니다. 신규 테이블·DB 재구축·실제 회수 처리는 실행하지 않았습니다. API(Contracts/Data 포함) → PDA 배포 후 실물 빈 팔렛과 중복·내용물 잔존 사례를 확인해야 합니다.
8. **로케이션 2단계 — 재고 위치 차단 코드 반영, 서버 검증 대기**
   - 상위 `LocationLevel=1`은 집계/분류용이고 하위 `LocationLevel=2`만 재고 위치입니다. WH 입고·위치 이동·수량 조정과 FG 적재/조정, 라인 목적지 판정, 자동 피킹 원재고에서 상위를 차단했습니다. 입고·이동은 같은 트랜잭션에서 위치 행을 잠가 검사합니다. 반품은 기존대로 위치 미배정이며 상위 위치를 받는 입력 자체가 없습니다.
   - **정정:** Web 지도는 이미 `LocationMapDatabase.Load`에서 하위만 표시하고 있었습니다. PDA 위치 목록·스캔 조회에도 하위 조건을 넣었습니다. 기존 DB의 상위/재고 데이터를 바꾸거나 임시 시드를 다시 돌리지는 않았습니다.
   - **부분 구현 정정:** 신규 Area는 상위와 첫 하위를 함께 만들고, 격자 추가도 `{prefix}AREA` 상위를 연결하는 코드가 이미 있습니다. 기존 Area의 임시 상위처럼 이름이 다른 경우 자동 연결이 안 될 수 있고, 지도에서 상위를 직접 고르는 UI는 없습니다. MD-018의 기존 Parent 선택으로 지정할 수 있으며, 다수 상위 중 기본 선택 규칙/지도 선택 UI는 별도 확인이 필요합니다. 새 Create 화면을 임의로 추가하지 않았습니다.
9. **PDA FG 팔렛 단위 위치 배정 — 기존 구현 확인, API/PDA 배포·실물 검증 대기**
   - `FgEndpoints`와 Fg01은 팔렛 바코드의 하위 LOT을 조회하고, 미배정 ASSY 전부를 하나의 위치로 원자적으로 배정합니다. 확정 시 내용물·수량 재검사, 용량 검사, LOT별 MOVE 기록, ASSY 아닌 내용물 차단을 유지합니다.
   - 「현재 LOT 하나씩만 가능」은 이전 판 기준 지적이므로 정정했습니다. 서버의 최신 팔렛 경로 배포와 실물 저장 검증은 별도이며, 출고·위치·수량 검사도 그대로 유지합니다.
10. **10-23 실제 결함 수정 — 배포·쓰기/동시성 검증 대기**
   - IMG 라벨은 공통 `ImgScanParser`로 LOT 9자를 동일하게 해석합니다(입고·출고·반품, 제어문자 보존/제거 모두). 잘못된 라벨은 거부합니다.
   - FG 대기는 양수 재고라도 위치 미배정이면 포함합니다. 생산 공정 양품과 `RWK` 수리 양품을 함께 합산하며 ASSY 제한은 유지합니다. Web 위치별 재고·RPT 조회도 미배정 재고를 누락하지 않고 컨테이너 합계를 중복하지 않게 했습니다. PDA 통합 재고는 이미 미배정 ASSY를 볼 수 있어 새 기능을 중복 추가하지 않았습니다.
   - 백플러시는 `MD_Location.LineID`의 하위 위치와 기존 LotPrefix 위치를 FIFO로 읽고 실제 차감 위치를 거래에 기록합니다. 기존 POP 프로시저는 변경하지 않고 조회만 애플리케이션 SQL로 대체했습니다. 부족분 자리표시의 기존 LotPrefix 규칙은 유지합니다. WH-AUTO는 단위를 품목 UOM으로 표시하고 조회 격리는 ReadCommitted로 낮추되 작업 잠금은 유지합니다.
   - 납품 박스 취소는 원입고·현재 재고·수량 조정·PO/DL 누적량을 검증하고 출고/백플러시/라인 이동을 거부합니다. 일반 창고 수량 조정 후 취소는 기존 합의대로 허용합니다. 입고 PO 상태를 Partial/Received로 맞추고 취소/종료 PO 입고를 차단합니다.
   - 일반 LOT 자재 입고는 생산 LOT·미등록/비활성 품목을 거부합니다. 같은 파트의 미입고 PO에 공급사가 여럿이거나 선택한 PO 잔량을 넘으면 자동 연결 대신 공급사 Unit Item 바코드를 요구합니다. 공급사별 실제 납품 연결이 있는 경우는 기존 Portal 박스 경로를 사용합니다.
   - 낱개 LOT 출고는 실물 팔렛을 Shipped로 바꾸지 않습니다. 제품 반품은 ParentLotNo와 PalletNo를 모두 비우고 빈 팔렛 회수와 분리합니다. DCR/개별 팔렛 출고는 POP처럼 MD_Pallet을 재고보다 먼저 잠급니다.
   - 로컬 DCR 출하가 있는 수주는 PO Sync/엑셀 재수집이 현재 ShippedQty를 낮추지 못하도록 보호합니다. 출하 확정과 같은 작업 잠금을 먼저 잡습니다. 외부 수량과 로컬 출하의 전면 정산/수동 하향 보정 정책은 2-2에 남깁니다.
   - 검증: API 테스트 228건, 관련 Data 테스트 82건(192.168.0.132/AMES_DEV의 SQL NOEXEC 컴파일 8건 포함) 통과, Web/API·스키마 생성기 빌드 통과입니다. 실제 입고·취소·출고·반품·생산 확정, 다중 PDA/Worker 부하 검증은 실행하지 않았습니다. Web/API와 POP의 AMES.Data(백플러시 실행 경로) 배포가 필요하며 DB 재구축·재고 삭제·서버 배포·퍼블리시 교체는 이번에 하지 않았습니다.

### 2-2. 보류 (7)
- **PO 자동 수집 정합성**: 고객 SRM에서 취소·삭제된 PO 라인은 조회에서 사라질 뿐 AMES에는 Open으로 남습니다. 이미 Confirmed인 수주의 수량·납기 변경도 덮어씁니다. 누락을 취소로 간주할지, 확정분 변경을 승인 대기로 둘지 업무 규칙 결정이 필요합니다. 로컬 출하가 있는 수주의 ShippedQty 하향 덮어쓰기는 막았지만, SRM 수량과 로컬 출하의 전면 정산·수동 하향 보정 승인 절차는 별도 결정입니다.
- **배지 단독 로그인 정책**: 사용자 요청대로 EOS 양식 QR은 PIN 없이 로그인합니다. 임의 사번 문자열 로그인은 차단했고 로그인 횟수 제한·브라우저 CORS 기본 차단을 추가했지만, EOS QR 자체는 서명된 인증 수단이 아닙니다. 관리자 배지 복제 방지를 위한 서명/PIN 도입은 정책 결정 대기입니다.
- **PO 자동 수집 실패**: 고객 테스트 서버 환경 문제로 09-17부터 실패합니다. 일별 구매계획 수집(`DPSYNC-SEMS`)도 URL이 자리표시자로 보고되어 외부 서버 복구·접속 정보 확인이 필요합니다.
- **개발 완료 시 정리**: appsettings 비밀번호 평문을 접속문자열 환경변수로 옮깁니다. CORS가 필요한 브라우저 클라이언트는 `Cors:AllowedOrigins`에 명시적으로 등록합니다(네이티브 PDA에는 CORS가 필요 없습니다).
- **고객 반품 판정 정책**: 현재는 제품을 위치 미배정 재고로 받고 Set Location으로 재배정하며, 빈 팔렛 회수와는 별개입니다. 「반드시 검사 후 재입고/폐기 판정」은 아직 합의되지 않은 새 업무 단계이므로 강제하지 않았습니다. Return Open 이후 상태·APS 가용 수량·검사/폐기 정책을 정한 뒤 함께 구현해야 합니다.
- **FG FIFO·고객 혼적 정책**: 현재는 스캔한 가용 재고·FG 위치·출하 고객을 검증하지만 FIFO 순서와 위치 단위 단일 고객은 강제하지 않습니다. 위치 고객 조회의 NULL을 실제 혼적 검사가 된 것으로 보지 않습니다. 사용자 요청은 ASSY 여부/위치/용량 확인이므로, 고객 전용 위치나 FIFO 강제를 임의로 추가하지 않습니다.
- **라인 실사·소수/음수 재고 정책**: 기존 PDA는 정수 수량 조정이며 BF 부족분은 별도 음수 자리표시입니다. 소수 단위 자재 실사와 BF 부족분 정산을 지원할지는 실제 재고/생산 소요를 구분해 결정해야 합니다. 음수 BF 행을 일반 스캔 재고로 노출하거나 생산 소요를 지우는 수정을 임의로 하지 않았습니다.

### 2-3. 테이블 (10-14 전수 조사)
| 테이블 | 상태 | 내용 |
|---|---|---|
| `PP_SupplyPlan`·`PP_SupplyPlanDetail` | 읽기만 | 구 공급 계획 API가 읽습니다. 외부 사용 여부 확인 전에는 엔드포인트·테이블을 삭제하지 않습니다. |
| `WH_ReleaseSchedule` | 미사용 | DROP 후보입니다. 이번에는 다른 WH 테이블과 식별자·행위자 길이만 맞췄고 테이블은 보존했습니다. |
| `PP_MaterialReservation` | 미사용 경로 | `ListPickingSlipCandidates` 호출처와 예약 생성 경로가 없습니다. 삭제 또는 예약 기능 설계 결정 대기입니다. |

### 2-4. 유효한 문제점 (1)

기존 10-22 점검의 코드 결함은 수정해 2-1 10의 배포·검증 대기로 옮겼고, 업무 정책과 맞지 않거나 미결정인 지적은 2-2·2-5로 정정했습니다. 코드 수정만으로 운영 반영/동시성 검증까지 끝났다고 보지 않습니다.

1. **[중] 스키마 추출 도구의 전이 패키지 취약성 경고**(10-23 빌드에서 확인)
   - `tools/schema_from_db`의 기존 SMO 의존성이 `System.Security.Cryptography.Pkcs 9.0.4`를 가져오며 NuGet 감사에서 NU1902·NU1903을 경고합니다. 배포 API에 새로 추가한 패키지는 아니며 도구는 실행하지 않았습니다. 패키지 보안 업데이트와 실제 스키마 추출 호환 검증이 필요합니다.

### 2-5. 재검토 정정 — 운영 결함 아님·선택적 정리 (7)
1. **`SCM_ItemVendor.PackingUnitCode`가 남아 있음 — 미사용 컬럼 정리이지 기능 결함은 아님**
   - 현재 SCM 품목·공급업체 연결은 `ScmRepository.ItemVendorRow`와 `ListItemVendors`/`LinkItemVendors`를 사용하며 이 컬럼을 읽거나 저장하지 않습니다. 런타임 소스 참조도 없습니다. 컬럼이 남아 있다는 사실만으로 조회·발주·입고가 잘못되지 않으므로 반드시 삭제해야 할 문제로 분류하지 않습니다. 별도 정리 승인 전에는 보존합니다.
2. **BOX·PLT·PALLET 제외와 SH 유지 — 과거 단위의 일괄 EA 변환은 하지 않음**
   - 현재 `migrate_retire_packaging_uom.sql`과 `MasterDataRepository.IsQuantityUnit`은 BOX·PLT·PALLET만 신규 수량 단위에서 제외합니다. **SH는 사용자 지시로 다시 활성화한 수량 단위**이므로 「SH도 사용 금지」는 틀린 지적입니다.
   - 기존 주문·재고 단위를 일괄 EA로 바꾸지 않습니다. 삭제된 Box Contents 정보로 과거 BOX 수량을 추정 환산할 수도 없습니다. 품목·BOM·발주의 기준 단위는 실제 업무 자료로 확인해야 하며, 과거 점검의 DB 행 수를 현재 값으로 단정하지 않습니다.
3. **일별 출하의 옛 운송사·회차 요약 — 현재 실출하 모델에는 해당 없음**
   - 현재 `FG_Shipment`와 보고서 데이터에는 운송사·회차가 없습니다. 예전 출하지시 기준의 요약을 되살리려고 임의 값을 만들거나 컬럼을 추가하지 않습니다. 실제 값이 있는 고객별 출하 건수·단위별 수량을 표시하며 서로 다른 UOM 수량은 합산하지 않습니다.
4. **FG는 ASSY만 — 사출/반제품까지 받도록 확대하지 않음**
   - 사용자가 합의한 Assy Ready/Set Location은 CONFIRMED인 ASSY 제품만 대상으로 하며 팔렛 안에 다른 유형이 섞이면 거부합니다. INJ MATERIAL이 FG에 안 들어온다는 사실만으로 결함이 아닙니다. 라우팅 마지막 공정만으로 FG 대상을 확대하라는 기존 제안은 적용하지 않았습니다.
5. **라인 보충 기준은 위치·파트별 MinimumQty — 품목 SafetyStock이 아님**
   - 현재 `WarehouseRepository.LineStockSql`은 활성 라인 상세 위치의 지정 PartNo·MinimumQty만 수요로 만들고, 해당 위치/파트 재고와 비교합니다. 「품목 SafetyStock을 라인마다 적용한다」는 이전 판 지적은 현재 코드와 달라 정정했습니다. 단위 표시 오류만 실제 수정했습니다.
6. **일반 PART 재고와 BOX 피킹은 다른 단위**
   - 일반 LOT은 PART로, 공급사 Unit Item은 BOX 재고로 받는 기존 구분을 유지합니다. 사용자가 피킹은 한 박스씩 처리하도록 정했으므로 PART를 임의로 BOX라고 바꾸거나 하나의 LOT을 한 박스로 환산하지 않았습니다. 생산 LOT 오입고·여러 공급사 PO 자동 추정·PO 잔량 초과는 실제 결함으로 보고 2-1 10에서 별도로 방어했습니다.
7. **미사용 수동 피킹 슬립 출고 — 현행 라인 보충 경로와 구분**
   - 현재 PDA는 WH-AUTO 지시를 통해 창고 재고를 라인으로 이동합니다. 구형 수동 슬립의 OUT 전용 분기는 현재 화면 호출이 없으므로 라인 이동 결함으로 일반화하지 않습니다. 다시 사용할 때는 업무별 출고/라인 이동 규칙을 먼저 정해야 하며, 이번에 임의로 재활성화하지 않았습니다.
---

## 3. 이승욱 — Web SYS·MD·MNT·RPT·PP-CAL/LSB/OEE/DTL/ODM·계정·공통

### 3-1. 미해결·적용·검증 대기 (1)
1. **[하] [적용·검증 대기] 라인 상태 기록(`PP_LineStateLog`) 운영 마무리** — 기록 기능은 10-22 심재현(`dbo.SP_PP_LineStateLog_Fill`, 에이전트 작업 「[AMES] Line State Fill」 1분·최근 2시간, 「[AMES] Line OEE Fill」 5분 — 개발서버 등록·동작 중, 10-23 `SYS_InterfaceMonitor` `STATEFILL`·`OEEFILL` OK). 6월 시연 행 720건은 10-23 개발·로컬 DB 에서 지웠습니다.
   - ① 매일 06:00 이틀치 재분류·OEE 재집계 작업 「[AMES] Line State Recalc」(`dist\setup_job_pp_linestatelog_recalc.sql`)을 개발서버 PC 에서 sa 로 등록(심재현·서버 관리자). `ames_app` 은 msdb 를 볼 수 없고 결과도 같은 모니터 행에 남으므로, 확인은 그 PC 의 작업 목록·작업 기록으로 합니다.
   - ② 임계값 `PP_LINE_RUN_GAP_CYCLES`(2)·`PP_LINE_RUN_GAP_MIN`(3분)(SYS-009) 현장 조정 — 지금은 처음 값 그대로입니다.
   - ③ 보존 기간 정리 작업(1년 약 840만 행) — 남길 기간을 정한 뒤 만듭니다(지금은 지우는 작업 없음).

### 3-2. 보류 (3)
- **B7 MNT-004 금형 정비 등록**(`MoldRepository.cs:46·74` StartChange·CompleteChange, 호출처 없음): 정비 유형·작업자·샷 리셋·정비일을 남길 곳이 없습니다. 금형 교체를 기록하던 메서드도 옛 POP 화면을 지우면서 끊겼습니다 — 그런데 `MoldRepository.cs:8`·`MntRepository.cs:419` 주석은 여전히 "POP 금형 교체가 기록" 한다고 씁니다.
- **개발 완료 시 정리**: 운영 admin 시드 차단, appsettings 비밀번호·AllowedHosts·접속문자열 환경변수(Web).
- **품목 추가 불출율·MRP 여유율 반영 방식**(10-20 — 고객 요구 10-14 "BOM 은 정량, 원단 불출 +10%·MRP 품목별 여유율"): `MD_Item.IssueAddPct`·`MrpAllowPct`(%, NULL = 0)는 MD-003(화면·엑셀)에서 저장·표시만 하고 **작업지시 불출·BOM 자동 차감·MRP(PP-005) 계산은 아직 읽지 않습니다**. 정할 것: ① 자동 차감(backflush)을 정량으로 할지 불출율을 포함할지 ② MRP 소요에 불출율도 더할지(여유율만 쓸지) ③ 원단 등 품목 분류별 기본값을 둘지 품목마다만 둘지 ④ BOM 불량률(`ScrapPct`, 지금 전부 0)과의 관계. 결정 뒤 구현은 MRP·백플러시 담당(심재현)과 함께합니다. 같은 날 넣은 조달형태 `MD_Item.ProcureType`(내작·매입·유상사급·무상사급)도 저장·표시만 하고 MRP·구매·SAP 에서 읽지 않습니다.

### 3-3. 테이블 — 웹 화면이 읽지만 쓰는 곳이 없음 (10-14 전수 조사)
1. **`MNT_OEELog`(56행) — 설비 OEE 기록**
   - 읽는 곳: MNT-008 OEE 분석, RPT-004 설비 OEE, RPT-008 월간 KPI 가용률, RPT-010 OEE 원본, MNT 대시보드 「오늘 평균 OEE」, Api `/api/mnt/oee`(`MntRepository.cs:354·368-392·2501-2505`, `RptRepository.cs:419·468·553·866`, `MntEndpoints.cs:25`)
   - 내용: 이 테이블을 채우는 코드가 없습니다. 남은 56행(전부 06-02 `mnt-seed`)은 시연 데이터라 — 10-17 심재현 설비 재시드가 없어진 설비의 행을 지워 224행에서 줄었습니다 — 위 화면들은 실제 생산과 무관한 고정 숫자를 보여 줍니다. PP-OEE 는 별도 테이블 `PP_LineOEE` 를 저장해 서로 연결되지 않습니다.
   - 제안(권장): 화면들이 `PP_LineOEE`(라인 단위)를 읽도록 바꾸고 이 테이블은 폐기합니다. 다른 길은 PP-OEE 를 설비 단위로 확장하는 것입니다.
2. **`PR_MoldChange`(3) — 금형 교체 이력**
   - 읽는 곳: MNT-004 「교체 이력」(`MntRepository.cs:425-446`)
   - 내용: 기록 메서드를 아무도 부르지 않아 시연 3행만 보입니다(B7). 실제 교체는 PP-LSB 금형 교체(MC) 슬롯으로 계획만 있습니다.
   - 제안: POP 교체 시작·종료 버튼(심재현) 또는 B7 정비 등록과 함께 기록합니다.
3. **`MNT_MoldShotCount`(0) — 금형 샷 누적**
   - 읽는 곳: MNT-004 금형 목록(`MntRepository.cs:406`), MD 금형·설비 삭제 사용처(`MasterDataRepository.cs:1974·3025`)
   - 내용: 샷을 쌓는 곳이 없어(InjAgent 는 `PR_ShotCount` 에만 씀) 소진율이 비어 있고 정비 한계 알림이 동작하지 않습니다.
   - 제안: InjAgent 가 샷을 기록할 때 이 카운터도 올립니다(심재현 연계).

### 3-4. 문제점 (2)
10-16 MNT 흐름 점검의 6건은 같은 날 사용자 결정대로 처리했습니다(CLAUDE.md MNT 규칙 ⑦·⑩·⑪). 안돈 보전 호출의 작업지시 자동 발행은 심재현 미해결 20 로 옮겼습니다. 아래는 10-22 흐름 전수 점검에서 찾은 것입니다.

1. **[중] 이미 쓰인 LOT PREFIX 를 다른 값으로 바꿔도 막지 않음** — 라인 로케이션 담당(안영훈)과 결정
   - 위치: 접두어 저장 `MasterDataRepository.cs:2577-2661`(LotPrefixGuardSql·InsertLine·UpdateLine — 10-22 안영훈 `d37a8449`, 형식 2자·라인 사이 유일 확인, 10-23 이승욱 LOT 공정 필수 규칙 `LotPrefixGuardSql` 52079)
   - 내용: 10-23 부터 LOT PREFIX 는 LOT 을 발행하는 공정(사출·감싸기·도장 — `Services/LotPrefixRules`)의 라인만 필수이고 재작업 등은 두지 않으며(MD-001 입력칸도 공정에 따라 필수/꺼짐), WO 릴리스·PP-003·PP-APS 는 접두어 없는 LOT 라인을 후보에서 빼고 지정하면 거부합니다(`LotPrefixMissingException`·`RejectNoLotPrefix`). 남은 것은 접두어가 LOT 번호뿐 아니라 라인 재고 위치(`WH_Inventory.LocationNo`)·WH 구역 코드(`WH_AREA`)·라인 로케이션 ID·백플러시 자리표시 `BF-{접두어}-`·채번 헤더(`SYS_LotSeq`)의 키인데, 이미 쓰인 접두어를 다른 값으로 바꿔도 막지 않아 바꾸면 기존 라인 재고·로케이션·구역과 연결이 끊긴다는 점입니다(비우기는 안 됨 — `COALESCE(@Prefix, LotPrefix)`).
   - 제안: 한 번 쓰인 접두어는 잠그고(입력칸 끔·저장소 거부) 바꿔야 할 때만 관리자가 SQL 로 일괄 처리 — 안영훈과 정합니다.
2. **[중] 나사·레진 품목 단위가 BOM Master List(정본)와 다름 — 되돌릴지 결정 대기**
   - 위치: `MD_Item.DefaultUOM` — `12490-04101`(나사) FT·정본 EA, `21343A0-PIDHANNB`(레진) FT·정본 G(10-19 15:23·15:24 개발서버 시각 admin 계정 MD-003 변경, 감사 904·906 — 누가 바꿨는지는 확정 못 함), `D3131-P8000RBQ`(원단) SH·정본 EA(안영훈 시트 단위 복원 — 의도된 값으로 보임). 정본 `dist/seed_md_item_bom_master_list.sql:333·339·383`
   - 내용: 10-23 부터 BOM 라인 단위는 구성품 기본 단위를 그대로 저장하고(MD-004 입력 없음, 엑셀 올리기도 같음) 품목 단위를 바꾸면 그 품목을 쓰는 BOM 라인도 같이 바뀝니다. 같은 날 사용자 지시로 어긋나 있던 BOM 13줄(나사 6·레진 6·원단 1, 모두 승인 버전)을 현재 품목 단위로 맞췄습니다(개발·로컬 DB, 수정자 `BOMUOM-1023`, 수량 그대로, 백업 `AppData\Local\AMES-claude-tools\backup_bom_uom_fix_20261023`). 그래서 지금은 「나사 8 FT」·「레진 90.4 FT」처럼 단위가 맞지 않는 값이 됩니다.
   - 제안: 나사 EA·레진 G 로 품목 단위를 되돌리고 그 BOM 12줄도 같이 되돌림(나사는 재고가 있어 화면에서는 잠김 — SQL). 단위 환산 기능을 둘지는 차감·MRP 담당(심재현)과 정합니다.

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
- POP 안돈의 알림 기록 고정값(`SUPERVISOR/PDA`·`LINE-LEAD/EMAIL`)을 PUSH·규칙 수신자로 바꾸는 일은 심재현 미해결 15 입니다.

---

## 5. 공통 — 다음 개발 항목
- `AMES.Pda`: PP 모듈·CM(고장) 작업지시 화면(예비품 SP 입출고·PM 작업지시 점검·완료는 구현됨) — 안영훈
- **[방향 확인] `AMES.Data` 의 SQL 위치**: CLAUDE.md 는 "inline SQL → `dbo.SP_*` 스토어드 프로시저 전환 예정" 입니다. 10-21 POP 은 그 방향으로 인라인 SQL 145개를 `dbo.SP_POP_*` 로 옮겼지만(김현진 c10a16ee, `dist/migrate_pop_stored_procedures.sql` — 개발 DB 적용, 로컬 DB 는 10-22 개발 DB 복제로 반영), 10-16 PDA 는 반대로 프로시저를 앱 SQL(`PdaWarehouseSql` 등)로 옮겼고, 같은 날 POP 팔레트 적재(`PalletStockRepository`, 심재현)와 웹·Api 저장소는 앱 SQL 입니다. 프로시저로 옮긴 SQL 은 스키마 파일(`AMES_Schema.sql`)에 없고 마이그레이션 하나가 정본이라, 프로시저를 고칠 때는 그 파일을 고쳐 두 DB 에 다시 적용해야 합니다. 어느 쪽으로 갈지(또는 POP 만 프로시저) 정한 뒤 CLAUDE.md 문구와 이 항목을 맞춥니다 — 공통
- `AMES.Web`: 실제 데이터 바인딩 완성(일부 화면 scaffold 상태) — 각 화면 담당
- `AMES.Pop`: 추가 공정 모듈(필요 시) — 심재현
- **SAP 마스터 수신**(10-21 회의 결정 — 이 마스터들은 MES 가 SAP 로 보내는 것이 아니라 **SAP 에서 받는** 데이터): `MD_Item`·`MD_Vendor`·`MD_Location`(상위 로케이션만 — 10-22 2단계, 하위는 MES 전용)·`MD_Customer`·`MD_BomVersion`·`MD_Uom`(10-22) 의 `SAPFlag`(구 `SAPTransFlag`, 1 = SAP 에서 받은 행)는 MES 화면이 표시만 하고(목록 「SAP」 열·수정 창 헤더 ID 옆 SAP 표시) 쓰지 않으며, 등록·수정 창 체크박스와 BOM 의 SAP 전송 규칙(변경 사유 필수·「SAP 전송 변경」·새 승인/만료 버전 자동 해제)은 10-21 삭제했다. 할 일: ① **SAP 에서 직접 받는 데이터(`SAPFlag` = 1 인 행)는 MES 에서 수정할 수 없도록 비활성화해야 한다** — 여섯 화면(MD-003·MD-004·MD-009·MD-010·MD-018·MD-019)의 수정 창 입력·저장·삭제(BOM 은 버전·라인 편집·승인 흐름 포함)와 엑셀 업로드·저장소 `Update*`/`Delete*` 를 함께 막을지, MES 만 쓰는 칸(예: 품목 적입수량·사출품 여부·불출율, 로케이션 적재 용량)은 열어 둘지 칸 단위로 정해야 한다 ② SAP 수신 기능(방식 SAP B1 Service Layer 등·주기·담당, 키 매칭과 새 행/변경/삭제 처리, 받은 시각·오류 기록 테이블, 순서: 단위 → 품목 → 고객사·협력업체·로케이션 → BOM) ③ 이미 MES 에서 만든 행과 SAP 행이 같은 키로 들어올 때의 처리(덮어쓰기·`SAPFlag` 1 로 전환 여부). 그 전까지 `SAPFlag` 는 SQL 로만 1 이 된다.

## 6. 참고 — 검증 기준선

지난 기준선은 커밋 이력으로 봅니다. 여기에는 지금 기준만 둡니다.

- **테스트(10-23, 이 커밋 Release 빌드, Data 는 로컬 DB = 개발 DB 복제본)**: Pop 184, Api 214(건너뜀 1), InjAgent 54 통과. Data 1,577건 중 1,555 통과·11 실패·11 건너뜀 — 실패는 심재현 보류 「라우팅 B·시드 변경」 10건(PlanSchedule 5·WorkOrderRepository 4·InjLot 금형 1)과 같은 보류에 적은 데이터 가정 1건(`MoldPlanningTests.LineLastMoldBefore_…`)입니다. 팔레트 적재 `PalletLoadDbTests` 를 포함한 나머지는 통과했고 시험 행(품번 `ZPLT-*`·팔레트 `EOS-PLT-9999-*` 등)은 남지 않았습니다. Web·Api·Pop·InjAgent 빌드 오류 0. InjAgent `PollerRunnerTests.Stop_timeout_keeps_running_flag_until_loop_exits` 는 타이밍에 기대어 가끔 1건 실패합니다(다시 돌리면 통과 — 심재현 문제점 23 과 같은 정지 경합).
- **두 DB 구조·데이터(10-23)**: 로컬 DB 를 개발 DB 기준으로 통째로 다시 맞췄습니다(04시 sqlpackage 구조+데이터 복제 → 이름 교체, IDENTITY 다음 번호·다이어그램 객체 보정 — 이전 로컬 DB 는 `AMES_DEV_BAK_20261023`, 백업 `AMES_DEV_local_20261023_before_pnt_sync.bak`). 그 뒤 원격의 `migrate_wonumber_unique.sql` 을 로컬에도 적용해 179 테이블의 컬럼·인덱스·CHECK·외래키·프로시저 정의가 **같습니다**(`schema_compare.ps1` IDENTICAL — `SP_POP_*` 144개는 저장소 `migrate_pop_stored_procedures.sql` 과 본문 일치). 데이터는 개발서버 에이전트 작업·로컬 IIS Api Worker 가 계속 쓰는 `PP_LineStateLog`·`PP_LineOEE`·`SYS_InterfaceMonitor` 만 다르고, 10-23 사용자 지시로 지운 6월 시연 상태 로그 720건과 BOM 13줄 단위 수정은 두 DB 에 같이 적용했습니다.
- **스키마 파일 ↔ 개발 DB(10-22, `tools/schema_from_db` 로 새로 떠서 객체 비교)**: 스키마 파일에 없는 것 — 테이블 4개 `FG_Delivery`·`FG_DeliveryLoad`·`FG_ShippingOrder`·`FG_ShippingOrderLine`(기본값·인덱스 포함, 재구축은 `migrate_line_locations_shipping_orders.sql` 이 만듦), 프로시저 `SP_POP_*` 145개(재구축은 `migrate_pop_stored_procedures.sql`), 옛 PDA 프로시저 27개(`FG_PDA_*` 5·`SP_PDA_*` 3·`WH_PDA_*` 19 — 안영훈이 파일에서 일부러 뺐고 개발 DB 에서는 지우지 않음). 정의가 다른 것 — `IX_WH_InventoryTransaction_Source`(심재현 문제점 30)·`UX_PR_PopSession_BearerHash` 중복 정의(안영훈 2-1 1 ②). 컬럼·기본값·CHECK 는 같습니다. 스키마 파일을 개발 DB 로 다시 뜰지는 위 재구축 문제(2-1 1)와 함께 정합니다.
- **게시**: 로컬 IIS Web·Api = 개발 DB(이 커밋). 개발서버용 `publish/AMES.Web_20261023-061656.zip`·`AMES.Api_20261023-061907.zip` — **개발서버 Web 을 바꾸기 전까지 서버의 구 Web 은 MD-003·004·009·010·018 과 `ListItems` 를 쓰는 화면이 `Invalid column name 'SAPTransFlag'`** 입니다(구 Api 는 영향 없음). 개발서버에 새로 적용할 마이그레이션은 없습니다(WO 번호 유니크는 작성자가 적용). POP(PNT-MAIN·팔레트 적재)은 터미널에 따로 배포합니다. 헤드리스로 MD-001·MD-003·MD-004·MD-018·PP-004·PP-003·PP-APS 7개 화면을 열어 Blazor 오류·오류 알림·가로 넘침이 없음을 확인했습니다(admin 로그인 기록 원복).
- **헤드리스 확인 방식**: 로컬 IIS·개발 DB 로 화면을 열 때 admin 로그인 기록(LastLoginTS·ModifiedTS·ModifiedBy·FailedLoginCount)과 `SYS_AuditLog` 최대 LogID 를 먼저 읽고 끝나면 되돌립니다. 검증 도구는 `AppData\Local\AMES-claude-tools\`.
- **NuGet(10-15)**: 취약 패키지 0(`dotnet list package --vulnerable --include-transitive`), 모든 패키지가 최신 안정 버전입니다(SqlClient 7.1.1, EF Core 10.0.13, Radzen 12.1.2, Test.Sdk 18.11.0, xunit 러너 4.0.1 등). 취약성 때문에 `System.Security.Cryptography.Pkcs` 를 AMES.Data 에서 10.0.13 으로 고정했으니 SqlClient 를 올릴 때 다시 확인합니다.
- **제품 코드 규칙**: `AddWithValue` 0곳(주석 3곳만), Web 의 `DateTime.Now/Today` 0곳, 라이트 테마 토큰화 대상 0.
- **두 번째 병합의 WH·FG·SCM 변경 검증(안영훈)**: API 74개 통과, 용량·위치 규칙 테스트 17개 통과, Web·PDA Android 빌드 성공. 실제 입출고·동시 PDA 부하 검증과 DB 전체 재구축은 실행하지 않았습니다.
- 항목을 해결하면 같은 커밋에서 이 문서의 해당 항목을 지웁니다(해결 기록은 남기지 않고 커밋 이력으로 봅니다).
