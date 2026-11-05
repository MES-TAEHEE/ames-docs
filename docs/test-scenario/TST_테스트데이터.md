# TST 테스트 데이터 — 입력값 정본

단위·통합 시나리오(`docs/test-scenario/*.js`)가 쓰는 `TST` 시험 데이터의 화면별 입력값과 등록 상태를 정리한 문서다.
시나리오 화면이 읽는 값은 `testdata.js` 의 `TD_COMMON`·`TD_GROUP` 이고, 이 문서는 그 값을 화면 칸 단위로 풀어 쓴 것이다. 값을 바꾸면 **두 곳을 같이** 고친다.

- 대상 DB: 개발·운영 공용 `172.29.3.21\MES` / `AMES` (11-04 기준)
- 실제 고객 정보·실제 사번은 넣지 않는다. 고객은 기존 `CUS-SAV` 를 그대로 쓴다.
- 등록 상태 열은 **2026-11-04 DB 조회 결과**다. 이후 바뀌었으면 아래 「확인 SQL」로 다시 본다.

## 등록 순서

라인 → 스테이션 → 품목 → 금형(제품 정보·라인 UPH) → BOP → BOM(초안 → 승인) → 설비 → 작업자·슈퍼바이저 → 협력업체 → 팔레트 → 수주·일별 계획.
금형 제품 정보는 품목이, BOP 는 스테이션과 품목이 먼저 있어야 고를 수 있다.

## 라인 (MD-001)

| 라인 | 이름 | 작업장 | LOT 접두어 | 스테이션 | 상태 |
|---|---|---|---|---|---|
| LINE-INJ-TST | 사출 TEST | WC-INJ | **IT** | ST-INJ-TST | 등록됨 (스테이션 없음) |
| LINE-IMG-TST | 감싸기 Test | WC-IMG | **WT** | ST-IMG-TST | 등록됨 (스테이션 없음) |
| LINE-PNT-TST | 도장 Test | WC-PNT | **PT** | ST-PNT-TST | 등록됨 (스테이션 없음) |
| LINE-RWK-01 | (기존 재작업 라인) | WC-RWK | 없음 | — | 기존 |

처음 문서의 접두어 I1·W1·P1 은 실제 등록값 IT·WT·PT 로 바꿨다. 라인 재고 위치 이름도 `TST-LINE-IT-01` 이다.

## 품목 (MD-003)

공통: 단가·최소재고·추가 불출율·MRP 여유율 0, 도면번호 비움, TOTE·개별 바코드 스캔 해제, 활성 켬.

| 칸 | TST-A-001 | TST-B-001 | TST-CORE-A | TST-A-001-LH | TST-A-001-RH | TST-RESIN |
|---|---|---|---|---|---|---|
| 용도 | IMG 완제품 | 도장 완제품 | 사출 코어 | 사출 LH (형제) | 사출 RH (형제) | 원자재 |
| 품명 | 테스트 완제품 A | 테스트 도장 완제품 B | CORE-TEST A | CORE-TEST A LH | CORE-TEST A RH | 테스트 레진 |
| 단위 | EA | EA | EA | EA | EA | **KG** |
| 품목 유형 | ASSY | ASSY | SUB | SUB | SUB | MATERIAL |
| 카테고리 | DOOR | DOOR | SUB | SUB | SUB | RESIN |
| 차종 | TST | TST | TST | TST | TST | 비움 |
| 조달형태 | INHOUSE | INHOUSE | INHOUSE | INHOUSE | INHOUSE | PURCHASE |
| 경로 | **A** (INJ → IMG) | **B** (INJ → PNT) | 비움 | 비움 | 비움 | 비움 |
| 사출품 | 해제 | 해제 | **체크** | **체크** | **체크** | 해제 |
| 적입수량 | 24 | 24 | 24 | 24 | 24 | 비움 |
| 최대 적재 | 4 | 4 | 비움 | 비움 | 비움 | 비움 |
| 안전재고 | 100 | 0 | 0 | 0 | 0 | 200 |
| 최대재고 | 0 | 0 | 0 | 0 | 0 | 500 |
| PGN / ALC | TST / A | TST / B | 비움 | 비움 | 비움 | 비움 |
| 상태 | 등록됨 (품명이 `TST-A-001` 로 들어가 있음) | 등록됨 | 등록됨 | 등록됨 | 등록됨 | 등록됨 |

- 안전재고 0 은 화면에서 **빈칸(NULL)** 으로 저장된다. 목록에서도 0 으로 보인다. 문서의 「0」은 모두 빈칸(NULL)을 뜻한다. TST-B-001 은 저장 확인으로 NULL 이 맞다(11-05).
- 사출품 체크는 유형 SUB 만 가능하다. 꺼져 있으면 BOM 에서 코어로 잡히지 않아 PP-003·APS 가 「코어 없음」으로 거부한다.
- 코어 품명은 `CORE` 로 시작한다 — 사출품이 여럿인 BOM 에서 이 이름으로 코어를 고른다.
- LH·RH 는 품목 두 개로 따로 등록한다(품번 하나에 `LH·RH` 로 넣지 않는다). BOM 부모가 없으며 팔레트 형제 적재·금형 형제 규칙 시험용이다.
- 사출품의 적입수량은 APS 포장 단위다. 비우면 포장 단위 1 + 경고.

## BOM (MD-004)

| 루트 품목 (부모) | 버전 ID | 버전 | 구성품 | 시작일 | 상태 |
|---|---|---|---|---|---|
| TST-A-001 | TST-A-001 | V1.0 | TST-CORE-A 1 EA | 2026-11-05 | 승인 (11-05 등록) |
| TST-B-001 | TST-B-001-V1 | V1.0 | TST-CORE-A 1 EA | 2026-11-05 | 승인 (11-05 등록) |
| TST-CORE-A | TST-B-001 | V1.0 | TST-RESIN 0.5 KG | 2026-11-05 | 승인 (코어 레진 BOM, 11-05 등록) |

- 버전 ID 는 버전마다 고유해야 한다. 코어 레진 BOM 이 `TST-B-001` 을 쓰고 있어 완제품 TST-B-001 BOM 은 `TST-B-001-V1` 로 등록했다. 이 이름의 차이 때문에 혼동하지 않도록 버전 ID 를 항상 같이 적는다.
- 승인된 버전은 수정할 수 없다. 바꿀 때는 새 버전을 만든다.
- 루트 품목은 완제품(TST-A-001, TST-B-001)이다. 코어 레진 BOM 의 루트는 TST-CORE-A 다.
- 라인 단위는 구성품 기본 단위로 자동 저장된다.

## 금형 (MD-007)

| 칸 | TST-M-CORE | TST-M-A |
|---|---|---|
| 금형명 | 테스트 코어 금형 | 테스트 LH/RH 금형 A |
| 캐비티수 | 1 | 2 |
| 톤수 | 150 | 150 |
| 누적·현재 Shot | 0 | 0 |
| 보관 위치 | WH A-3 | WH A-4 |
| 마지막 정비 | 비움 | 비움 |
| 상태 | AVAILABLE | AVAILABLE |
| 교체시간(분) | 30 | 30 |
| 차종 | TST | TST |
| 참조코드 · 조립사출실적 | 비움 · 해제 | 비움 · 해제 |
| 제품 정보 (품번 · 색상) | TST-CORE-A · BK | TST-A-001-LH · BK / TST-A-001-RH · BK |
| 라인 배정 · UPH | LINE-INJ-TST · 120 | LINE-INJ-TST · 120 (품번당 시간당 60) |
| 상태 | 등록됨 — **제품 정보·라인 미등록** | 등록됨 — **제품 정보·라인 미등록** |

- 금형 ID 에 품번(TST-CORE-A)을 넣지 않는다. 품번은 저장 뒤 제품 정보에서 연결한다.
- LH·RH 는 같은 색상이어야 한 샷에 같이 나오는 형제로 묶인다.
- **보관 위치는 영문으로 넣는다.** 이 칸은 한글을 담지 못하는 형식이다(varchar). 11-04 에 한글로 넣어 `?? A-3` 으로 깨졌던 값은 11-05 에 `WH A-3`, `WH A-4` 로 고쳐 저장했다(DB 확인 완료).

## 스테이션 · BOP (MD-005)

| 스테이션 | 라인 | BOP 품번 | 표준 사이클 | 상태 |
|---|---|---|---|---|
| ST-INJ-TST | LINE-INJ-TST | TST-CORE-A · TST-A-001-LH · TST-A-001-RH | 30초 | 미등록 |
| ST-IMG-TST | LINE-IMG-TST | TST-A-001 | 60초 | 미등록 |
| ST-PNT-TST | LINE-PNT-TST | TST-B-001 | 60초 | 미등록 |

스테이션이 없으면 POP 로그인 자체가 안 되고, BOP 가 없으면 INJ/IMG/PNT-MAIN 좌측 품번 목록이 빈다.

## 설비 · 보전

| 대상 | 화면 | 값 | 상태 |
|---|---|---|---|
| 설비 INJ-TST-01 | MD-014 | 유형 INJ · 톤수 150 · 라인 LINE-INJ-TST | 미등록 |
| 예비품 TST-SP-01 | MD-026 | 분류 K · 적용 설비 9 · 초기 재고 5 · 재주문점 2 (번호 자동 발행) | 확인 안 함 |
| PM 템플릿 PM-TST-INJ | MD-027 | 설비 유형 INJ · ① 노즐 청소(TST-SP-01 1 EA) ② 히터 점검 | 확인 안 함 |

## 사람 · 업체 · 팔레트

| 대상 | 값 | 상태 |
|---|---|---|
| 작업자 W001 | PIN 1234 · 사원증 `EOS*W001*이름` | DB 에 있음 — 이름 `Jang Dae-ho`, PIN 있음 |
| 작업자 W002 | PIN 없음 (PIN 강제 설정 시험용) | DB 에 있음 — **PIN 이 이미 있음** → PIN 없는 작업자가 따로 필요 |
| 웹 계정 TST-SUP01 | Supervisor 역할 · LINE-INJ-TST 슈퍼바이저(MD-033) | 슈퍼바이저 미등록 |
| 협력업체 TST-VEN-01 / TST-VEN-02 | MD-009 · 외부 사용자 `tst-vendor01@test.local`(SCM-004) | 미등록 |
| 팔레트 EOS-PLT-2611-0001 ~ 0005 | MD-034 발행, 0005 는 외부용·CUS-SAV 전용 | 미발행 (번호는 발행 순서대로 붙는다) |

## 수주 · 계획

| 대상 | 값 |
|---|---|
| 수주 SO-TST-001 (2줄) | TST-A-001 · 120 EA씩 · 납기 2026-11-10 / 2026-11-12 · CUS-SAV |
| 일별 구매계획 | TST-A-001 · 2026-11-11 · 40 EA · CUS-SAV |
| 비가동 시험 (PP-DTL) | LINE-INJ-TST · 2026-11-05 10:00–10:30 · 사유 MACHINE · 원인 비고 필수 사유 TST-REASON |

그 밖의 그룹별 값(MRP·입고·QC·출하·포탈 등)은 `testdata.js` 의 `TD_GROUP` 이 정본이다.

## 확인 SQL

```sql
SELECT ItemNo, ItemName, ItemType, ItemCategory, DefaultUOM, InjFlag, RoutingType, CarType, ProcureType,
       PalletQty, MaxPalletQty, SafetyStock, MaxStock, Pgn, Alc
FROM   dbo.MD_Item WHERE ItemNo LIKE 'TST%' ORDER BY ItemNo;
SELECT MoldID, MoldName, CavityCount, Tonnage, MoldChangeMin, StorageLoc FROM dbo.MD_Mold WHERE MoldID LIKE 'TST%';
SELECT MoldID, ItemNo, Color, ActiveFlag FROM dbo.MD_MoldItem WHERE MoldID LIKE 'TST%';
SELECT LineCode, MoldID, UPH FROM dbo.MD_MoldLine WHERE MoldID LIKE 'TST%';
SELECT v.RootItemNo, v.VersionNo, v.Status, b.CompItemNo, b.QtyPer, b.UOM
FROM   dbo.MD_BomVersion v LEFT JOIN dbo.MD_Bom b ON b.VersionID = v.VersionID WHERE v.RootItemNo LIKE 'TST%';
SELECT l.LineID, l.LotPrefix, s.StationCode FROM dbo.MD_Line l LEFT JOIN dbo.MD_Station s ON s.LineID = l.LineID WHERE l.LineID LIKE '%TST%';
SELECT StationCode, ItemNo, StdCycleTime FROM dbo.MD_Bop WHERE StationCode LIKE '%TST%';
SELECT EquipID, EquipType, LineID, Tonnage FROM dbo.MD_Equipment WHERE EquipID LIKE '%TST%';
```
