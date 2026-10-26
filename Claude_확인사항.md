# Claude 확인사항 — 담당자별 미해결·보류·문제점

> **이 파일이 미해결·보류·문제점·다음 개발 항목의 유일한 정본입니다**(10-14 사용자 결정). CLAUDE.md·다른 문서·작업 메모에는 목록을 두지 않습니다. 항목을 해결하면 같은 커밋에서 여기 항목을 지우고, 새로 찾은 문제는 담당자 절에 더합니다.

> **재검토 원칙**(10-16 사용자 확인): 이 문서의 지적·제안 자체가 구현 원칙이나 수정 지시는 아닙니다. 현재 호출 경로·합의된 업무 방향과 대조해 실제 결함인지 먼저 확인합니다. 지적이 틀렸거나 방향이 바뀌었다면 코드·DB를 억지로 바꾸지 않고, 이 문서에 해당하지 않는 이유·근거·현재 방향을 정정합니다. 운영 결함, 배포·검증 대기, 정책 결정, 선택적 정리를 구분하며 과거 DB 점검값을 현재 값으로 단정하지 않습니다.

- **작성**: 2026-10-14, Claude Code(이승욱 세션). 그 뒤 원격 병합·작업 때마다 해결된 항목을 지우고 남은 것만 둡니다(지난 판은 커밋 이력). **10-22 재정리**: 기준 소스를 `566893cd` 로 올리고 10-17(ba968b99) 이후 바뀐 파일을 가리키는 줄 번호를 다시 맞췄습니다(AppConfig·LineStock·스키마 파일·PP→POP 초기화 스크립트·MasterDataRepository — 나머지 참조 파일은 그 뒤 바뀌지 않음). 해결된 항목(안영훈 2-5 3 — 두 DB·스키마의 `FG_CustomerReturn.PartName` 위치 차이)을 지우고, 이승욱 10-21~22 작업(SAPFlag·로케이션 2단계)에 맞춰 안영훈 2-1 8·공통 SAP 항목·검증 기준선을 고쳤습니다. 다른 담당자 항목의 판정·문장은 바꾸지 않았습니다. **10-22 2차 재정리**: 기준 소스를 `2de82e9c` 로 올렸습니다. 김현진 POP 인라인 SQL → `dbo.SP_POP_*` 145개 전환(c10a16ee)과 심재현 팔레트 적재(dabed559·41f99dca)로 바뀐 POP 저장소·화면(Inj/ImgLotRepository·WorkOrderRepository·AndonRepository·QcRepository·PntRepository·ReworkRepository·AuthRepository·BackflushRepository·LotDefectWriter·MoldRepository·InjMain·ImgMain·ImgJudgePopup)의 줄 번호를 다시 맞췄고, SQL 이 프로시저로 옮겨 간 곳은 C# 메서드 범위와 `dist/migrate_pop_stored_procedures.sql` 의 줄을 함께 적었습니다. 지적 내용은 모두 그대로 남아 있음을 새 코드로 확인했습니다. 안영훈 2-1 의 번호 중복(8 두 개)을 9 로 고치고 공통 「SQL 위치」·검증 기준선을 고쳤습니다. **10-22 흐름 전수 점검**(사용자 요청 — 웹 마스터 → 계획 → POP 사출·감싸기·도장 → PDA 자재·완제품 입출고를 5구간으로 나눠 소스 읽기·로컬 복제 DB 조회만, 소스 변경 없음): 새로 찾은 것을 심재현 문제점 39~51·안영훈 2-4 1~11(2-1 8 세부 포함)·이승욱 3-4 1~2 로 더했습니다. 기존 항목과 겹치는 것은 넣지 않고 관련 항목 번호만 적었습니다. 로컬 DB 에 아직 팔레트 적재 데이터가 없어 팔레트 관련 판단은 코드 기준입니다. **10-22 3차·4차(심재현)**: 심재현 절의 줄 번호를 `7cd85fa4` 기준으로 다시 맞췄습니다(PNT-MAIN 전환으로 바뀐 저장 프로시저·스키마·Backflush·Rework·PalletStock·ImgJudgePopup). 해결·소멸한 항목은 지우고 뒤 번호를 당겼으며, 무엇을 지웠는지는 커밋 이력에만 둡니다. **10-23 이승욱**: 사용자 결정대로 쓰인 LOT PREFIX 잠금(52080·MD-001 입력칸 잠금)을 구현하고 바꿀 때 쓸 관리자 SQL 은 만들지 않기로 해(쓰인 접두어는 바꾸지 않고 새 라인으로 등록) 3-4 1 을 지웠으며, 나사·레진 품목 단위를 정본(EA·G)으로 되돌려(두 DB, BOM 12줄 함께) 3-4 2 를 지웠으며, 개발 DB 이름을 `AMES` 로 바꾸는 안의 조사 결과를 보류 3-2 에 더했습니다. 3-1 1(라인 상태 기록 운영 마무리)은 실제 라인이 돌 때 볼 일이라 지웠습니다 — 06:00 재분류 작업은 10-22 개발서버에 등록돼 10-23 06:00 성공, 실제 생산 속도가 개당 1분 이내라 가동 판정 임계값(사이클 × 2·최소 3분)은 처음 값 그대로 두고, 분 단위 기록의 보존 기간은 실제 가동 때 정합니다. 품목 추가 불출율·MRP 여유율 보류는 MD-003 저장·표시까지가 이승욱 몫이라, 계산 반영을 심재현 1-2(자동 차감·MRP·조달형태)와 안영훈 2-2(원단 불출)의 요청으로 옮겼습니다. 테이블 3-3 1(`MNT_OEELog` 를 채우는 코드 없음)은 사용자 결정 방안 A — 라인 OEE 스냅샷을 그 라인의 설비 행으로 옮기는 `SP_MNT_OEELog_Fill` 과 에이전트 작업 「[AMES] MNT OEE Fill」(5분, 개발서버 등록) — 로 구현해 지우고 뒤 번호를 당겼습니다. **10-23 23시 재정리**: 안영훈 `young` 병합(`a811750a`)과 이승욱 작업으로 바뀐 파일(PpRepository·BackflushRepository·MasterDataRepository·스키마 파일)을 가리키는 심재현·이승욱 절의 줄 번호를 다시 맞췄습니다(심재현 문제점 30 의 스키마 파일 줄·문제점 40 의 PP-003 두 번째 FG 재고 조회와 MRP 재고 조회 줄, 이승욱 3-3 의 설비 삭제 사용처 줄은 전에도 다른 줄을 가리켜 내용으로 다시 찾았습니다). 지적 내용과 다른 담당자 절의 문장·판정은 바꾸지 않았고, 검증 기준선을 지금 상태로 고쳤습니다. **10-23 23시 전수 대조**(사용자 요청 — 573ca09a 기준, 절별로 소스·저장 프로시저를 열고 개발·로컬 DB 는 조회만): 해결된 항목은 없었습니다. 심재현 절은 어긋난 줄 번호(1-1 4·6·10·16·20, 1-2 B23, 1-4 1·4~7·18~20·23·29·30·34~39·43)와 사실 문구(1-1 10 빠지는 컬럼, 1-1 20 안돈 고장 0건, 1-2 팔레트 ① PNT 포함, 1-3 「주석으로만」, 1-4 11·16·31·39·43)만 고치고 판정은 바꾸지 않았습니다. 안영훈 절은 10-23 본인 재검토 문장을 그대로 두고 「10-23 이승욱 대조」 메모(2-1 1·8·10, 2-2 PO 자동 수집)와 새 문제 2-4 2(테스트 시드)를 더했습니다. 이승욱 3-2 의 에이전트 작업 수를 5개로 고쳤습니다. **10-24 PUSH 발송기**: 사용자 결정(수신자 A안·FCM·PIN 없는 작업자 앱 로그인 불가)대로 AMES.Api 에 발송기와 앱 Api 를 만들고, 오세영 절을 앱 개발 참고사항으로 바꿨으며(전송 방식 보류 해결), 안영훈 2-1 에 Api 변경 알림을 더했습니다. **10-24 재정리**(원격 변경 없음 — `dev`·`young` 모두 새 커밋 없음): 발송기 마이그레이션을 개발 DB 에도 적용해 두 DB 구조가 같음을 다시 확인했고, 스키마 파일에 알림 인덱스를 넣어 밀린 심재현 1-4 30 의 줄 번호만 고쳤으며, 검증 기준선·게시 상태를 지금으로 바꿨습니다. 다른 담당자 절의 문장·판정은 바꾸지 않았습니다. **10-24 03시 재정리**: 원격 김현진 커밋 2개(POP 오프라인 동작 `8c9998cc`·안돈 라인 잠금 `7462e7e0` — 심재현 미해결 21 → 18, 번호와 다른 절·CLAUDE.md 의 참조는 작성자가 맞춤)를 병합하고, 작성자가 개발 DB 에만 적용한 `migrate_pop_stored_procedures.sql` 을 로컬 DB 에도 적용했습니다. SYS-008 에서 PUSH 채널 주소를 읽기 전용으로 바꾸고(이승욱) 개발 DB 의 메일 주소가 든 PUSH 채널 행을 지웠습니다(오세영 4-1 3). 검증 기준선·게시 상태를 바꿨고, 다른 담당자 절의 문장·판정은 바꾸지 않았습니다. **10-24 04시 스키마 파일 재생성**(사용자 지시): `tools/schema_from_db` 에 시퀀스를 더해 개발 DB 에서 `AMES_Schema.sql` 을 다시 떴습니다(179 테이블·프로시저 158·시퀀스 1). 그에 따라 심재현 1-4 30 의 위치·내용(스키마 파일 쪽은 맞음, 마이그레이션 정의만 남음), 이승욱 3-2 의 「스키마 파일만으로는 동작하지 않음」, 공통 5 의 「프로시저는 스키마 파일에 없음」, 6절 스키마 파일 대조를 사실에 맞게 고쳤습니다(판정은 그대로). 로컬 DB 의 POP 프로시저 2개는 개발 DB 정의 그대로 다시 만들어 두 DB 구조가 완전히 같습니다. 같은 날 SYS-008 발송 이력의 시연 행 40건(`sys-seed`, 06-02 — 두 DB 의 이력 전부)을 사용자 지시로 지웠습니다(오세영 4-1 2 항목 해결). **10-24 05시**: 원격 `young` 병합분(안영훈 7커밋 — 고객 반품 검사·PDA 사번 목록/PIN 키패드·옛 WH 출고 일정 퇴역·재고 테스트 시드)을 받아 검증·게시하고, 6절 기준선만 고쳤습니다(안영훈 절과 「young 병합 시 정정」 문장은 그대로). **10-26 이승욱**: 안영훈 2-2 에 SAP DB to DB 연동 실패 처리·알림 감지 기준값 정의(사용자 지시, 처리자 안영훈)를 더했고, 전체 테스트 중 찾은 MNT 고장 동시 등록 교착을 채번 카운터(`SYS_NumberSeq`)로 고쳐 두 DB 에 적용해 이승욱 항목에서 지웠습니다(POP 안돈 고장 등록 프로시저도 같은 카운터 — 6절 공유). 김현진 `7b54981b`~`54a4207b`(운영 설정 미정 주석·POP 오프라인 설정)를 병합했고 그 문장은 그대로 두었습니다.
- **기준 소스**: `dev` 10-23(이 문서를 고친 커밋 — 심재현 PNT-MAIN·APS 거부 규칙·WO 번호 유니크·팔레트 불량 재고 제거, 김현진 POP PIN 키패드 수정, 이승욱 BOM 단위·LOT PREFIX 규칙·쓰인 LOT PREFIX 잠금·설비 OEE 자동 채움·10-24 PUSH 발송기·SYS-008 PUSH 주소 읽기 전용, 김현진 POP 오프라인 동작·안돈 라인 잠금(10-23), 안영훈 `young` 병합(`a811750a` — WH·FG·PDA·SCM·재구축 스크립트)까지 포함). 심재현·이승욱 절의 줄 번호는 이 커밋 기준이고, 안영훈 절은 10-23 `young`에서 현재 호출 경로와 사용자 정책을 재검토해 수정·정정했습니다. 해당 절은 고정된 옛 줄 번호 대신 클래스/메서드와 검증 상태를 기준으로 봅니다.
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
- **결함으로 보지 않는 것(사용자 결정)**: FK(외래키)를 두지 않는 것, 열린 WO 없이 생산 허용(WoID NULL), 배지 EOS QR 자동 등록, 현장 작업자 PIN 잠금 없음, 쓰지 않는 SYS_Config 키, 2FA 미사용, 공장 하나(SYS-005 공장 구분 없음), 스페인어 화면의 공통코드 이름 영어 표시, 예비품 수량 조정은 PDA 에서만, 개발서버 배포는 zip 이 아닌 폴더, 공휴일은 SQL 로 관리(`SYS_PublicHoliday`), 설정 파일의 내부망 IP·장치값(외부 노출 없음 — 10-26 사용자 결정). 수동 라벨 생성·예외 발행·재출력은 누를 때마다 한 번씩 동작한다 — 연속으로 누르면 LOT·라벨도 누른 만큼 나온다(10-26 사용자 결정).

---

## 0. 요약

| 담당  | 미해결                  | 보류  | 테이블 | 문제점 | 가장 먼저 볼 것                                                                                                                   |
| --- | -------------------- | --- | --- | --- | --------------------------------------------------------------------------------------------------------------------------- |
| 심재현 | 16                   | 11  | 7묶음 | 42  | [중] BOM 자동 차감이 서브조립 아래 자재를 빼지 않음(문제점 28) · 안돈 보전 호출 작업지시(요청 12) |
| 안영훈 | 8(배포·검증) | 8 | 5 | 1 | 서버 아이디 목록 API 반영 · WH/FG 실제 쓰기·동시성 검증 · 기존 Area 부모 선택 · 생성기 패키지 보안 경고 · 정책/비결함 정정은 2-2·2-5 |
| 이승욱 | 0                    | 3   | 2   | 0   | 보류만 — 금형 정비 등록(B7)·개발 완료 시 정리·개발 DB 이름 `AMES` 전환 |
| 오세영 | 3(앱 개발 참고)          | 1   | —   | —   | 알림 앱 개발 — 서버 발송기·앱 Api 준비됨(4-1 1 참고사항), Firebase 서비스 계정 키 전달                                                                                               |

---

## 1. 심재현 — POP(QC 포함)·InjAgent·Devices + Web PP-001~006·PP-APS

### 1-1. 미해결 (16)

1. **[중] 스캔 확정 뒤에 온 로봇 NG 판정이 버려짐**
   - 위치: `InjLotRepository.cs:551-568`(MarkNgBlocked), 호출 `MachinePoller.cs:206-210`
   - 내용: 로봇 검사 결과가 작업자 스캔 확정보다 늦게 오면, LOT 이 RAW 가 아니라는 이유로 NG 표시를 무시합니다. 불량품이 양품으로 집계됩니다.
   - 제안: 확정된 LOT 에 늦은 NG 가 오면 불량 등록(역분개) 경로로 넘기거나, 최소한 경고를 남깁니다.
2. **[중] APS 등록 계획 단위 불일치**
   - 위치: `ApsRepository.cs:324-326·409-414·733·738`, `PpRepository.Aps.cs:1008·1035`(ReadScheduledQty — 헤더 품번 `w.ItemNo`)
   - 내용: BOM 규칙 WO 의 사출 슬롯을 부모 품번으로 읽고, 이미 자식 단위인 수량에 QtyPer 를 한 번 더 곱합니다. 잠긴 칸·부하가 부풀고, WO 생성 때 기존 슬롯 차감도 자식 슬롯을 놓칩니다(문제점 5 와 같은 원인).
   - 제안: 슬롯 품번을 단계 품번 기준으로 읽고 재곱을 없앱니다.
3. **[중] InjAgent 수집 오류 묶음**
   - 위치: `MachinePoller.cs:76-77·117-157·160-175·202-221`, `Plc/FEnetClient.cs:140-149`
   - 내용
     - 카운터 리셋이나 금형 변경만으로 LOT 이 생깁니다.
     - 샷이 2 이상 건너뛰면 1건만 생깁니다.
     - 로봇 쓰기 실패를 무시해 판정이 다른 LOT 에 붙습니다.
     - 검사 이력이 재시도 때 중복됩니다.
     - FEnet 응답을 한 번만 받아 잘린 응답을 그대로 씁니다.
   - 제안: 샷 증가분만큼 LOT 을 만들고, 쓰기 실패는 재시도하며, 응답 길이만큼 반복해서 받습니다.
4. **[중] 재작업이 취소된 WO 에 실적을 귀속**
   - 위치: `ReworkRepository.cs:160`, `WorkOrderRepository.cs:402-412`(BumpStepCompleted → `migrate_pop_stored_procedures.sql:304-307·313-322`)
   - 내용: 수리한 양품이 취소 WO 로 들어갑니다. 마지막 단계면 Cancelled 가 Closed 로 바뀌고 완료 수량도 바뀝니다.
   - 제안: 취소 WO 면 열린 WO 를 다시 찾고, 없으면 WO 없이 확정합니다.
5. **[중] QC 홀드 해제 조건 없음·QC 번호 중복**
   - 위치: `QcRepository.cs:151-178`(ReleaseHold → `migrate_pop_stored_procedures.sql:1173`), 번호 `migrate_pop_stored_procedures.sql:1100·1141`(CreateInspection·CreateNcr)
   - 내용: 이미 해제된 홀드도 다시 해제됩니다. 검사·NCR 번호가 분 단위(yyMMddHHmm)라 같은 분에 두 건이면 번호가 겹치고, 유니크 제약도 없습니다.
   - 제안: 상태 조건을 추가합니다. 번호는 범위 잠금으로 채번합니다(MNT `ClaimNumber` 방식).
6. **[중] inj_flag 마이그레이션이 컬럼을 데이터째 지움**
    - 위치: `dist/migrate_md_item_inj_flag.sql:63-112`
    - 내용: 재생성 목록(63-96행)에 `ScanRequired`·`SAPFlag`(10-16 `SAPTransFlag`, 10-21 개명)·`IssueAddPct`·`MrpAllowPct`·`ProcureType`(10-20) 가 없고 감사 컬럼 순서도 지금 DB(맨 뒤)와 다르며 기본값 제약(`DF_MD_Item_ScanRequired`)·인덱스도 되살리지 않으며, 컬럼 구성 대조 가드가 없습니다. 18-24행 가드 덕분에 `InjFlag` 가 이미 `ItemCategory` 바로 뒤에 있는 DB(개발·로컬)는 재실행해도 건너뛰지만, `InjFlag` 가 없거나 자리가 다른 DB 에 적용하면 뒤 컬럼이 데이터째 사라집니다.
    - 제안: 재생성 목록을 현재 `MD_Item` 구성으로 보강하고 컬럼 대조 가드를 둡니다. 이미 적용된 DB 가 대부분이면 "컬럼이 있으면 건너뜀" 만 남기는 것도 방법입니다.
7. **[하] 안돈 기록 오류 묶음**
    - 위치: `AndonPopup.razor:246-250`, `AndonRepository.cs:228·36-37`, 종료 `migrate_pop_stored_procedures.sql:698-700`(SP_POP_ANDON_Resolve)
    - 내용
      - 기존 안돈을 복원만 해도 설비 STOP 기록이 중복됩니다.
      - 원인코드 매개변수가 `VarChar 15`(공통코드 값은 20자까지 — 지금 최대 8자라 잠재 결함)입니다.
      - 종료 때 CLOSED 를 RESOLVED 로 덮습니다.
      - 알림 수신자·채널이 고정값입니다(오세영 PUSH 와 연계).
8. **[하] 마이그레이션 FK 6개·그룹 이름 옛 접두어**
    - 위치: `migrate_aps.sql:74·75·100·157·185`, `migrate_aps_plan_line_shift.sql:21`, `migrate_demand_plan.sql:69·92-98`
    - 내용: FK 를 두지 않는 원칙과 달리 FK 6개를 새로 만듭니다. DPSYNC 공통코드 그룹 이름에 폐지된 `ScheduledWorker · ` 접두어가 있습니다.
9. **[하] 테스트 코드에 접속 비밀번호·외부 IP**
    - 위치: `AmesDevDb.cs:15` 외 `AMES.Data.Tests` 7파일(총 8파일, 192.168.0.132 를 쓰는 6파일은 Connect Timeout=10, `MoldPlanningTests:20`, `PlanScheduleTests:21` 은 98.95.142.192 이고 Connect Timeout 도 없음)
    - 제안: 모두 `AMES_TEST_CONN` 하나로 바꿉니다.
    - 범위 추가(10-26 확인): `AMES_TEST_CONN` 은 `AMES.Data.Tests` 뿐 아니라 `AMES.Pop.Tests/OfflineReplayDbTests.cs`·`OfflineReplayFinishedDbTests.cs`(10-23 오프라인 테스트, 기본값 `192.168.0.132`)에도 쓰입니다. 같은 방식으로 바꿉니다.
10. **[하] POP PLAN 기준 결정**
    - 위치: `migrate_pop_stored_procedures.sql:1713-1721`(SP_POP_INJ_GetDailyItemSummary)·`2082-2090`(SP_POP_IMG_GetDailyItemSummary)·`985-993`(SP_POP_PNT_GetDailyItemSummary)
    - 내용: PP-LSB 「발행」이 폐지돼(10-14), 「적용」만 해도 현장 PLAN 에 바로 반영됩니다. 어떤 기준으로 할지 POP 담당이 정합니다.
11. **[하] POP IMG 코어 스캔 검사를 InjFlag 기준으로**
    - 위치: `ImgLotRepository.cs:128`(CreateFromCore → `CoreItemResolver.Read`)
    - 내용: 웹 WO 발행은 10-14 부터 PP-APS 와 같은 InjFlag 기준(`CoreItemResolver.ReadByInjFlag`)으로 코어를 정하는데, IMG 코어 스캔의 `CoreMismatch` 검사는 아직 품명 CORE 규칙입니다. 개발 DB 완제품 77개는 두 규칙의 결과가 같지만, 레일만 있는 품번처럼 둘이 갈리면 WO 의 사출 단계 품번과 IMG 검사가 다른 코어를 봅니다.
    - 제안: `ReadByInjFlag` 로 바꿉니다(POP 담당 작업 예정).
12. **[중] [요청] 안돈 보전 호출에 CM 작업지시 발행·안돈 종료 때 함께 닫기**(10-16 사용자 결정 — 이승욱 요청)
    - 위치: `AndonRepository.cs:223-236`(보전 호출 → `SP_POP_ANDON_CallDepts_InsMNT_FailureRegister` `migrate_pop_stored_procedures.sql:574-602` — `MNT_FailureRegister` 만 생성, `WorkOrderID` NULL) · `AndonRepository.cs:269-281`(안돈 종료 → `SP_POP_ANDON_Resolve` `migrate_pop_stored_procedures.sql:698-700` — 고장만 RESOLVED)
    - 내용: 안돈 고장에는 작업지시가 없어 MNT-007 배정·착수·점검·PDA 부품 출고를 쓸 수 없습니다(개발 DB 작업지시 없는 열린 고장 3건은 OPERATOR·PLC·QC 시연 고장이고, 안돈(ANDON) 출처 고장은 10-23 기준 0건). 웹은 10-16 부터 MNT-002 상세의 「작업지시 발행」(`MntRepository.IssueFailureWorkOrder` — CM, 출처 ANDON, SourceRefID 고장번호)으로 수동 연결합니다. 그런데 그렇게 연결한 고장이 안돈 종료로 RESOLVED 가 되면 작업지시는 열린 채 남아 MNT-007 에서 따로 완료·종료해야 합니다.
    - 요청: ① 보전 호출 때 같은 트랜잭션에서 CM 작업지시를 발행해 `WorkOrderID` 를 채웁니다(웹과 같은 규칙 — `MntRepository.IssueFailureWorkOrder` 를 그대로 쓰거나 같은 값: WoType CM·SourceType ANDON·SourceRefID 고장번호·우선순위는 심각도·상태 ISSUED, 번호는 `MWO-yyMM-nnn` 범위 잠금 채번). ② 안돈 종료 때 연결된 작업지시가 끝나지 않았으면 같이 완료합니다(결과 OK — CM 작업지시는 점검 항목이 없어 완료 차단 규칙 `MwoChecklistRules` 에 걸리지 않습니다).
13. **[하] [요청] 금형 샷 누적 기록 — 사이클 PM 자동화의 전제**(10-16 이승욱 요청)
    - 위치: InjAgent(샷은 `PR_ShotCount` 에만 기록), `MNT_MoldShotCount`(0행 — 이승욱 테이블 3)
    - 내용: 사이클(CYCLE) 주기 PM 은 지금 완료할 때 사람이 다음 예정일을 넣습니다(10-16 사용자 결정 — 샷 누적 연동 전까지, `PmRules`). 샷이 쌓이지 않아 금형 소진율·정비 한계 알림도 비어 있습니다.
    - 요청: InjAgent 가 샷을 기록할 때 `MNT_MoldShotCount` 도 올립니다. 그 뒤 이승욱이 사이클 PM 의 다음 회차를 샷 기준으로 자동 발행하도록 바꿉니다.

14. **[중] [요청] POP 오프라인 범위 밖 — InjAgent·안돈·재작업·QC·WO 목록**(10-23 오프라인 작업)
    - 내용: POP INJ/IMG/PNT 의 로그인·스캔 확정·불량·수동 라벨은 DB 가 끊겨도 로컬 사본으로 동작하고 복구 때 반영됩니다(CLAUDE.md 「POP 오프라인 동작」). 아직 안 되는 것: ① InjAgent 는 DB 가 끊기면 샷 LOT 을 만들지 못합니다(재시도 없음 — 미해결 3) ② 안돈·WO 목록 팝업·재작업·QC 화면은 오프라인 대응이 없습니다 ③ 복구 때 서버가 거부한 작업의 처리 정책은 충돌 목록 표시까지만 정했습니다(사용자 보류).
    - 제안: InjAgent 오프라인 대응부터 범위를 정합니다.

15. **[중] INJ 2번(`INJ-02-01-1800`, `LINE-INJ-02`) PLC 접속 불가**(10-26 확인)
    - 위치: `src/07_Etc/AMES.InjAgent/appsettings.json`(`Agent.Machines` 는 1번 한 대뿐), `Plc/ModbusMachineClient.cs`·`FEnetClient.cs`
    - 내용: ① 설정에 2번 항목이 없어 InjAgent 가 접속을 시도하지 않는다(작업본에는 주석 처리한 2번 예시만 있다) ② `PLC_Simulator.exe` 가 실행 중이 아니고 Modbus 502·FEnet 2004 가 LISTEN 상태가 아니다 ③ 설정된 `127.0.1.1`·`127.0.2.1` 은 이 PC 에 할당돼 있지 않다(로컬 주소는 `127.0.0.1` 뿐). 1번도 같은 이유로 접속이 안 된다.
    - 요청: 시뮬레이터의 기계별 IP·포트를 정하고 설정에 2번을 추가한다(1번·2번 포트는 달라야 한다).
16. **[중] PLC 시뮬레이터 범위 결정**(10-26 요청 — INJ·IMG·PNT)
    - 내용: 저장소 밖의 `D:\dev_work\PLC_Simulator.exe`(PyInstaller, 소스 없음)에는 사출 프로파일(`profiles.injection`·로봇·패널)과 FEnet 서버가 들어 있다. IMG·PNT 프로파일 여부는 확인하지 못했다. 신규로 만들면 INJ 는 InjAgent 가 읽는 주소를 그대로 흉내 낸다(Modbus 5000번 샷 LONG·5330번 금형 ASCII 6워드, FEnet 5000번부터 48워드). 현재 IMG·PNT 에는 PLC 가 없다(스캔·판정을 POP 화면에서 입력) — 흉내 낼 신호를 정해야 한다.
    - 정할 것: ① 기존 exe 를 쓸지 신규 INJ 전용으로 만들지 ② IMG·PNT 를 이벤트 생성기(스캔·판정 자동 입력)로 할지, 시뮬레이터 범위에서 뺄지

### 1-2. 보류 (11)
- **PNT 확정의 코어 재고 처리 vs PP-APS 가용재고**(10-22 검토에서 발견): PNT 확정은 BOM 사출품 자식을 PNT 라인 위치의 자리표시 `BF-P{n}-{코어}` 음수로 뺀다(사용자 결정 10-22). APS 가용재고는 `WH_Inventory.Qty > 0` 만 더해 음수 자리표시를 보지 않으므로, 도장으로 소비된 코어의 팔레트 행이 창고 라인 보충 전까지 APS 코어 재고에 그대로 잡힌다(IMG 는 `ConsumeCore` 가 코어 재고 행을 지움). **10-22 사용자 결정: 지금은 라인 재고 차감 그대로 둔다.** 후속으로 사출품을 라인 재고로 옮기는 처리(팔레트의 코어 LOT 행 → 그 라인 위치의 품번 수량 행으로 전환)를 만들면 그 수량이 자리표시 음수를 메우고 팔레트 행이 사라져 APS 과대 계상도 함께 풀린다. 그때까지 APS 코어 재고는 도장 소비분만큼 과대하다(IMG 는 영향 없음).
- **POP 팔레트 적재(10-21) 의 범위 밖**: ① 재작업 스테이션의 수리 양품은 팔레트 스캔이 없어 `WH_Inventory` 에 넣지 않는다(INJ·IMG·PNT 확정만 싣는다) — 재작업에도 팔레트 스캔을 둘지. ② 현재 팔레트는 화면 메모리뿐이라 재로그인·재시작하면 다시 스캔한다 — 마지막으로 실은 팔레트를 DB(그 라인의 마지막 `PROD_CONFIRM` 거래)에서 되살릴지. ③ 팔레트 바코드는 `EOS-PLT-` 접두어로만 LOT 과 가르므로 다른 양식의 외부 팔레트는 받지 않는다. ④ LH/RH 쌍 팔레트의 `MD_Item.PalletQty` 는 "그 품번이 쌍 팔레트에 실리는 개수" 기준이다(10-21 결정 — 품번별로 센다) — 값이 있는 NEAFUC 쌍(D0111-PI010·D0121-PI010 32/32)이 "LH 32 + RH 32 = 64개" 인지 "16쌍" 인지 현장 적재 기준을 확인해 맞춰야 한다(값을 바꾸면 APS 계획 올림 단위도 같이 바뀐다).
- **APS 금형 교체(MC) 한계**: "직전 금형"을 그 라인·그 날 처리 순서상 마지막 금형으로 보므로, 하루 안에 금형이 여러 번 바뀌면 그 사이 되돌림 MC 를 만들지 않습니다(같은 라인·같은 날 금형 그룹당 MC 최대 1개). 교체 자리가 없을 때 억지로 둔 MC 는 휴게·점유 구간과 겹칠 수 있습니다. 조정은 PP-LSB 에서 합니다.
- **B2 IMG 라벨 교대 글자가 항상 C**(`Common/LabelPrinter.cs:27-32`): DAY/NIGHT 만 A/B 로 바꾸는데, 실제 교대 코드는 A/B/C 라 항상 C 가 찍힙니다. 교대 코드를 그대로 쓸지 답을 기다립니다.
- **InjAgent 작업본 연결 설정 커밋 보류**(10-26): 작업본은 로컬 연결을 `ames_app`·`Connect Timeout=30` 으로 바꾸고 2번 예시를 주석으로 두었다. 이 중 연결 문자열 변경은 "커밋은 주석만" 원칙에 따라 올리지 않았다. 운영 반영 때 정리한다(심재현·안영훈 확인).
- **오프라인 LOT 번호 이어쓰기 한계**(10-26 변경): 번호 구간을 떼지 않고 이 PC 의 마지막 번호 다음부터 이어 받는다. 남은 한계는 ① DB 장애 중 INJ 샷은 LOT 이 생기지 않는다(14번 요청과 같이 본다) ② 라인당 POP 터미널은 한 대라는 가정이다(두 대가 같은 라인에서 출력하면 번호가 겹칠 수 있다).
- **B11 InjAgent 시각을 DB 서버 기준으로**: POP 은 10-16(eb5ace39) 로그인 때 `DbClock` 을 DB 시각에 맞추도록 바뀌었습니다(호스트 시계는 유휴·로그·토스트에만 남음). InjAgent 는 아직 `DbClock.Configure` 를 하지 않아, 자동 LOT 채번(`InjLotRepository.cs:66`, `MachinePoller.cs:144` 에서 호출)의 LotNo 날짜 글자가 PC 시계 기준입니다.
- **B3 POP 의 "오늘" 이 자정 기준**
  - 위치: `InjLotRepository.cs:120`, `migrate_pop_stored_procedures.sql:1732`(INJ 좌측)·`2101`(IMG 좌측)·`1004`(PNT 좌측 — 10-22 `SP_POP_PNT_GetDailyItemSummary`, 구 PNT 프로시저와 같은 자정 기준)·`1243·1245·1277`(QC-07)
  - 내용: 웹 보고서는 07시 전기일인데, INJ/IMG 좌측 수치·QC-07·PNT·금형 일 사용량은 자정 기준입니다. 10-16 부터 POP 의 "오늘" 은 DB 날짜(B11)이지만 경계는 여전히 자정입니다.
  - 쓰지 않는 POP 차트 `HourlyBarChart`·`HourlyOutputDto` 도 정리 대상입니다.
- **라우팅 B·시드 변경으로 실패하는 테스트 10건**: PlanScheduleTests 5, WorkOrderRepositoryTests 4, GetMoldItems 1(10-17 설비 재시드 병합 뒤에도 같은 10건). 10-22 로컬 DB 를 개발 DB 로 복제한 뒤 `MoldPlanningTests.LineLastMoldBefore_falls_back_to_mounted_mold_with_min_date` 1건이 더 실패합니다 — 테스트는 `LINE-INJ-01` 에 이전 날짜의 금형 슬롯이 없다고 보고 설비 장착 금형으로 돌아가길 기대하는데, 개발 데이터에 10-16~20 슬롯(금형 `NEAFUC--`)이 있어 그 금형을 돌려줍니다(코드 결함 아님 — 데이터 가정, 이승욱 10-22 확인).
- **품목 추가 불출율·MRP 여유율을 차감·MRP 계산에 반영**(10-23 이승욱 요청 — 고객 요구 10-14 "BOM 은 정량, 원단 불출 +10%·MRP 품목별 여유율"): `MD_Item.IssueAddPct`(추가 불출율)·`MrpAllowPct`(MRP 여유율)(decimal(5,2) %, NULL = 0, 10-20)는 MD-003(화면·엑셀)에서 저장·표시만 하고 계산하는 코드가 없습니다 — BOM 을 읽는 곳은 `BackflushRepository`(자동 차감)·`PpRepository`(MRP·APS)뿐입니다. 정할 것: ① BOM 자동 차감을 정량으로 둘지 불출율을 포함할지(원단 불출 쪽 안영훈 보류와 같이) ② MRP(PP-005 `RunMrp`·`MrpCalculator`) 소요에 여유율만 더할지 불출율도 더할지 ③ 이미 차감·MRP 가 쓰는 BOM 불량률 `ScrapPct`(지금 전부 0)와 겹치지 않게 하는 관계. 같은 날 넣은 조달형태 `MD_Item.ProcureType`(공통코드 `PROCURE_TYPE` — 내작·매입·유상사급·무상사급)도 MRP·구매요청이 읽지 않습니다(SAP 는 공통 「SAP 마스터 수신」). 품목 분류별 기본값이 필요하면 MD 쪽(이승욱)에 요청합니다.

### 1-3. 테이블 (10-14 전수 조사)
읽기만 있는 테이블입니다. POP 화면이 조회만 하고, 데이터는 시연 행뿐입니다.

| 테이블(행) | 용도 | 읽는 곳 | 상태 |
|---|---|---|---|
| `QC_InspectionStd`(3) | 검사 기준서(버전·AQL·항목) | POP QC-08(`QcRepository.cs:267-291` → `migrate_pop_stored_procedures.sql:1291`), 웹 MD 고객 삭제 사용처 | 등록·개정·승인 화면이 없음. QC-01~03 검사도 이 기준을 쓰지 않음 |
| `QC_CAPA`(2)·`QC_CAPA_Action`(5) | 시정·예방 조치 | POP QC-06·QC-07(`QcRepository.cs:181-218·238-246` → `migrate_pop_stored_procedures.sql:1197-1227·1264`) | 등록·진행 기능 없음 |

읽기·쓰기 모두 없는 테이블입니다. 소스(src)에는 이름이 없고(`PR_BondSetupAudit` 만 `BondRepository.cs:10` 문서 주석에 남음), 시연 행은 `tools/seed_*` 프로그램이 넣은 것입니다.
- **PNT_ 22개**: DailyPlan(2행), DailyReport, JigBindingLog, JigLoad(1행), JigUnload, LabelPrintJob, LabelScanLog, LineEvent(12행), LotLabel, OvenDeviationLog, OvenLog(1행), OvenSpikeLog, OvenTempSample(120행), PartLossLog, QcQueue, SeqAllocator, ShiftReport, ShiftReportAudit, ShiftReportLineItem, StationStatsCache, TagFailureLog, VirtualLot(7행). 구 도장 모듈(가상 LOT·지그·오븐)의 테이블 — **10-22 PNT-MAIN 전환으로 읽는 코드·프로시저가 없다**(PNT 실적은 `tbl_Lot`/`PR_PntLot`, QC-07 도 실적 테이블로 센다). 행은 시연 데이터뿐. DROP 여부 결정 대기.
- **PR_ 7개**: BondSetupAudit(3행), CycleAnomalyLog, DashTileCache, DefectAutoLink, DefectRateCache, PlcInterlock, ShiftHandover.
- **QC_ 2개**: QC_Disposition(부적합 처분), QC_NCR_Action(NCR 처리 이력). QC-04·05 가 처분·이력을 남기지 않는다는 뜻이기도 합니다.
- **죽은 코드만 쓰는 5개**: `BondRepository`·`FabricRepository` 는 어디서도 생성되지 않습니다(IMG 는 원단·본딩을 다루지 않기로 함). 대상은 PR_BondSetup(2), PR_BondCycleLog(14), PR_FabricIssue(1), PR_FabricDeductionLog(14), PR_FabricIssueAttempt(0) 입니다.

**결정 필요**
- 읽기만 있는 테이블: 등록 화면·수집기를 만들지, 화면을 「시연용」으로 표시하거나 숨길지
- 아무도 안 쓰는 테이블: DROP 할지(두 리포지토리 삭제 포함). FK 가 없어 지워도 다른 테이블에는 영향이 없습니다.

### 1-4. 문제점 (42)
1. **[중] APS 재고 역산에서 WO 없이 만든 생산이 빠짐**
   - 위치: `ApsRepository.cs:900-923`(ReadProduced)`·954-977`(ReadWipStock)
   - 내용: 당일 실적을 WO 품번으로 묶어, WO 없는 생산과 BOM 자식 사출 실적이 빠집니다. 시작 재고가 부풀고 사출 소요가 적게 잡힙니다.
   - 제안: 실적 품번을 LOT 품번 → 단계 품번 → WO 품번 순으로(보고서와 같은 규칙) 정합니다.
2. **[중] PP-004 릴리스·취소가 승인 권한만 있는 역할에서 무반응**
   - 위치: `WorkOrder.razor:290·313`
   - 내용: 버튼은 승인 권한으로 보이는데, 처리 첫 줄은 수정 권한을 봅니다. 읽기+승인 역할은 눌러도 아무 일이 없고 안내도 없습니다.
   - 제안: 처리 첫 줄을 `_canApprove` 로 바꿉니다.
3. **[중] MRP 에서 같은 부족분 PR 이 두 번 생김**
   - 위치: `PpRepository.cs:1197-1269`, `Mrp.razor:399`
   - 내용: PR 을 만들 때 그 MRP 실행이 최신인지, 같은 품번의 열린 PR 이 있는지 보지 않습니다.
4. **[중] APS 「WO 생성」 수주 초과 발행 경합**
   - 위치: `PpRepository.Aps.cs:217·964-984·269·289·354·1000-1025`
   - 내용: 수주 잔량과 기존 슬롯을 잠금 없이 읽은 뒤에야 채번 잠금을 잡아, 두 실행이 겹치면 같은 수주를 초과 배정합니다.
5. **[중] WO 미리보기가 읽기 사용자에게도 쓰기 트랜잭션을 돌림**
   - 위치: `ApsWoPreview.razor:445-453·474-490`(갱신 버튼 `:62`)
   - 내용: 들어가자마자 dryRun(실제 INSERT 후 롤백)이 돌아 잠금을 잡고 WoID(IDENTITY)를 소모합니다(채번 범위 잠금은 `UX_PP_WorkOrder_WoNumber` 덕에 그 날 접두어 범위뿐이라 POP 확정은 막지 않는다). 「미리보기 갱신」에도 권한 검사가 없습니다.
6. **[중] 이미 IMG 에 쓰인 코어를 INJ 불량으로 등록할 수 있음**
   - 위치: `InjLotRepository.cs:577-637`(상태 검사 610-611)
   - 내용: 완제품은 양품인데 그 코어는 폐기되는 모순이 생깁니다.
   - 제안: 자식 IMG LOT 이 있으면 거부합니다.
7. **[중] 실적 테이블에 LotID 인덱스 없음**
    - 위치: `LotDefectWriter.cs:25`(→ `SP_POP_LOT_ReverseConfirmedResult_GetProductionResult`)(`InjLotRepository.cs:615`·`ImgLotRepository.cs:319`·`PntLotRepository.cs:207` 에서 호출)
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
    - 위치: `InjMain.razor:447-451`(catch 476-483), `ImgJudgePopup.razor:120-137`(catch 150 — PNT 도 같은 판정 팝업)
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
    - 내용: 수주 확정, WO 생성·릴리스·취소, PR 생성·전송·승인이 SYS-007 에 남지 않습니다. PP-002 「자동 확정」 스위치를 켤 때 일괄 확정되는 수주도 마찬가지입니다(스위치 켜고 끄기와 확정 건수만 기록되고 수주별 기록은 없음).
17. **[하] WO CSV 품번 누락·MRP 상태 색**
    - 위치: `WorkOrder.razor:368`, `Mrp.razor:195·376`(PR 상태 색이 옛 상태값 Pending/Rejected 기준)
18. **[하] 행위자 매개변수 형식이 컬럼과 다름**
    - 위치: `PpRepository.Aps.cs:1052·1079·1097`, `ApsRepository.cs:1041·1214`, `WorkOrderRepository.cs:558`, `QcRepository.cs:67`(FinalizeInspection `@A` → ApproverID·ModifiedBy)(10-15 사람 컬럼 사번 전환 때 주간 구매계획·WO 릴리스·INJ/IMG 확정 매개변수는 고침)
    - 내용: varchar(20) 컬럼에 NVarChar 로 넘깁니다(기능 오류는 아님). `PpRepository.CreateShortagePrs` 는 `@By` 를 `VarChar, 20` 으로 선언하고 값은 `Trunc(by, 50)` 으로 자릅니다(`:1249`, PR 전송 로그 `:1431` 도 같음 — 행위자가 20자 이하라 지금은 영향 없음).
19. **[하] APS WO 를 취소해도 계획 연결이 남음**
    - 위치: `WorkOrderRepository.cs:619-649`, 대상 선정 `PpRepository.Aps.cs:209`
    - 내용: 취소한 계획 행이 계속 '기발행' 으로 남아 다시 만들 수 없습니다.
20. **[하] IMG 라벨과 실적의 WO 가 다를 수 있음**
    - 위치: `ImgLotRepository.cs:111-120`(코어 스캔 WO)`·186-199`(라벨 수주처)`·212`(LOT WoID NULL — `migrate_pop_stored_procedures.sql:1887`)`·400-408`(판정 때 다시 해석)
21. **[하] QC 최종 판정 덮어쓰기**
    - 위치: `QcRepository.cs:60-69`(FinalizeInspection → `migrate_pop_stored_procedures.sql:1079-1082` UPDATE — `Verdict IS NULL` 가드 없음)
22. **[하] POP 창을 닫아도 세션이 열린 채 남음**
    - 위치: `Forms/PopBlazorForm.cs:63`(세션 닫기는 `AppRoot.razor:144` 로그아웃 경로뿐)
23. **[하] InjAgent 정지가 2초를 넘으면 상태 경합**
    - 위치: `Core/PollerRunner.cs:48-74`(69-70)
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
    - 위치: `BackflushRepository.cs:101-112`(ReadBomRows → `migrate_pop_stored_procedures.sql:2460` — `ParentItemNo = @Item` 1단계), `BackflushRules.cs:31-49`
    - 내용: 완제품 → 서브조립(SUB, InjFlag 0) → 자재 구조에서 서브조립은 LOT 확정 단계가 없어, 그 아래 MATERIAL 은 어느 확정에서도 차감되지 않습니다(개발 DB 완제품 36개·자재 간선 88개). CLAUDE.md 는 "SUB 자식 = 코어" 로 전제하지만 InjFlag 0 인 SUB 는 코어가 아닙니다.
    - 제안: InjFlag 0 인 SUB 는 QtyPer 를 곱하며 내려가고 InjFlag 1 인 코어에서 멈춥니다(`CoreItemResolver.ReadByInjFlag`·APS BOM 다단계와 같은 규칙).
29. **[하] 확정된 적 없이 폐기된 LOT 의 자재가 차감되지 않음**(10-16 백플러시)
    - 위치: `ReworkRepository.cs:194-196`(Rework 만 차감)`·213-247`(Scrap 차감 없음), `LotDefectRules.cs:57`(WasNeverConfirmed)
    - 내용: 로봇 NG 샷처럼 RAW/NG_BLOCKED → DEFECT → SCRAPPED 로 끝난 LOT 은 레진을 실제로 썼는데 차감 기록이 없어 라인 재고가 폐기율만큼 부풉니다. CLAUDE.md 는 "되돌리지 않는다" 만 적고 이 경우는 정하지 않았습니다.
    - 제안: 폐기 때 차감할지 사용자 결정을 받습니다.
30. **[하] 백플러시 멱등 인덱스 정의가 스키마 파일과 두 DB 에서 다름**
    - 위치: `migrate_wh_inventory_backflush_placeholder.sql:32-34`(두 열), DB 의 넓은 정의는 `migrate_fg_shipment_actual.sql:68-70` · 스키마 파일 `dist/AMES_Schema.sql:9655-9661`(10-24 재생성 — 넓은 정의, 두 DB 와 같음)·`:15753-15755`(시드 쪽 같은 정의 — 있으면 건너뜀)
    - 내용: 개발·로컬 DB 와 스키마 파일은 SourceType·SourceID·TransactionType + 포함 열 5개로 같습니다(10-24 스키마 파일을 다시 떠서 맞음). 남은 차이는 백플러시 마이그레이션이 인덱스가 없는 DB 에 두 열 정의로 만든다는 것뿐입니다.
    - 제안: 마이그레이션이 넓은 정의로 만들게 바꾸고 스키마 파일을 다시 뜹니다.
31. **[하] CLAUDE.md 백플러시 문단의 MRP 설명이 코드와 다름**
    - 위치: CLAUDE.md "MRP 가용재고 … 음수 자리표시까지 더한 순수량"(백플러시 문단), 실제 `PpRepository.cs:1031-1037`
    - 내용: MRP 재고는 `Qty>0 AND AreaCode='MAT_AREA'` 라 라인 위치(I1·W1 등)와 음수 자리표시가 빠집니다. 라인이 불출보다 더 쓴 양은 MRP 에 보이지 않습니다. WH-AUTO 라인 보충(`WarehouseRepository.LineStock.cs:144-152`)도 지정 품번이 있는 상세 위치만 합산해, `MD_Location` 에 없는 LotPrefix 위치(I1 등)의 `BF-` 음수 자리표시를 보지 않습니다(10-23 개발 DB `BF-` 14행 모두 미포함 — 10-23 안영훈 `7b3536de` 로 차감 자체는 라인 상세 위치에서도 함).
    - 제안: 문구를 코드에 맞추고, MRP 반영이 필요한지 정합니다.
32. **[하] PP→POP 시험 초기화 스크립트가 다른 데이터와 어긋나게 지움**(개발 전용, 10-16 943cdf24)
    - 위치: `tools/test_pp_pop/test_pp_pop_reset_dev.sql:374`
    - 내용: 10-17 기준 끊긴 `DowntimeID` 는 0건이라 실제 피해는 아직 없습니다. `PP_LineDowntimeLog` 를 전부 지워 고장(라인 정지)의 `DowntimeID` 가 없는 행을 가리키고, LOT 은 지우면서 BACKFLUSH 거래·`BF-` 자리표시 재고는 남기며, 공용 개발서버의 다른 개발자 PP 데이터도 통째로 지웁니다.
      안돈 고장(`MNT_FailureRegister` Source='ANDON')·조치 이력은 지우면서 `MNT_WorkOrder` 는 다루지 않아, 10-16 부터 MNT-002 「작업지시 발행」으로 만든 작업지시가 지워진 고장번호(`SourceRefID`)를 가리킨 채 남습니다(`:376-379`).
    - 제안: 비가동은 안돈 행만 지우거나 고장의 `DowntimeID` 도 비우고, 그 고장의 작업지시도 같이 지우며, 백플러시 거래·자리표시를 같이 되돌리고, 실행 전 서버 확인 가드를 둡니다.
33. **[중] 감싸기가 코어만 소모해 두 번째 사출품(레일 등) 재고가 줄지 않음**(10-22 흐름 점검)
    - 위치: `ImgLotRepository.cs:134-136`(코어 하나만 연결·`ConsumeCore`), `PalletStockRepository.cs:101-117`, `BackflushRules.cs:38`(MATERIAL 만)
    - 내용: 완제품 77개 중 31개가 사출품 2개(코어 + 레일)입니다(로컬 DB 다단계 BOM). 레일도 INJ 확정 때 팔레트 재고가 되지만 IMG 는 코어만 0 으로 내리고 백플러시는 SUB 자식을 빼므로 레일은 쌓이기만 합니다 → APS 가 레일 재고가 충분하다고 보고 레일 사출을 줄입니다.
    - 제안: IMG LOT 생성(또는 판정 OK) 때 코어가 아닌 InjFlag 1 자식을 QtyPer 만큼 FIFO 로 소모합니다(위치 규칙은 안영훈 2-4 4 와 함께).
34. **[중] 헤더가 Closed 인 WO 의 열린 사출 단계로 실적이 들어감**(10-22 흐름 점검)
    - 위치: `WorkOrderRepository.cs:281-292`(OpenStepWhere·OpenStepOrder), `migrate_pop_stored_procedures.sql:214-237`·`1572-1589`, `BumpStepCompleted` `migrate_pop_stored_procedures.sql:313-322`
    - 내용: 한 코어를 여러 색상 완제품 WO(A·B)가 쓰면, 사출 스캔은 In Progress 인 WO-A 로 몰리고 감싸기는 WO-B 의 IMG 단계를 끝내 WO-B 헤더가 Closed 가 됩니다(INJ 단계는 Released 로 남음). 열린 WO 조건은 Cancelled 만 빼므로 WO-A 가 차면 다음 스캔이 마감된 WO-B 로 가고, 그 단계가 In Progress 가 돼 계속 먼저 받습니다. 문제점 9(불량 역분개 재개방)과 원인이 다릅니다.
    - 제안: 열린 WO 조건에서 헤더 Closed 를 빼거나, 헤더가 Closed 될 때 남은 단계도 닫습니다.
35. **[중] In Progress WO 의 취소·강제 마감이 없고 끝난 WO 의 남은 슬롯이 정리되지 않음**(10-22 흐름 점검)
    - 위치: `WorkOrderRepository.cs:619-649`(취소는 Draft·Planned·Released 만, 단계 행 잠금 없음), `WorkOrder.razor:120-131`, `LineScheduleRepository.cs:352-355`(능력이 WO 상태와 무관하게 슬롯을 셈)
    - 내용: 실적이 일부 오른 WO 를 멈출 방법이 없어 남은 슬롯이 POP PLAN·PP-003/APS 능력·기발행 수량에 계속 잡힙니다. 계획보다 일찍 Closed 된 WO 의 미래 슬롯도 남습니다. 취소가 단계 행을 잠그지 않아, POP 확정이 단계를 찾은 직후 취소가 커밋되면 실적이 취소 WO 에 붙습니다(라우팅 C 는 마지막 단계라 Cancelled → Closed). 미해결 4(재작업이 취소 WO 에 귀속)과 같은 계열.
    - 제안: 「강제 마감」 전이를 두고 마감·취소 때 오늘 이후 슬롯을 정리하며 단계 행도 잠급니다.
36. **[중] `PP_WorkOrder.MoldID` 를 어떤 계획 경로도 채우지 않는데 POP 이 이 값을 씀**(10-22 흐름 점검)
    - 위치: WO INSERT `PpRepository.cs:904-926`·`PpRepository.Aps.cs:101-104`(금형은 슬롯 `PP_LineSchedule.MoldID` 에만), 사용처 `InjMain.razor:316-327·541-542`, `InjLotRepository.cs:164-180`
    - 내용: 로컬 DB WO 13건이 모두 MoldID NULL — INJ-MAIN 금형 샷 칩이 늘 「—」, 수동 라벨 LOT 의 금형·캐비티가 NULL, 확정 실적의 금형이 비고 캐비티 기준 사이클이 0.
    - 제안: POP 이 그 라인 오늘 슬롯의 금형(없으면 설비 장착 금형)을 씁니다.
37. **[중] PP-003 이 LH/RH 를 동시에 찍는 금형을 따로 계산해 사출 시간이 두 배**(10-22 흐름 점검)
    - 위치: `Scheduling/DeadlinePacker.cs:218-222`(분/EA = BOP 사이클), `PpRepository.cs:734-831·845-858`(형제 금형 처리 없음) ↔ APS `PpRepository.Aps.cs:382-404`
    - 내용: 2캐비티 금형의 LH·RH 수주 각 100개를 PP-003 으로 만들면 100분 + 100분 슬롯이 생깁니다(실제 100분) → Late·Shortfall 과다.
    - 제안: PP-003 도 같은 금형·색상 형제를 0분 행으로 묶습니다(APS 규칙과 같게).
38. **[중] 팔레트에 「닫힘」이 없고 적입수량이 거의 비어 있어 계속 실림**(10-22 흐름 점검) — 보류 「팔레트 적재 범위 밖」 ②·④ 관련
    - 위치: `PalletStockRepository.cs:69-94`(`ReadState` `:153-177`), `PalletLoadRules.cs:65`(PalletQty NULL = 제한 없음)
    - 내용: 이미 FG 위치를 받았거나 출하 배차에 실린 팔레트에도 POP 이 계속 실어, 새로 생긴 위치 없는 행 때문에 그 팔레트 출고가 52006·배차 완료가 「contents changed」 로 막힙니다. 개발·로컬 DB 사출품 56개 중 53개·완제품(ASSY) 79개(PNTDEMO 2개 포함) 중 30개가 `PalletQty` 가 없어 「가득 참」·「새 팔레트를 스캔하세요」가 나오지 않습니다(APS 포장 단위도 1).
    - 제안: 자식 행에 위치가 있거나 배차에 실린 팔레트는 적재 거부. 적입수량 등록은 MD-003 데이터 작업.
39. **[중] PP-003·APS·MRP 의 재고 기준이 서로 다름**(10-22 흐름 점검)
    - 위치: PP-003 `PpRepository.cs:595-599`·`912-916`(FG 구역 위치만) ↔ APS `ApsRepository.cs:789-794`(위치 무관), MRP `PpRepository.cs:1031-1037`(`MD_Location` INNER JOIN — 위치 NULL 제외)
    - 내용: POP 이 실었지만 아직 입고하지 않은 완제품을 PP-003 순수요는 재고로 보지 않고 APS 는 봐서 두 화면의 WO 수량이 다릅니다(PP-003 쪽 중복 WO). 입고만 하고 적재 전(위치 NULL)인 자재(로컬 15행·501)는 재고에도 발주중에도 없어 MRP 가 가짜 부족·중복 PR 을 냅니다. 문제점 31(MRP 가 라인 위치 제외)와 별개.
    - 제안: 위치 미배정 재고를 재고로 볼지 정하고 세 계산을 맞춥니다.
40. **[중] 사출 확정마다 그 설비의 LOT 이력 전체를 훑어 사이클을 구함**(10-22 흐름 점검)
    - 위치: `SP_POP_INJ_ConfirmByLotCode_GetLot2`(`migrate_pop_stored_procedures.sql:1591-1601`), 호출 `InjLotRepository.cs:466-474`
    - 내용: `MAX(CreatedTS) WHERE EquipID·CavityPos·CreatedTS < @ThisTs` — 로컬 실행계획에서 설비로 seek 한 뒤 그 설비 LOT 마다 `tbl_Lot` PK 조회. LOT·팔레트·단계 잠금을 쥔 채 돌아, 1년이면 설비당 약 150만 건(추정)을 매 스캔마다 읽어 확정이 느려지다 30초 제한에 걸릴 수 있습니다. 문제점 7(실적 LotID 인덱스)과 별개.
    - 제안: `pe.LotID < @ThisLot ORDER BY pe.LotID DESC` TOP 1 로 읽습니다.
41. **[중] 감싸기에서 잘못 발행한 LOT 을 취소할 수 없어 코어가 영구 소모됨**(10-22 흐름 점검)
    - 위치: `ImgLotRepository.cs:85-147`, `ImgMain.razor:303·493`, `SP_POP_IMG_FindCoreUser`(`migrate_pop_stored_procedures.sql:1811-1819` — DEFECT·SCRAPPED 자식도 사용으로 셈)
    - 내용: 좌측 기본 선택이 다른 색이면 그 색으로 라벨이 나가고 코어 재고는 0 이 됩니다. 남는 길은 NG 등록 → 폐기뿐이라 멀쩡한 코어·제품이 불량·폐기로 집계됩니다.
    - 제안: 판정 전(RAW) IMG LOT 의 발행 취소(LOT VOID·코어 연결 해제·`IMG_CORE` 역거래)를 둡니다.
42. **[하] 흐름 점검 묶음**(10-22 흐름 점검)
    - LOT 순번이 라인·일 9999 에서 막힘(`LotNoGenerator.cs:57-58` — 2캐비티 약 17초 이하 사이클이면 도달, 에이전트는 재시도 없음 — 미해결 3 과 겹침).
    - 불량 등록 뒤에도 `tbl_Lot.Status` 가 CONFIRMED(`migrate_pop_stored_procedures.sql:1670-1682` 은 QualityFlag 만) — QC 추적성이 CONFIRMED 로 표시.
    - `ConsumeCore` OUT 거래에 위치가 남지 않음(`PalletStockRepository.cs:101-117`).
    - 팔레트 적재가 수주처·고객 전용 팔레트(`MD_Pallet.CustomerID`)·내부용을 보지 않음 — 수주처가 다른 LOT 이 섞이면 팔레트 출고가 52018 로 거부. 정책 결정 필요.
    - 이미 출하된 LOT 도 POP 불량 등록이 됨(실적 −1·재작업 대기열).
    - 잠재: APS BOM 규칙 WO 의 INJ 단계 라인은 품번순 첫 자식(레일), 단계 품번은 코어라 라인이 갈리면 코어 사출이 WO 를 못 찾음(`PpRepository.Aps.cs:170·334-341`), 완제품 단계 라인이 비면 생성 전체 롤백. 단계 마감 기준이 모든 단계에서 헤더 OrderQty 라 QtyPer ≠ 1 자식이 생기면 일찍 Closed(`migrate_pop_stored_procedures.sql:292-293`).
    - WO 취소가 지난 날짜 슬롯까지 지우고(일정 준수 보고서에서 사라짐) 단계는 Released 로 남김(`WorkOrderRepository.cs:630-633`).
    - 열린 WO 규칙이 C# 상수(쓰는 곳은 테스트용 `ListForLine` 뿐)와 SP 12개 프로시저(`migrate_pop_stored_procedures.sql:214·240·748·897·1572·1776·1830·1994·2406`·일별 요약 3곳 `974·1702·2071` — 10-22 PNT 추가로 늘어남)에 복사돼 하나만 고치면 화면의 활성 WO 와 실적이 붙는 WO 가 어긋남.
    - 마스터 미비를 조용히 넘김(로컬 실측): 사출품 19개에 INJ 스테이션 BOP 없음, `83314-P8000`·`P8010` 은 BOP 라인(INJ-01)과 금형 라인(INJ-02)이 다르고 금형 후보 조회(`MasterDataRepository.cs:2199-2222`)가 라인 미배정 금형도 돌려줘 UPH 없는 라인에 배치될 수 있음, PP-004 수동 릴리스는 금형·BOP 를 확인하지 않음.
    - 추정: 라우팅 B 에서 도장품 BOM 에 SUB 가 없으면 INJ 단계 품번이 도장 완제품(ASSY)이 돼 도장 전 부품이 완제품으로 FG 입고·출하될 수 있음(`WorkOrderRepository.cs:81-90`) — 10-22 PNT-MAIN 으로 B 경로(사출 → 도장 라벨 발행·판정·확정)는 이어졌으므로 도장품 BOM 에는 사출품(InjFlag 1) 자식을 반드시 둘 것(없으면 코어 해석이 완제품 자신을 돌려준다).

---

## 2. 안영훈 — PDA·Api + Web WH·FG·SCM·Portal + 재구축 스크립트

### 2-1. 배포·검증 대기 (8)

완료된 코드 수정·구현 설명은 삭제했습니다. 남은 배포·검증만 기록하며, 이 목록을 근거로 DB 재구축·시드·재고 변경을 실행하지 않습니다.

1. **재구축 통합 검증 보류**
   - 전체 재구축은 사용자 지시대로 실행하지 않습니다. 추후 별도 승인 시 MNT 마이그레이션 3개(`migrate_mnt_pm_checklist.sql`·`migrate_mnt_failure_downtime.sql`·`migrate_mnt_pm_lead_days.sql`)도 함께 검증합니다. `migrate_mnt_failure_downtime.sql`은 기존 DowntimeID 값이 있으면 중단하므로 목록에 있다는 이유만으로 실행하지 않습니다.
   - (10-23 이승욱 대조) `rebuild_db.sh` 의 opt-in 은 `PDA_SEED`·`seed_portal_dev` 둘뿐이고 `seed_inj_img_master_dev`·`seed_pnt_dev`·`seed_pp_customer_order_dev`·`seed_md_worker_dev`·`seed_aps_dev`·`seed_demand_plan_dev` 6개는 기본으로 돕니다(이전 판의 「개발 시드는 기본 제외」 설명과 다름). 머리말도 없는 `dist/rebuild_db.ps1`·「시드 12개」를 가리킵니다. 573ca09a 가 `migrate_mnt_oeelog_fill.sql`·다섯 번째 작업 등록(`setup_job_mnt_oeelog_fill.sql`)을 더했으니 전체 재구축 검증 범위에 넣어 주세요(10-22 `migrate_pp_lineoee_fill`·`migrate_pp_linestatelog_fill` 과 작업 등록도 같음).
2. **서버 API 교체·설치본 확인**
   - 서버 `/api/auth/employees`는 현재 404입니다. 교체용 `publish/AMES.Api`는 준비했고 사용자가 직접 서버에 반영합니다. 기존 서버 설정파일을 유지한 뒤 PDA에서 Retry로 아이디 목록을 확인해야 합니다.
   - PIN 키패드 앱은 Honeywell에 설치했습니다. 서버의 나머지 API·Web·POP AMES.Data 설치본은 필요한 수정이 포함됐는지 DLL/해시로 확인합니다. health의 ok만으로 최신 배포라고 판단하지 않습니다.
   - **[알림] AMES.Api 에 PUSH 발송기·알림 앱 Api 추가**(10-24 이승욱, 사용자 지시)
     - `Workers/Notify/NotifyWorker`·`FcmSender`(ScheduledWorker 상속, `Program.cs` 등록), `Endpoints/NotificationEndpoints`(`/api/notifications`), appsettings `Notify:FcmServiceAccountPath`(빈 값). 저장소 `AMES.Data.Repositories.NotificationRepository`, 마이그레이션 `migrate_notification_push_sender.sql`(재구축 목록에도 넣음, 10-24 개발·로컬 DB 적용). 개발서버 Api 는 10-24 게시본(`publish/AMES.Api_20261024-023331.zip`)을 반영해야 발송기·알림 Api 가 생깁니다.
     - 개발서버·로컬 IIS Api 가 같은 DB 를 쓰면 둘 다 발송기가 돌지만 감지는 applock, 발송은 행 잡기(SENDING)로 겹치지 않습니다. 기존 엔드포인트·인증은 바꾸지 않았습니다(알림 Api 만 PIN 로그인 세션 요구).

3. **WH·FG 실제 쓰기 통합 검증**
   - 입고→조정→취소→재입고, 생산 LOT 오입고·공급사/PO 잔량 검증, FG 라벨·위치 미배정/수리 재고, 백플러시 실제 차감 위치, 낱개/팔렛 출고·반품·DCR 누적 수량 보호를 배포 후 확인해야 합니다.
   - 10-23 사용자 결정으로 고객 반품은 검사 대기 → 전체 LOT 양품 승인/불량 판정으로 구현했습니다. 기존 `Open`도 검사 대기로 취급하며 기존 컬럼만 사용합니다. 새 Web·API를 함께 배포한 뒤 FG-01 Customer Returns의 판정(기존 Inventory Defect 승인 권한), PDA의 출고·적재·조정 차단, 양품 승인 후 Set Location, 불량 수량·거래·감사 이력을 실제로 확인해야 합니다. 물리적 폐기 완료 기록은 별도이며 불량 판정과 같다고 보지 않습니다. SQL 테이블 변수 판정 테스트와 실제 스키마 NOEXEC/조회 검증만 실행했고 실제 반품/재고 행은 변경하지 않았습니다.
   - 실제 재고 쓰기·생산 확정은 실행하지 않았습니다. 수정한 재고 테스트 fixture도 명시적인 테스트 DB에서만 재실행합니다. 사용자 지시로 10-23 개발 DB(192.168.0.132/AMES_DEV)의 구형 WH/FG/SP PDA 업무 프로시저 27개를 원본 정의·명시 권한 백업 후 삭제했습니다. 테스트 초기화 6개는 정의 해시가 그대로이며, POP 공통 인증·생산·MNT 프로시저와 재고 테이블은 변경하지 않았습니다. 회귀 검사 38건 통과, 실제 입출고 검증은 별개입니다.
   - (10-23 이승욱 대조) 미배정 재고 포함은 Web RPT-005(`RptRepository.ListInventorySku`)만이고, Api `/api/rpt/inventory` 의 `RptRepository.ListInventory` 는 아직 `AreaCode='FG_AREA'`·위치 필수입니다(이 엔드포인트를 부르는 클라이언트는 없음).
4. **동시 출고·보충 Worker 부하 검증**
   - 여러 PDA의 동시 출고·라인 보충, 팔렛 잠금 순서, 수주 수집과 로컬 출하 동시 실행을 실제 부하로 검증하지 않았습니다.
5. **예비품 출고의 보전 작업지시 연결**
   - 서버 API 반영 후 미완료/예정 작업지시 선택, 설비 일치, 연속 출고의 선택 유지, 작업지시 완료와 출고/취소의 경합을 확인해야 합니다. 작업지시 미선택 출고와 구 PDA 요청 호환도 확인합니다.
6. **빈 팔렛 회수 실물 검증**
   - 서버 반영 후 실물 빈 팔렛·중복 회수·미출하/비활성·연결 양수 재고 잔존·스캔 이후 출하 시각 변경 사례를 확인해야 합니다. 제품 반품과 빈 팔렛 회수는 별개이며 실제 회수 처리는 실행하지 않았습니다.
7. **로케이션 2단계와 기존 Area 부모 선택**
   - 서버에서 상위 LocationLevel=1의 재고 배정 차단과 하위 위치 조회를 확인해야 합니다.
   - 기존 Area의 임시 상위 이름이 기본 규칙과 다르면 자동 연결되지 않을 수 있습니다. 현재 MD-018의 Parent 선택을 사용하며, 다수 상위의 기본 선택 규칙/지도 선택 UI는 별도 확인이 필요합니다.
   - (10-23 이승욱 대조) 라인 로케이션 저장(`WarehouseRepository.LineLocations`)은 `ParentLocationID` 없이 넣습니다(상위 미지정으로 남음).
8. **FG 팔렛 단위 위치 배정 실물 검증**
   - 서버의 팔렛 조회 경로와 ASSY 하위 LOT 전체의 원자적 위치 배정, 내용물/수량 재검사, 용량 초과·비 ASSY 혼재 차단, LOT별 MOVE 기록을 실제 저장으로 확인해야 합니다.

### 2-2. 보류 (8)
- **PO 자동 수집 정합성**: 고객 SRM에서 취소·삭제된 PO 라인은 조회에서 사라질 뿐 AMES에는 Open으로 남습니다. 이미 Confirmed인 수주의 수량·납기 변경도 덮어씁니다. 누락을 취소로 간주할지, 확정분 변경을 승인 대기로 둘지 업무 규칙 결정이 필요합니다. 로컬 출하가 있는 수주의 ShippedQty 하향 덮어쓰기는 막았지만, SRM 수량과 로컬 출하의 전면 정산·수동 하향 보정 승인 절차는 별도 결정입니다.
- **PO 자동 수집 실패**: 고객 테스트 서버 환경 문제로 09-17부터 실패합니다. 일별 구매계획 수집(`DPSYNC-SEMS`)도 URL이 자리표시자로 보고되어 외부 서버 복구·접속 정보 확인이 필요합니다. (10-23 이승욱 대조: 개발 DB `SW_POSYNC.INTERVAL` 은 09-17 부터 0 이라 자동 수집은 꺼져 있고 마지막 시도는 09-22 수동 실행의 연결 시간 초과입니다. `DPSYNC-SEMS` 는 자리표시 URL 로 매시간 실패 중 — RetryCount 423.)
- **개발 완료 시 정리**: appsettings 비밀번호 평문을 접속문자열 환경변수로 옮깁니다. CORS가 필요한 브라우저 클라이언트는 `Cors:AllowedOrigins`에 명시적으로 등록합니다(네이티브 PDA에는 CORS가 필요 없습니다).
- **FG FIFO·고객 혼적 정책**: 현재는 스캔한 가용 재고·FG 위치·출하 고객을 검증하지만 FIFO 순서와 위치 단위 단일 고객은 강제하지 않습니다. 위치 고객 조회의 NULL을 실제 혼적 검사가 된 것으로 보지 않습니다. 사용자 요청은 ASSY 여부/위치/용량 확인이므로, 고객 전용 위치나 FIFO 강제를 임의로 추가하지 않습니다.
- **라인 실사·소수/음수 재고 정책**: 기존 PDA는 정수 수량 조정이며 BF 부족분은 별도 음수 자리표시입니다. 소수 단위 자재 실사와 BF 부족분 정산을 지원할지는 실제 재고/생산 소요를 구분해 결정해야 합니다. 음수 BF 행을 일반 스캔 재고로 노출하거나 생산 소요를 지우는 수정을 임의로 하지 않았습니다.
- **원단 불출 +10%(추가 불출율) 반영**(10-23 이승욱 요청 — 고객 요구 10-14 "BOM 은 정량, 원단 불출 +10%"): MD-003 이 품목별 추가 불출율 `MD_Item.IssueAddPct`(%, NULL = 0)를 저장하지만 WH·PDA 는 BOM 을 읽지 않고(라인 보충은 라인 로케이션 최소 재고 기준) 이 값을 쓰는 곳이 없습니다. 정할 것: 작업지시·BOM 기준 자재 불출 기능을 새로 둘지, 라인 보충 수량에 비율을 더할지. BOM 자동 차감에 불출율을 넣을지는 심재현 보류(같은 이름 항목)와 함께 정합니다.
- **SAP 연동(DB to DB) 실패 처리 방안**(10-26 이승욱 정리, 사용자 지시 — 처리자 안영훈; SAP 연동 자체는 아직 없음, 공통 5 「SAP 마스터 수신」과 함께 설계)
  - 실패는 세 층에서 생기므로 한곳에 모아 감지합니다: ① **연결·작업 실패**(SAP DB 접속 불가, 연동 작업·프로시저 오류) → `SYS_InterfaceMonitor` 연동별 행(`SAP-ITEM`·`SAP-BOM`·`SAP-PO` 등 — OEE·라인 상태 작업이 쓰는 방식 그대로: OK/ERROR·메시지·RetryCount·마지막 성공 시각) · ② **행 단위 거부**(모르는 단위·없는 품번·형식 오류) → 주고받는 중간(인터페이스) 테이블의 행마다 처리 상태·오류 메시지·처리 시각 칸 · ③ **멈춤(조용한 실패)**(작업은 돌았는데 오래 새 데이터가 없음) → 모니터 행의 마지막 성공 시각 vs `MaxGapMinutes`.
  - 발송기 `SAP_SYNC_FAIL` 감지(MES 쪽 DB 변경 없음 — 감지 SQL 만 더함): SAP 연동 모니터 행이 ERROR 가 되면(같은 오류는 한 번만 — 원천 번호 `연동코드@오류 시각`), 마지막 성공이 허용 간격보다 오래되면, 처리 못 한 행이 N건을 넘으면 알림. 수신자는 SYS-008 규칙(지금 시스템 관리자).
  - SAP 쪽과 정할 것: ① 연동 작업을 어느 쪽에서 돌리는지(MES 에이전트 작업 / SAP 쪽 스케줄러 — 실패 기록 주체가 정해짐) ② 중간 테이블의 처리 상태·오류 칸 형식(양쪽이 같은 테이블을 봄) ③ 연동별 허용 간격(예 품목 마스터 24시간·발주 1시간) ④ 행 단위 거부를 바로 알릴지, 건수가 쌓였을 때만 알릴지 ⑤ 실패 뒤 재시도 주체(다음 주기 자동 / 사람이 재처리).
- **알림 감지 기준값 정의 — 납기 위험·PLC 정지·일일 요약**(10-26 이승욱 정리, 사용자 지시 — 처리자 안영훈; 발송기·이벤트 코드는 있고 감지 기준만 없음, 오세영 4-1 2): 기준값이 정해지면 설정값 몇 행(공통코드 `SW_NOTIFY` 또는 `SYS_Config`)과 발송기 감지 SQL(`NotificationRepository.EnqueuePush`)만 더하면 됩니다.
  - **납기 위험(`OTD_AT_RISK`)**: 기준 날짜 — 고객 납기 `DueDate` / 생산 마감일 `ProdDeadline`(= 납기 − 버퍼 3근무일) 중 무엇 · 경고 시작 — 그 날짜 며칠(근무일) 전부터 · 위험 판단(하나 또는 조합) — 완료율 X% 미만 / 남은 수량 > 남은 계획 슬롯(미배치·초과 수량) / 마감일 경과 미완료 · 대상 — 작업지시만 / WO 없는 확정 수주도. 참고: `SYS_Config.OTD_TARGET_PCT`(95%)는 전체 납기 준수율 목표라 WO 단위 판단에는 맞지 않습니다.
  - **PLC 정지(`PLC_DOWN`)**: 무엇을 정지로 볼지 — PLC 통신 끊김(InjAgent 가 값을 못 읽음) / 설비 멈춤(통신은 되는데 샷이 안 올라옴) / 둘 다 · 대상 — InjAgent 가 실제 연결된 사출기 목록 · 무신호 시간 — 고정 N분 / 표준 사이클 × k배 · 제외 — 계획 가동 시간 밖(휴게·식사·교대 외), 비가동이 이미 등록됨, PM 중 · 회복(재가동) 알림 여부·같은 정지 재알림 간격.
  - **일일 요약(`DAILY_DIGEST`)**: 발송 시각·대상 기간(예 전기일 경계 07:00 뒤 07:30 에 전날분) · 담을 내용(골라서) — 라인별 계획 대비 실적·불량률·OEE·비가동 시간·미해결 고장·안돈 수·PM 지연·재고 부족 수·납기 위험 WO 수 · 휴무일 발송 여부 · 수신 역할(지금 규칙은 생산관리·시스템 관리자, 사용 안 함). PUSH 본문은 짧아야 하므로 핵심 수치 3~4개만 보내고 자세한 내용은 앱 상세로 보이는 방식을 권합니다.

### 2-3. 테이블 (10-14 전수 조사)
| 테이블 | 상태 | 내용 |
|---|---|---|
| `PP_SupplyPlan`·`PP_SupplyPlanDetail` | 읽기만 | 구 공급 계획 API가 읽습니다. 외부 사용 여부 확인 전에는 엔드포인트·테이블을 삭제하지 않습니다. |
| `WH_ReleaseSchedule` | 구형·삭제 승인 대기 | 10-23 개발 DB에서 0건, 현재 코드는 `WH_PickSlip` 사용, 이 테이블의 SQL 모듈·FK 참조 없음 확인. 신규 스키마·추출 대상에서 제외하고 구형 데모 시더는 DB 접근 전 중단하도록 폐기했습니다. `retire_wh_release_schedule.sql`은 기본 조회만 하며, 승인 후 빈 테이블·참조 유무를 재검사해 이 테이블만 삭제할 수 있습니다. 실제 DB에서는 아직 삭제하지 않았습니다. |
| `TEST_SRM_PurchaseOrderResponse` | 독립 SRM 테스트 API 조회용 | 10-23 개발 DB 426건. `test-api/SrmMockApi/Program.cs`의 발주 조회·health가 읽고 `Initialize-TestDatabase.ps1`이 최초 적재합니다. AMES 업무 앱의 직접 SQL 참조는 없습니다. |
| `TEST_SRM_Shipment`·`TEST_SRM_ShipmentItem` | 독립 SRM 테스트 API 저장용 | 10-23 개발 DB 각각 2건·4건. `test-api/SrmMockApi/SqlShipmentStore.cs`가 출하 요청·품목과 재전송 판정용 JSON을 저장·조회하고 `Verify-Shipments.ps1`이 테스트합니다. 업무 출하/입고 테이블이 아니며 삭제하지 않았습니다. |
| `PP_MaterialReservation` | 미사용 경로 | `ListPickingSlipCandidates` 호출처와 예약 생성 경로가 없습니다. 삭제 또는 예약 기능 설계 결정 대기입니다. |

`TEST_SRM` 사용 경로: 현재 개발 DB의 `SW_POSYNC_URL/SEMS`는 `http://192.168.1.68:5220/Service/WEBSRV_INQUERY_PO.ashx`로 설정돼 있어 PO 수집이 모의 API를 간접 호출하는 구성입니다. 이 PC에서는 해당 서버의 health 요청이 시간 초과됐으므로 현재 서비스 실행 여부·그 프로세스가 접속한 DB는 미확인입니다. 저장소 `Start-TestApi.ps1`은 `192.168.1.100/AMES_DEV`를 지정하므로, 개발 DB(132)에 복제된 이 3개 테이블을 실제 원격 서비스가 읽는다고 단정하지 않습니다. 코드상 사용처가 분명해 임의 삭제하지 않습니다.

10-23 정리 실행 당시 개발 DB의 구형 PDA 업무 프로시저 삭제를 확인했습니다. 원본 복구 파일은 `publish/DB_Backup/PDA_BusinessProcedures_20261023_before_cleanup.sql`, 기본 조회용 정리 스크립트는 `dist/retire_pda_business_procedures.sql`입니다. 삭제 대상 27개가 0개 남았고, 재고·거래·출하·반품·팔렛 행 수는 전후 동일합니다. DB 모듈의 외부 참조는 없었으나 SQL Agent 작업은 조회 권한이 없어 확인하지 못했습니다. 로컬 복제 DB는 변경하지 않았습니다. `AMES_ERD_data.js`·`AMES_DB_Schema.html`은 통합 전 구 설계 문서로, 현재 테이블 판정 근거로 쓰지 않습니다.

### 2-4. 유효한 문제점 (1)

해결된 코드 결함의 상세 이력은 삭제했습니다. 아직 끝나지 않은 배포·실동작 검증은 2-1에, 정책 결정·현재 방향 정정은 2-2·2-5에 남깁니다.

1. **[중] 스키마 추출 도구의 전이 패키지 취약성 경고**(10-23 빌드에서 확인)
   - `tools/schema_from_db`의 기존 SMO 의존성이 `System.Security.Cryptography.Pkcs 9.0.4`를 가져오며 NuGet 감사에서 NU1902·NU1903을 경고합니다. 배포 API에 새로 추가한 패키지는 아니며 도구는 실행하지 않았습니다. 패키지 보안 업데이트와 실제 스키마 추출 호환 검증이 필요합니다.

### 2-5. 재검토 정정 — 운영 결함 아님·선택적 정리 (8)
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
   - 일반 LOT은 PART로, 공급사 Unit Item은 BOX 재고로 받는 기존 구분을 유지합니다. 사용자가 피킹은 한 박스씩 처리하도록 정했으므로 PART를 임의로 BOX라고 바꾸거나 하나의 LOT을 한 박스로 환산하지 않았습니다. 생산 LOT 오입고·공급사/PO 잔량 방어의 실제 검증만 2-1에 남깁니다.
7. **미사용 수동 피킹 슬립 출고 — 현행 라인 보충 경로와 구분**
   - 현재 PDA는 WH-AUTO 지시를 통해 창고 재고를 라인으로 이동합니다. 구형 수동 슬립의 OUT 전용 분기는 현재 화면 호출이 없으므로 라인 이동 결함으로 일반화하지 않습니다. 다시 사용할 때는 업무별 출고/라인 이동 규칙을 먼저 정해야 하며, 이번에 임의로 재활성화하지 않았습니다.
8. **PDA 배지 스캔 즉시 로그인 유지 — 복제 방지 추가 인증은 도입하지 않음**
   - 10-23 사용자 재확인: EOS 양식 배지는 관리자 계정을 포함해 기존처럼 PIN 없이 스캔 즉시 로그인하고, 아이디를 직접 입력하거나 목록에서 선택할 때는 PIN을 입력합니다. 이 결정을 미구현 결함이나 정책 결정 대기로 분류하지 않습니다.
   - 고정 QR의 사진·복사본은 원본과 구별할 수 없으므로 동일 계정으로 로그인할 수 있는 위험을 수용한 정책입니다. 현재 형식 검사·계정 유효성 검사·로그인 횟수 제한을 유지하며, 복제 방지를 이유로 PIN을 강제하거나 로그인 코드를 변경하지 않습니다.
---

## 3. 이승욱 — Web SYS·MD·MNT·RPT·PP-CAL/LSB/OEE/DTL/ODM·계정·공통

### 3-1. 미해결·적용·검증 대기 (0)
없습니다.

### 3-2. 보류 (3)
- **B7 MNT-004 금형 정비 등록**(`MoldRepository.cs:46·74` StartChange·CompleteChange, 호출처 없음): 정비 유형·작업자·샷 리셋·정비일을 남길 곳이 없습니다. 금형 교체를 기록하던 메서드도 옛 POP 화면을 지우면서 끊겼습니다 — 그런데 `MoldRepository.cs:8`·`MntRepository.cs:419` 주석은 여전히 "POP 금형 교체가 기록" 한다고 씁니다.
- **개발 완료 시 정리**: 운영 admin 시드 차단, appsettings 비밀번호·AllowedHosts·접속문자열 환경변수(Web).
- **개발 DB 이름 `AMES_DEV` → `AMES` 전환**(10-23 사용자 요청 — 조사만 하고 보류): 같은 개발서버에 `AMES` 를 만들고 계정·권한·SQL Server 에이전트 작업을 옮긴 뒤 소스의 Database 를 `AMES` 로 바꾸는 안.
  - **스키마 파일(`dist/AMES_Schema.sql`)만으로 만들면 동작하지 않습니다**: 구조는 10-24 재생성으로 개발 DB 와 같아졌지만(옛 PDA 프로시저 27개만 뺌), 업무 데이터(공통코드·설정·역할 권한·사용자·품목·BOM·라인 패턴·공휴일)가 없습니다(시드는 `SYS_Screen` 96행·샘플 몇 행). 재구축으로 채우려 해도 `rebuild_db.sh` 가 DB 이름 `AMES_DEV` 가 아니면 거부하고, `USE AMES_DEV` 로 시작하는 dist 스크립트 22개(재구축 목록 8개)는 그대로 돌리면 옛 DB 를 고칩니다.
  - **권장 안 — AMES_DEV 백업 → `AMES` 로 복원**(같은 서버 Developer 15.0.2000.5, 약 2.1GB): 구조·데이터·프로시저 192개·IDENTITY 가 같고, DB 사용자는 `ames_app`(db_owner) 하나라 같은 서버 로그인 SID 로 그대로 연결됩니다. 할 일 = ① 복원(`WITH MOVE`) ② 사용자 연결 확인 ③ 에이전트 작업 5개(`[AMES] Factory Calendar Fill`·`Line State Fill`·`Line State Recalc`·`Line OEE Fill`·`MNT OEE Fill`, 단계 6개 — 지금 모두 `AMES_DEV`)를 `setup-*.ps1 -Database AMES` 로 다시 등록하거나 단계 DB 만 변경(작업 이름이 서버에 하나라 옮기면 AMES_DEV 자동 집계는 멈춤) ④ 접속 문자열 — Web(`appsettings*.json` 3·`launchSettings.json`)·Api(1)·Pop(3)·InjAgent(1)·`tools/publish-web.ps1`·`publish-api.ps1`·`setup-*.ps1` 기본값·문서, 또는 환경변수 `ConnectionStrings__AMES` ⑤ 개발서버 IIS Web·Api·로컬 IIS·POP 터미널·InjAgent PC 설정 교체. PDA·Tablet 은 Api 경유라 바꿀 것이 없고, 코드의 DB 이름 판정은 PDA 테스트 초기화 가드(`AMES_TEST*` 만 허용)뿐이라 영향 없습니다. 작업량은 반나절 정도(현장 PC 별 설정 제외).
  - 정할 것: ① 로컬 비상 DB(`localhost\MSSQLSERVER01`)·복제본(98.95)·`sync-dev-db-to-remote.ps1`·`tools/db-route.ps1` 도 같은 이름으로 바꿀지 ② `AMES.Data.Tests`(48개 파일)·테스트 기본 접속 ③ `USE AMES_DEV` dist 스크립트 22개와 `rebuild_db.sh` 이름 가드를 DB 이름에 묶이지 않게 바꿀지 ④ 전환 시점(옛 DB 에 기록하는 클라이언트가 남으면 데이터가 갈라지므로 모든 클라이언트를 한 번에) ⑤ AMES_DEV 를 남길지(남기지 않을 거라면 `ALTER DATABASE … MODIFY NAME` 이 가장 간단하지만 그 순간 모든 클라이언트가 끊김).

### 3-3. 테이블 — 웹 화면이 읽지만 쓰는 곳이 없음 (10-14 전수 조사)
1. **`PR_MoldChange`(3) — 금형 교체 이력**
   - 읽는 곳: MNT-004 「교체 이력」(`MntRepository.cs:425-446`)
   - 내용: 기록 메서드를 아무도 부르지 않아 시연 3행만 보입니다(B7). 실제 교체는 PP-LSB 금형 교체(MC) 슬롯으로 계획만 있습니다.
   - 제안: POP 교체 시작·종료 버튼(심재현) 또는 B7 정비 등록과 함께 기록합니다.
2. **`MNT_MoldShotCount`(0) — 금형 샷 누적**
   - 읽는 곳: MNT-004 금형 목록(`MntRepository.cs:406`), MD 금형·설비 삭제 사용처(`MasterDataRepository.cs:1974·3068`)
   - 내용: 샷을 쌓는 곳이 없어(InjAgent 는 `PR_ShotCount` 에만 씀) 소진율이 비어 있고 정비 한계 알림이 동작하지 않습니다.
   - 제안: InjAgent 가 샷을 기록할 때 이 카운터도 올립니다(심재현 연계).

### 3-4. 문제점 (0)
없습니다.

---

## 4. 오세영 — 알림(PUSH) 앱·발송

### 4-1. 미해결·앱 개발 참고 (3)
1. **[중] 알림 안드로이드 앱 개발 — 서버(발송기·앱 Api)는 준비됨**(10-24 이승욱, 사용자 결정: FCM ·                                                          is.com` 으로 나갈 수 있어야 합니다. 키가 없는 동안에도 알림 이력은 쌓여 앱 목록·상세로는 시험할 수 있습니다(SYS-Interfaces `NOTIFY-PUSH` 는 ERROR).
   - **기기 등록**: 로그인 직후와 `onNewToken` 때 `POST /api/notifications/devices` `{ token }`(FCM 등록 토큰, 255자 이하) → 204. 같은 기기에 다른 사람이 로그인하면 토큰이 그 사람에게 옮겨집니다. 「알림 끄기」는 `POST /api/notifications/devices/off` `{ token }`. **로그아웃(`POST /api/auth/logout`)은 알림을 끊지 않습니다** — 한 번 로그인한 계정은 계속 받는다는 결정이라, 앱 로그아웃 때 끄기를 부르지 마세요(끄는 화면을 따로 둘 것).
   - **수신**: 서버는 `notification{ title, body(본문 앞 3줄) }` + `data{ historyId, eventType, refType, refId }`(모두 문자열)를 `android.priority=high`, 알림 채널 ID **`ames_alerts`** 로 보냅니다 — 앱이 이 ID 로 채널을 만들어야 소리·중요도가 적용됩니다. 앱이 앞에 떠 있을 때는 notification 이 자동 표시되지 않으니 `onMessageReceived` 에서 직접 띄웁니다. 알림을 누르면 `historyId` 로 상세를 엽니다(Android 13+ 는 `POST_NOTIFICATIONS` 권한 요청 필요).
   - **조회 Api**: `GET /api/notifications?unread=false&before={id}&take=30`(최신순, 같은 이벤트·원천은 기기가 여럿이어도 1행, 다음 쪽은 `before` = 마지막 `id`) · `GET /api/notifications/{id}`(남의 것·없는 번호는 404) · `POST /api/notifications/{id}/read`(204) · `GET /api/notifications/unread-count` → `{ unread }`. 항목 필드: `id, eventTypeCode, eventName, subject, body, sourceRefType, sourceRefId, status(PENDING·SENDING·SENT·FAILED·QUIET), createdTs, sentAt, readAt`. 시각은 DB 서버 시각(공장 현지시각, 시간대 표시 없음)이라 기기 시간대로 바꾸지 마세요.
   - **상세의 원천 정보**(10-24): `GET /api/notifications/{id}` 응답에 `source` 가 붙습니다 — 알림을 보낸 뒤의 **지금** 상태입니다. 공통 `refType, refId, status, occurredAt, closedAt, lineId, lineName, equipId, equipName, itemNo, itemName, severity` + 종류별 `fields: [{ key, value }]`(값은 글자 — 날짜 `yyyy-MM-dd`, 일시 `yyyy-MM-dd HH:mm:ss`). 키: 고장 `failureType·symptom·source·reportedBy·workOrder·workOrderStatus·technician·workStartedAt` · 안돈 `cause·supervisor·supervisorAckedAt·departments`(`MAINT:ACKED,QC:CALLED` — CALLED·ARRIVED·ACKED)`·correctiveAction` · QC 홀드 `heldQty·affectedType·lot·location·heldBy` · PM `pmClass·pmType·nextDueDate·lastPmDate·workOrder·workOrderStatus` · 예비품 `sparePartNo·onHand·reorderPoint·safetyStock·uom·location`. `status` 는 원천 상태 코드 그대로이고 PM 은 `OK·DUE·OVERDUE·CANCELLED`, 예비품은 `LOW·OK·INACTIVE` 입니다. 원천이 지워졌으면 `source: null`. 키 이름은 바뀌지 않으니 앱이 화면 이름을 번역해 보이면 됩니다.
   - **방해 금지 Api**(10-24): `GET /api/notifications/quiet-hours` → `{ start, end, devices }`(HH:mm, 꺼져 있으면 null) · `PUT /api/notifications/quiet-hours` `{ start, end }` → 204(둘 다 빈 값이면 해제, 시작 > 끝이면 자정을 넘는 구간, 한쪽만·형식 오류·같은 시각은 400, 기기를 아직 등록하지 않았으면 404). 사람 단위 설정입니다 — 그 사번의 기기 전부에 적용되고, 나중에 등록하는 기기도 이어받습니다. 시각은 공장 현지시각 기준이라 기기 시간대로 바꾸지 말고 그대로 보냅니다. 이 시간에 생긴 알림은 보내지 않고 목록에만 남습니다(`status` QUIET).
   - **이벤트 5종**(1차): `FAILURE_OPEN`(원천 `FAILURE`/고장번호) · `ANDON_RAISED`(`ANDON`/안돈 ID) · `QC_HOLD`(`QC_HOLD`/홀드번호) · `PM_OVERDUE`(`PM`/`계획번호@yyyyMMdd` — 하루 한 번) · `LOW_STOCK`(`SPARE_PART`/`예비품번호@yyyyMMdd` — 하루 한 번). 제목·본문은 영어 「항목: 값」 줄이고 `eventName` 은 SYS-008 규칙 이름입니다 — 화면 언어에 맞추려면 앱이 `eventTypeCode` 별 제목을 따로 가져도 됩니다. 상세 화면은 지금 본문 문자열만 줍니다(원천의 자세한 정보·바로가기가 필요하면 요청).
   - **누가 받나**: SYS-008 규칙의 수신 역할을 가진 활성 웹 사용자와, 규칙에 Operator 가 있으면 PIN 이 있는 활성 작업자 전원입니다. 10-24 지금 규칙 9건에는 Operator 가 없으니 작업자로 시험하려면 SYS-008 에서 Operator 를 더하세요. 규칙에 PUSH 채널이 있어야 하고 공통코드 `NOTIFICATION_CHANNEL` PUSH 가 사용 중이어야 합니다.
   - **시험**: 개발서버 Swagger(`/swagger`, Notifications 그룹) — 개발서버 Api 에 10-24 게시본을 반영한 뒤부터 보입니다(DB 마이그레이션은 10-24 적용 완료). 개발 시드 작업자 W001~W004(PIN 1234)는 개발 DB 에만 있습니다. 발송기는 1분마다 돌고(`SW_NOTIFY.INTERVAL`), 고장·안돈·QC 홀드는 생긴 뒤 60분 안(`LOOKBACK_MIN`)에 등록된 기기에만 갑니다(그 전에 생긴 것은 보내지 않음).
2. **[하] 서버에 아직 없는 것 — 앱이 필요하면 이승욱에게 요청**
   - 반복 알림(SYS-009 `ALERT_REPEAT_MINUTES` 15분 — 해결 전 고장·안돈 다시 보내기)
   - 감지 기준이 없는 이벤트 4종: 납기 위험(`OTD_AT_RISK`)·SAP 연동 실패(`SAP_SYNC_FAIL`)·PLC 정지(`PLC_DOWN`)·일일 요약(`DAILY_DIGEST`) — 기준 정의는 안영훈 2-2(10-26)
3. **지킬 규칙**: 사용 중인 채널로만 발송, 시각은 DB 서버 시각, 행위자는 20자(발송기 `PUSH-SVC`), 비밀값(FCM 키)은 서버 파일 — 공통코드·감사 로그·Git 에 넣지 않습니다. PUSH 기기(`SYS_NotificationChannel` 의 PUSH 행)는 앱의 `POST /api/notifications/devices` 로만 생깁니다 — SYS-008 은 PUSH 행을 만들거나 주소를 고치지 못하고 수신 여부·방해금지 시각만 고칩니다(10-24; 그 전에 웹에서 EMAIL 행을 PUSH 로 바꿔 메일 주소가 토큰 자리에 남은 개발 DB 행 1건은 지웠습니다 — 개발 DB 채널 행 0건).

### 4-2. 보류 (1)
- 앱 개발 일정·착수 시점 미정

### 4-3. 연계
- POP 안돈의 알림 기록 고정값(`SUPERVISOR/PDA`·`LINE-LEAD/EMAIL`)을 정리하는 일은 심재현 미해결 7 입니다(안돈 PUSH 자체는 발송기가 `PR_AndonCall` 을 보고 보냅니다).
- 발송기·앱 Api 는 AMES.Api 안에 있습니다(안영훈 2-1 알림).

---

## 5. 공통 — 다음 개발 항목
- `AMES.Pda`: PP 모듈·CM(고장) 작업지시 화면(예비품 SP 입출고·PM 작업지시 점검·완료는 구현됨) — 안영훈
- **[방향 확인] `AMES.Data` 의 SQL 위치**: CLAUDE.md 는 "inline SQL → `dbo.SP_*` 스토어드 프로시저 전환 예정" 입니다. 10-21 POP 은 그 방향으로 인라인 SQL 145개를 `dbo.SP_POP_*` 로 옮겼지만(김현진 c10a16ee, `dist/migrate_pop_stored_procedures.sql` — 개발 DB 적용, 로컬 DB 는 10-22 개발 DB 복제로 반영), 10-16 PDA 는 반대로 프로시저를 앱 SQL(`PdaWarehouseSql` 등)로 옮겼고, 같은 날 POP 팔레트 적재(`PalletStockRepository`, 심재현)와 웹·Api 저장소는 앱 SQL 입니다. 프로시저로 옮긴 SQL 은 마이그레이션 하나가 정본이라(스키마 파일에는 10-24 재생성 때 개발 DB 정의가 같이 들어감), 프로시저를 고칠 때는 그 파일을 고쳐 두 DB 에 다시 적용하고 스키마 파일도 다시 떠야 합니다. 어느 쪽으로 갈지(또는 POP 만 프로시저) 정한 뒤 CLAUDE.md 문구와 이 항목을 맞춥니다 — 공통
- `AMES.Web`: 실제 데이터 바인딩 완성(일부 화면 scaffold 상태) — 각 화면 담당
- `AMES.Pop`: 추가 공정 모듈(필요 시) — 심재현
- **로컬 DB 이름 정리**(10-26): CLAUDE.md 의 비상용 로컬 DB `localhost\MSSQLSERVER01` 은 현재 없고 실제 로컬은 `localhost\SQLEXPRESS`(AMES_DEV). CLAUDE.md 와 `dist` 의 주석을 맞춘다 — 이승욱.
- **SAP 마스터 수신**(10-21 회의 결정 — 이 마스터들은 MES 가 SAP 로 보내는 것이 아니라 **SAP 에서 받는** 데이터): `MD_Item`·`MD_Vendor`·`MD_Location`(상위 로케이션만 — 10-22 2단계, 하위는 MES 전용)·`MD_Customer`·`MD_BomVersion`·`MD_Uom`(10-22) 의 `SAPFlag`(구 `SAPTransFlag`, 1 = SAP 에서 받은 행)는 MES 화면이 표시만 하고(목록 「SAP」 열·수정 창 헤더 ID 옆 SAP 표시) 쓰지 않으며, 등록·수정 창 체크박스와 BOM 의 SAP 전송 규칙(변경 사유 필수·「SAP 전송 변경」·새 승인/만료 버전 자동 해제)은 10-21 삭제했다. 할 일: ① **SAP 에서 직접 받는 데이터(`SAPFlag` = 1 인 행)는 MES 에서 수정할 수 없도록 비활성화해야 한다** — 여섯 화면(MD-003·MD-004·MD-009·MD-010·MD-018·MD-019)의 수정 창 입력·저장·삭제(BOM 은 버전·라인 편집·승인 흐름 포함)와 엑셀 업로드·저장소 `Update*`/`Delete*` 를 함께 막을지, MES 만 쓰는 칸(예: 품목 적입수량·사출품 여부·불출율, 로케이션 적재 용량)은 열어 둘지 칸 단위로 정해야 한다 ② SAP 수신 기능(방식 SAP B1 Service Layer 등·주기·담당, 키 매칭과 새 행/변경/삭제 처리, 받은 시각·오류 기록 테이블, 순서: 단위 → 품목 → 고객사·협력업체·로케이션 → BOM) ③ 이미 MES 에서 만든 행과 SAP 행이 같은 키로 들어올 때의 처리(덮어쓰기·`SAPFlag` 1 로 전환 여부). 그 전까지 `SAPFlag` 는 SQL 로만 1 이 된다.

## 6. 참고 — 검증 기준선

지난 기준선은 커밋 이력으로 봅니다. 여기에는 지금 기준만 둡니다.

- **POP 카운트 선행·발행 원장·오프라인 번호 이어쓰기(10-26)**: `SP_POP_INJ_DecrementPrintedCount` 와 `dist/migrate_pop_stored_procedures.sql` 전체, `dist/migrate_sys_number_seq.sql` 을 **개발 DB 와 로컬 DB 에 적용했습니다**(10-26, 개발 DB 프로시저 145개). 스키마 파일 재생성은 아직 하지 않았습니다. 신 POP 배포가 남았습니다(배포 전 출력 실패 되돌리기가 프로시저 없이 실패하면 수동 재출력으로 복구). 검증 대기: 시나리오 `tools/test_lot_flow/시나리오.md` S2~S5(DB 장애 재현)는 아직 실행하지 않았습니다(로컬 전용 도구 문서).
- **테스트(10-26 23시, 이 커밋 Debug 빌드, Data 는 로컬 DB = 개발 DB 복제본)**: Pop 263(10-26 출력 폴더 테스트 4개·라벨 워터마크 테스트 2개·카운트 선행·발행 원장·번호 이어쓰기 테스트 포함), Data DB `DecrementPrintedCount`·`LotNoCursorDbTests`(`EnsureAtLeast`) 로컬 복제본에서 통과, Api 244(건너뜀 2 — 안영훈 PDA 로그인 테스트 포함), InjAgent 54 통과. Data 1,680건 중 1,650 통과·11 실패·19 건너뜀 — 실패는 심재현 보류 「라우팅 B·시드 변경」 10건(PlanSchedule 5·WorkOrderRepository 4·InjLot 금형 1)과 같은 보류에 적은 데이터 가정 1건(`MoldPlanningTests.LineLastMoldBefore_…`)입니다. 전체 실행 시간대의 교착 기록(로컬 `system_health` `xml_deadlock_report`)은 0건입니다(채번 카운터 전에는 같은 실행에서 5건). WH·FG·포탈 다국어화로 영문 리터럴이 키로 바뀐 안영훈 소스 내용 테스트 6건(`WhPdaTestScreenTests`·`LineReplenishmentRulesTests`·`LocationStorageTests` 3·`ScmShippingUnitUiTests`)은 찾는 문자열을 새 키로 맞췄습니다(확인하는 기능은 그대로). Web·Api·Pop Release 빌드 오류 0. InjAgent `PollerRunnerTests.Stop_timeout_keeps_running_flag_until_loop_exits` 는 타이밍에 기대어 가끔 1건 실패합니다(다시 돌리면 통과 — 심재현 문제점 23 과 같은 정지 경합).
- **두 DB 구조(10-26)**: `dist/migrate_sys_number_seq.sql` 을 개발·로컬 DB 에 적용했습니다(표 `SYS_NumberSeq` + 카운터 3행 FAIL·MWO·PM, 프로시저 `SP_SYS_NumberSeq_Next`, POP 안돈 고장 등록 프로시저 `SP_POP_ANDON_CallDepts_InsMNT_FailureRegister` 본문 교체 — 다시 돌려 변경 0, 두 DB 의 표 컬럼·두 프로시저 본문 같음). **POP 담당(심재현·김현진) 공유**: 그 안돈 프로시저는 이제 고장번호를 `SP_SYS_NumberSeq_Next` 로 받습니다(정본 `migrate_pop_stored_procedures.sql` 의 같은 프로시저도 같은 본문으로 고침 — 다시 적용해도 되돌아가지 않음). 나머지는 아래 10-24 상태 그대로입니다.
- **두 DB 구조(10-24)**: 발송기 마이그레이션 `migrate_notification_push_sender.sql` 을 개발 DB 에도 적용했습니다(공통코드 `SW_NOTIFY` 4행·인덱스 5개, 채널 1행·발송 이력 40행의 사용자 ID → 사번 변환, 다시 돌려 변경 0 확인). 로컬·개발 DB 의 컬럼 서명(180 테이블)·인덱스(시스템이 붙인 이름 제외)·프로시저 192개 본문(공백 무시)이 같습니다. 데이터는 아래 10-23 기록과 같은 테이블(에이전트·Worker 가 쓰는 상태 로그·OEE·모니터)만 다르고, 로컬 IIS Api 의 발송기가 개발 DB 에 `NOTIFY-PUSH` 모니터 행(OUTBOUND, FCM 키가 없어 ERROR — 설계대로)을 씁니다. 03시: 원격 `migrate_pop_stored_procedures.sql`(안돈 발동 라인 잠금 `SP_POP_ANDON_Raise`·라벨 선점 순서 `SP_POP_INJ_ClaimForPrint`)은 작성자가 개발 DB 에 적용해 두었고 로컬 DB 에도 적용했습니다(재실행 안전) — 04시에 로컬의 두 프로시저를 개발 DB 정의 그대로 다시 만들어, 두 DB 의 테이블·컬럼 순서·인덱스·CHECK·외래키·프로시저 192개(공백 포함)·시퀀스가 완전히 같습니다(`schema_compare.ps1` IDENTICAL; 컬럼 설명은 개발 DB 에만 SSMS 다이어그램 표시 8건). 05시: 옛 PDA 업무 프로시저 27개는 개발 DB 에서 이미 없음을 확인하고(10-23 안영훈 삭제) 로컬 DB 에서도 지웠습니다 — `retire_pda_business_procedures.sql` 은 로컬 정의가 줄 끝 공백만 달라 해시 검사에서 멈춰(아무것도 안 지움), 같은 목록·같은 의존성 검사로 로컬 전용 스크립트를 돌렸습니다(로컬 정의 백업 `AppData\Local\AMES-claude-tools\backup_local_pda_procs_20261024.sql`). 두 DB 는 다시 `schema_compare.ps1` IDENTICAL 입니다. `WH_ReleaseSchedule` 은 두 DB 모두 0건으로 남아 있습니다(`retire_wh_release_schedule.sql` 은 미리보기만 — 삭제 승인 대기).
- **두 DB 구조·데이터(10-23 기록)**: 로컬 DB 를 개발 DB 기준으로 통째로 다시 맞췄습니다(04시 sqlpackage 구조+데이터 복제 → 이름 교체, IDENTITY 다음 번호·다이어그램 객체 보정 — 이전 로컬 DB 는 `AMES_DEV_BAK_20261023`, 백업 `AMES_DEV_local_20261023_before_pnt_sync.bak`). 그 뒤 원격의 `migrate_wonumber_unique.sql` 을 로컬에도 적용해 179 테이블의 컬럼·인덱스·CHECK·외래키·프로시저 정의가 **같습니다**(`schema_compare.ps1` IDENTICAL — `SP_POP_*` 144개는 저장소 `migrate_pop_stored_procedures.sql` 과 본문 일치). 데이터는 개발서버 에이전트 작업·로컬 IIS Api Worker 가 계속 쓰는 `PP_LineStateLog`·`PP_LineOEE`·`SYS_InterfaceMonitor` 만 다르고, 10-23 사용자 지시로 지운 6월 시연 상태 로그 720건, BOM 13줄 단위 수정, 나사 `12490-04101`·레진 `21343A0-PIDHANNB` 품목 단위를 정본 EA·G 로 되돌린 것(품목 2행 + 그 품목을 쓰는 BOM 12줄, 수정자 `UOMFIX-1023`, 수량 그대로, 백업 `AppData\Local\AMES-claude-tools\backup_item_uom_revert_20261023`)은 두 DB 에 같이 적용했습니다 — 지금 BOM 라인 단위와 구성품 단위가 다른 줄은 0 입니다. 설비 OEE 자동 채움(`migrate_mnt_oeelog_fill.sql` — 프로시저 2개·유니크 인덱스, 시연 `MNT_OEELog` 56행 삭제, 백업 `AppData\Local\AMES-claude-tools\backup_mnt_oeelog_demo_20261023`)도 두 DB 에 적용해 구조(컬럼 서명·프로시저 192개·인덱스 수)가 같고, `MNT_OEELog` 는 개발서버 작업이 계속 쓰므로 데이터가 다릅니다(10-20~10-23 백필 개발 99행·로컬 72행). 원격 `young` 의 `dist/` 변경(스키마 파일·`rebuild_db.sh`·`migrate_scm_portal_*` 2개)은 개발 DB 에 맞춘 스크립트 정리라 두 DB 에 다시 적용할 것이 없습니다(SCM·포탈 화면 등록 두 DB 동일 확인).
- **스키마 파일 ↔ 두 DB(10-26, `tools/schema_from_db` 로 개발 DB 에서 다시 뜸)**: 테이블 179개(새 `SYS_NumberSeq` 포함)·프로시저 159개·시퀀스 1개 — 직전 판과의 차이는 새 표·`SP_SYS_NumberSeq_Next`·POP 안돈 고장 등록 프로시저 본문뿐입니다. 안영훈이 생성기에서 뺀 `WH_ReleaseSchedule`(삭제 승인 대기, 두 DB 에는 아직 있음)은 파일에 없습니다. 빈 DB 에 파일을 돌려 전체를 대조하는 확인은 이번에 하지 않았습니다(10-24 대조 이후 바뀐 것은 위 세 객체뿐).
- **게시(10-26 23:20)**: 로컬 IIS Web·Api 를 이 커밋으로 올렸습니다(health 200). 개발서버용 `publish/AMES.Web_20261026-231842.zip`·`AMES.Api_20261026-232007.zip` — Web 은 SCM·WH·FG·포탈 다국어화·조회부 콤보와 목록 열 폭(3언어 실측 잘림 0)·MD 조회부 콤보 폭·PP-OEE 상단 페이저·PP-DTL·PP-ODM 재작업 라인 제외, Web·Api 모두 MNT 채번 카운터(개발 DB 마이그레이션은 적용 완료 — 개발서버에 따로 돌릴 것은 없음), Api 는 10-26 앞 판의 알림 방해 금지·상세 원천 정보도 들어 있습니다. FCM 키는 오세영 4-1 1 「Firebase」. POP(오프라인 동작·PNT-MAIN·팔레트 적재)·PDA(사번 목록·PIN 키패드)는 장비에 따로 배포합니다.
- **young 병합 시 정정**: 위 10-24 전수 검사·DB 동기화·게시 내용은 해당 담당자가 남긴 당시 기준선을 보존한 것입니다. young의 반품 검사·ID 목록/PIN 키패드·fixture 수정은 별도 후속 변경이며 이 병합에서 서버 배포·DB 동기화·재구축을 실행하지 않았습니다. 옛 PDA 프로시저의 10-23 삭제 기록과 10-24 DB 비교 기록은 시점이 다르므로 현재 DB 잔존 여부는 재확인 대상입니다. 스키마 파일은 최신 시퀀스/프로시저 정의를 유지하고 WH_ReleaseSchedule만 제외했습니다.
- **헤드리스 확인 방식**: 로컬 IIS·개발 DB 로 화면을 열 때 admin 로그인 기록(LastLoginTS·ModifiedTS·ModifiedBy·FailedLoginCount)과 `SYS_AuditLog` 최대 LogID 를 먼저 읽고 끝나면 되돌립니다. 검증 도구는 `AppData\Local\AMES-claude-tools\`.
- **NuGet(10-15)**: 취약 패키지 0(`dotnet list package --vulnerable --include-transitive`), 모든 패키지가 최신 안정 버전입니다(SqlClient 7.1.1, EF Core 10.0.13, Radzen 12.1.2, Test.Sdk 18.11.0, xunit 러너 4.0.1 등). 취약성 때문에 `System.Security.Cryptography.Pkcs` 를 AMES.Data 에서 10.0.13 으로 고정했으니 SqlClient 를 올릴 때 다시 확인합니다.
- **제품 코드 규칙**: `AddWithValue` 0곳(주석 3곳만), Web 의 `DateTime.Now/Today` 0곳, 라이트 테마 토큰화 대상 0.
- **두 번째 병합의 WH·FG·SCM 변경 검증(안영훈)**: API 74개 통과, 용량·위치 규칙 테스트 17개 통과, Web·PDA Android 빌드 성공. 실제 입출고·동시 PDA 부하 검증과 DB 전체 재구축은 실행하지 않았습니다.
- 항목을 해결하면 같은 커밋에서 이 문서의 해당 항목을 지웁니다(해결 기록은 남기지 않고 커밋 이력으로 봅니다).
