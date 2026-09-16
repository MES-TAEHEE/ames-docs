# AMES DB 테이블별 CRUD 커버리지 분석 — 2026-09-16

## 1. 목적·방법
- **목적**: 개발 DB(AMES_DEV, 172 테이블)의 각 테이블이 솔루션 어디에서 읽기/등록/수정/삭제되는지 전수 확인하고, 처리가 빠진 부분을 데모 사이트(`AMES_Office_Web_화면참조.md`) 기능과 연결해 정리한다.
- **범위**: `src/**/*.cs`·`*.razor` 471개 파일(Web·Api·Pop·Pda·Data·InjAgent·Tablet·Tests) + PDA 프로시저 `dist/pda/PDA_SCHEMA.sql`(표에서 `PdaSql`).
- **방법**: 테이블명 기준 정규식 스캔 — 읽기 = `FROM/JOIN/APPLY 테이블`, 등록 = `INSERT INTO`, 수정 = `UPDATE`(MERGE 는 `(M:…)` 로 병기), 삭제 = `DELETE`. 스크립트는 세션 스크래치패드 `crudscan.js`.
- **한계**: 원시 SQL 문자열만 본다. EF Core 가 관리하는 Identity 6 테이블(AspNet*)은 오탐이며 제외한다. 동적으로 조립되는 테이블명은 놓칠 수 있다(스팟체크 20 테이블은 모두 일치). 행 수는 2026-09-16 개발 DB 기준.

## 2. 집계

| 분류 | 정의 | 테이블 수 |
|---|---|---|
| 전체 | R·I·U·D 모두 있음 | 62 |
| 부분 | 일부 연산만 있음 | 43 |
| 읽기만 | 읽는 코드만 있고 쓰는 코드 없음(생산자 없음) | 21 |
| 쓰기만 | 쓰는 코드만 있고 읽는 화면 없음(소비자 없음) | 16 |
| 무참조 | 어디서도 참조하지 않음 | 30 (Identity 6 포함) |

## 3. 데모(Office Web) 기능과 직결되는 공백

### 3-1. 화면은 있으나 데이터 생산자가 없어 2026-06-02 시드에 멈춘 테이블
| 테이블 | 행 | 읽는 화면 | 없는 것 | 데모 근거 |
|---|---|---|---|---|
| MNT_OEELog | 224 | MNT-003 OEE 분석 · RPT-006 · MNT-009 KPI | 일별 OEE 집계기 | MNT-03 OEE/MTBF/MTTR 추이 |
| PP_LineStateLog | 720 | PP-ODM 비가동 모니터 | 분 단위 상태 분류기(패턴×설비신호) | PP-ODM 3상태 분류 → 이벤트 병합 |
| SYS_InterfaceMonitor | 8 | SYS-006 인터페이스 모니터 | 연동 결과 기록자(SAP/PLC 연동 자체 없음) | SYS-04 |
| SYS_NotificationHistory | 40 | SYS-005 알림 · SYS-010 Health 카운트 | 발송 엔진(Rule/Channel 은 CRUD) · Send Test | SYS-06 「Send Test」 |
| MNT_WorkOrderTask | 24 | MNT-007 상세 | WO 발행 시 템플릿(MD_PmTemplateStep) 복사·항목별 결과 입력 | MNT-07 체크리스트(보류 B) |
| MNT_MoldShotCount | 4 | MNT-004 금형 | Shot 갱신·정비 등록 | MNT-04 「Update Shot Count」「Register Mold Maintenance」(보류) |
| PP_SupplyPlan / PP_SupplyPlanDetail | 3 / 9 | PP-002 가져오기 화면 | PP-002 Excel 가져오기는 `PP_CustomerOrder` 에 직접 INSERT — SupplyPlan 두 테이블은 정본이 아님 | PP-02 SAP 공급계획 |
| PP_MaterialReservation | 0 | PP-005/007 | 자재 예약 생성 | PP-07 릴리스 게이트(자재)(보류 ⑥) |
| MD_LineSupervisor | 5 | POP 안돈 검증 | 등록 화면(시드 `seed_andon_dev.sql` 의존) | 안돈 운영 전제 |
| MD_InjCondItem | 2 | InjAgent 수집 | 마스터 화면 | — |
| PNT_DailyPlan | 2 | POP PNT-01 | 계획 생성 | — |

### 3-2. 기록은 되는데 어느 화면도 보여주지 않는 테이블 (조회 메서드 + 모달만 붙이면 됨)
| 테이블 | 행 | 기록자 | 노출 후보 화면 |
|---|---|---|---|
| PP_ForecastHistory | 0 | `PpRepository` 수량 변경 시 | PP-001 이력 탭(현재는 `PP_Forecast` 배치 목록만) · 상세 모달 |
| MNT_PMExecution | 3 | `MntRepository.CompleteWoCore` | MNT-005/010 PM 상세 · MNT-007 상세 |
| MNT_FailureAction | 4 | 안돈 ARRIVED/ACK · 수리 REPAIRED | MNT-002 고장 상세 타임라인 · MNT-009 Live Failure Alerts(보류 ④) |
| PR_MoldChange | 3 | POP 금형 교체 | MNT-004 금형 상세(교체 이력) |
| PR_InjCondLog | 16 | InjAgent | 사출조건 이력 조회(화면 없음) |
| PR_EquipStatusLog | 58 | POP/Agent (INSERT 만) | MNT-001 설비카드 상태 이력 |
| PR_AndonPush / PR_PopAuthLog / PR_PopSession | 64 / 420 / 350 | POP | 감사 성격 — 노출 여부 선택 |
| FG_DayEndClose | 1 | Api `/api/fg/dayend` | FG 웹 화면 없음 |
| SYS_LotSeq | 18 | `LotNoGenerator` | 내부용, 노출 불필요 |

### 3-3. MNT-08 입고(Receive Stock) — 웹에는 없지만 PDA API 에 이미 있음
`src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs` 의 `/api/wh/sp/inbound`·`/sp/release`·`/sp/adjust/save` 가 `MNT_SparePartsTxn` INSERT + `MD_SparePart` 갱신을 처리한다(`MoveSparePart`). 웹 MNT-008 에 입고 모달을 붙일 때 `MntRepository.AdjustSparePartStock` 과 함께 이 경로를 재사용하면 스키마 변경이 없다.

## 4. 데모 밖 영역(POP·PDA)의 공백
- **PNT(도장)**: 22 테이블 중 무참조 17, 읽기만 2(`PNT_DailyPlan`·`PNT_OvenTempSample`). POP PNT 9화면은 사실상 스캐폴드.
- **QC**: `QC_Hold` 는 해제 UPDATE 만 있고 INSERT 가 없어 QC-05 홀드 등록 불가 · `QC_CAPA`·`QC_CAPA_Action`·`QC_InspectionStd` 읽기만 · `QC_Disposition`·`QC_NCR_Action` 무참조 · `QC_NCR` 등록만 있고 상태 갱신 없음.
- **WH/FG**: `WH_TransactionHistory`·`FG_PickingDetail` 은 PDA 프로시저가 읽기만(기록자 없음) · `WH_InboundPackage` INSERT 없음(WH-02 입고 스캔이 시드 의존) · `FG_ShipmentOrder` INSERT 는 `PDA_SEED.sql` 뿐(출하지시 등록 화면 없음, 웹 Fg/Shipments 는 조회) · `FG_InventoryAdjust`·`WH_ReleasePicking` 은 PDA 프로시저만.

## 5. 스키마 잔재(무참조)
`PR_BondSetupAudit`(시드 3행)·`PR_CycleAnomalyLog`·`PR_DashTileCache`·`PR_DefectAutoLink`·`PR_DefectRateCache`·`PR_PlcInterlock`·`PR_ShiftHandover` — IMG 본딩·캐시 초기 설계 잔재. `PR_BondCycleLog`·`PR_FabricDeductionLog`·`PR_FabricIssueAttempt` 는 INSERT 코드만 남고 IMG-MAIN 이 호출하지 않는다. DROP 여부 결정 대상.

## 6. 설계상 정상(조치 불필요)
append-only 로그(`SYS_AuditLog`·`PR_ProductionResult`·`PR_WoAcceptance`·`PP_PRSendLog`·`QC_InspectionItem`·`PR_RobotInspection`), 값 수정만 있는 `SYS_Config`, DELETE 없는 `MNT_WorkOrder`(CANCELED 는 보류), MERGE 패턴의 `WH_WarehouseMaster`·`WH_AreaMaster`·`WH_AreaSection`·`WH_AreaLayout`, 삭제 없는 `PP_Forecast`·`PP_LineDowntimeLog`·`PR_AndonCall`·`QC_Inspection`.

## 7. 권장 순서
1. MNT-008 웹 입고 모달 — API 경로 재사용, 스키마 변경 없음.
2. §3-2 이력 노출(PMExecution·FailureAction·MoldChange·ForecastHistory) — 조회 메서드 + 모달.
3. 생산자 없는 KPI 테이블의 방향 결정 — OEE 집계기·라인 상태 분류기·알림 발송기(기존 보류 B/D 항목과 동일 결정).
4. PP-002 정본(`PP_CustomerOrder` vs `PP_SupplyPlan`) 결정, §5 잔재 테이블 DROP 결정.

## 8. 전체 표 (테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류)
프로젝트 표기: Web·Api·Pop·Pda·Data·InjAgent·Tablet·*.Tests, `PdaSql` = PDA_SCHEMA.sql 프로시저. `(M:…)` 는 MERGE.

### MD (41)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| MD_Bom | 509 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_BomVersion | 180 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_Bop | 15 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_CodeGroup | 82 | Data | Data | Data | Data | 전체 |
| MD_CodeItem | 372 | Data + Api + InjAgent.Tests + PdaSql | Data | Data | Data | 전체 |
| MD_Customer | 7 | Data | Data | Data | Data | 전체 |
| MD_DefectCause | 5 | Data | Data | Data | Data | 전체 |
| MD_DefectCode | 18 | Data | Data | Data | Data | 전체 |
| MD_Equipment | 8 | Data | Data | Data | Data | 전체 |
| MD_InjCondItem | 2 | Data | — | — | — | 읽기만 |
| MD_InspectionStandard | 1 | Data | Data | Data | Data | 전체 |
| MD_Item | 1548 | Data + Api + Pda + InjAgent.Tests + Tablet + PdaSql | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_Jig | 6 | Data | Data | Data | Data | 전체 |
| MD_LabelTemplate | 1 | Data | Data | Data | Data | 전체 |
| MD_Line | 7 | Data | Data | Data | Data | 전체 |
| MD_LineSupervisor | 5 | Data | — | — | — | 읽기만 |
| MD_LineTimePattern | 3 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_LineTimeSegment | 23 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_Location | 156 | Data + Api + Pda + Tablet + PdaSql | Data | Data + PdaSql(M:Data) | Data | 전체 |
| MD_Mold | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_MoldColor | 4 | Data | Data | — | Data | 부분 |
| MD_MoldItem | 5 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_MoldLine | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MD_Oven | 1 | Data | Data | Data | Data | 전체 |
| MD_PackagingSpec | 1 | Data + Api | Data | Data | Data | 전체 |
| MD_PaintFabric | 1 | Data | Data | Data | Data | 전체 |
| MD_PmTemplate | 1 | Data | Data | Data | Data | 전체 |
| MD_PmTemplateStep | 1 | Data | Data | Data | Data | 전체 |
| MD_RalColor | 2 | Data | Data | Data | Data | 전체 |
| MD_ReasonCode | 1 | Data | Data | Data | Data | 전체 |
| MD_Recipe | 4 | Data | Data | Data | Data | 전체 |
| MD_RfidReader | 3 | Data | Data | Data | Data | 전체 |
| MD_RfidTag | 6 | Data | Data | Data | Data | 전체 |
| MD_RoutingStep | 7 | Data | Data | Data | Data | 전체 |
| MD_ShipmentDest | 1 | Data | Data | Data | Data | 전체 |
| MD_SparePart | 13 | Data + Api + PdaSql | Data + PdaSql | Data + Api + PdaSql | Data | 전체 |
| MD_Station | 3 | Data | Data | Data | Data | 전체 |
| MD_Uom | 10 | Data | Data | Data | Data | 전체 |
| MD_Vendor | 9 | Data + Api + PdaSql | Data | Data | Data | 전체 |
| MD_WorkCenter | 5 | Data | Data | Data | Data | 전체 |
| MD_Worker | 6 | Contracts + Data | Data | Data | Data | 전체 |

### PP (19)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| PP_CustomerOrder | 41 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | InjAgent.Tests | 전체 |
| PP_EquipSignal | 4 | Data | Data | — | — | 부분 |
| PP_Forecast | 3631 | Data | Data | Data | — | 부분 |
| PP_ForecastHistory | 0 | — | Data | — | — | 쓰기만 |
| PP_LineDowntimeLog | 16 | Data | Data | Data | — | 부분 |
| PP_LineOEE | 42 | Data | Data | Data | Data | 전체 |
| PP_LineSchedule | 61 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| PP_LineStateLog | 720 | Data | — | — | — | 읽기만 |
| PP_MaterialReservation | 0 | Data | — | — | — | 읽기만 |
| PP_MRPLog | 7 | Data + InjAgent.Tests | Data | Data | InjAgent.Tests | 전체 |
| PP_MRPResult | 18 | Data + InjAgent.Tests | Data | Data | InjAgent.Tests | 전체 |
| PP_MRPResultWo | 32 | Data + InjAgent.Tests | Data | — | InjAgent.Tests | 부분 |
| PP_ProductionCalendarOverride | 3 | Data | Data | Data | — | 부분 |
| PP_PRSendLog | 3 | Data + InjAgent.Tests | Data | — | — | 부분 |
| PP_PurchaseRequest | 10 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data + InjAgent.Tests | InjAgent.Tests | 전체 |
| PP_SupplyPlan | 3 | Data | — | — | — | 읽기만 |
| PP_SupplyPlanDetail | 9 | Data | — | — | — | 읽기만 |
| PP_WorkOrder | 51 | Data + Api + InjAgent.Tests | Data + InjAgent.Tests | Data + InjAgent.Tests | InjAgent.Tests | 전체 |
| PP_WorkOrderRouting | 40 | Data + Api + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |

### PR (27)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| PR_AndonCall | 36 | Data | Data | Data | — | 부분 |
| PR_AndonDeptCall | 5 | Data | Data | Data | — | 부분 |
| PR_AndonPush | 64 | — | Data | — | — | 쓰기만 |
| PR_BondCycleLog | 14 | — | Data | — | — | 쓰기만 |
| PR_BondSetup | 2 | Data | Data | — | — | 부분 |
| PR_BondSetupAudit | 3 | — | — | — | — | 무참조 |
| PR_CycleAnomalyLog | 0 | — | — | — | — | 무참조 |
| PR_DashTileCache | 0 | — | — | — | — | 무참조 |
| PR_DefectAutoLink | 0 | — | — | — | — | 무참조 |
| PR_DefectDetail | 31 | Data + InjAgent.Tests | Data | Data | — | 부분 |
| PR_DefectRateCache | 0 | — | — | — | — | 무참조 |
| PR_EquipStatusLog | 58 | Data | Data | — | — | 부분 |
| PR_FabricDeductionLog | 14 | — | Data | — | — | 쓰기만 |
| PR_FabricIssue | 1 | Data | Data | Data | — | 부분 |
| PR_FabricIssueAttempt | 0 | — | Data | — | — | 쓰기만 |
| PR_ImgLot | 11 | Data | Data | Data | — | 부분 |
| PR_InjCondLog | 16 | — | Data | — | — | 쓰기만 |
| PR_InjLot | 54 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | InjAgent.Tests | 전체 |
| PR_MoldChange | 3 | — | Data | Data | — | 쓰기만 |
| PR_PlcInterlock | 0 | — | — | — | — | 무참조 |
| PR_PopAuthLog | 420 | — | Data | — | — | 쓰기만 |
| PR_PopSession | 350 | — | Data | Data | — | 쓰기만 |
| PR_ProductionResult | 194 | Data + InjAgent.Tests | Data | — | InjAgent.Tests | 부분 |
| PR_RobotInspection | 4 | Data + InjAgent.Tests | Data | — | InjAgent.Tests | 부분 |
| PR_ShiftHandover | 0 | — | — | — | — | 무참조 |
| PR_ShotCount | 4 | — | — | (M:Data) | — | 쓰기만 |
| PR_WoAcceptance | 17 | Data + InjAgent.Tests | Data | — | — | 부분 |

### WH (12)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| WH_AreaLayout | 0 | Data | — | (M:Data) | Data | 부분 |
| WH_AreaMaster | 12 | Data + Api + Pda + PdaSql | — | (M:Data + PdaSql) | Data | 부분 |
| WH_AreaSection | 47 | Data + PdaSql | — | (M:Data + PdaSql) | Data | 부분 |
| WH_InboundPackage | 29 | PdaSql | — | PdaSql | — | 부분 |
| WH_Inventory | 62 | Data + Api + Pda + InjAgent.Tests + Tablet + PdaSql | InjAgent.Tests + PdaSql | Api + PdaSql | InjAgent.Tests + PdaSql | 전체 |
| WH_InventoryTransaction | 27 | Data + Api + Pda + PdaSql | Api + PdaSql | — | Api + PdaSql | 부분 |
| WH_PurchaseOrder | 19 | Data + InjAgent.Tests + PdaSql | InjAgent.Tests | PdaSql | InjAgent.Tests | 전체 |
| WH_Receiving | 2 | PdaSql | Api + PdaSql | PdaSql | PdaSql | 전체 |
| WH_ReleasePicking | 0 | Data + Api + PdaSql | PdaSql | — | PdaSql | 부분 |
| WH_ReleaseSchedule | 17 | Data + Api + PdaSql | Data | Data + PdaSql | — | 부분 |
| WH_TransactionHistory | 20 | PdaSql | — | — | — | 읽기만 |
| WH_WarehouseMaster | 5 | Data + Api + Pda + PdaSql | — | (M:Data + PdaSql) | Data | 부분 |

### FG (12)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| FG_CustomerReturn | 1 | Data + Api + PdaSql | — | — | PdaSql | 부분 |
| FG_DayEndClose | 1 | — | Api | — | — | 쓰기만 |
| FG_DeliveryNote | 2 | Data + PdaSql | Api | — | PdaSql | 부분 |
| FG_Inventory | 30 | Data + Api + PdaSql | Api | PdaSql | PdaSql | 전체 |
| FG_InventoryAdjust | 1 | PdaSql | PdaSql | — | PdaSql | 부분 |
| FG_LoadingConfirm | 6 | Data + Api + PdaSql | — | — | PdaSql | 부분 |
| FG_LocationMaster | 15 | Data + PdaSql | PdaSql | (M:Data) | Data | 전체 |
| FG_PickingDetail | 8 | PdaSql | — | — | — | 읽기만 |
| FG_PickingFifo | 4 | Data + PdaSql | — | — | PdaSql | 부분 |
| FG_PutAway | 1 | Data + PdaSql | Api | — | PdaSql | 부분 |
| FG_ShipmentOrder | 15 | Data + Api + PdaSql | — | PdaSql | — | 부분 |
| FG_ShipmentOrderLine | 23 | Data + Api + PdaSql | — | — | PdaSql | 부분 |

### MNT (10)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| MNT_EquipmentStatus | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 |
| MNT_FailureAction | 4 | — | Data | — | — | 쓰기만 |
| MNT_FailureRegister | 16 | Data | Data | Data | Data | 전체 |
| MNT_MoldShotCount | 4 | Data | — | — | — | 읽기만 |
| MNT_OEELog | 224 | Data | — | — | — | 읽기만 |
| MNT_PMExecution | 3 | — | Data | — | — | 쓰기만 |
| MNT_PMSchedule | 11 | Data | Data | Data | Data | 전체 |
| MNT_SparePartsTxn | 38 | Data + Api + PdaSql | Data + Api + PdaSql | — | PdaSql | 부분 |
| MNT_WorkOrder | 16 | Data | Data | Data | — | 부분 |
| MNT_WorkOrderTask | 24 | Data | — | — | — | 읽기만 |

### QC (10)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| QC_CAPA | 2 | Data | — | — | — | 읽기만 |
| QC_CAPA_Action | 5 | Data | — | — | — | 읽기만 |
| QC_Disposition | 0 | — | — | — | — | 무참조 |
| QC_Hold | 3 | Data | — | Data | — | 부분 |
| QC_HoldRelease | 1 | — | Data | — | — | 쓰기만 |
| QC_Inspection | 53 | Data + Api + PdaSql | Data | Data | — | 부분 |
| QC_InspectionItem | 10 | Data | Data | — | — | 부분 |
| QC_InspectionStd | 3 | Data | — | — | — | 읽기만 |
| QC_NCR | 4 | Data | Data | — | — | 부분 |
| QC_NCR_Action | 0 | — | — | — | — | 무참조 |

### SYS (11)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| SYS_AuditLog | 67 | Data | Data | — | — | 부분 |
| SYS_Config | 16 | Data | — | Data + InjAgent.Tests | — | 부분 |
| SYS_FactoryCalendar | 147 | Data | Data | Data | Data | 전체 |
| SYS_InterfaceMonitor | 8 | Data | — | — | — | 읽기만 |
| SYS_LotSeq | 18 | — | Data | Data | — | 쓰기만 |
| SYS_NotificationChannel | 1 | Data | Data | Data | Data | 전체 |
| SYS_NotificationHistory | 40 | Data | — | — | — | 읽기만 |
| SYS_NotificationRule | 9 | Data | Data | Data | Data | 전체 |
| SYS_RolePermission | 122 | Data + Web | Data | Data(M:Data) | Data | 전체 |
| SYS_Screen | 83 | Data | Data | Data + PdaSql | Data | 전체 |
| SYS_UserProfile | 18 | Data | Data | Data | Data | 전체 |

### PNT (22)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| PNT_DailyPlan | 2 | Data | — | — | — | 읽기만 |
| PNT_DailyReport | 0 | — | — | — | — | 무참조 |
| PNT_JigBindingLog | 0 | — | — | — | — | 무참조 |
| PNT_JigLoad | 1 | — | Data | — | — | 쓰기만 |
| PNT_JigUnload | 0 | — | — | — | — | 무참조 |
| PNT_LabelPrintJob | 0 | — | — | — | — | 무참조 |
| PNT_LabelScanLog | 0 | — | — | — | — | 무참조 |
| PNT_LineEvent | 12 | Data + Pop | Data | — | — | 부분 |
| PNT_LotLabel | 0 | — | — | — | — | 무참조 |
| PNT_OvenDeviationLog | 0 | — | — | — | — | 무참조 |
| PNT_OvenLog | 1 | — | — | — | — | 무참조 |
| PNT_OvenSpikeLog | 0 | — | — | — | — | 무참조 |
| PNT_OvenTempSample | 120 | Data + Pop | — | — | — | 읽기만 |
| PNT_PartLossLog | 0 | — | — | — | — | 무참조 |
| PNT_QcQueue | 0 | — | — | — | — | 무참조 |
| PNT_SeqAllocator | 0 | — | — | — | — | 무참조 |
| PNT_ShiftReport | 0 | — | — | — | — | 무참조 |
| PNT_ShiftReportAudit | 0 | — | — | — | — | 무참조 |
| PNT_ShiftReportLineItem | 0 | — | — | — | — | 무참조 |
| PNT_StationStatsCache | 0 | — | — | — | — | 무참조 |
| PNT_TagFailureLog | 0 | — | — | — | — | 무참조 |
| PNT_VirtualLot | 7 | Data + Pop | Data | Data | — | 부분 |

### tbl (1)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| tbl_Lot | 182 | Contracts + Data + Api + Pda + InjAgent.Tests + Tablet + PdaSql | Data + InjAgent.Tests | Data + Api + PdaSql | InjAgent.Tests | 전체 |

### Identity (7)
| 테이블 | 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 |
|---|---:|---|---|---|---|---|
| AspNetRoleClaims | 0 | — | — | — | — | 무참조 |
| AspNetRoles | 6 | Data + Web | — | — | — | 읽기만 |
| AspNetUserClaims | 0 | — | — | — | — | 무참조 |
| AspNetUserLogins | 0 | — | — | — | — | 무참조 |
| AspNetUserRoles | 18 | Data | — | — | — | 읽기만 |
| AspNetUsers | 18 | Data + PdaSql | — | — | — | 읽기만 |
| AspNetUserTokens | 0 | — | — | — | — | 무참조 |
