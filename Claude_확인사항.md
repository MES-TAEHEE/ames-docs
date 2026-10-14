# Claude 확인사항 — 담당자별 미해결·보류·문제점

> **이 파일이 미해결·보류·문제점·다음 개발 항목의 유일한 정본입니다**(10-14 사용자 결정). CLAUDE.md·다른 문서·작업 메모에는 목록을 두지 않습니다. 항목을 해결하면 같은 커밋에서 여기 항목을 지우고, 새로 찾은 문제는 담당자 절에 더합니다.

- **작성**: 2026-10-14, Claude Code(이승욱 세션). 10-15 원격 병합(안영훈 SCM·포탈 10건) 뒤, 10-15 06시 두 번째 원격 병합(안영훈 WH·FG·SCM·Api 7건) 뒤 다시 정리했습니다. 해결된 항목은 지우고 남은 것만 둡니다.
- **기준 소스**: `dev` 브랜치 10-15 두 번째 병합(3a573d9e 병합) 이후. 줄 번호는 점검 당시 기준이라 몇 줄 어긋날 수 있습니다.
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

| 담당  | 미해결 | 보류  | 테이블 | 문제점 | 가장 먼저 볼 것                                                                          |
| --- | --- | --- | --- | --- | ---------------------------------------------------------------------------------- |
| 심재현 | 21  | 7   | 7묶음 | 31  | [상] WO 번호 인덱스 없음 → POP 확정 지연 · [상] APS WO 생성 전체 실패 · [상] POP·InjAgent 배포 설정이 로컬 DB |
| 안영훈 | 3(적용·검증) | 4 | 3 | 5 | WH PDA 구 프로시저 참조 · 서버 Web·Api/IIS 반영 · 포장 단위 기존 데이터 정책 |
| 이승욱 | 0   | 5   | 6   | 0   | PP-OEE 가동률이 시연 데이터로 계산됨                                                              |
| 오세영 | 5   | 2   | —   | —   | 발송기가 없어 SYS-008 발송 이력이 시연 데이터                                                      |

### 지금 결정이 필요한 것(이승욱 작업분)
1. **개발서버 반영(가장 급함)**: 개발서버 Web 은 PP-LSB 발행 폐지 뒤 버전이지만, 그 뒤 들어온 심재현 10-13 BoxQty 폐지(`migrate_aps.sql` §1, 개발 DB 적용됨) 이전 버전입니다(10-14 밤 개발서버 화면 확인).
   - MD-003 품목 목록이 "등록된 품목이 없습니다" 로 비어 보입니다(구 Web 이 삭제된 `BoxQty` 를 읽다 실패). PP-APS 「조회」도 `BoxQty` 를 읽어 실패할 것으로 보입니다(화면은 열리고, 조회는 눌러 보지 않음).
   - PP-002 「자동 확정」 스위치가 없습니다.
   - 최신 패키지: `publish\AMES.Web_20261015-060006.zip`·`publish\AMES.Api_20261015-061557.zip`(06:16 — 개발서버에서 `/swagger` 를 켜는 판, CLAUDE.md AMES.Api 배포 참고)(또는 `publish\AMES.Web`·`AMES.Api` 폴더 — .NET 10.0.13 런타임 포함). 10-15 NuGet 업데이트(취약 Pkcs 고정·SqlClient 7.1.1·Radzen 12.1.2 등)와 두 번째 원격 병합(WH·FG·SCM·Api)까지 들어 있습니다. 반영 뒤 그 PC 에서 관리자로 `tools\iis-watchdog.ps1 -Install` 을 한 번 실행합니다(10-15 판은 ANCM 기동 실패 500.3x 와 종료 중 멈춤 503 을 함께 복구). Api 는 안영훈 2-1 1 의 IIS 상시 기동 설정도 같이 적용합니다.
   - DB 는 준비돼 있습니다: 10-15 마이그레이션 6개(`migrate_md_paint_fabric_lotno`·`migrate_sys_auditlog_actiontype`·`migrate_by_columns_employee_no`·`migrate_fg_customer_return_columns`·`migrate_wh_fg_scm_column_lengths`·`migrate_fg_location_type`)가 개발·로컬 DB 모두 적용됐습니다.
   - **반영한 뒤에** `dist/migrate_retire_packaging_uom.sql`(안영훈 10-14 — `MD_Item`·`WH_PurchaseOrder` 의 `BoxContentUnit`·`BoxContentQty` 삭제, 단위 BOX·PLT·SH 사용 안 함)을 개발 DB 와 로컬 DB 에 적용합니다. 지금 개발서버 Web 은 이 컬럼을 읽으므로 먼저 적용하면 MD-003·SCM 발주가 깨집니다. 두 DB 모두 아직 적용 전이고, 스키마 파일은 적용 뒤 구조(컬럼 없음)입니다. 적용 전에 안영훈 문제점 4(남는 데이터) 처리 방향을 정하는 것이 좋습니다.
2. **PP-OEE 가동률 원천**: 지금은 시연 데이터(`PP_LineStateLog`)로 계산됩니다. 실제로 쌓이는 비가동 기록(`PP_LineDowntimeLog`)으로 바꿀지 정해야 합니다(이승욱 테이블 2).
3. **화면 저장 검증**: 아래 변경은 DB 테스트·리포지토리 왕복(로컬 DB)·화면 열기(헤드리스)로만 확인했고, 화면에서 실제로 저장해 보지는 않았습니다.
   - 10-14: 감사 로그(MNT·PP-OEE·PP-LSB 적용/초기화)·MNT 부분 완료와 원래 작업지시의 고장 표시·PP-CAL 거부·SYS-004 바뀐 화면만 저장
   - 10-15: MD-008 LOT 번호 50자(nvarchar) 저장, SYS-007 감사 동작 20자, 사람 컬럼(…By) 사번 전환 뒤 등록자·보고자 표시(PP-001 업로드 이력 등), Radzen 12 날짜 선택기(103개)·차트 4화면 — 헤드리스로는 11.x 와 같은 월·연 드롭다운·축 눈금·라벨을 확인했습니다(아래 6절).

---

## 1. 심재현 — POP(QC 포함)·InjAgent·Devices + Web PP-001~006·PP-APS

### 1-1. 미해결 (21)

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
   - 위치: `MachinePoller.cs:76-77·117-157·160-175·202-221`, `Plc/FEnetClient.cs:140-149`
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
    - 내용: 재생성 목록(64-95행)에 `ScanRequired` 가 없고 기본값 제약(`DF_MD_Item_ScanRequired`)·인덱스도 되살리지 않으며, 컬럼 구성 대조 가드가 없습니다. 18-24행 가드 덕분에 `InjFlag` 가 이미 `ItemCategory` 바로 뒤에 있는 DB(개발·로컬)는 재실행해도 건너뛰지만, `InjFlag` 가 없거나 자리가 다른 DB 에 적용하면 뒤 컬럼이 데이터째 사라집니다.
    - 제안: 재생성 목록을 현재 `MD_Item` 구성으로 보강하고 컬럼 대조 가드를 둡니다. 이미 적용된 DB 가 대부분이면 "컬럼이 있으면 건너뜀" 만 남기는 것도 방법입니다.
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
    - 위치: `migrate_aps.sql:74·75·100·157·185`, `migrate_aps_plan_line_shift.sql:21`, `migrate_demand_plan.sql:69·92-96`
    - 내용: FK 를 두지 않는 원칙과 달리 FK 6개를 새로 만듭니다. DPSYNC 공통코드 그룹 이름에 폐지된 `ScheduledWorker · ` 접두어가 있습니다.
19. **[하] 테스트 코드에 접속 비밀번호·외부 IP**
    - 위치: `AmesDevDb.cs:11` 외 `AMES.Data.Tests` 7파일(총 8파일, 192.168.0.132 를 쓰는 6파일은 Connect Timeout=10, `MoldPlanningTests:20`, `PlanScheduleTests:21` 은 98.95.142.192 이고 Connect Timeout 도 없음)
    - 제안: 모두 `AMES_TEST_CONN` 하나로 바꿉니다.
20. **[하] POP PLAN 기준 결정**
    - 위치: `InjLotRepository.cs:750-758`, `ImgLotRepository.cs:537-546`
    - 내용: PP-LSB 「발행」이 폐지돼(10-14), 「적용」만 해도 현장 PLAN 에 바로 반영됩니다. 어떤 기준으로 할지 POP 담당이 정합니다.
21. **[하] POP IMG 코어 스캔 검사를 InjFlag 기준으로**
    - 위치: `ImgLotRepository.cs:138`(CreateFromCore → `CoreItemResolver.Read`)
    - 내용: 웹 WO 발행은 10-14 부터 PP-APS 와 같은 InjFlag 기준(`CoreItemResolver.ReadByInjFlag`)으로 코어를 정하는데, IMG 코어 스캔의 `CoreMismatch` 검사는 아직 품명 CORE 규칙입니다. 개발 DB 완제품 77개는 두 규칙의 결과가 같지만, 레일만 있는 품번처럼 둘이 갈리면 WO 의 사출 단계 품번과 IMG 검사가 다른 코어를 봅니다.
    - 제안: `ReadByInjFlag` 로 바꿉니다(POP 담당 작업 예정).

### 1-2. 보류 (7)
- **APS 금형 교체(MC) 한계**: "직전 금형"을 그 라인·그 날 처리 순서상 마지막 금형으로 보므로, 하루 안에 금형이 여러 번 바뀌면 그 사이 되돌림 MC 를 만들지 않습니다(같은 라인·같은 날 금형 그룹당 MC 최대 1개). 교체 자리가 없을 때 억지로 둔 MC 는 휴게·점유 구간과 겹칠 수 있습니다. 조정은 PP-LSB 에서 합니다.
- **B2 IMG 라벨 교대 글자가 항상 C**(`LabelPrinter.cs:27-32`): DAY/NIGHT 만 A/B 로 바꾸는데, 실제 교대 코드는 A/B/C 라 항상 C 가 찍힙니다. 교대 코드를 그대로 쓸지 답을 기다립니다.
- **B11 POP·InjAgent 시각을 DB 서버 기준으로**(AMES.Pop 30개 파일 78곳): DbClock 을 맞추지 않아 PC 시계를 씁니다. 결정은 됐고 아직 구현하지 않았습니다.
- **B15 PNT-09 교대 하드코딩**(`PntRepository.cs:443·451`): 8–15시/16–23시로 박혀 있어 공통코드 교대 시각과 다릅니다.
- **B3 POP 의 "오늘" 이 자정 기준**
  - 위치: `InjLotRepository.cs:769·120`, `QcRepository.cs:324·326·341·379`, `PntRepository.cs:39·109·449`
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
   - 위치: `PpRepository.Aps.cs:297·326` → `WorkOrderRepository.cs:580`
   - 내용: 대상 중 한 완제품이라도 코어를 정할 수 없으면 예외로 트랜잭션 전체가 롤백돼, 정상 행의 WO 도 0건입니다. PP-003 처럼 행 단위로 거부하지 않습니다. 코어 판정이 10-14 부터 InjFlag 기준이라 레일만 있는 품번은 이제 풀리지만, 사출품(InjFlag)이 없거나 여럿인데 품명 CORE 로 하나를 고를 수 없는 품번은 여전히 전체를 막습니다.
   - 제안: 그 행만 `NoCore` 로 거부하고 나머지는 진행합니다.
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
   - 내용: 들어가자마자 dryRun(실제 INSERT 후 롤백)이 돌아 잠금을 잡고 WoID(IDENTITY)를 소모합니다. 문제점 1 과 겹치면 POP 확정까지 막힙니다. 「미리보기 갱신」에도 권한 검사가 없습니다.
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
    - 위치: `LotDefectWriter.cs:25-30`(`InjLotRepository.cs:684`·`ImgLotRepository.cs:368` 에서 호출)
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
    - 위치: `SupplyPlanImport.razor:355`
    - 내용: 중간에 실패하면 앞 건만 확정되고, 몇 건이 반영됐는지 안내가 없습니다.
20. **[하] PP-002~006 승인·생성 동작에 감사 로그 없음**
    - 내용: 수주 확정, WO 생성·릴리스·취소, PR 생성·전송·승인이 SYS-007 에 남지 않습니다. PP-002 「자동 확정」 스위치를 켤 때 일괄 확정되는 수주도 마찬가지입니다(스위치 켜고 끄기만 기록됨).
21. **[하] 단위 EA 고정·WO CSV 품번 누락·MRP 상태 색**
    - 위치: `WorkOrder.razor:162·367`, `PlanConfirmBatchDialog.razor:31`, `Mrp.razor:195·376`(PR 상태 색이 옛 상태값 Pending/Rejected 기준)
22. **[하] 행위자 매개변수 형식이 컬럼과 다름**
    - 위치: `PpRepository.Aps.cs:1012·1039·1057`, `ApsRepository.cs:1041·1214`, `WorkOrderRepository.cs:629`(10-15 사람 컬럼 사번 전환 때 주간 구매계획·WO 릴리스·INJ/IMG 확정 매개변수는 고침)
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

### 2-1. 적용·검증 대기 (3)
1. **API IIS 상시 기동 설정 — 서버 적용 대기**
   - `tools/publish-api.ps1`에 AlwaysRunning·유휴 종료 해제·사이트 자동 시작·preload 설정을 추가했습니다.
   - 개발서버 IIS에 이 설정과 새 Web·Api 파일을 적용해야 합니다. 이번 작업에서는 서버 배포·앱풀 재시작을 하지 않았습니다.
2. **재구축 스크립트 — 전체 재구축 검증 대기**
   - Portal 번호 변경은 임시 코드 경유로 충돌을 피하고 현재 001~004 화면에 맞췄습니다.
   - 선택적 개발 시드는 PDA_SEED → seed_portal_dev 순서이며, 기본 실행에서 제외합니다. 테스트 시드 실행에는 AMES_DEVELOPMENT_SEEDS=1과 세션 opt-in이 필요합니다.
   - SQL 구문 검사는 통과했습니다. 사용자 지시에 따라 DB 삭제·재생성·전체 재구축은 실행하지 않았습니다.
3. **동시 출고·보충 Worker 부하 검증 대기**
   - 출고의 Serializable을 ReadCommitted로 낮추고 LOT 비교의 UPPER를 제거했습니다. 재고 행 변경 잠금과 피킹 지시 잠금은 유지합니다.
   - 동일 품목 여러 줄의 수요 분배 SQL은 읽기 전용 회귀 검사로 확인했습니다. 실제 PDA 여러 대와 보충 Worker를 겹쳐 실행하는 검증은 아직 하지 않았습니다.

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

### 2-4. 문제점 (5)
1. **[하] 사용되지 않는 BOX 내용·포장 단위 컬럼이 DB에 남음**
   - `MD_Item.BoxContentUnit/BoxContentQty`, `WH_PurchaseOrder.BoxContentUnit/BoxContentQty`, `SCM_ItemVendor.PackingUnitCode`.
   - 실행 코드 참조는 없습니다. 이번에는 컬럼 삭제 승인이 없으므로 유지했습니다. 컬럼 순서를 맞추기 위한 테이블 재구축도 하지 않습니다.
2. **[상] WH PDA 프로시저 5개가 지워진 옛 테이블을 가리킴**
    - 위치: 개발 DB 프로시저 `WH_PDA_INBOUND_RECEIVE_LOT`·`WH_PDA_INBOUND_MOVE_LOCATION`·`WH_PDA_INBOUND_CANCEL_RECEIPT`(→ `WH_OLD_Inventory`·`WH_Receiving`·`WH_InboundPackage`), `WH_PDA_RELEASE_PICK_LINES`·`WH_PDA_RELEASE_PICK_LOT`(→ `WH_ReleasePicking`). 스키마 파일에도 같은 정의로 들어 있습니다.
    - 내용: 네 테이블은 개발 DB 에 없습니다(`sys.sql_expression_dependencies` 에서 참조 대상 없음으로 확인). `WH_PDA_INBOUND_RECEIVE_LOT` 은 Api `WhEndpoints`·`ScmDeliveryCases`(케이스 입고)가 실제로 호출하므로, 그 문장에 닿는 입고·이동·취소·출고 피킹이 `Invalid object name` 으로 실패할 수 있습니다(실행해 보지는 않음). 옛 `migrate_wh_core_tables.sql` 이 프로시저를 옛 정의로 덮은 흔적으로 보입니다.
    - 제안: 통합 재고(`WH_Inventory`) 기준 정의로 다시 만들고, 스키마 파일을 다시 뜹니다.

3. **[하] `PR_PopSession` 새 컬럼이 CLAUDE.md 배포 순서에 없음**
    - 위치: `migrate_pda_bearer_session.sql:6-9`
    - 내용: `BearerHash`·`SessionJson` 을 만드는 이 마이그레이션이 CLAUDE.md 배포 순서에 없습니다(스키마 파일·`rebuild_db.sh` 에는 있음). 이 마이그레이션이 빠진 기존 DB 에서는 PDA 로그인이 전부 500 입니다.

4. **[하] 폐지한 포장 단위가 기존 데이터에 그대로 남음**
    - 위치: `dist/migrate_retire_packaging_uom.sql`(10-14), `tools/gen_md_item_bom_seed.py`(`quantity_unit`)
    - 내용
      - 마이그레이션은 단위 BOX·PLT·SH 를 사용 안 함으로만 바꾸고 데이터는 바꾸지 않습니다. 개발 DB 에는 SH 품목 30개, BOM 줄 77개, BOX 품목 1개, BOX 발주 6건이 남습니다.
      - 시드 생성기는 SH 를 EA 로 바꿨지만, 이미 들어간 데이터에는 반영되지 않습니다.
      - 마이그레이션이 지우는 `BoxContentUnit`·`BoxContentQty` 에 값이 든 행이 있습니다(품목 1, 발주 2). 이 값은 옮기지 않고 사라집니다.
    - 제안: 시드와 같게 SH → EA 로 바꿀지 정하고, BOX 품목·발주를 어떻게 다룰지 정한 뒤 마이그레이션에 데이터 정리를 더합니다.

5. **[하] `FG_CustomerReturn.PartName` 위치가 개발 DB 와 스키마 파일에서 다름**
    - 위치: `dist/migrate_fg_customer_return_columns.sql:23-24`(ALTER ADD), `dist/AMES_Schema.sql` 의 `FG_CustomerReturn`
    - 내용: 마이그레이션이 `PartName` 을 ALTER ADD 로 붙여 개발 DB 에서는 감사 컬럼 뒤 맨 끝에 있는데, 스키마 파일은 `PartNo` 바로 뒤입니다. 감사 컬럼을 맨 뒤에 두는 규칙(10-08)과도 어긋나고, 재구축한 DB·마이그레이션을 적용한 DB 의 컬럼 순서가 갈립니다. 10-15 로컬 DB 는 개발 DB 에 맞춰 맨 끝으로 두었습니다(코드는 이름으로만 써서 동작 영향은 없음).
    - 제안: 개발 DB 를 스키마 파일 순서로 재생성하거나, 스키마 파일을 개발 DB 순서로 맞춥니다. 마이그레이션 뒤에는 `migrate_audit_columns_last.sql` 을 다시 돌립니다.
---

## 3. 이승욱 — Web SYS·MD·MNT·RPT·PP-CAL/LSB/OEE/DTL/ODM·계정·공통

### 3-1. 미해결
없습니다.

### 3-2. 보류 (5)
- **B4 MNT-007 확장**
  - 위치: `MntRepository.cs:643·835·1298·1436·965-971·1464`, `WorkOrder.razor:252-263`
  - ① 수동 발행: 고장·PM 밖의 작업(개선·PdM)을 기록할 수 없습니다.
  - ② 체크리스트: 템플릿 단계를 작업지시로 복사하지 않고, 단계별 결과 입력이 없습니다.
  - ③ 부품 차감: 사용 부품이 자유 텍스트라 재고가 줄지 않습니다. "예비품 조정은 PDA 에서만" 결정과 충돌해 방향을 정해야 합니다.
  - 권장 순서는 ③ → ② → ① 입니다.
- **B5 보전 상태 설계**(`MntCodes.cs:33-40`, `MntRepository.cs:1002`): ASSIGNED 상태가 없고(배정해도 ISSUED), CLOSED 마감 경로가 없으며, OPEN 동의어·PdM 유형이 시연 데이터에만 있습니다.
- **B7 MNT-004 금형 정비 등록**(`MoldRepository.cs:53·81`, 호출처 없음): 정비 유형·작업자·샷 리셋·정비일을 남길 곳이 없습니다. 금형 교체를 기록하던 메서드도 옛 POP 화면을 지우면서 끊겼습니다.
- **B8 PP-ODM 라인 상태 기록**: `PP_LineStateLog` 에 쓰는 곳은 `tools/seed_pp_demo` 뿐입니다(아래 테이블 2).
- **개발 완료 시 정리**: 운영 admin 시드 차단, appsettings 비밀번호·AllowedHosts·접속문자열 환경변수(Web).

### 3-3. 테이블 — 웹 화면이 읽지만 쓰는 곳이 없음 (10-14 전수 조사)
1. **`MNT_OEELog`(224행) — 설비 OEE 기록**
   - 읽는 곳: MNT-008 OEE 분석, RPT-004 설비 OEE, RPT-008 월간 KPI 가용률, RPT-010 OEE 원본, MNT 대시보드 「오늘 평균 OEE」, Api `/api/mnt/oee`(`MntRepository.cs:299·321·1639`, `RptRepository.cs:472·521·611·932`)
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

### 3-4. 문제점
없습니다.

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
- `AMES.Pda`: PP·MNT(보전 작업) 등 추가 모듈 MAUI 화면(예비품 SP 입출고는 구현됨) — 안영훈
- `AMES.Data`: inline SQL → `dbo.SP_*` 스토어드 프로시저 전환 — 공통
- `AMES.Web`: 실제 데이터 바인딩 완성(일부 화면 scaffold 상태) — 각 화면 담당
- `AMES.Pop`: 추가 공정 모듈(필요 시) — 심재현

## 6. 참고 — 검증 기준선

- **테스트(2026-10-15 06시, 두 번째 원격 병합 + NuGet 업데이트 후, Data 는 로컬 DB)**
  - Pop 149, Api 74, InjAgent 54 모두 통과. InjAgent `PollerRunnerTests.Stop_timeout_keeps_running_flag_until_loop_exits` 는 타이밍에 기대어 가끔 1건 실패합니다(바로 5번 다시 돌려 모두 통과 — 심재현 문제점 27 과 같은 정지 경합 부분).
  - Data 1,139건 중 1,121 통과, 10 실패, 8 건너뜀
  - 실패 10건은 모두 기존 항목입니다(PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1 — 심재현 보류). 병렬 실행 중 교착이 가끔 1~2건 더 나오며, 단독 실행은 통과합니다.
- **두 DB 구조(10-15 06시)**: 개발 DB 를 기준으로 로컬 DB 를 비교해 **같습니다** — 테이블 175개의 컬럼 순서·형식·NULL·콜레이션·IDENTITY·기본값, 인덱스(키·포함 열·필터), CHECK, 외래키, 프로시저·뷰·함수 정의(도구 `AppData\Local\AMES-claude-tools\schema_compare.ps1`).
  - 맞추면서 로컬에 한 것: 두 번째 병합의 마이그레이션 3개 적용(그 전에 `WH_InventoryTransaction` 268행 `OperatorID` 를 개발 DB 처럼 NULL 로 — 길이 마이그레이션이 20바이트를 넘는 값을 거부), 개발 DB 에만 있던 `SCM_ItemVendor.PackingUnitCode` 추가, `FG_CustomerReturn.PartName` 을 개발 DB 처럼 맨 끝으로.
  - 두 DB 모두 `migrate_retire_packaging_uom.sql` 적용 전이라 `BoxContentUnit/Qty` 4개가 남아 있습니다(0절 1).
  - 스키마 파일과 개발 DB 의 차이: 위 4개 컬럼(스키마 파일은 적용 뒤 구조)과 `FG_CustomerReturn.PartName` 위치(안영훈 문제점 5).
- **NuGet(10-15)**: 취약 패키지 0(`dotnet list package --vulnerable --include-transitive`), 모든 패키지가 최신 안정 버전입니다(SqlClient 7.1.1, EF Core 10.0.13, Radzen 12.1.2, Test.Sdk 18.11.0, xunit 러너 4.0.1 등). 취약성 때문에 `System.Security.Cryptography.Pkcs` 를 AMES.Data 에서 10.0.13 으로 고정했으니 SqlClient 를 올릴 때 다시 확인합니다.
- **제품 코드 규칙**: `AddWithValue` 0곳(주석 3곳만), Web 의 `DateTime.Now/Today` 0곳, 라이트 테마 토큰화 대상 0.
- **헤드리스 확인**
  - 10-15 06시(로컬 DB, ko, 1920): Radzen 12 게시본에서 RPT-001·RPT-010·MNT-008·PP-OEE·MD-008·SYS-007·PP-001·FG 반품·MD 위치·MNT-002 10화면이 Blazor 오류 없이 열리고, 날짜 선택기는 모두 11.x 와 같은 월·연 드롭다운입니다. 차트 축 눈금·카테고리 라벨(회전·생략 없음)은 11.5.1 게시본과 같은 값입니다(OEE 두 화면은 로컬 DB 에 추이 데이터가 없어 차트 없음). 점검 로그인의 admin 기록은 되돌렸습니다.
  - 10-15 새벽(ko·en·es, 1920): 첫 병합 뒤 SCM·PP 내부 17화면과 포탈 8화면 목록에서 헤더 잘림·두 줄·셀 넘침이 없습니다. 포탈 「박스 라벨」 납품서 열의 두 줄은 원래 설계입니다.
- **두 번째 병합의 WH·FG·SCM 변경 검증(안영훈)**: API 74개 통과, 용량·위치 규칙 테스트 17개 통과, Web·PDA Android 빌드 성공. 실제 입출고·동시 PDA 부하 검증과 DB 전체 재구축은 실행하지 않았습니다.
- 항목을 해결하면 같은 커밋에서 이 문서의 해당 항목을 지웁니다(해결 기록은 남기지 않고 커밋 이력으로 봅니다).
