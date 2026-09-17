# AMES DB 테이블별 CRUD 커버리지 분석 — 2026-09-17 (09-16 판 갱신)

## 1. 목적·방법
- **목적**: 개발 DB(AMES_DEV, 172 테이블)의 각 테이블이 솔루션 어디에서 읽기/등록/수정/삭제되는지 전수 확인하고, 09-16 분석 이후 처리된 항목과 남은 공백을 현재 상태 기준으로 정리한다.
- **범위**: `src/**/*.cs`·`*.razor` 478개 파일(Web·Api·Pop·Pda·Data·InjAgent·Tablet·Tests) + PDA 프로시저 `dist/pda/PDA_SCHEMA.sql`(표에서 `PdaSql`). 기준 커밋 `12a2f80`(로컬 dev). 원격 dev 의 PP-007 화면 제거 2커밋은 아직 미반영.
- **방법**: 테이블명 기준 정규식 스캔 — 읽기 = `FROM/JOIN/APPLY`, 등록 = `INSERT INTO`, 수정 = `UPDATE`(MERGE 는 `(M:…)` 병기), 삭제 = `DELETE`. 결과 원본은 `publish/crud_coverage_20260917.tsv`(gitignore 대상 — 이 문서 §9 표가 같은 내용).
- **DB 확인**: 개발(192.168.1.100)·로컬(localhost\MSSQLSERVER01) 두 DB 의 테이블 172개 이름이 같고, 컬럼 구조 서명(이름·형·길이·널·순서)도 172개 전부 동일하다(09-17 `migrate_md_codeitem_widen`·`migrate_pr_imglot_column_order` 적용 후). 행 수는 다르다(§8).
- **한계**: 원시 SQL 문자열만 본다. Identity 7 테이블(AspNet*)은 EF Core 가 관리하므로 `EF관리` 로 따로 분류했다.

## 2. 집계

| 분류 | 정의 | 09-16 | 09-17 |
|---|---|---:|---:|
| 전체 | R·I·U·D 모두 있음 | 62 | 63 |
| 부분 | 일부 연산만 있음 | 43 | 45 |
| 읽기만 | 읽는 코드만 있고 쓰는 코드 없음(생산자 없음) | 18 | 17 |
| 쓰기만 | 쓰는 코드만 있고 읽는 화면 없음(소비자 없음) | 16 | 14 |
| 무참조 | 어디서도 참조하지 않음 | 26 | 26 |
| EF관리 | Identity — EF Core 가 관리(스캔 대상 아님) | 7 | 7 |

## 3. 09-16 이후 처리된 항목
| 테이블 | 09-16 | 현재 | 처리 내용 |
|---|---|---|---|
| MD_LineSupervisor | 읽기만 | 전체 | Web MD-033 「라인 책임자 관리」 신설(`LineSupervisorRepository` 등록·활성 전환·삭제, 후보 = Supervisor 역할 웹 사용자). 컬럼 `WorkerNo → EmployeeNo`. 시드도 역할 기준으로 재작성 |
| MNT_PMExecution | 쓰기만 | 부분(R·I) | MNT-005/010 달력의 완료 칩·보기 전용 모달·일정 상세 최근 이력으로 노출. 스냅샷 컬럼(PMPlanNumber·EquipID·PMClass·PMType·DueDate·LaborMinutes) 추가. 수정·삭제가 없는 것은 이력 테이블이라 정상 |
| PR_MoldChange | 쓰기만 | 부분(R·I·U) | MNT-004 하단 「금형 교체 이력」 목록·검색·내보내기, 상세 모달에서 금형별 조회 |
| MD_Worker | 전체 | 전체 | 컬럼 `WorkerNo/WorkerName → EmployeeNo/EmployeeName`(SYS_UserProfile 과 통일). MD-032·POP 로그인·API 로그인 경로 반영 |
| MD_SparePart · MNT_SparePartsTxn | 전체 / 부분 | 동일 | 채번 사고(`EOS-SP-K9-261000`) 원인 제거 — 순번 9999(PDA 테스트 고정) 제외·범위 초과 예외. 깨진 데이터 삭제, 테스트 샘플 260002~260004 등록 |
| MD_Item · MD_Location · WH_AreaMaster · WH_Inventory · WH_InventoryTransaction · WH_WarehouseMaster · tbl_Lot | Pda·Tablet 직접 읽기 | Api 경유 | 다른 개발자의 PDA/태블릿 리팩터 — 단말이 DB 를 직접 읽지 않고 `AMES.Api` 를 거친다(분류 변화 없음, 읽기 주체에서 Pda·Tablet 이 빠짐) |
| MD_CodeItem · PR_ImgLot | — | — | 개발·로컬 DB 구조 불일치 해소(폭 200/500 통일, 컬럼 순서 재정렬). CRUD 분류와는 무관 |

## 4. 남은 공백 — 데모(Office Web) 기능과 직결

### 4-1. 화면은 있으나 데이터 생산자가 없는 테이블 (읽기만)
| 테이블 | 행(dev) | 읽는 화면 | 없는 것 |
|---|---:|---|---|
| MNT_OEELog | 224 | MNT-003 · RPT-006 · MNT-009 KPI | 일별 OEE 집계기 |
| PP_LineStateLog | 720 | PP-ODM 비가동 모니터 | 분 단위 상태 분류기(패턴 × 설비신호) — 신호 원천 결정 선행 |
| SYS_InterfaceMonitor | 9 | SYS-006 · SYS-010 | 연동 결과 기록자(SAP/PLC 연동 자체 없음). 09-16 대비 +1행은 수동 입력 |
| SYS_NotificationHistory | 40 | SYS-005 · SYS-010 | 발송 엔진 · Send Test |
| MNT_WorkOrderTask | 24 | MNT-007 상세 | WO 발행 시 템플릿(MD_PmTemplateStep) 복사·항목별 결과 입력 |
| MNT_MoldShotCount | 4 | MNT-004 | Shot 갱신·정비 등록(보류) |
| PP_SupplyPlan / PP_SupplyPlanDetail | 3 / 9 | PP-002 목록 | PP-002 가져오기는 `PP_CustomerOrder` 에 직접 INSERT(dev 467행으로 증가) — 정본 결정 필요 |
| PP_MaterialReservation | 0 | WH 재고 예약 수량 | WO 릴리스 시 자재 예약. 원격에서 PP-007 화면이 제거되는 중이라 게이트 자체가 재검토 대상 |
| MD_InjCondItem | 2 | InjAgent 수집 | 사출조건 항목 마스터 화면 |

### 4-2. 기록은 되는데 어느 화면도 보여주지 않는 테이블 (쓰기만)
| 테이블 | 행(dev) | 기록자 | 노출 후보 |
|---|---:|---|---|
| MNT_FailureAction | 6 | 안돈 ARRIVED/ACK · 수리 REPAIRED | MNT-002 상세 타임라인 · MNT-009 (보류 ④) |
| PP_ForecastHistory | 0 | 수요예측 수량 변경 | PP-001 이력 탭·상세 |
| PR_InjCondLog | 16 | InjAgent | 사출조건 이력 조회 화면(없음) |
| PR_AndonPush · PR_PopAuthLog · PR_PopSession | 68 / 434 / 364 | POP | 감사 성격 — 노출 여부 선택 |
| PR_ShotCount | 4 | InjAgent MERGE | MNT-004 Shot 원천과 통합 검토 |
| FG_DayEndClose | 1 | Api `/api/fg/dayend` | FG 웹 화면 없음 |
| QC_HoldRelease | 1 | POP QC-05 | 홀드 이력 조회 |
| PNT_JigLoad | 1 | POP PNT-03 | 도장 모듈 미개발 |
| PR_BondCycleLog · PR_FabricDeductionLog · PR_FabricIssueAttempt | 14 / 14 / 0 | INSERT 코드만 잔존 | IMG-MAIN 이 호출하지 않음 — 잔재 |
| SYS_LotSeq | 20 | LotNoGenerator | 내부용, 노출 불필요 |

### 4-3. MNT-08 입고(Receive Stock)
웹에는 아직 없고 PDA API(`/api/wh/sp/inbound`·`/sp/release`·`/sp/adjust/save`)와 `MntRepository.AdjustSparePartStock` 이 이미 있다. 웹 MNT-008 에 입고 모달을 붙이면 스키마 변경이 없다(착수 대기).

## 5. 데모 밖 영역(POP·PDA)의 공백 — 09-16 과 동일
- **PNT(도장)**: 22 테이블 중 무참조 17, 읽기만 2(`PNT_DailyPlan`·`PNT_OvenTempSample`), 쓰기만 1(`PNT_JigLoad`). POP PNT 9화면은 스캐폴드.
- **QC**: `QC_Hold` INSERT 없음(홀드 등록 불가) · `QC_CAPA`·`QC_CAPA_Action`·`QC_InspectionStd` 읽기만 · `QC_Disposition`·`QC_NCR_Action` 무참조 · `QC_NCR` 등록만.
- **WH/FG**: `WH_TransactionHistory`·`FG_PickingDetail` 은 PDA 프로시저가 읽기만 · `WH_InboundPackage` INSERT 없음 · `FG_ShipmentOrder` 생성 화면 없음.

## 6. 무참조 26개
- **PNT 17**: `PNT_DailyReport` · `PNT_JigBindingLog` · `PNT_JigUnload` · `PNT_LabelPrintJob` · `PNT_LabelScanLog` · `PNT_LotLabel` · `PNT_OvenDeviationLog` · `PNT_OvenLog` · `PNT_OvenSpikeLog` · `PNT_PartLossLog` · `PNT_QcQueue` · `PNT_SeqAllocator` · `PNT_ShiftReport` · `PNT_ShiftReportAudit` · `PNT_ShiftReportLineItem` · `PNT_StationStatsCache` · `PNT_TagFailureLog` — 도장 모듈 설계 자리(보존).
- **PR 7**: `PR_BondSetupAudit` · `PR_CycleAnomalyLog` · `PR_DashTileCache` · `PR_DefectAutoLink` · `PR_DefectRateCache` · `PR_PlcInterlock` · `PR_ShiftHandover` — 대체·삭제된 기능의 흔적(DROP 후보, 결정 대기).
- **QC 2**: `QC_Disposition` · `QC_NCR_Action` — NCR·처분 흐름 미구현.

## 7. 설계상 정상(조치 불필요)
append-only 로그(`SYS_AuditLog`·`PR_ProductionResult`·`PR_WoAcceptance`·`PP_PRSendLog`·`QC_InspectionItem`·`PR_RobotInspection`·`MNT_PMExecution`), 값 수정만 있는 `SYS_Config`, DELETE 없는 `MNT_WorkOrder`(CANCELED 는 보류), MERGE 패턴의 `WH_WarehouseMaster`·`WH_AreaMaster`·`WH_AreaSection`·`WH_AreaLayout`, Identity 7개(EF Core).

## 8. DB 상태 (개발 vs 로컬)
- 테이블 172개 이름·구조 서명 전부 동일.
- 행 수가 다른 테이블 76개 — 로컬은 비상용 사본이라 운영성 데이터가 적다. 차이가 큰 순:

| 테이블 | 개발 | 로컬 |
|---|---:|---:|
| PP_Forecast | 3631 | 40 |
| MD_Bom | 509 | 2 |
| PP_CustomerOrder | 467 | 6 |
| MD_Item | 1548 | 1190 |
| PR_PopAuthLog | 434 | 169 |
| PR_PopSession | 364 | 134 |
| MD_BomVersion | 180 | 3 |
| tbl_Lot | 182 | 10 |
| MD_Location | 156 | 6 |
| PR_ProductionResult | 194 | 129 |
| PR_AndonPush | 68 | 6 |
| PR_EquipStatusLog | 62 | 7 |
| PR_InjLot | 54 | 0 |
| MD_CodeItem | 376 | 324 |
| WH_Inventory | 62 | 12 |

- 09-16 대비 개발 DB 행 수 변화: MD_CodeGroup +4 · MD_CodeItem +4 · MD_LineSupervisor +7 · MD_SparePart +2 · MNT_FailureAction +2 · MNT_FailureRegister +1 · MNT_SparePartsTxn +3 · PP_CustomerOrder +426 · PP_LineDowntimeLog +2 · PP_LineSchedule +4 · PR_AndonCall +2 · PR_AndonDeptCall +2 · PR_AndonPush +4 · PR_EquipStatusLog +4 · PR_PopAuthLog +14 · PR_PopSession +14 · SYS_InterfaceMonitor +1 · SYS_LotSeq +2.
- 이번 작업으로 맞춘 데이터: `MD_LineSupervisor`(두 DB 12행 동일), `SYS_Screen` MD-033, Supervisor 역할 보유자(E004·E007). 테스트 예비품 260002~260004 는 개발 DB 에만 있다.

## 9. 권장 순서 (갱신)
1. MNT-008 웹 입고 모달 — API 경로 재사용, 스키마 변경 없음.
2. 남은 이력 노출: `MNT_FailureAction`(MNT-002 타임라인) · `PP_ForecastHistory` · `PR_InjCondLog`.
3. 생산자 없는 KPI 테이블의 방향 결정 — OEE 집계기·라인 상태 분류기·알림 발송기.
4. PP-002 정본(`PP_CustomerOrder` vs `PP_SupplyPlan`), `WH_TransactionHistory` vs `WH_InventoryTransaction`, `QC_InspectionStd` vs `MD_InspectionStandard` 결정.
5. PR 잔재 7개 + 본딩 INSERT 잔재 3개 DROP 결정.
6. 원격의 PP-007 제거 반영 후 `PP_MaterialReservation`·릴리스 게이트 보류 항목 재정리.

## 10. 전체 표 (테이블 | 개발 행 | 로컬 행 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 분류)
프로젝트 표기: Web·Api·Pop·Pda·Data·InjAgent·Tablet·*.Tests, `PdaSql` = PDA_SCHEMA.sql 프로시저. `(M:…)` 는 MERGE. 분류가 바뀐 행은 굵게.

### MD (41)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| MD_Bom | 509 | 2 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_BomVersion | 180 | 3 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_Bop | 15 | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_CodeGroup | 86 | 74 | Data | Data | Data | Data | 전체 | 전체 |
| MD_CodeItem | 376 | 324 | Api + Data + InjAgent.Tests + PdaSql | Data | Data | Data | 전체 | 전체 |
| MD_Customer | 7 | 7 | Data | Data | Data | Data | 전체 | 전체 |
| MD_DefectCause | 5 | 5 | Data | Data | Data | Data | 전체 | 전체 |
| MD_DefectCode | 18 | 18 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Equipment | 8 | 8 | Data | Data | Data | Data | 전체 | 전체 |
| MD_InjCondItem | 2 | 2 | Data | — | — | — | 읽기만 | 읽기만 |
| MD_InspectionStandard | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Item | 1548 | 1190 | Api + Data + InjAgent.Tests + PdaSql | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_Jig | 6 | 6 | Data | Data | Data | Data | 전체 | 전체 |
| MD_LabelTemplate | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Line | 7 | 7 | Data | Data | Data | Data | 전체 | 전체 |
| **MD_LineSupervisor** | 12 | 12 | Data | Data | Data | Data | **전체** | 읽기만 |
| MD_LineTimePattern | 3 | 3 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_LineTimeSegment | 23 | 23 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_Location | 156 | 6 | Api + Data + PdaSql | Data | Data + PdaSql(M:Data) | Data | 전체 | 전체 |
| MD_Mold | 8 | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_MoldColor | 4 | 4 | Data | Data | — | Data | 부분 | 부분 |
| MD_MoldItem | 5 | 6 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_MoldLine | 8 | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MD_Oven | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_PackagingSpec | 1 | 1 | Api + Data | Data | Data | Data | 전체 | 전체 |
| MD_PaintFabric | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_PmTemplate | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_PmTemplateStep | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_RalColor | 2 | 2 | Data | Data | Data | Data | 전체 | 전체 |
| MD_ReasonCode | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Recipe | 4 | 4 | Data | Data | Data | Data | 전체 | 전체 |
| MD_RfidReader | 3 | 3 | Data | Data | Data | Data | 전체 | 전체 |
| MD_RfidTag | 6 | 6 | Data | Data | Data | Data | 전체 | 전체 |
| MD_RoutingStep | 7 | 8 | Data | Data | Data | Data | 전체 | 전체 |
| MD_ShipmentDest | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| MD_SparePart | 15 | 12 | Api + Data + PdaSql | Data + PdaSql | Api + Data + PdaSql | Data | 전체 | 전체 |
| MD_Station | 3 | 3 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Uom | 10 | 9 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Vendor | 9 | 5 | Api + Data + PdaSql | Data | Data | Data | 전체 | 전체 |
| MD_WorkCenter | 5 | 5 | Data | Data | Data | Data | 전체 | 전체 |
| MD_Worker | 6 | 5 | Contracts + Data | Data | Data | Data | 전체 | 전체 |

### PP (19)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| PP_CustomerOrder | 467 | 6 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | InjAgent.Tests | 전체 | 전체 |
| PP_EquipSignal | 4 | 4 | Data | Data | — | — | 부분 | 부분 |
| PP_Forecast | 3631 | 40 | Data | Data | Data | — | 부분 | 부분 |
| PP_ForecastHistory | 0 | 0 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PP_LineDowntimeLog | 18 | 12 | Data | Data | Data | — | 부분 | 부분 |
| PP_LineOEE | 42 | 42 | Data | Data | Data | Data | 전체 | 전체 |
| PP_LineSchedule | 65 | 26 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| PP_LineStateLog | 720 | 720 | Data | — | — | — | 읽기만 | 읽기만 |
| PP_MRPLog | 7 | 5 | Data + InjAgent.Tests | Data | Data | InjAgent.Tests | 전체 | 전체 |
| PP_MRPResult | 18 | 0 | Data + InjAgent.Tests | Data | Data | InjAgent.Tests | 전체 | 전체 |
| PP_MRPResultWo | 32 | 0 | Data + InjAgent.Tests | Data | — | InjAgent.Tests | 부분 | 부분 |
| PP_MaterialReservation | 0 | 0 | Data | — | — | — | 읽기만 | 읽기만 |
| PP_PRSendLog | 3 | 0 | Data + InjAgent.Tests | Data | — | — | 부분 | 부분 |
| PP_ProductionCalendarOverride | 3 | 4 | Data | Data | Data | — | 부분 | 부분 |
| PP_PurchaseRequest | 10 | 5 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data + InjAgent.Tests | InjAgent.Tests | 전체 | 전체 |
| PP_SupplyPlan | 3 | 3 | Data | — | — | — | 읽기만 | 읽기만 |
| PP_SupplyPlanDetail | 9 | 9 | Data | — | — | — | 읽기만 | 읽기만 |
| PP_WorkOrder | 51 | 9 | Api + Data + InjAgent.Tests | Data + InjAgent.Tests | Data + InjAgent.Tests | InjAgent.Tests | 전체 | 전체 |
| PP_WorkOrderRouting | 40 | 24 | Api + Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |

### PR (27)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| PR_AndonCall | 38 | 7 | Data | Data | Data | — | 부분 | 부분 |
| PR_AndonDeptCall | 7 | 0 | Data | Data | Data | — | 부분 | 부분 |
| PR_AndonPush | 68 | 6 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PR_BondCycleLog | 14 | 6 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PR_BondSetup | 2 | 2 | Data | Data | — | — | 부분 | 부분 |
| PR_BondSetupAudit | 3 | 3 | — | — | — | — | 무참조 | 무참조 |
| PR_CycleAnomalyLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PR_DashTileCache | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PR_DefectAutoLink | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PR_DefectDetail | 31 | 8 | Data + InjAgent.Tests | Data | Data | — | 부분 | 부분 |
| PR_DefectRateCache | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PR_EquipStatusLog | 62 | 7 | Data | Data | — | — | 부분 | 부분 |
| PR_FabricDeductionLog | 14 | 6 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PR_FabricIssue | 1 | 1 | Data | Data | Data | — | 부분 | 부분 |
| PR_FabricIssueAttempt | 0 | 0 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PR_ImgLot | 11 | 0 | Data | Data | Data | — | 부분 | 부분 |
| PR_InjCondLog | 16 | 0 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PR_InjLot | 54 | 0 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | InjAgent.Tests | 전체 | 전체 |
| **PR_MoldChange** | 3 | 3 | Data | Data | Data | — | **부분** | 쓰기만 |
| PR_PlcInterlock | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PR_PopAuthLog | 434 | 169 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PR_PopSession | 364 | 134 | — | Data | Data | — | 쓰기만 | 쓰기만 |
| PR_ProductionResult | 194 | 129 | Data + InjAgent.Tests | Data | — | InjAgent.Tests | 부분 | 부분 |
| PR_RobotInspection | 4 | 0 | Data + InjAgent.Tests | Data | — | InjAgent.Tests | 부분 | 부분 |
| PR_ShiftHandover | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PR_ShotCount | 4 | 0 | — | — | (M:Data) | — | 쓰기만 | 쓰기만 |
| PR_WoAcceptance | 17 | 0 | Data + InjAgent.Tests | Data | — | — | 부분 | 부분 |

### WH (12)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| WH_AreaLayout | 0 | 0 | Data | — | (M:Data) | Data | 부분 | 부분 |
| WH_AreaMaster | 12 | 1 | Api + Data + PdaSql | — | (M:Data + PdaSql) | Data | 부분 | 부분 |
| WH_AreaSection | 47 | 3 | Data + PdaSql | — | (M:Data + PdaSql) | Data | 부분 | 부분 |
| WH_InboundPackage | 29 | 0 | PdaSql | — | PdaSql | — | 부분 | 부분 |
| WH_Inventory | 62 | 12 | Api + Data + InjAgent.Tests + PdaSql | InjAgent.Tests + PdaSql | Api + PdaSql | InjAgent.Tests + PdaSql | 전체 | 전체 |
| WH_InventoryTransaction | 27 | 20 | Api + Data + PdaSql | Api + PdaSql | — | Api + PdaSql | 부분 | 부분 |
| WH_PurchaseOrder | 19 | 8 | Data + InjAgent.Tests + PdaSql | InjAgent.Tests | PdaSql | InjAgent.Tests | 전체 | 전체 |
| WH_Receiving | 2 | 1 | PdaSql | Api + PdaSql | PdaSql | PdaSql | 전체 | 전체 |
| WH_ReleasePicking | 0 | 0 | Api + Data + PdaSql | PdaSql | — | PdaSql | 부분 | 부분 |
| WH_ReleaseSchedule | 17 | 5 | Api + Data + PdaSql | Data | Data + PdaSql | — | 부분 | 부분 |
| WH_TransactionHistory | 20 | 20 | PdaSql | — | — | — | 읽기만 | 읽기만 |
| WH_WarehouseMaster | 5 | 3 | Api + Data + PdaSql | — | (M:Data + PdaSql) | Data | 부분 | 부분 |

### FG (12)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| FG_CustomerReturn | 1 | 0 | Api + Data + PdaSql | — | — | PdaSql | 부분 | 부분 |
| FG_DayEndClose | 1 | 1 | — | Api | — | — | 쓰기만 | 쓰기만 |
| FG_DeliveryNote | 2 | 1 | Data + PdaSql | Api | — | PdaSql | 부분 | 부분 |
| FG_Inventory | 30 | 8 | Api + Data + PdaSql | Api | PdaSql | PdaSql | 전체 | 전체 |
| FG_InventoryAdjust | 1 | 0 | PdaSql | PdaSql | — | PdaSql | 부분 | 부분 |
| FG_LoadingConfirm | 6 | 1 | Api + Data + PdaSql | — | — | PdaSql | 부분 | 부분 |
| FG_LocationMaster | 15 | 0 | Data + PdaSql | PdaSql | (M:Data) | Data | 전체 | 전체 |
| FG_PickingDetail | 8 | 0 | PdaSql | — | — | — | 읽기만 | 읽기만 |
| FG_PickingFifo | 4 | 0 | Data + PdaSql | — | — | PdaSql | 부분 | 부분 |
| FG_PutAway | 1 | 0 | Data + PdaSql | Api | — | PdaSql | 부분 | 부분 |
| FG_ShipmentOrder | 15 | 4 | Api + Data + PdaSql | — | PdaSql | — | 부분 | 부분 |
| FG_ShipmentOrderLine | 23 | 8 | Api + Data + PdaSql | — | — | PdaSql | 부분 | 부분 |

### MNT (10)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| MNT_EquipmentStatus | 8 | 8 | Data + InjAgent.Tests | Data + InjAgent.Tests | Data | Data + InjAgent.Tests | 전체 | 전체 |
| MNT_FailureAction | 6 | 0 | — | Data | — | — | 쓰기만 | 쓰기만 |
| MNT_FailureRegister | 17 | 12 | Data | Data | Data | Data | 전체 | 전체 |
| MNT_MoldShotCount | 4 | 4 | Data | — | — | — | 읽기만 | 읽기만 |
| MNT_OEELog | 224 | 224 | Data | — | — | — | 읽기만 | 읽기만 |
| **MNT_PMExecution** | 3 | 0 | Data | Data | — | — | **부분** | 쓰기만 |
| MNT_PMSchedule | 11 | 8 | Data | Data | Data | Data | 전체 | 전체 |
| MNT_SparePartsTxn | 41 | 36 | Api + Data + PdaSql | Api + Data + PdaSql | — | PdaSql | 부분 | 부분 |
| MNT_WorkOrder | 16 | 8 | Data | Data | Data | — | 부분 | 부분 |
| MNT_WorkOrderTask | 24 | 24 | Data | — | — | — | 읽기만 | 읽기만 |

### QC (10)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| QC_CAPA | 2 | 2 | Data | — | — | — | 읽기만 | 읽기만 |
| QC_CAPA_Action | 5 | 5 | Data | — | — | — | 읽기만 | 읽기만 |
| QC_Disposition | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| QC_Hold | 3 | 3 | Data | — | Data | — | 부분 | 부분 |
| QC_HoldRelease | 1 | 1 | — | Data | — | — | 쓰기만 | 쓰기만 |
| QC_Inspection | 53 | 17 | Api + Data + PdaSql | Data | Data | — | 부분 | 부분 |
| QC_InspectionItem | 10 | 10 | Data | Data | — | — | 부분 | 부분 |
| QC_InspectionStd | 3 | 3 | Data | — | — | — | 읽기만 | 읽기만 |
| QC_NCR | 4 | 4 | Data | Data | — | — | 부분 | 부분 |
| QC_NCR_Action | 0 | 0 | — | — | — | — | 무참조 | 무참조 |

### SYS (11)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| SYS_AuditLog | 67 | 64 | Data | Data | — | — | 부분 | 부분 |
| SYS_Config | 16 | 16 | Data | — | Data + InjAgent.Tests | — | 부분 | 부분 |
| SYS_FactoryCalendar | 147 | 147 | Data | Data | Data | Data | 전체 | 전체 |
| SYS_InterfaceMonitor | 9 | 8 | Data | — | — | — | 읽기만 | 읽기만 |
| SYS_LotSeq | 20 | 2 | — | Data | Data | — | 쓰기만 | 쓰기만 |
| SYS_NotificationChannel | 1 | 1 | Data | Data | Data | Data | 전체 | 전체 |
| SYS_NotificationHistory | 40 | 40 | Data | — | — | — | 읽기만 | 읽기만 |
| SYS_NotificationRule | 9 | 9 | Data | Data | Data | Data | 전체 | 전체 |
| SYS_RolePermission | 122 | 123 | Data + Web | Data | Data(M:Data) | Data | 전체 | 전체 |
| SYS_Screen | 83 | 86 | Data | Data | Data + PdaSql | Data | 전체 | 전체 |
| SYS_UserProfile | 18 | 16 | Data | Data | Data | Data | 전체 | 전체 |

### PNT (22)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| PNT_DailyPlan | 2 | 2 | Data | — | — | — | 읽기만 | 읽기만 |
| PNT_DailyReport | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_JigBindingLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_JigLoad | 1 | 1 | — | Data | — | — | 쓰기만 | 쓰기만 |
| PNT_JigUnload | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_LabelPrintJob | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_LabelScanLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_LineEvent | 12 | 12 | Data + Pop | Data | — | — | 부분 | 부분 |
| PNT_LotLabel | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_OvenDeviationLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_OvenLog | 1 | 1 | — | — | — | — | 무참조 | 무참조 |
| PNT_OvenSpikeLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_OvenTempSample | 120 | 120 | Data + Pop | — | — | — | 읽기만 | 읽기만 |
| PNT_PartLossLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_QcQueue | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_SeqAllocator | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_ShiftReport | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_ShiftReportAudit | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_ShiftReportLineItem | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_StationStatsCache | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_TagFailureLog | 0 | 0 | — | — | — | — | 무참조 | 무참조 |
| PNT_VirtualLot | 7 | 7 | Data + Pop | Data | Data | — | 부분 | 부분 |

### tbl (1)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| tbl_Lot | 182 | 10 | Api + Contracts + Data + InjAgent.Tests + PdaSql | Data + InjAgent.Tests | Api + Data + PdaSql | InjAgent.Tests | 전체 | 전체 |

### Identity (7)
| 테이블 | 개발 | 로컬 | 읽기 | 등록 | 수정 | 삭제 | 분류 | 09-16 |
|---|---:|---:|---|---|---|---|---|---|
| AspNetRoleClaims | 0 | 0 | — | — | — | — | EF관리 | EF관리 |
| AspNetRoles | 6 | 6 | Data + Web | — | — | — | EF관리 | EF관리 |
| AspNetUserClaims | 0 | 0 | — | — | — | — | EF관리 | EF관리 |
| AspNetUserLogins | 0 | 0 | — | — | — | — | EF관리 | EF관리 |
| AspNetUserRoles | 18 | 18 | Data | — | — | — | EF관리 | EF관리 |
| AspNetUserTokens | 0 | 0 | — | — | — | — | EF관리 | EF관리 |
| AspNetUsers | 18 | 16 | Data + PdaSql | — | — | — | EF관리 | EF관리 |
