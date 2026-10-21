# Claude 확인사항 — 담당자별 미해결·보류·문제점

> **이 파일이 미해결·보류·문제점·다음 개발 항목의 유일한 정본입니다**(10-14 사용자 결정). CLAUDE.md·다른 문서·작업 메모에는 목록을 두지 않습니다. 항목을 해결하면 같은 커밋에서 여기 항목을 지우고, 새로 찾은 문제는 담당자 절에 더합니다.

> **재검토 원칙**(10-16 사용자 확인): 이 문서의 지적·제안 자체가 구현 원칙이나 수정 지시는 아닙니다. 현재 호출 경로·합의된 업무 방향과 대조해 실제 결함인지 먼저 확인합니다. 지적이 틀렸거나 방향이 바뀌었다면 코드·DB를 억지로 바꾸지 않고, 이 문서에 해당하지 않는 이유·근거·현재 방향을 정정합니다. 운영 결함, 배포·검증 대기, 정책 결정, 선택적 정리를 구분하며 과거 DB 점검값을 현재 값으로 단정하지 않습니다.

- **작성**: 2026-10-14, Claude Code(이승욱 세션). 10-15 원격 병합(안영훈 SCM·포탈 10건) 뒤, 10-15 06시 두 번째 원격 병합(안영훈 WH·FG·SCM·Api 7건) 뒤, 10-16 MNT 작업(설비 상태 자동 판정·고장 라인 정지·PM 점검 완료 규칙·PM 일정 규칙)과 화면 저장 검증 뒤, 10-16 23시 그날 들어온 커밋(심재현 백플러시·POP DbClock·PP-APS·LOT 라벨, 안영훈 PDA 직접 SQL 전환·구 프로시저 정의 정리, 이승욱 SAPTransFlag·포탈/SCM 화면 표준화·다국어화)과 항목을 대조해 다시 정리했습니다. 해결된 항목은 지우고 남은 것만 둡니다. **10-17 전 항목을 현재 소스(ba968b99 — MD-034 팔레트·MD-035 사출 조건 항목, 심재현 설비 재시드 병합 뒤)와 다시 대조**해 해결된 1건(이승욱 테이블 5 `MD_InjCondItem` — MD-035 화면)을 지우고, 줄 번호·경로·행 수를 고치고, 새로 찾은 3건을 더했습니다. 10-16 이후 POP·PP·PNT·QC 쪽 코드는 바뀌지 않아 심재현 항목은 모두 그대로 유효합니다.
- **기준 소스**: `dev` 브랜치 10-17(ba968b99). 줄 번호는 전 절을 이 시점으로 고쳤습니다. 그 뒤 10-17 오후 dd8df481(심재현 `MD_MoldColor` 폐지 병합 — 심재현 항목에 영향 없음, 이승욱 테이블 4 줄 번호만 갱신)과 대조했습니다. **10-20 이승욱 세션**은 이승욱 절(보류 4 추가)·공통 「SAP 마스터 전송」·검증 기준선만 고쳤고, 다른 담당 절은 그대로 두었습니다.
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
| 심재현 | 23                   | 7   | 7묶음 | 38  | [상] WO 번호 인덱스 없음 → POP 확정 지연 · [상] APS WO 생성 전체 실패 · [상] POP·InjAgent 배포 설정이 로컬 DB · [중] BOM 자동 차감이 서브조립 아래 자재를 빼지 않음(문제점 32) · 안돈 보전 호출 작업지시(요청 22) |
| 안영훈 | 7(배포·검증·승인 대기·알림) | 4 | 3 | 0 | 직접 SQL·LOT 원입고량 취소·빈 팔렛 회수·출하 화면의 서버 배포 및 실제 쓰기 검증 대기 · 비결함·선택적 정리 4건은 2-5 참조 |
| 이승욱 | 0                    | 4   | 4   | 0   | PP-OEE 가동률이 시연 데이터(`PP_LineStateLog` 기록 — 심재현과 진행 예정) · 품목 불출율·MRP 여유율 반영 방식 결정(보류 4)                                                                       |
| 오세영 | 5                    | 2   | —   | —   | 발송기가 없어 SYS-008 발송 이력이 시연 데이터                                                                                               |

---

## 1. 심재현 — POP(QC 포함)·InjAgent·Devices + Web PP-001~006·PP-APS

### 1-1. 미해결 (23)

1. **[상] POP·InjAgent 배포 설정이 로컬 DB 를 가리킴**
   - 위치: Pop `appsettings.Production.json:3`, `Common/AppConfig.cs:48-49·82-86` / InjAgent `appsettings.json:3`, `Core/AgentConfig.cs:56-57`, LOT 생성 실패 재시도 없음 `Core/MachinePoller.cs:144-154`
   - 내용
     - POP 배포 설정이 비상용 `localhost\MSSQLSERVER01` 이고, 기본 설정(개발서버)을 Production 파일이 덮어씁니다. 그래서 현장 터미널이 비상용 DB 에 쓰거나 접속에 실패합니다.
     - InjAgent 는 `sa@localhost` 에 Connect Timeout 이 없고 재시도도 없어, 샷 LOT 이 유실될 수 있습니다.
     - 둘 다 환경변수 오버라이드(`AddEnvironmentVariables`)를 읽지 않습니다.
   - 제안: 배포 설정을 개발서버 문자열로 바꾸고 환경변수 오버라이드를 추가합니다. InjAgent 는 전용 계정과 `Connect Timeout=30` 을 씁니다.
2. **[중] 스캔 확정 뒤에 온 로봇 NG 판정이 버려짐**
   - 위치: `InjLotRepository.cs:623-640`(MarkNgBlocked), 호출 `MachinePoller.cs:206-210`
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
   - 위치: `MachinePoller.cs:76-77·117-157·160-175·202-221`, `Plc/FEnetClient.cs:140-149`
   - 내용
     - 카운터 리셋이나 금형 변경만으로 LOT 이 생깁니다.
     - 샷이 2 이상 건너뛰면 1건만 생깁니다.
     - 로봇 쓰기 실패를 무시해 판정이 다른 LOT 에 붙습니다.
     - 검사 이력이 재시도 때 중복됩니다.
     - FEnet 응답을 한 번만 받아 잘린 응답을 그대로 씁니다.
   - 제안: 샷 증가분만큼 LOT 을 만들고, 쓰기 실패는 재시도하며, 응답 길이만큼 반복해서 받습니다.
8. **[중] 재작업이 취소된 WO 에 실적을 귀속**
   - 위치: `ReworkRepository.cs:201`, `WorkOrderRepository.cs:452·457-466`
   - 내용: 수리한 양품이 취소 WO 로 들어갑니다. 마지막 단계면 Cancelled 가 Closed 로 바뀌고 완료 수량도 바뀝니다.
   - 제안: 취소 WO 면 열린 WO 를 다시 찾고, 없으면 WO 없이 확정합니다.
9. **[중] QC 홀드 해제 조건 없음·QC 번호 중복**
   - 위치: `QcRepository.cs:220-225·127·173`
   - 내용: 이미 해제된 홀드도 다시 해제됩니다. 검사·NCR 번호가 분 단위(yyMMddHHmm)라 같은 분에 두 건이면 번호가 겹치고, 유니크 제약도 없습니다.
   - 제안: 상태 조건을 추가합니다. 번호는 범위 잠금으로 채번합니다(MNT `ClaimNumber` 방식).
10. **[중] inj_flag 마이그레이션이 컬럼을 데이터째 지움**
    - 위치: `dist/migrate_md_item_inj_flag.sql:63-111`
    - 내용: 재생성 목록(63-96행)에 `ScanRequired`·`SAPFlag`(10-16 `SAPTransFlag`, 10-21 개명) 가 없고 감사 컬럼 순서도 지금 DB(맨 뒤)와 다르며 기본값 제약(`DF_MD_Item_ScanRequired`)·인덱스도 되살리지 않으며, 컬럼 구성 대조 가드가 없습니다. 18-24행 가드 덕분에 `InjFlag` 가 이미 `ItemCategory` 바로 뒤에 있는 DB(개발·로컬)는 재실행해도 건너뛰지만, `InjFlag` 가 없거나 자리가 다른 DB 에 적용하면 뒤 컬럼이 데이터째 사라집니다.
    - 제안: 재생성 목록을 현재 `MD_Item` 구성으로 보강하고 컬럼 대조 가드를 둡니다. 이미 적용된 DB 가 대부분이면 "컬럼이 있으면 건너뜀" 만 남기는 것도 방법입니다.
11. **[하] PIN 키패드 기본 사번 E001**
    - 위치: `Login.razor:403`
    - 내용: 사원을 고르지 않고 PIN 을 치면 E001 로 시도해 E001 실패 횟수가 쌓입니다.
    - 제안: 사원을 고르기 전에는 입력을 막습니다.
12. **[하] POP 로그인 화면 예외 처리**
    - 위치: `Login.razor:465·285-286·300`
    - 내용: DB 장애 때 예외를 다시 던지고, 배지 등록·초기 목록 조회가 try 밖이라 로그인 화면 자체가 깨집니다.
13. **[하] 로그·라벨 폴더가 실행 위치 기준**
    - 위치: `AppConfig.cs:57`, `PopLog.cs:15`, `Common/BackflushNotice.cs:13`(10-16 백플러시 로그도 같은 `PopLog`)
    - 내용: 바로가기의 작업 폴더에 따라 파일 위치가 달라집니다.
    - 제안: 실행 파일 폴더 기준으로 바꿉니다.
14. **[하] 라벨 선점 순서·출력 후 카운트 실패 시 중복**
    - 위치: `InjLotRepository.cs:404`, `InjMain.razor:443-454`, `ImgMain.razor:381-388·495`
    - 내용: 선점 UPDATE 에 순서가 없습니다. 출력은 됐는데 카운트 갱신이 실패하면 "실패" 로 안내해, 작업자가 다시 출력합니다.
15. **[하] 로그아웃 뒤 확인창이 남음**
    - 위치: `Services/ConfirmService.cs:15-30`, `Services/AppState.cs:46-57`
16. **[하] 안돈 기록 오류 묶음**
    - 위치: `AndonPopup.razor:246-250`, `AndonRepository.cs:316·411-413·51-53`
    - 내용
      - 기존 안돈을 복원만 해도 설비 STOP 기록이 중복됩니다.
      - 원인코드 매개변수가 `VarChar 15`(공통코드 값은 20자까지 — 지금 최대 8자라 잠재 결함)입니다.
      - 종료 때 CLOSED 를 RESOLVED 로 덮습니다.
      - 알림 수신자·채널이 고정값입니다(오세영 PUSH 와 연계).
17. **[하] PNT 단위 EA 고정·불량코드 버림**
    - 위치: Pnt01·02·03·06·07·08·09 화면과 도움말, `PntRepository.cs:421-436`
18. **[하] 마이그레이션 FK 6개·그룹 이름 옛 접두어**
    - 위치: `migrate_aps.sql:74·75·100·157·185`, `migrate_aps_plan_line_shift.sql:21`, `migrate_demand_plan.sql:69·92-96`
    - 내용: FK 를 두지 않는 원칙과 달리 FK 6개를 새로 만듭니다. DPSYNC 공통코드 그룹 이름에 폐지된 `ScheduledWorker · ` 접두어가 있습니다.
19. **[하] 테스트 코드에 접속 비밀번호·외부 IP**
    - 위치: `AmesDevDb.cs:15` 외 `AMES.Data.Tests` 7파일(총 8파일, 192.168.0.132 를 쓰는 6파일은 Connect Timeout=10, `MoldPlanningTests:20`, `PlanScheduleTests:21` 은 98.95.142.192 이고 Connect Timeout 도 없음)
    - 제안: 모두 `AMES_TEST_CONN` 하나로 바꿉니다.
20. **[하] POP PLAN 기준 결정**
    - 위치: `InjLotRepository.cs:759-767`, `ImgLotRepository.cs:546-555`
    - 내용: PP-LSB 「발행」이 폐지돼(10-14), 「적용」만 해도 현장 PLAN 에 바로 반영됩니다. 어떤 기준으로 할지 POP 담당이 정합니다.
21. **[하] POP IMG 코어 스캔 검사를 InjFlag 기준으로**
    - 위치: `ImgLotRepository.cs:138`(CreateFromCore → `CoreItemResolver.Read`)
    - 내용: 웹 WO 발행은 10-14 부터 PP-APS 와 같은 InjFlag 기준(`CoreItemResolver.ReadByInjFlag`)으로 코어를 정하는데, IMG 코어 스캔의 `CoreMismatch` 검사는 아직 품명 CORE 규칙입니다. 개발 DB 완제품 77개는 두 규칙의 결과가 같지만, 레일만 있는 품번처럼 둘이 갈리면 WO 의 사출 단계 품번과 IMG 검사가 다른 코어를 봅니다.
    - 제안: `ReadByInjFlag` 로 바꿉니다(POP 담당 작업 예정).
22. **[중] [요청] 안돈 보전 호출에 CM 작업지시 발행·안돈 종료 때 함께 닫기**(10-16 사용자 결정 — 이승욱 요청)
    - 위치: `AndonRepository.cs:251-275·306-318`(보전 호출 → `MNT_FailureRegister` 만 생성, `WorkOrderID` NULL) · `:390-414`(안돈 종료 → 고장만 RESOLVED)
    - 내용: 안돈 고장에는 작업지시가 없어 MNT-007 배정·착수·점검·PDA 부품 출고를 쓸 수 없습니다(개발 DB 작업지시 없는 열린 고장 10-17 기준 3건). 웹은 10-16 부터 MNT-002 상세의 「작업지시 발행」(`MntRepository.IssueFailureWorkOrder` — CM, 출처 ANDON, SourceRefID 고장번호)으로 수동 연결합니다. 그런데 그렇게 연결한 고장이 안돈 종료로 RESOLVED 가 되면 작업지시는 열린 채 남아 MNT-007 에서 따로 완료·종료해야 합니다.
    - 요청: ① 보전 호출 때 같은 트랜잭션에서 CM 작업지시를 발행해 `WorkOrderID` 를 채웁니다(웹과 같은 규칙 — `MntRepository.IssueFailureWorkOrder` 를 그대로 쓰거나 같은 값: WoType CM·SourceType ANDON·SourceRefID 고장번호·우선순위는 심각도·상태 ISSUED, 번호는 `MWO-yyMM-nnn` 범위 잠금 채번). ② 안돈 종료 때 연결된 작업지시가 끝나지 않았으면 같이 완료합니다(결과 OK — CM 작업지시는 점검 항목이 없어 완료 차단 규칙 `MwoChecklistRules` 에 걸리지 않습니다).
23. **[하] [요청] 금형 샷 누적 기록 — 사이클 PM 자동화의 전제**(10-16 이승욱 요청)
    - 위치: InjAgent(샷은 `PR_ShotCount` 에만 기록), `MNT_MoldShotCount`(0행 — 이승욱 테이블 4)
    - 내용: 사이클(CYCLE) 주기 PM 은 지금 완료할 때 사람이 다음 예정일을 넣습니다(10-16 사용자 결정 — 샷 누적 연동 전까지, `PmRules`). 샷이 쌓이지 않아 금형 소진율·정비 한계 알림도 비어 있습니다.
    - 요청: InjAgent 가 샷을 기록할 때 `MNT_MoldShotCount` 도 올립니다. 그 뒤 이승욱이 사이클 PM 의 다음 회차를 샷 기준으로 자동 발행하도록 바꿉니다.

### 1-2. 보류 (7)
- **APS 금형 교체(MC) 한계**: "직전 금형"을 그 라인·그 날 처리 순서상 마지막 금형으로 보므로, 하루 안에 금형이 여러 번 바뀌면 그 사이 되돌림 MC 를 만들지 않습니다(같은 라인·같은 날 금형 그룹당 MC 최대 1개). 교체 자리가 없을 때 억지로 둔 MC 는 휴게·점유 구간과 겹칠 수 있습니다. 조정은 PP-LSB 에서 합니다.
- **B2 IMG 라벨 교대 글자가 항상 C**(`Common/LabelPrinter.cs:27-32`): DAY/NIGHT 만 A/B 로 바꾸는데, 실제 교대 코드는 A/B/C 라 항상 C 가 찍힙니다. 교대 코드를 그대로 쓸지 답을 기다립니다.
- **B11 InjAgent 시각을 DB 서버 기준으로**: POP 은 10-16(eb5ace39) 로그인 때 `DbClock` 을 DB 시각에 맞추도록 바뀌었습니다(호스트 시계는 유휴·로그·토스트에만 남음). InjAgent 는 아직 `DbClock.Configure` 를 하지 않아, 자동 LOT 채번(`InjLotRepository.cs:66`, `MachinePoller.cs:144` 에서 호출)의 LotNo 날짜 글자가 PC 시계 기준입니다.
- **B15 PNT-09 교대 하드코딩**(`PntRepository.cs:443·451`): 8–15시/16–23시로 박혀 있어 공통코드 교대 시각과 다릅니다.
- **B3 POP 의 "오늘" 이 자정 기준**
  - 위치: `InjLotRepository.cs:778·120`, `QcRepository.cs:324·326·341·379`, `PntRepository.cs:39·109·449`
  - 내용: 웹 보고서는 07시 전기일인데, INJ/IMG 좌측 수치·QC-07·PNT·금형 일 사용량은 자정 기준입니다. 10-16 부터 POP 의 "오늘" 은 DB 날짜(B11)이지만 경계는 여전히 자정입니다.
  - 쓰지 않는 POP 차트 `HourlyBarChart`·`HourlyOutputDto` 도 정리 대상입니다.
- **B23 유휴 로그아웃 뒤 라벨 자동 발행 누락**
  - 위치: `Forms/PopBlazorForm.cs:87-100`, `Services/LabelDispatcher.cs:111-123`
  - 내용: 로그인할 때마다 발행 기준점을 새로 잡아, 로그아웃 동안 생긴 LOT 은 자동 발행에서 영구히 빠집니다.
- **라우팅 B·시드 변경으로 실패하는 테스트 10건**: PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1(10-17 설비 재시드 병합 뒤에도 같은 10건).

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

### 1-4. 문제점 (38)
1. **[상] `PP_WorkOrder.WoNumber` 인덱스가 없어 POP 확정이 막힘**
   - 위치: `PpRepository.cs:939-948`(NextWoSeq), `WorkOrderRepository.cs:428-429`. 개발·로컬 DB 둘 다 PK 하나뿐(10-17), WoNumber 중복 0건이라 유니크로 만들 수 있음. `NextWoSeq` 주석은 "WoNumber 유니크 인덱스가 최종 방어선" 이라고 쓰지만 그 인덱스가 없음
   - 내용: WO 번호를 매길 때 잠금이 테이블 전체에 걸립니다. APS WO 미리보기(읽기 권한 사용자도 가능)나 PP-003 이 도는 동안 POP 스캔 확정이 기다리다, 30초를 넘으면 실패합니다. `dist/migrate_wonumber_unique.sql` 은 있지만 스키마·재구축 목록에 없고 어느 DB 에도 적용되지 않았습니다.
   - 제안: 인덱스를 추가하고(중복 정리 후 유니크 또는 일반), 스키마·`rebuild_db.sh` 에도 반영합니다.
2. **[상] APS 「WO 생성」이 한 품번 때문에 전체 실패**
   - 위치: `PpRepository.Aps.cs:296-298·326-327` → `WorkOrderRepository.cs:580-581`
   - 내용: 대상 중 한 완제품이라도 코어를 정할 수 없으면 예외로 트랜잭션 전체가 롤백돼, 정상 행의 WO 도 0건입니다. PP-003 처럼 행 단위로 거부하지 않습니다. 코어 판정이 10-14 부터 InjFlag 기준이라 레일만 있는 품번은 이제 풀리지만, 사출품(InjFlag)이 없거나 여럿인데 품명 CORE 로 하나를 고를 수 없는 품번은 여전히 전체를 막습니다.
   - 제안: 그 행만 `NoCore` 로 거부하고 나머지는 진행합니다.
3. **[중] APS 재고 역산에서 WO 없이 만든 생산이 빠짐**
   - 위치: `ApsRepository.cs:900-923`(ReadProduced)`·953-976`(ReadWipStock)
   - 내용: 당일 실적을 WO 품번으로 묶어, WO 없는 생산과 BOM 자식 사출 실적이 빠집니다. 시작 재고가 부풀고 사출 소요가 적게 잡힙니다.
   - 제안: 실적 품번을 LOT 품번 → 단계 품번 → WO 품번 순으로(보고서와 같은 규칙) 정합니다.
4. **[중] PP-004 릴리스·취소가 승인 권한만 있는 역할에서 무반응**
   - 위치: `WorkOrder.razor:290·313`
   - 내용: 버튼은 승인 권한으로 보이는데, 처리 첫 줄은 수정 권한을 봅니다. 읽기+승인 역할은 눌러도 아무 일이 없고 안내도 없습니다.
   - 제안: 처리 첫 줄을 `_canApprove` 로 바꿉니다.
5. **[중] MRP 에서 같은 부족분 PR 이 두 번 생김**
   - 위치: `PpRepository.cs:1180-1252`, `Mrp.razor:399`
   - 내용: PR 을 만들 때 그 MRP 실행이 최신인지, 같은 품번의 열린 PR 이 있는지 보지 않습니다.
6. **[중] APS 「WO 생성」 수주 초과 발행 경합**
   - 위치: `PpRepository.Aps.cs:202·924-945·252·314·797-802`
   - 내용: 수주 잔량과 기존 슬롯을 잠금 없이 읽은 뒤에야 채번 잠금을 잡아, 두 실행이 겹치면 같은 수주를 초과 배정합니다.
7. **[중] WO 미리보기가 읽기 사용자에게도 쓰기 트랜잭션을 돌림**
   - 위치: `ApsWoPreview.razor:434-441·463-479`(갱신 버튼 `:62`)
   - 내용: 들어가자마자 dryRun(실제 INSERT 후 롤백)이 돌아 잠금을 잡고 WoID(IDENTITY)를 소모합니다. 문제점 1 과 겹치면 POP 확정까지 막힙니다. 「미리보기 갱신」에도 권한 검사가 없습니다.
8. **[중] 이미 IMG 에 쓰인 코어를 INJ 불량으로 등록할 수 있음**
   - 위치: `InjLotRepository.cs:649-718`(상태 검사 687-689)
   - 내용: 완제품은 양품인데 그 코어는 폐기되는 모순이 생깁니다.
   - 제안: 자식 IMG LOT 이 있으면 거부합니다.
9. **[중] PNT 상태 전이 검사 없음**
   - 위치: `PntRepository.cs:181-238·339-379·382-394`
   - 내용: 지그 사용 여부·LOT 상태를 보지 않아, 두 터미널이 같은 지그를 쓰고 하차 확정이 이미 라벨 붙은 LOT 을 되돌립니다.
10. **[중] PNT 하차 확정이 누적 불량을 지움**
    - 위치: `PntRepository.cs:345-352`(PNT-08 누적은 `:421-437`)
    - 내용: 하차 확정이 불량 수량을 덮어써 PNT-08 에서 쌓은 불량이 사라집니다. 확정 뒤에도 PNT-08 이 불량을 더해 양품+불량이 투입을 넘습니다.
11. **[중] 실적 테이블에 LotID 인덱스 없음**
    - 위치: `LotDefectWriter.cs:25-30`(`InjLotRepository.cs:693`·`ImgLotRepository.cs:368` 에서 호출)
    - 내용: 확정 LOT 의 불량 등록이 LOT 잠금을 쥔 채 실적 전체를 훑습니다. 운영 기간에 비례해 느려집니다.
12. **[중] POP PLAN 과 INPUT/FINAL 의 하루 기준이 다름**
    - 위치: `InjLotRepository.cs:765 vs 778`(IMG 552 vs 565)
    - 내용: PLAN 은 07시 근무일, INPUT/FINAL 은 자정 기준이라 00~07시에는 수치가 어긋납니다.
13. **[하] 불량 역분개가 마감된 WO 를 다시 엶**
    - 위치: `WorkOrderRepository.cs:422-481`(재개방 445-447·468-469), `:272-284`(OpenStepOrder)
    - 내용: 다음 코어 스캔이 Released WO 대신 마감 WO 로 들어갑니다. CLAUDE.md 는 단계를 되돌리는 것을 의도된 동작으로 적고 있어, 다음 스캔이 그 WO 로 가는 것까지 결함으로 볼지는 결정이 필요합니다.
14. **[하] APS BOM 규칙 WO 의 다른 자식은 POP 에서 "WO 없음"**
    - 위치: `InjLotRepository.cs:759-760`, `WorkOrderRepository.cs:280-284`
    - 내용: 단계 품번이 첫 자식 하나라, 레일 등은 PLAN 0·WO 없음으로 보이고 WO 없이 확정됩니다. 보고서의 계획 품번도 첫 자식으로 몰립니다.
    - 제안: 슬롯 품번 칸 추가를 검토합니다.
15. **[하] 확정은 됐는데 "확정 실패" 로 표시**
    - 위치: `InjMain.razor:403-407`(catch 430-433), `ImgJudgePopup.razor:118-124`(catch 135)
    - 내용: 커밋 뒤의 WO 조회가 같은 try 안이라, 순간 끊김이면 실패로 보입니다. IMG 는 팝업이 남아 다시 OK 를 누르면 AlreadyConfirmed 가 됩니다.
16. **[하] QC 검사 품번이 상수**
    - 위치: `Qc01Incoming:147`, `Qc02InProcess:138`, `Qc03Final:138`, `Qc04Ncr:108`(기본값), `QcRepository.cs:119`(CreateInspection — LOT·WO·라인 인자 없음)
    - 내용: 모든 검사가 "DR-TRM-LH-A1" 로 저장되고 LOT·WO·라인이 없습니다.
17. **[하] POP 로그인이 웹 계정 잠금을 무력화**
    - 위치: `PopAuthService.cs:89` → `AuthRepository.cs:258-267`
    - 내용: POP 로그인 성공(배지 포함)이 웹 비밀번호·PP-CAL PIN 실패 횟수를 0 으로 되돌립니다.
18. **[하] PP-001 주간 탭이 조회 오류를 숨김**
    - 위치: `Forecast.razor:484`(주간)`·371`(이력)`·394`(일별 이력)`·361`(고객) — 일별 조회(`:380`)만 오류를 안내
19. **[하] PP-002 일괄 확정이 건마다 따로 저장**
    - 위치: `SupplyPlanImport.razor:344-361`(355)
    - 내용: 중간에 실패하면 앞 건만 확정되고, 몇 건이 반영됐는지 안내가 없습니다.
20. **[하] PP-002~006 승인·생성 동작에 감사 로그 없음**
    - 내용: 수주 확정, WO 생성·릴리스·취소, PR 생성·전송·승인이 SYS-007 에 남지 않습니다. PP-002 「자동 확정」 스위치를 켤 때 일괄 확정되는 수주도 마찬가지입니다(스위치 켜고 끄기만 기록됨).
21. **[하] 단위 EA 고정·WO CSV 품번 누락·MRP 상태 색**
    - 위치: `WorkOrder.razor:162·368`, `PlanConfirmBatchDialog.razor:31`, `Mrp.razor:195·376`(PR 상태 색이 옛 상태값 Pending/Rejected 기준)
22. **[하] 행위자 매개변수 형식이 컬럼과 다름**
    - 위치: `PpRepository.Aps.cs:1012·1039·1057`, `ApsRepository.cs:1041·1214`, `WorkOrderRepository.cs:629`, `QcRepository.cs:93`(FinalizeInspection `@A` → ApproverID·ModifiedBy)(10-15 사람 컬럼 사번 전환 때 주간 구매계획·WO 릴리스·INJ/IMG 확정 매개변수는 고침)
    - 내용: varchar(20) 컬럼에 NVarChar 로 넘깁니다(기능 오류는 아님). `PpRepository.CreateShortagePrs` 는 `@By` 를 `VarChar, 20` 으로 선언하고 값은 `Trunc(by, 50)` 으로 자릅니다(`:1230` — 행위자가 20자 이하라 지금은 영향 없음).
23. **[하] APS WO 를 취소해도 계획 연결이 남음**
    - 위치: `WorkOrderRepository.cs:688-718`, 대상 선정 `PpRepository.Aps.cs:194`
    - 내용: 취소한 계획 행이 계속 '기발행' 으로 남아 다시 만들 수 없습니다.
24. **[하] IMG 라벨과 실적의 WO 가 다를 수 있음**
    - 위치: `ImgLotRepository.cs:118-129`(코어 스캔 WO)`·200-215`(라벨 수주처)`·243-247`(LOT WoID NULL)`·442-455`(판정 때 다시 해석)
25. **[하] QC 최종 판정 덮어쓰기**
    - 위치: `QcRepository.cs:80-95`(UPDATE 83-87 — `Verdict IS NULL` 가드 없음)
26. **[하] POP 창을 닫아도 세션이 열린 채 남음**
    - 위치: `Forms/PopBlazorForm.cs:63`(세션 닫기는 `AppRoot.razor:144` 로그아웃 경로뿐)
27. **[하] InjAgent 정지가 2초를 넘으면 상태 경합**
    - 위치: `Core/PollerRunner.cs:48-75`(69-70)
28. **[하] QC-07 이 실적 없는 라인의 LOT 불량을 빠뜨림**
    - 위치: `QcRepository.cs:310-327`
29. **[하] 금형 시드가 일부 참조만 정리**
    - 위치: `dist/seed_md_mold_master.sql:22-24·137-146`
    - 내용: 다시 돌리면 MNT_MoldShotCount·PR_InjLot·PP_WorkOrder 에 옛 금형을 가리키는 행이 남습니다. 머리말은 `MNT_MoldShotCount` 를 "이력" 이라 남기지만 실제로는 현재 상태(CurrentShots·MountedEquipID) 테이블이고, `PP_WorkOrder.MoldID` 는 언급이 없습니다.
30. **[하] 안돈 고장번호가 한 달 999건을 넘으면 반복**
    - 위치: `AndonRepository.cs:249-261`
    - 내용: RIGHT 3자리로 읽어 1000번째는 `-000` 이 되고, 그 뒤 MAX 가 계속 999 라 `-000` 이 되풀이되며 웹 번호와 겹칠 수 있습니다.
    - 제안: 웹 `ClaimNumber` 와 같은 방식으로 맞춥니다.
31. **[중] 안돈 발동이 같은 라인의 열린 비가동을 보지 않고 새 행을 엶**
    - 위치: `AndonRepository.cs:35-37`(PP_LineDowntimeLog INSERT)
    - 내용: 같은 라인에 이미 열린 비가동(PP-DTL 웹 등록, 10-16 부터 웹 고장의 「라인 정지」)이 있어도 안돈이 또 하나를 열어, 두 행이 겹친 시간만큼 OEE·비가동 분석이 두 번 셉니다. 웹 쪽(PP-DTL 등록, MNT-002 라인 정지)은 겹침을 막거나 열린 행에 연결합니다.
    - 제안: 열린 행이 있으면 그 행의 `AndonID` 를 채워 연결하거나(종료는 안돈 종료 시각과 그 행 소유자 중 늦은 쪽), 최소한 안돈 행 시작 시각에 기존 열린 행을 닫습니다 — POP 코드 변경이라 이승욱과 방식을 맞춥니다.
32. **[중] BOM 자동 차감(백플러시)이 직접 자식만 봐서 서브조립 아래 자재가 빠지지 않음**(10-16 a6bbd59c)
    - 위치: `BackflushRepository.cs:99-128`(ReadBomRows — `ParentItemNo = @Item` 1단계), `BackflushRules.cs:25-40`
    - 내용: 완제품 → 서브조립(SUB, InjFlag 0) → 자재 구조에서 서브조립은 LOT 확정 단계가 없어, 그 아래 MATERIAL 은 어느 확정에서도 차감되지 않습니다(개발 DB 완제품 36개·자재 간선 88개). CLAUDE.md 는 "SUB 자식 = 코어" 로 전제하지만 InjFlag 0 인 SUB 는 코어가 아닙니다.
    - 제안: InjFlag 0 인 SUB 는 QtyPer 를 곱하며 내려가고 InjFlag 1 인 코어에서 멈춥니다(`CoreItemResolver.ReadByInjFlag`·APS BOM 다단계와 같은 규칙).
33. **[하] 확정된 적 없이 폐기된 LOT 의 자재가 차감되지 않음**(10-16 백플러시)
    - 위치: `ReworkRepository.cs:251-252`(Rework 만 차감)`·269`(Scrap 차감 없음), `LotDefectRules.cs:57`(WasNeverConfirmed)
    - 내용: 로봇 NG 샷처럼 RAW/NG_BLOCKED → DEFECT → SCRAPPED 로 끝난 LOT 은 레진을 실제로 썼는데 차감 기록이 없어 라인 재고가 폐기율만큼 부풉니다. CLAUDE.md 는 "되돌리지 않는다" 만 적고 이 경우는 정하지 않았습니다.
    - 제안: 폐기 때 차감할지 사용자 결정을 받습니다.
34. **[하] 백플러시 멱등 인덱스 정의가 스키마 파일과 두 DB 에서 다름**
    - 위치: `dist/AMES_Schema.sql:9242-9246`(`IX_WH_InventoryTransaction_Source` — SourceType·SourceID 두 열)·`:11454-11456`(같은 이름의 넓은 정의 — 건너뜀), `migrate_wh_inventory_backflush_placeholder.sql:32-34`(두 열)
    - 내용: 개발·로컬 DB 는 SourceType·SourceID·TransactionType + 포함 열 5개입니다. 스키마로 새로 만든 DB 는 구조 서명이 개발 DB 와 어긋납니다.
    - 제안: 마이그레이션이 넓은 정의로 만들게 바꾸고 스키마 파일을 다시 뜹니다.
35. **[하] CLAUDE.md 백플러시 문단의 MRP 설명이 코드와 다름**
    - 위치: CLAUDE.md "MRP 가용재고 … 음수 자리표시까지 더한 순수량"(백플러시 문단), 실제 `PpRepository.cs:1014-1020`
    - 내용: MRP 재고는 `Qty>0 AND AreaCode='MAT_AREA'` 라 라인 위치(I1·W1 등)와 음수 자리표시가 빠집니다. 라인이 불출보다 더 쓴 양은 MRP 에 보이지 않습니다(WH-AUTO 라인 보충 `WarehouseRepository.LineStock.cs:130` 은 음수를 포함해 맞음).
    - 제안: 문구를 코드에 맞추고, MRP 반영이 필요한지 정합니다.
36. **[하] Pop 개발 설정에 개인 장치값이 커밋됨**(10-16 e528cbca)
    - 위치: `src/03_Pop/AMES.Pop/appsettings.Development.json:9-19`(Printer `Mode: Tcp`·`192.168.10.20:9100`, Scanner `COM5`)
    - 내용: 어느 PC 에서든 `dotnet run` 하면 실제 네트워크 프린터로 ZPL 을 보내고 COM5 를 엽니다. CLAUDE.md 의 "포트 리포지토리 기본은 빈 값" 과도 어긋납니다(비밀번호는 기본 파일에 이미 있던 값이라 새 노출은 아님).
    - 제안: 저장소에는 File 모드·빈 포트를 두고 장치값은 커밋하지 않는 로컬 파일이나 환경변수로 줍니다.
37. **[하] PP→POP 시험 초기화 스크립트가 다른 데이터와 어긋나게 지움**(개발 전용, 10-16 943cdf24)
    - 위치: `tools/test_pp_pop/test_pp_pop_reset_dev.sql:346`
    - 내용: 10-17 기준 끊긴 `DowntimeID` 는 0건이라 실제 피해는 아직 없습니다. `PP_LineDowntimeLog` 를 전부 지워 고장(라인 정지)의 `DowntimeID` 가 없는 행을 가리키고, LOT 은 지우면서 BACKFLUSH 거래·`BF-` 자리표시 재고는 남기며, 공용 개발서버의 다른 개발자 PP 데이터도 통째로 지웁니다.
      안돈 고장(`MNT_FailureRegister` Source='ANDON')·조치 이력은 지우면서 `MNT_WorkOrder` 는 다루지 않아, 10-16 부터 MNT-002 「작업지시 발행」으로 만든 작업지시가 지워진 고장번호(`SourceRefID`)를 가리킨 채 남습니다(`:348-351`).
    - 제안: 비가동은 안돈 행만 지우거나 고장의 `DowntimeID` 도 비우고, 그 고장의 작업지시도 같이 지우며, 백플러시 거래·자리표시를 같이 되돌리고, 실행 전 서버 확인 가드를 둡니다.
38. **[하] PNT 라벨 부착이 넘겨받은 사번을 쓰지 않음**(10-17 재대조)
    - 위치: `PntRepository.cs:382-394`(ApplyLabel), 호출 `Pnt07LabelApply.razor:151`
    - 내용: `employeeNo` 를 받지만 `ModifiedBy` 에는 `userId` 만 넣고, 그 값이 null 이면 NULL 로 저장합니다. 상태도 보지 않습니다(문제점 9). 지금은 화면이 세션 사번을 넘겨 드러나지 않습니다.

---

## 2. 안영훈 — PDA·Api + Web WH·FG·SCM·Portal + 재구축 스크립트

### 2-1. 적용·검증 대기·요청 (8)
1. **재구축 스크립트 — 전체 재구축 검증 대기**
   - Portal 번호 변경은 임시 코드 경유로 충돌을 피하고 현재 001~004 화면에 맞췄습니다.
   - 선택적 개발 시드는 PDA_SEED → seed_portal_dev 순서이며, 기본 실행에서 제외합니다. 테스트 시드 실행에는 AMES_DEVELOPMENT_SEEDS=1과 세션 opt-in이 필요합니다.
   - SQL 구문 검사는 통과했습니다. 사용자 지시에 따라 DB 삭제·재생성·전체 재구축은 실행하지 않았습니다.
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
8. **[요청] 로케이션 2단계(1차·상세) — WH·PDA 반영**(10-22 이승욱, 사용자 결정 — 사용자가 따로 협의)
   - `MD_Location` 에 `LocationLevel`(1 = 1차 — SAP 저장위치 또는 MES 등록, **재고 없음** · 2 = 상세 — 재고 위치, 기본값)과 `ParentLocationID`(상세의 상위 1차, NULL = 미지정)를 더했습니다(`dist/migrate_md_location_parent.sql`, 로컬 DB 만 적용, `rebuild_db.sh` 는 `migrate_line_locations_eos.sql` 다음). 기존 행은 모두 상세·상위 미지정이라 지금 WH·PDA 동작은 바뀌지 않습니다. 1차 재고는 나중에 상세 재고를 합산해 처리합니다(사용자).
   - 요청 ① **1차(`LocationLevel = 1`)는 재고 위치가 될 수 없게** — PDA·Web 입고·적재·이동·조정·라인 보충·반품이 1차 로케이션 바코드를 받으면 거부. MD-018 은 재고가 있는 로케이션을 상위로 지정하는 것만 막습니다(52075).
   - 요청 ② WH 로케이션 지도(`Components/Shared/LocationMapDatabase.cs` 가 `ListLocations` 전체를 칸으로 그림)·WH 로케이션 목록 Api 에서 1차 행 제외 — 1차는 위치 칸이 없어 지도에 `-` 셀로 보입니다. 임시 1차 시드(`dist/seed_md_location_temp_primary.sql` — `TMP-FG_AREA`·`TMP-LINE`(구역 코드 `LINE`, 공통코드 `WH_AREA` 에 없는 값)·`TMP-MAT_AREA`)를 적용한 DB 에서는 이런 셀이 1차마다 하나씩 생기므로 **①·② 반영이 필요**합니다 — 사용자 지시로 10-22 개발 DB 에도 적용했으므로(`TMP-FG_AREA` 24·`TMP-LINE` 16·`TMP-MAT_AREA` 100) 지금 개발 DB 의 WH 로케이션 지도에 이 셀 3개가 보이고 PDA 가 `TMP-` 1차를 재고 위치로 받는 것도 막혀 있지 않습니다.
   - 요청 ③ WH 화면이 상세 로케이션을 만들 때 상위 1차를 고를 수 있게(지금은 상위 미지정으로 생기고 MD-018 수정 창에서 지정). 저장소 `UpdateLocation(…, setParent, parentLocationId)`·`InsertPrimaryLocation`·상수 `MasterDataRepository.LocationLevelPrimary/Detail` 를 쓰면 됩니다.
   - 알림: 사용자 결정으로 MD-018 「등록」이 상위 로케이션 콤보를 비우면 상위(1차), 고르면 그 아래 하위(재고 위치, 창고·구역은 상위를 따름)를 만듭니다(`InsertPrimaryLocation`·`InsertChildLocation`). 그래서 `InventoryDefectTests.Location_creation_and_fake_inventory_status_are_not_shown`(10-19)의 `OpenCreate` 부재 검사를 「`MD.InsertLocation(`(격자 생성) 부재 + 두 등록 메서드 존재」로 바꿨습니다. MD-018 에서 만든 하위는 통로·열·층(Aisle·Bay·Slot)이 비어 있어 WH 로케이션 지도에서 `-` 칸으로 보입니다. 화면에는 「1차」 대신 상위/하위 로케이션이라고 씁니다.
   - 알림: 스키마 파일의 `MD_Location` 에 `LineID`·`PartNo`·`MinimumQty`·`IX_MD_Location_Line`(10-19 라인 로케이션)이 없습니다 — `rebuild_db.sh` 의 마이그레이션이 만들므로 재구축은 되지만 파일이 개발 DB 와 다릅니다. 개발 DB 는 이 세 컬럼이 감사 컬럼 뒤에 있는데, 이번 마이그레이션이 `SAPFlag`·감사 컬럼을 맨 뒤로 옮기므로 적용하면 업무 컬럼 쪽으로 정리됩니다.


### 2-2. 보류 (4)
- **PO 자동 수집 정합성**: 고객 SRM에서 취소·삭제된 PO 라인은 조회에서 사라질 뿐 AMES에는 Open으로 남습니다. 이미 Confirmed인 수주의 수량·납기 변경도 덮어씁니다. 누락을 취소로 간주할지, 확정분 변경을 승인 대기로 둘지 업무 규칙 결정이 필요합니다.
- **배지 단독 로그인 정책**: 사용자 요청대로 EOS 양식 QR은 PIN 없이 로그인합니다. 임의 사번 문자열 로그인은 차단했고 로그인 횟수 제한·브라우저 CORS 기본 차단을 추가했지만, EOS QR 자체는 서명된 인증 수단이 아닙니다. 관리자 배지 복제 방지를 위한 서명/PIN 도입은 정책 결정 대기입니다.
- **PO 자동 수집 실패**: 고객 테스트 서버 환경 문제로 09-17부터 실패합니다. 일별 구매계획 수집(`DPSYNC-SEMS`)도 URL이 자리표시자로 보고되어 외부 서버 복구·접속 정보 확인이 필요합니다.
- **개발 완료 시 정리**: appsettings 비밀번호 평문을 접속문자열 환경변수로 옮깁니다. CORS가 필요한 브라우저 클라이언트는 `Cors:AllowedOrigins`에 명시적으로 등록합니다(네이티브 PDA에는 CORS가 필요 없습니다).

### 2-3. 테이블 (10-14 전수 조사)
| 테이블 | 상태 | 내용 |
|---|---|---|
| `PP_SupplyPlan`·`PP_SupplyPlanDetail` | 읽기만 | 구 공급 계획 API가 읽습니다. 외부 사용 여부 확인 전에는 엔드포인트·테이블을 삭제하지 않습니다. |
| `WH_ReleaseSchedule` | 미사용 | DROP 후보입니다. 이번에는 다른 WH 테이블과 식별자·행위자 길이만 맞췄고 테이블은 보존했습니다. |
| `PP_MaterialReservation` | 미사용 경로 | `ListPickingSlipCandidates` 호출처와 예약 생성 경로가 없습니다. 삭제 또는 예약 기능 설계 결정 대기입니다. |

### 2-4. 유효한 문제점 (0)

코드 수정 완료 항목은 제거했습니다. 서버 배포·실제 쓰기 검증 대기는 2-1에서 관리합니다.

### 2-5. 재검토 정정 — 운영 결함 아님·선택적 정리 (4)
1. **`SCM_ItemVendor.PackingUnitCode`가 남아 있음 — 미사용 컬럼 정리이지 기능 결함은 아님**
   - 현재 SCM 품목·공급업체 연결은 `ScmRepository.ItemVendorRow`와 `ListItemVendors`/`LinkItemVendors`를 사용하며 이 컬럼을 읽거나 저장하지 않습니다. 런타임 소스 참조도 없습니다. 컬럼이 남아 있다는 사실만으로 조회·발주·입고가 잘못되지 않으므로 반드시 삭제해야 할 문제로 분류하지 않습니다. 별도 정리 승인 전에는 보존합니다.
2. **기존 BOX·PLT·SH 기록 — 신규 사용 금지와 과거 데이터 보존을 혼동한 지적 정정**
   - `migrate_retire_packaging_uom.sql`은 기존 수량·단위를 환산하지 않는다고 명시하고 포장 UOM을 비활성화합니다. `MasterDataRepository.IsQuantityUnit`과 `ScmPreview.LoadEditorMaterials`는 신규 발주 품목에서 이 단위들을 제외하며 저장 시 품목 단위도 확인합니다. 따라서 "과거 값이 남았으니 일괄 EA 변환해야 한다"는 결론은 현재 구현 방향과 맞지 않습니다.
   - 생성기의 SH→EA는 새로 생성할 시드 규칙이며 기존 데이터 환산 승인이 아닙니다. 특히 BOX의 내용물 환산 컬럼은 삭제돼 과거 BOX 수량을 EA로 추정 변환하면 의미가 달라질 수 있습니다. 실제 해당 품목을 다시 발주해야 할 때 품목·BOM·발주의 기준 수량을 개별 확인합니다. 과거 점검의 품목·발주 건수는 현재 DB 값으로 재확인하지 않았습니다.
3. **`FG_CustomerReturn.PartName`의 물리적 컬럼 순서 — 실행 오류 아님**
   - `FinishedGoodsRepository.ListReturns`는 컬럼을 명시해 조회하고 `PartName` 등 이름으로 값을 읽으며, PDA 저장 SQL도 INSERT 컬럼을 명시합니다. 물리적 순서 차이로 데이터가 다른 필드에 들어가는 경로는 확인되지 않았습니다.
   - 개발 DB 에서 `PartName` 은 감사 컬럼 뒤 맨 끝(column_id 31)입니다. 새 스키마는 이미 `LotNo` → `PartName` → 감사 컬럼 순서로 통일했습니다. 기존 DB까지 그 순서로 맞추는 것은 선택적 표준화이며, 이 차이만을 없애려고 감사 컬럼 복사·삭제·재생성이 포함된 `migrate_audit_columns_last.sql`을 자동 실행하지 않습니다. 실제 장애 수정과 분리합니다.
4. **일별 출하의 옛 운송사·회차 요약 — 현재 실출하 모델에는 해당 없음**
   - 현재 `FG_Shipment`와 보고서 데이터에는 운송사·회차가 없습니다. 예전 출하지시 기준의 요약을 되살리려고 임의 값을 만들거나 컬럼을 추가하지 않습니다. 실제 값이 있는 고객별 출하 건수·단위별 수량을 표시하며 서로 다른 UOM 수량은 합산하지 않습니다.
---

## 3. 이승욱 — Web SYS·MD·MNT·RPT·PP-CAL/LSB/OEE/DTL/ODM·계정·공통

### 3-1. 미해결
없습니다.

### 3-2. 보류 (4)
- **B7 MNT-004 금형 정비 등록**(`MoldRepository.cs:53·81` StartChange·CompleteChange, 호출처 없음): 정비 유형·작업자·샷 리셋·정비일을 남길 곳이 없습니다. 금형 교체를 기록하던 메서드도 옛 POP 화면을 지우면서 끊겼습니다 — 그런데 `MoldRepository.cs:8`·`MntRepository.cs:419` 주석은 여전히 "POP 금형 교체가 기록" 한다고 씁니다.
- **B8 PP-ODM·PP-OEE 라인 상태 기록**: `PP_LineStateLog` 에 쓰는 곳은 `tools/seed_pp_demo` 뿐입니다. 이 테이블을 원천으로 쓰기로 정했고(10-16, 심재현과 합의) 기록 방식은 심재현과 진행 예정입니다(아래 테이블 2).
- **개발 완료 시 정리**: 운영 admin 시드 차단, appsettings 비밀번호·AllowedHosts·접속문자열 환경변수(Web).
- **품목 추가 불출율·MRP 여유율 반영 방식**(10-20 — 고객 요구 10-14 "BOM 은 정량, 원단 불출 +10%·MRP 품목별 여유율"): `MD_Item.IssueAddPct`·`MrpAllowPct`(%, NULL = 0)는 MD-003(화면·엑셀)에서 저장·표시만 하고 **작업지시 불출·BOM 자동 차감·MRP(PP-005) 계산은 아직 읽지 않습니다**. 정할 것: ① 자동 차감(backflush)을 정량으로 할지 불출율을 포함할지 ② MRP 소요에 불출율도 더할지(여유율만 쓸지) ③ 원단 등 품목 분류별 기본값을 둘지 품목마다만 둘지 ④ BOM 불량률(`ScrapPct`, 지금 전부 0)과의 관계. 결정 뒤 구현은 MRP·백플러시 담당(심재현)과 함께합니다. 같은 날 넣은 조달형태 `MD_Item.ProcureType`(내작·매입·유상사급·무상사급)도 저장·표시만 하고 MRP·구매·SAP 에서 읽지 않습니다.

### 3-3. 테이블 — 웹 화면이 읽지만 쓰는 곳이 없음 (10-14 전수 조사)
1. **`MNT_OEELog`(56행) — 설비 OEE 기록**
   - 읽는 곳: MNT-008 OEE 분석, RPT-004 설비 OEE, RPT-008 월간 KPI 가용률, RPT-010 OEE 원본, MNT 대시보드 「오늘 평균 OEE」, Api `/api/mnt/oee`(`MntRepository.cs:354·368-392·2501-2505`, `RptRepository.cs:419·468·553·866`, `MntEndpoints.cs:25`)
   - 내용: 이 테이블을 채우는 코드가 없습니다. 남은 56행(전부 06-02 `mnt-seed`)은 시연 데이터라 — 10-17 심재현 설비 재시드가 없어진 설비의 행을 지워 224행에서 줄었습니다 — 위 화면들은 실제 생산과 무관한 고정 숫자를 보여 줍니다. PP-OEE 는 별도 테이블 `PP_LineOEE` 를 저장해 서로 연결되지 않습니다.
   - 제안(권장): 화면들이 `PP_LineOEE`(라인 단위)를 읽도록 바꾸고 이 테이블은 폐기합니다. 다른 길은 PP-OEE 를 설비 단위로 확장하는 것입니다.
2. **`PP_LineStateLog`(720) — 라인 상태 분 단위 기록**
   - 읽는 곳: PP-ODM 비가동 모니터(`PpRepository.cs:1848-1899`), PP-OEE 계산(`OeeRepository.cs:36·230·265`), Api `PpEndpoints.cs:61`
   - 내용: 기록 코드가 없어 PP-ODM 현황뿐 아니라 **PP-OEE 가동률도 시연 데이터로 계산**됩니다(B8).
   - 방향(10-16 사용자 결정 — 심재현과 합의): PP-OEE 가동률 원천은 `PP_LineDowntimeLog` 로 바꾸지 않고 **`PP_LineStateLog` 를 그대로 쓴다**. 남은 일은 이 테이블에 실제 라인 상태를 분 단위로 기록하는 쪽을 만드는 것으로, 기록 주체(InjAgent 신호·POP·Api Worker 등)는 심재현과 정해 진행할 예정입니다.
3. **`PR_MoldChange`(3) — 금형 교체 이력**
   - 읽는 곳: MNT-004 「교체 이력」(`MntRepository.cs:425-446`)
   - 내용: 기록 메서드를 아무도 부르지 않아 시연 3행만 보입니다(B7). 실제 교체는 PP-LSB 금형 교체(MC) 슬롯으로 계획만 있습니다.
   - 제안: POP 교체 시작·종료 버튼(심재현) 또는 B7 정비 등록과 함께 기록합니다.
4. **`MNT_MoldShotCount`(0) — 금형 샷 누적**
   - 읽는 곳: MNT-004 금형 목록(`MntRepository.cs:406`), MD 금형·설비 삭제 사용처(`MasterDataRepository.cs:1760·2812`)
   - 내용: 샷을 쌓는 곳이 없어(InjAgent 는 `PR_ShotCount` 에만 씀) 소진율이 비어 있고 정비 한계 알림이 동작하지 않습니다.
   - 제안: InjAgent 가 샷을 기록할 때 이 카운터도 올립니다(심재현 연계).

### 3-4. 문제점
없습니다. 10-16 MNT 흐름 점검의 6건은 같은 날 사용자 결정대로 처리했습니다(CLAUDE.md MNT 규칙 ⑦·⑩·⑪). 안돈 보전 호출의 작업지시 자동 발행은 심재현 미해결 22 로 옮겼습니다.

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

## 5. 공통 — 다음 개발 항목
- `AMES.Pda`: PP 모듈·CM(고장) 작업지시 화면(예비품 SP 입출고·PM 작업지시 점검·완료는 구현됨) — 안영훈
- **[방향 확인] `AMES.Data` 의 SQL 위치**: CLAUDE.md 는 "inline SQL → `dbo.SP_*` 스토어드 프로시저 전환 예정" 인데, 10-16 PDA 는 반대로 프로시저를 앱 SQL(`PdaWarehouseSql` 등)로 옮겼습니다. 어느 쪽으로 갈지 정한 뒤 CLAUDE.md 문구와 이 항목을 맞춥니다 — 공통
- `AMES.Web`: 실제 데이터 바인딩 완성(일부 화면 scaffold 상태) — 각 화면 담당
- `AMES.Pop`: 추가 공정 모듈(필요 시) — 심재현
- **SAP 마스터 수신**(10-21 회의 결정 — 이 마스터들은 MES 가 SAP 로 보내는 것이 아니라 **SAP 에서 받는** 데이터): `MD_Item`·`MD_Vendor`·`MD_Location`(1차 로케이션만 — 10-22 2단계, 상세는 MES 전용)·`MD_Customer`·`MD_BomVersion`·`MD_Uom`(10-22) 의 `SAPFlag`(구 `SAPTransFlag`, 1 = SAP 에서 받은 행)는 MES 화면이 표시만 하고(목록 「SAP」 열·수정 창 헤더 ID 옆 SAP 표시) 쓰지 않으며, 등록·수정 창 체크박스와 BOM 의 SAP 전송 규칙(변경 사유 필수·「SAP 전송 변경」·새 승인/만료 버전 자동 해제)은 10-21 삭제했다. 할 일: ① **SAP 에서 직접 받는 데이터(`SAPFlag` = 1 인 행)는 MES 에서 수정할 수 없도록 비활성화해야 한다** — 여섯 화면(MD-003·MD-004·MD-009·MD-010·MD-018·MD-019)의 수정 창 입력·저장·삭제(BOM 은 버전·라인 편집·승인 흐름 포함)와 엑셀 업로드·저장소 `Update*`/`Delete*` 를 함께 막을지, MES 만 쓰는 칸(예: 품목 적입수량·사출품 여부·불출율, 로케이션 적재 용량)은 열어 둘지 칸 단위로 정해야 한다 ② SAP 수신 기능(방식 SAP B1 Service Layer 등·주기·담당, 키 매칭과 새 행/변경/삭제 처리, 받은 시각·오류 기록 테이블, 순서: 단위 → 품목 → 고객사·협력업체·로케이션 → BOM) ③ 이미 MES 에서 만든 행과 SAP 행이 같은 키로 들어올 때의 처리(덮어쓰기·`SAPFlag` 1 로 전환 여부). 그 전까지 `SAPFlag` 는 SQL 로만 1 이 된다.

## 6. 참고 — 검증 기준선

- **10-22 04시(이승욱 SAPFlag 개명·MD_Uom SAPFlag·로케이션 2단계(상위/하위)·임시 상위 로케이션·BOM 소요량 + 김현진 4건(Api Scalar·POP 크기·접속 DB 로그) 병합 caaa520e 뒤, Release 빌드, Data 는 로컬 DB)**: Pop 154, Api 214(건너뜀 1), InjAgent 54 통과, Data 1,435건 중 1,414 통과·같은 10 실패(PlanSchedule 5·WorkOrderRepository 4·InjLot 1)·11 건너뜀, 개발 DB 대상 `ColumnTagsTests`·`UomListTests` 3/3. **개발 DB 적용**(COPY_ONLY 백업 `AMES_DEV_before_sapflag_location_20261022.bak` 서버 기본 백업 폴더): `migrate_md_customer_sap_flag` → `migrate_md_sap_flag_rename` → `migrate_md_uom_sap_flag` → `migrate_md_location_parent` → `seed_md_location_temp_primary`(상위 3건·하위 140건) → `migrate_audit_columns_last`(`FG_CustomerReturn`·`FG_Delivery` 감사 컬럼 정렬 — 로컬도 `FG_Delivery`). 로컬 DB 는 SAP 표시 샘플 8행 0 으로 원복, 개발 DB 에서 은퇴된 구 PDA 프로시저 4개(`FG_PDA_OUTBOUND_COMPLETE`·`RETURN_RECEIVE`·`RETURN_SCAN`·`TRANSACTION_LIST`, 코드 참조 없음)를 지워 **두 DB 구조 동일**(178 테이블, 도구 `schema_compare.ps1` IDENTICAL). 스키마 파일 `MD_Location` 에 안영훈 `LineID`·`PartNo`·`MinimumQty`·`IX_MD_Location_Line` 을 더해 재구축 순서도 DB 와 같게. 헤드리스(로컬 IIS·개발 DB, 저장 없음) 7/7: MD-003·004·009·010·019 「SAP」 열, MD-004 「소요량」, MD-018 상위 로케이션 묶음, admin 로그인 기록 원복·감사 행 증가 없음. 게시본 `publish/AMES.Web_20261022-040608.zip`·`AMES.Api_20261022-040735.zip` — **개발서버 Web·Api 를 이 zip 으로 바꾸기 전까지 서버의 구 Web 은 MD-003·004·009·010·018 과 `ListItems` 를 쓰는 화면이 `Invalid column name 'SAPTransFlag'`, 구 Api 의 `/api/sys/uoms` 는 영향 없음**(구 Api 는 `SAPFlag` 를 읽지 않음).
- **10-20 06시(이승욱 MD 작업 — 조달형태·불출율/여유율·OEELogID bigint·BOM 상위 품번·승인 요청 차단·등록일·단위 잠금·SAP 전송·품목/BOM 엑셀 — 과 안영훈 WH·FG 13건·심재현 PP→POP 시험 스크립트 병합 뒤, Release 빌드, Data 는 로컬 DB)**: Pop 154, Api 214(건너뜀 1), InjAgent 54 통과, Data 1,444건 중 1,423 통과·같은 10 실패(PlanSchedule 5·WorkOrderRepository 4·InjLot 1)·11 건너뜀. 마이그레이션 `migrate_md_item_procure_type`·`migrate_md_item_allowance_pct`·`migrate_mnt_oeelog_bigint`·`migrate_md_bomversion_sap_trans_flag` 는 두 DB 적용, 안영훈 4개(`restore_sheet_uom`·`line_locations_eos`·`fg_lot_defect_permission`·`wh_pda_test_permission`)는 개발 DB 작성자 적용·로컬 적용. 두 DB 구조 차이는 `FG_CustomerReturn.PartName` 위치뿐. 헤드리스(로컬 IIS·개발 DB, 저장 없음): FG·WH·MD 화면 열기, 엑셀 버튼 순서·색 3언어 7/7, 밀린 BOM 버전 SAP 4/4, 상위 BOM 8/8. 엑셀 업로드(로컬 DB 쓰기, 끝나고 삭제): 다운로드 → 그대로 업로드 등록 0(품목 235·BOM 178 건너뜀), 시험 파일 품목 1·BOM 새 버전 1(라인 2)·초안 버전에 라인 1 더하기 14/14.
- **10-17 05시(MD-034 출고 컬럼 a7c460d2 + 심재현 `MD_MoldColor` 폐지 병합 dd8df481 뒤, Release 빌드, Data 는 로컬 DB)**: Pop 154, Api 214(건너뜀 1), InjAgent 54 통과, Data 1,335건 중 1,314 통과·같은 10 실패·11 건너뜀. `migrate_drop_md_mold_color.sql` 로컬 적용(개발 DB 는 작성자 적용). 두 DB 구조 차이는 `FG_CustomerReturn.PartName` 위치와 안영훈 2-1 8(개발 DB 에만 있는 FG·WH 변경). 헤드리스(로컬 IIS·개발 DB, 저장 없음): MD-034·MD-007·MD-035·MD-010·FG 출하·일별 출하 열기 7/7(FG 두 화면은 빈 목록 — 2-1 8), MD-034 등록·수정·삭제 23/23(로컬 DB). 게시본 `publish/AMES.Web_20261017-044941.zip`·`AMES.Api_20261017-045111.zip`.
- **10-17 03시(MD-034·035 커밋 de3ae140 + 심재현 설비 재시드 병합 ba968b99 뒤, Data 는 로컬 DB)**: Pop 154, Api 214(건너뜀 1), InjAgent 54 통과, Data 1,335건 중 1,314 통과·같은 10 실패·11 건너뜀. 두 DB 175 테이블, 구조 차이는 `FG_CustomerReturn.PartName` 위치 하나(`migrate_fg_shipment_no_foreign_keys`·`migrate_md_pallet`·`reseed_md_equipment_inj_lines` 모두 두 DB 적용). 헤드리스: MD-034·035 등록·수정·삭제 22/22(로컬 DB), 개발 DB 경유 화면 열기 5/5.

- **테스트(2026-10-16 06시, 10-16 MNT 작업·원격 병합 2회(SYS-002, 안영훈 FG 실출하·PDA 보전)·PP-OEE 계산 수정 후, Data 는 로컬 DB)** — 20시 SAPTransFlag 뒤 다시 돌려 Pop·Api·InjAgent 같은 수치, Data 1,266건 중 1,248 통과(새 `SapTransFlagTests` 1건)·같은 10 실패·8 건너뜀. **22시 원격 병합(심재현 백플러시·POP DbClock·PP-APS) 뒤**: Pop 153, Api 204(건너뜀 1), InjAgent 54 통과, Data 1,291건 중 1,270 통과·같은 10 실패·11 건너뜀. 이어 22시 10분 안영훈 PDA 직접 SQL 전환 병합 뒤: Api 206(건너뜀 1), Data 1,323건 중 1,302 통과·같은 10 실패·11 건너뜀(Pop·InjAgent 같음), 웹 WH·FG·SCM·PP-APS 8화면 헤드리스 열기 정상. 22시 50분 심재현 LOT 라벨 병합·포탈 다국어화 뒤: Pop 154(나머지 같음), 포탈 한국어 화면 영어 문구 0(목록·상세·인쇄·납품서 생성 창). 구 PDA 프로시저는 스키마·마이그레이션 파일에서만 빠졌고 두 DB 에는 그대로 있습니다(37개, 두 DB 같음 — 삭제는 작성자 보류). 로컬 DB 에 `migrate_wh_inventory_backflush_placeholder.sql` 적용(개발 DB 는 작성자 적용), 개발용 시드 변경(`seed_aps_dev.sql` 적입수량·`seed_md_mold_master.sql` CBK)은 두 DB 모두 미적용(작성자도 미적용) — 두 DB 구조 차이는 `FG_CustomerReturn.PartName` 위치뿐.
  - Pop 149, Api 204(건너뜀 1), InjAgent 54 모두 통과. InjAgent `PollerRunnerTests.Stop_timeout_keeps_running_flag_until_loop_exits` 는 타이밍에 기대어 가끔 1건 실패합니다(바로 5번 다시 돌려 모두 통과 — 심재현 문제점 27 과 같은 정지 경합 부분).
  - Data 1,265건 중 1,247 통과, 10 실패, 8 건너뜀(Data 테스트는 10-15 부터 직렬 실행). 병합 뒤 `CodeGroupsUsedByCodeTests` 가 깨져 `FG_STORAGE_METHOD`(FG 입고 테이블 폐지로 읽는 코드가 없어짐)를 삭제 금지 목록에서 뺐습니다.
  - 실패 10건은 모두 기존 항목입니다(PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1 — 심재현 보류).
- **화면 저장 검증 10-16 04시**(로컬 IIS 를 로컬 DB 로 돌려 실제 저장, 끝난 뒤 검증 전 백업으로 로컬 DB 복원·로컬 IIS 는 개발 DB 로 되돌림 — 도구 `AppData\Local\AMES-claude-tools\save_verify_a/b/c*.mjs`): 10-14~10-16 의 저장 경로를 모두 화면에서 저장해 DB·감사 로그로 확인했습니다 — MNT-001 수동 상태, MNT-002 라인 정지 등록·해제·재설정·작업지시 발행·보고자 이름(사번), PP-DTL 보전 행 잠금·비고 저장, MNT-005 PM 등록(템플릿)·「예정」·취소·다시 사용, 달력 「완료 처리」 → MNT-007, 점검 결과·메모, 정상 완료 차단·CM 후속(메모 포함), 사이클 PM 다음 예정일(빈 값·같은 날 거부), 부분 완료 → 후속 작업지시·원래 작업지시 안내, 후속 정상 완료 → 고장 해결·비가동 닫힘, 종료, PP-OEE 스냅샷 생성·덮어쓰기 감사, PP-LSB 적용·초기화 감사, PP-CAL 끝난 WO 안내·그사이 끝난 WO 거부·정상 변경, SYS-004 바뀐 화면만 저장, MD-008 LOT 50자(한글), PP-002 자동 확정 켜기·끄기 → SYS-007 `AUTOCONFIRM_OFF`(15자) 표시, PP-001 업로더 이름(사번). 이 과정에서 **PP-OEE 계산이 실적 없는 구간에서 실패하던 결함**(`OeeRepository.ComputeOee` 의 `RowsWithStd` SUM 이 NULL → DBNull 변환 예외)을 찾아 고쳤습니다(테스트 `OeeComputeTests`).
- **헤드리스 확인 10-16 05시**(로컬 IIS·개발 DB, ko, 1920): PP-OEE 라인 상세 「계산」(2026-06-02, 저장 없음)이 오류 없이 결과를 내고, MNT-007(상태 필터 「예정」·`?id=&complete=1` 로 완료 창 바로 열림)·MNT-002(작업지시 없는 안돈 고장의 「작업지시 발행」)·MNT-005·MNT-009(PM 예정 「설비 n · 보전 m」·보전 PM 타일)·SYS-002(역할 사용자 팝업)가 Blazor 오류·가로 넘침 없이 열립니다. 저장은 하지 않았고, 점검 로그인의 admin 기록은 되돌렸습니다.
- **두 DB 구조(10-15 06시)**: 개발 DB 를 기준으로 로컬 DB 를 비교해 **같습니다** — 테이블 175개의 컬럼 순서·형식·NULL·콜레이션·IDENTITY·기본값, 인덱스(키·포함 열·필터), CHECK, 외래키, 프로시저·뷰·함수 정의(도구 `AppData\Local\AMES-claude-tools\schema_compare.ps1`).
  - 그 뒤 10-16 MNT 마이그레이션 3개(`migrate_mnt_pm_checklist`·`migrate_mnt_failure_downtime`·`migrate_mnt_pm_lead_days`)를 두 DB 에 같게 적용했고, `PP_LineDowntimeLog`·`MNT_WorkOrder` 구조가 같은 것을 확인했습니다.
  - 맞추면서 로컬에 한 것: 두 번째 병합의 마이그레이션 3개 적용(그 전에 `WH_InventoryTransaction` 268행 `OperatorID` 를 개발 DB 처럼 NULL 로 — 길이 마이그레이션이 20바이트를 넘는 값을 거부), 개발 DB 에만 있던 `SCM_ItemVendor.PackingUnitCode` 추가, `FG_CustomerReturn.PartName` 을 개발 DB 처럼 맨 끝으로.
  - 10-16 `migrate_retire_packaging_uom.sql` 을 두 DB 에 적용해 `BoxContentUnit/Qty` 4개가 없어졌습니다(스키마 파일과 같음).
  - **10-16 06시 원격 병합(안영훈 FG 실출하·PDA 보전 9건)** 뒤 로컬 DB 에 `migrate_fg_shipment_actual.sql`·`migrate_fg_putaway_transactions.sql`·`migrate_wh_fg_scm_column_lengths.sql` 을 적용했습니다(개발 DB 는 작성자가 이미 적용, 적용 전 로컬 백업 `AMES_DEV_before_fg_actual_20261016.bak`). `schema_compare.ps1` 결과 두 DB 모두 **테이블 174개**이고 차이는 `FG_CustomerReturn.PartName` 위치 하나입니다(안영훈 문제점 5).
  - 스키마 파일과 개발 DB 의 차이: `FG_CustomerReturn.PartName` 위치(안영훈 문제점 5).
  - **10-16 20시** `migrate_md_sap_trans_flag.sql`(`MD_Item`·`MD_Vendor`·`MD_Location.SAPTransFlag`)을 두 DB 에 적용했고, 세 테이블의 컬럼 순서·형식·NULL·기본값이 두 DB 에서 같습니다(68행 비교, 차이 0). 스키마 파일에도 같은 위치로 넣었습니다.
  - **10-21** `migrate_md_customer_sap_flag.sql`(`MD_Customer.SAPFlag`, `CreatedBy` 바로 앞)과 `migrate_md_sap_flag_rename.sql`(다섯 테이블 `SAPTransFlag` → `SAPFlag`, 기본값 제약·설명 포함)을 **로컬 DB 에만** 적용했습니다 — 개발 DB 는 신 Web 게시와 함께 적용합니다(적용 뒤 구 Web 은 `Invalid column name 'SAPTransFlag'`). 스키마 파일·`rebuild_db.sh` 반영, 옛 SAP 마이그레이션 4개는 `SAPFlag` 가 있으면 옛 컬럼을 다시 만들지 않게 고쳤고 로컬 DB 에서 다시 돌려 확인했습니다. 헤드리스(로컬 웹·로컬 DB, 도구 `sap_flag_recv_check.mjs`, 확인 동안만 다섯 테이블 `SAPFlag` 1 → 끝나고 0, 저장 없음) 13/13: 다섯 목록 「SAP」 열 61~70px, 등록·수정 창 SAP 체크박스 없음, 수정 창 헤더 ID 옆 SAP 표시, MD-004 버전 정보 SAP 표시·SAP 버튼 없음(초안 버전 수정 창은 첫 화면에 초안이 없어 확인 못 함). 테스트 `SapFlagTests`·`BomImport*`·`MasterExcel*`·`ProcureType*` 등 73건 통과(로컬 DB). `ColumnTagsTests` 는 개발 DB 와 대조하므로 개발 DB 적용 전에는 `SAPFlag` 태그로 실패합니다.
  - **10-22** `migrate_md_uom_sap_flag.sql`(`MD_Uom.SAPFlag`, `ActiveFlag` 다음 = `CreatedBy` 바로 앞)을 **로컬 DB 에만** 적용(두 번 실행해 재실행 안전 확인)했습니다 — 새 컬럼이라 개발 DB 는 신 Web·Api 보다 먼저 적용하면 되고 구 Web·Api 는 적용 뒤에도 안전합니다. 스키마 파일·`rebuild_db.sh` 반영. 헤드리스(로컬 IIS·로컬 DB, 도구 `uom_sap_check.mjs`, 로컬 DB 의 KG·M 을 `SAPFlag` 1 로 둔 상태, 저장 없음) 5/5: MD-019 목록 「SAP」 열 70px·KG·M 만 체크, 등록 창 SAP 없음, KG 수정 창 헤더 SAP 표시·EA 없음. `SapFlagTests`(단위 추가)·`UomListTests`·`ScmQuantityUnitTests` 등 35건 통과(로컬 DB). `UomListTests`·`ColumnTagsTests` 는 개발 DB 적용 전에는 개발 DB 대상으로 실패합니다.
  - **10-22** `migrate_md_location_parent.sql`(`MD_Location.ParentLocationID`·`LocationLevel`, `SAPFlag`·감사 컬럼을 맨 뒤로)을 **로컬 DB 에만** 적용(재실행 확인)했습니다. 10-21 샘플로 SAP 표시했던 상세 `FA01A1F1`·`FA01B1F1` 은 0 으로 되돌림. 이어서 사용자 요청으로 임시 1차 `seed_md_location_temp_primary.sql` 을 로컬 DB 에 적용(확인용 샘플 `S1000` 은 지움) — 처음에는 구역 18개마다 `TMP-{구역}` 를 만들었다가 사용자 요청으로 사출·감싸기·도장 라인 16개를 1차 하나 `TMP-LINE` 로 묶어 최종 1차 3건(`TMP-FG_AREA` 21·`TMP-LINE` 16·`TMP-MAT_AREA` 100, 상세 137건 전부 상위 지정, 라인별 임시 1차 16건은 스크립트가 지움, 두 번 실행해 재실행 안전 확인). MD-018 목록은 1차 아래에 하위가 이어지는 묶음 정렬 — 헤드리스(`loc_tree_order_check.mjs`) 3/3. 헤드리스(로컬 IIS·로컬 DB, 도구 `loc_parent_check.mjs`, 저장 없음) 10/10: KPI 「1차」, 상위 열(1차 하위 3·상세의 상위 ID), 1차 수정 창(안내·SAP 표시·품번/용량/상위 없음), 상세 수정 창(상위 콤보), 「1차 등록」 창. `LocationHierarchyTests` 와 로케이션·재고 관련 테스트 83건 통과(로컬 DB, 1건은 원래 건너뜀).
- **NuGet(10-15)**: 취약 패키지 0(`dotnet list package --vulnerable --include-transitive`), 모든 패키지가 최신 안정 버전입니다(SqlClient 7.1.1, EF Core 10.0.13, Radzen 12.1.2, Test.Sdk 18.11.0, xunit 러너 4.0.1 등). 취약성 때문에 `System.Security.Cryptography.Pkcs` 를 AMES.Data 에서 10.0.13 으로 고정했으니 SqlClient 를 올릴 때 다시 확인합니다.
- **제품 코드 규칙**: `AddWithValue` 0곳(주석 3곳만), Web 의 `DateTime.Now/Today` 0곳, 라이트 테마 토큰화 대상 0.
- **헤드리스 확인**
  - 10-16 23시(로컬 IIS·개발 DB, ko, 1920, 저장 없음 — 도구 `scm_design_check.mjs`·`portal_i18n_check.mjs`·`dropdown_shot.mjs`): SCM-001·003·004 조회부·창 입력이 Radzen(기본 select·date·checkbox 0)이고 콤보·체크박스로 목록이 걸러지며, 포탈 한국어·스페인어 화면에 영어 고정 문구가 없고(목록·상세·인쇄·납품서 생성 창), 협력업체 조회 콤보(360px)를 펼쳐도 두 줄 항목이 없습니다. 점검 로그인 기록은 되돌렸습니다.
  - 10-16 20시(로컬 IIS·개발 DB, ko, 1920, 저장 없음 — 도구 `sap_flag_check.mjs`): MD-003·MD-009·MD-018 목록에 「SAP 전송」 열이 있고 등록·수정 창에 체크박스가 보이며 Blazor 오류·가로 넘침이 없습니다. 테스트 `SapTransFlagTests`·`TypedSaveRoundTripTests`·`ColumnTagsTests`·`MdDeleteUsageTests` 등 29건 통과(로컬 DB). 점검 로그인의 admin 기록은 되돌렸습니다.
  - 10-15 06시(로컬 DB, ko, 1920): Radzen 12 게시본에서 RPT-001·RPT-010·MNT-008·PP-OEE·MD-008·SYS-007·PP-001·FG 반품·MD 위치·MNT-002 10화면이 Blazor 오류 없이 열리고, 날짜 선택기는 모두 11.x 와 같은 월·연 드롭다운입니다. 차트 축 눈금·카테고리 라벨(회전·생략 없음)은 11.5.1 게시본과 같은 값입니다(OEE 두 화면은 로컬 DB 에 추이 데이터가 없어 차트 없음). 점검 로그인의 admin 기록은 되돌렸습니다.
  - 10-15 새벽(ko·en·es, 1920): 첫 병합 뒤 SCM·PP 내부 17화면과 포탈 8화면 목록에서 헤더 잘림·두 줄·셀 넘침이 없습니다. 포탈 「박스 라벨」 납품서 열의 두 줄은 원래 설계입니다.
- **두 번째 병합의 WH·FG·SCM 변경 검증(안영훈)**: API 74개 통과, 용량·위치 규칙 테스트 17개 통과, Web·PDA Android 빌드 성공. 실제 입출고·동시 PDA 부하 검증과 DB 전체 재구축은 실행하지 않았습니다.
- 항목을 해결하면 같은 커밋에서 이 문서의 해당 항목을 지웁니다(해결 기록은 남기지 않고 커밋 이력으로 봅니다).
