# SAP 연동 — 비가동 시간 데이터 정의 (11-23)

SAP(웅진) 요청: 설비별·일별 비가동, **정규 일정 정지와 실제 비가동을 합쳐서**. EOS_IF 로 AMES 가 밀어넣는다(테이블은 웅진이 AMES 뷰 정의에 맞춰 만든다).
설비 = 라인(`MD_Line.LineID`, 라인 1 : 설비 1 : 스테이션 1). 정본 스펙은 `SAP_IF_비가동_DB스펙.xlsx`.

## 뷰 `V_SAP_Downtime` (`dist/migrate_sap_if_downtime_views.sql`, 운영 AMES 적용)
한 행 = 설비의 정지 구간 하나(시작·종료 시각). 자정을 넘으면 날짜별로 나눈다 → 설비·일별 합계는 `SUM(DurationMin) GROUP BY EquipCode, WorkDate`.

| 컬럼 | 설명 |
|---|---|
| EventKey | 고유키 `{D/P}{원본키}-{yyMMdd}` |
| EquipCode / WorkDate | 설비(라인) 코드 / 조각이 속한 날 |
| StartTS / EndTS / DurationMin | 시작·종료·분 |
| StopType | `ACTUAL`(PP_LineDowntimeLog: 안돈·보전 라인정지·수동) / `PLAN`(PP_LineStateLog PLAN-STOP 연속 분: 휴게·식사·교대 사이·PM) |
| ReasonCode / CauseCode / MES_WO_NO / Comment | ACTUAL 만 채움(PLAN 은 ReasonCode=`SCHEDULE`) |

## 제약
- 끝난 구간만 나온다. 정규 일정 원본 `PP_LineStateLog` 는 7일 보관 — 그 안에 송신.
- 실제 비가동이 정규 일정과 겹치면 두 행 모두 나온다. 사람이 등록하지 않은 정지(미등록)는 넣지 않았다.
- AMES 에는 DB 트리거를 두지 않는다 — 송신은 아웃박스 수집기로.

## 남은 일
웅진이 EOS_IF 테이블(IF 번호·컬럼)을 만들면 AMES 송신 수집기(아웃박스) 구현.
