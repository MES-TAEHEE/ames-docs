# TST 테스트 데이터 — 입력값 정본

단위·통합 시나리오(`docs/test-scenario/*.js`)가 쓰는 `TST` 시험 데이터의 화면별 입력값과 등록 상태를 정리한 문서다.
시나리오 화면이 읽는 값은 `testdata.js` 의 `TD_COMMON`·`TD_GROUP` 이고, 이 문서는 그 값을 칸 단위로 풀어 쓴 것이다. 값을 바꾸면 **두 곳을 같이** 고친다.

- 대상 DB: 개발·운영 공용 `172.29.3.21\MES` / `AMES`
- 실제 고객 정보·실제 사번은 넣지 않는다. 고객은 기존 `CUS-SAV` 를 그대로 쓴다.
- 등록 상태는 **2026-11-05 DB 조회 결과**다. 이후 바뀌었으면 아래 「확인 SQL」로 다시 본다.

## 등록 현황 요약 (2026-11-05)

| 구분 | 상태 |
|---|---|
| 라인, 스테이션, 품목, BOP, BOM, 금형, 금형 제품 정보, 금형 라인 UPH | 입력 완료 |
| 설비 INJ-TST-01, 협력업체 2곳, 예비품 TST-SP-01, PM 템플릿 PM-TST-INJ | 입력 완료 |
| 팔레트 5개, 일별 구매계획, 수주 SO-TST-001 (2줄) | 입력 완료 |
| 슈퍼바이저 TST-SUP01 (LINE-INJ-TST), PIN 없는 작업자 W002 | 완료 |
| 외부 사용자 tst-vendor01 / tst-vendor02 (SCM-004) | 완료 |
| 팔레트 0001~0005 (0005 외부용 CUS-SAV 전용) | 완료 |
| 수주 SO-TST2-001-10 (TST2-B-001) | 업로드 완료, **Open** (확정은 POP 테스트 때) |
| 비가동 시험 (PP-DTL) | 완료 (11-05 10:00–10:30, MACHINE, 비고 TST-REASON) |

## 등록 순서

라인 → 스테이션 → 품목 → 금형(제품 정보·라인 UPH) → BOP → BOM(초안 → 승인) → 설비 → 작업자·슈퍼바이저 → 협력업체 → 팔레트 → 수주·일별 계획.
금형 제품 정보는 품목이, BOP 는 스테이션과 품목이 먼저 있어야 고를 수 있다.

## 라인 (MD-001)

| 라인 | 이름 | 작업장 | LOT 접두어 | 스테이션 | 상태 |
|---|---|---|---|---|---|
| LINE-INJ-TST | 사출 TEST | WC-INJ | **IT** | ST-INJ-TST | 등록됨 |
| LINE-IMG-TST | 감싸기 Test | WC-IMG | **WT** | ST-IMG-TST | 등록됨 |
| LINE-PNT-TST | 도장 Test | WC-PNT | **PT** | ST-PNT-TST | 등록됨 |
| LINE-RWK-01 | (기존 재작업 라인) | WC-RWK | 없음 | — | 기존 |

라인 재고 위치 이름은 `TST-LINE-IT-01` 이다.

## 스테이션 (MD-002)

| 스테이션 | 라인 | 순서 | 상태 |
|---|---|---|---|
| ST-INJ-TST | LINE-INJ-TST | 1 | 등록됨 |
| ST-IMG-TST | LINE-IMG-TST | 1 | 등록됨 |
| ST-PNT-TST | LINE-PNT-TST | 1 | 등록됨 |

## 품목 (MD-003)

공통: 단가·최소재고·추가 불출율·MRP 여유율 0, 도면번호 비움, TOTE·개별 바코드 스캔 해제, 활성 켬.

| 칸 | TST-A-001 | TST2-B-001 | TST-CORE-A | TST2-CORE-B | TST-A-001-LH | TST-A-001-RH | TST-RESIN |
|---|---|---|---|---|---|---|---|
| 용도 | 완제품 (경로 A, IMG) | 완제품 (경로 B, PNT) | 사출 코어 (경로 A) | 사출 코어 (경로 B) | 사출 LH (형제) | 사출 RH (형제) | 원자재 |
| 품명 | 테스트 완제품 A | 테스트 도장 완제품 B | CORE-TEST A | CORE-TEST B | CORE-TEST A LH | CORE-TEST A RH | 테스트 레진 |
| 단위 | EA | EA | EA | EA | EA | EA | **KG** |
| 품목 유형 | ASSY | ASSY | SUB | SUB | SUB | SUB | MATERIAL |
| 카테고리 | DOOR | DOOR | SUB | SUB | SUB | SUB | RESIN |
| 차종 | TST | TST | TST | TST | TST | TST | 비움 |
| 조달형태 | INHOUSE | INHOUSE | INHOUSE | INHOUSE | INHOUSE | INHOUSE | PURCHASE |
| 경로 | **A** | **B** | **A** | **B** | **A** | **A** | 비움 |
| 사출품 | 해제 | 해제 | **체크** | **체크** | **체크** | **체크** | 해제 |
| 적입수량 | 24 | 24 | 24 | 24 | 24 | 24 | 비움 |
| 최대 적재 | 4 | 4 | 비움 | 비움 | 비움 | 비움 | 비움 |
| 안전재고 | 100 | 빈칸 | 빈칸 | 빈칸 | 빈칸 | 빈칸 | 200 |
| 최대재고 | 0 | 0 | 0 | 0 | 0 | 0 | 500 |
| PGN / ALC | TST / A | TST / B | 비움 | 비움 | 비움 | 비움 | 비움 |
| 상태 | 등록됨 | 등록됨 | 등록됨 | 등록됨 | 등록됨 | 등록됨 | 등록됨 |

- **TST-B-001 은 삭제되었다.** 완제품 도장 품목은 TST2-B-001 로 다시 만들었다. 기존 TST-B-001 은 승인된 BOM 때문에 화면에서 지울 수 없어서 DB 에서 직접 지웠다(11-05).
- 시나리오·테스트 데이터의 완제품 B 는 모두 `TST2-B-001` 로 바꿨다. 단, 코어 레진 BOM 의 버전 ID `TST-B-001` 은 그대로 둔다(완제품과 무관한 이름).
- 경로 B 의 사출 코어는 TST2-CORE-B 다. 경로 A 의 TST-CORE-A 와 같은 품번을 쓰지 않는 이유는 BOP 가 품목의 경로를 따르기 때문이다.
- 안전재고 0 은 화면에서 **빈칸(NULL)** 으로 저장된다. 목록에서는 0 으로 보인다. 문서의 「빈칸」은 0 과 같은 뜻이다.
- 사출품 체크는 유형 SUB 만 가능하다. 꺼져 있으면 BOM 에서 코어로 잡히지 않아 PP-003·APS 가 「코어 없음」으로 거부한다.
- 코어 품명은 `CORE` 로 시작한다. 사출품이 여럿인 BOM 에서 이 이름으로 코어를 고른다.
- LH·RH 는 품목 두 개로 따로 등록한다(품번 하나에 `LH·RH` 로 넣지 않는다).
- 사출품의 적입수량은 APS 포장 단위다. 비우면 포장 단위 1 + 경고.

## BOM (MD-004)

| 루트 품목 (부모) | 버전 ID | 버전 | 구성품 | 시작일 | 상태 |
|---|---|---|---|---|---|
| TST-A-001 | TST-A-001 | V1.0 | TST-CORE-A 1 EA | 2026-11-05 | 승인 |
| TST2-B-001 | TST2-B-001-V1 | V1.0 | TST2-CORE-B 1 EA | 2026-11-05 | 승인 |
| TST-CORE-A | TST-B-001 | V1.0 | TST-RESIN 0.5 KG | 2026-11-05 | 승인 (코어 레진) |
| TST2-CORE-B | TST2-CORE-B-V1 | V1.0 | TST-RESIN 0.5 KG | 2026-11-05 | 승인 (코어 레진) |

- 버전 ID 는 버전마다 고유해야 한다. 코어 레진 BOM 의 버전 ID `TST-B-001` 은 **완제품 TST-B-001 과 무관한 이름**이다. 완제품 BOM 은 버전 ID 를 `TST2-B-001-V1` 처럼 다른 값으로 만든다.
- 승인된 버전은 수정할 수 없다. 바꿀 때는 새 버전을 만든다.
- 루트 품목은 완제품(TST-A-001, TST2-B-001)이다. 코어 레진 BOM 의 루트는 코어(TST-CORE-A, TST2-CORE-B)다.
- 레진은 코어 BOM 에만 둔다. 완제품 BOM 에는 레진을 넣지 않는다(이중 차감 방지).

## 금형 (MD-007)

| 칸 | TST-M-CORE | TST-M-A | TST2-M-B |
|---|---|---|---|
| 금형명 | 테스트 코어 금형 | 테스트 LH/RH 금형 A | 테스트 B 코어 금형 |
| 캐비티수 | 1 | 2 | 1 |
| 톤수 | 150 | 150 | 150 |
| 보관 위치 | WH A-3 | WH A-4 | WH A-5 |
| 교체시간(분) | 30 | 30 | 30 |
| 상태 | AVAILABLE | AVAILABLE | AVAILABLE |
| 차종 | TST | TST | TST |
| 제품 정보 (품번 · 색상) | TST-CORE-A · BK | TST-A-001-LH · BK / TST-A-001-RH · BK | TST2-CORE-B · BK |
| 라인 배정 · UPH | LINE-INJ-TST · 120 | LINE-INJ-TST · 120 | LINE-INJ-TST · 120 |
| 수지 품목 | 비움 | 비움 | 비움 |

- 금형 ID 에 품번을 넣지 않는다. 품번은 저장 뒤 「금형별 제품 관리」 탭에서 연결한다.
- 「금형별 제품 관리」 목록의 품목 번호 칸에는 원자재도 보인다. 금형에는 **사출품(SUB, 사출품 체크)만** 고른다.
- 수지 품목 칸은 기록용이며 계획·차감에 쓰이지 않는다. 비워 둔다.
- LH·RH 는 같은 색상(BK)이어야 한 샷에 같이 나오는 형제로 묶인다.
- 보관 위치는 영문으로 넣는다(varchar, 한글 불가). 11-04 에 한글로 넣어 `?? A-3` 으로 깨졌던 값은 11-05 에 `WH A-3`, `WH A-4` 로 고쳐 저장했다.

## 스테이션 · BOP (MD-005)

| 스테이션 | 품목 | 경로 | 단계 | 표준 사이클 | 상태 |
|---|---|---|---|---|---|
| ST-INJ-TST | TST-CORE-A | A | 1 (INJ) | 30초 | 등록됨 |
| ST-INJ-TST | TST-A-001-LH | A | 1 (INJ) | 30초 | 등록됨 |
| ST-INJ-TST | TST-A-001-RH | A | 1 (INJ) | 30초 | 등록됨 |
| ST-IMG-TST | TST-A-001 | A | 2 (IMG) | 60초 | 등록됨 |
| ST-INJ-TST | TST2-CORE-B | B | 1 (INJ) | 30초 | 등록됨 |
| ST-PNT-TST | TST2-B-001 | B | 2 (PNT) | 60초 | 등록됨 |

- BOP 의 경로와 품목은 품목 정보에서 자동으로 가져오며, 화면에서 고칠 수 없다.
- 단계 번호는 품목 안에서의 순서 값이다. 같은 스테이션에서 여러 품목이 같은 번호를 쓰는 것은 정상이다.
- 스테이션이 없으면 POP 로그인이 안 되고, BOP 가 없으면 INJ/IMG/PNT-MAIN 좌측 품번 목록이 빈다.

## 설비 · 보전 · 예비품 (MD-014, MD-026, MD-027)

| 대상 | 값 | 상태 |
|---|---|---|
| 설비 INJ-TST-01 | 유형 INJ · 톤수 150 · 라인 LINE-INJ-TST | 등록됨 |
| 예비품 TST-SP-01 | 자동 번호 **EOS-SP-K9-260005** · 분류 K · 적용 설비 9 · 단위 EA · 초기 재고 5 · 재주문점 2 | 등록됨 |
| PM 템플릿 PM-TST-INJ | 설비 유형 INJ · 표준 소요 50분 · 사용 | 등록됨 |
| PM 단계 ① 노즐 청소 | 순서 10 · 30분 · 필요 부품 EOS-SP-K9-260005 · 수량 1 | 등록됨 |
| PM 단계 ② 히터 점검 | 순서 20 · 20분 · 필요 부품 없음 | 등록됨 |

- 예비품의 번호는 품번(TST-SP-01)이 아니라 **자동 번호(EOS-SP-K9-260005)** 다. PM 템플릿의 필요 부품도 이 번호로 저장된다.

## 사람 · 업체 · 팔레트 · 수주

| 대상 | 값 | 상태 |
|---|---|---|
| 작업자 W001 | PIN 1234 · 사원증 `EOS*W001*이름` | 있음 (시드) |
| 작업자 W002 | **PIN 없음** (PIN 강제 설정 시험용) | 완료 (PIN 제거) |
| 웹 계정 TST-SUP01 | Supervisor 역할 (ROLE-SUPERVISOR) · 활성 | 완료 |
| 슈퍼바이저 | LINE-INJ-TST ← TST-SUP01 (MD-033) | 등록됨 |
| 웹 계정 TST-VIEW01 | 역할 없음 · 활성 (조회 권한 시험용) | 있음 |
| 협력업체 TST-VEN-01 | 거래선 ID VD1F5257553064E · MATERIAL · 활성 | 등록됨 |
| 협력업체 TST-VEN-02 | 거래선 ID V4CB69F2812C64A · MATERIAL · 활성 | 등록됨 |
| 팔레트 EOS-PLT-2611-0001 ~ 0005 | MD-034 발행 (5건), 모두 외부용 | 등록됨 · 0005 는 CUS-SAV 전용, 0001~0004 는 고객 비움 |
| 고객 CUS-SAV | SRM 업체 코드 **310471** (수주·일별 계획 업로드 시 맞춰야 함) | 있음 |
| 외부 사용자 tst-vendor01@test.local | 협력업체 TST-VEN-01 · 활성 · 잠금 아님 (SCM-004) | 등록됨 |
| 외부 사용자 tst-vendor02@test.local | 협력업체 TST-VEN-02 · 활성 · 잠금 아님 (SCM-004) | 등록됨 |

- 협력업체 ID 는 자동 발행이므로 `TST` 로 시작하지 않는다. 찾을 때는 거래선명으로 찾는다.
- 수주·일별 계획 업로드 파일의 업체 코드는 310471 이어야 한다. 다른 값이면 「업체 코드 불일치」로 막힌다.

## 수주 · 계획 (PP-001, PP-002)

| 대상 | 값 | 상태 |
|---|---|---|
| 수주 SO-TST-001 -10 | TST-A-001 · 120 EA · 납기 2026-11-10 · CUS-SAV | **Confirmed** |
| 수주 SO-TST-001 -20 | TST-A-001 · 120 EA · 납기 2026-11-12 · CUS-SAV | **Confirmed** |
| 수주 SO-TST2-001 -10 | TST2-B-001 · 120 EA · 납기 2026-11-12 · CUS-SAV | **Open** (확정은 POP 테스트 때) |
| 일별 구매계획 | TST-A-001 · 2026-11-11 · 40 EA · 포장 24 · CUS-SAV | 업로드 완료 |
| 비가동 시험 (PP-DTL) | LINE-INJ-TST · 2026-11-05 10:00–10:30 · 사유 MACHINE · 원인 비움 · 비고 TST-REASON | 완료 |

- 업로드 파일에 없는 기존 수주는 「파일에 없는 수주 취소」 체크박스를 켜면 취소된다. **TST2 업로드 때는 체크하지 않는다**(SO-TST-001 이 취소되는 것을 막기 위해).
- 비가동 원인 코드는 MD-025 사유 코드(DOWNTIME)에서 고른다. 현재 등록된 원인 코드는 DT-001 한 개뿐이므로, 원인은 비워 둔다(선택 항목). 사유 코드를 늘리려면 MD-025 에서 추가한다.

그 밖의 그룹별 값(MRP·입고·QC·출하·포탈 등)은 `testdata.js` 의 `TD_GROUP` 이 정본이다.

## 확인 SQL

```sql
SELECT ItemNo, ItemName, ItemType, InjFlag, RoutingType, PalletQty, MaxPalletQty, SafetyStock
FROM   dbo.MD_Item WHERE ItemNo LIKE 'TST%' ORDER BY ItemNo;
SELECT StationCode, ItemNo, RoutingType, StepSeq, StdCycleTime FROM dbo.MD_Bop WHERE StationCode LIKE '%TST%' OR ItemNo LIKE 'TST%';
SELECT v.VersionID, v.RootItemNo, v.Status, c.CompItemNo, c.QtyPer FROM dbo.MD_BomVersion v
LEFT JOIN dbo.MD_Bom c ON c.VersionID = v.VersionID WHERE v.RootItemNo LIKE 'TST%' ORDER BY v.VersionID;
SELECT MoldID, CavityCount, Tonnage, MoldChangeMin, StorageLoc FROM dbo.MD_Mold WHERE MoldID LIKE 'TST%';
SELECT MoldID, ItemNo, Color, ActiveFlag, ResinItemNo FROM dbo.MD_MoldItem WHERE MoldID LIKE 'TST%';
SELECT MoldID, LineCode, UPH FROM dbo.MD_MoldLine WHERE MoldID LIKE 'TST%';
SELECT EquipID, EquipType, LineID, Tonnage FROM dbo.MD_Equipment WHERE EquipID LIKE '%TST%';
SELECT VendorName, VendorID, VendorType, ActiveFlag FROM dbo.MD_Vendor WHERE VendorName LIKE '%TST%';
SELECT SparePartNo, PartNo, PartName, ApplicableEquip, OnHandQty FROM dbo.MD_SparePart WHERE PartName LIKE '%TST%' OR SparePartNo LIKE 'EOS-SP-K9-26%';
SELECT PMTemplateID, TemplateName, EquipType, StdDurationMin FROM dbo.MD_PmTemplate WHERE PMTemplateID LIKE '%TST%';
SELECT PalletNo, PalletUse, CustomerID, ActiveFlag FROM dbo.MD_Pallet WHERE PalletNo LIKE 'EOS-PLT-2611%' ORDER BY PalletNo;
SELECT SoNumber, SoLineNo, ItemNo, OrderQty, RequestedDeliveryDate, Status FROM dbo.PP_CustomerOrder WHERE SoNumber LIKE 'SO-TST%' ORDER BY SoNumber, SoLineNo;
SELECT ItemNo, PlanDate, ScheduledQty, PoQty, PackQty FROM dbo.PP_DemandPlan WHERE ItemNo LIKE 'TST%';
SELECT LineID, StartTS, EndTS, DurationMin, ReasonCode, CauseCode, Comment FROM dbo.PP_LineDowntimeLog WHERE LineID = 'LINE-INJ-TST' ORDER BY StartTS;
SELECT p.EmployeeNo, r.Id AS RoleId FROM dbo.SYS_UserProfile p JOIN dbo.AspNetUserRoles ur ON ur.UserId = p.UserID JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId WHERE p.EmployeeNo = 'TST-SUP01';
SELECT EmployeeNo, CASE WHEN PinHash IS NULL OR PinHash = '' THEN 'NOPIN' ELSE 'PIN' END AS Pin FROM dbo.MD_Worker WHERE EmployeeNo IN ('W001','W002');
```
