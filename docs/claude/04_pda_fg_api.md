# AMES.Pda(WH·FG 시나리오)와 AMES.Api 그룹

> `CLAUDE.md` 에서 분리한 상세 문서입니다(원문 그대로, 11-18 토큰 절감 정리). 필요한 주제만 `grep -rn 키워드 docs/claude/` 로 찾아 해당 줄 범위만 읽으세요.

### AMES.Pda — 핸디 스캐너 (MAUI Blazor Hybrid)

API 서버(`AMES.Api`)와 HTTP 통신. Bearer Token 인증.

#### WH (창고) — 8화면
`Wh01InboundSchedule` / `Wh02PdaInbound` / `Wh03InventoryStatus` / `Wh04LocationMap`
`Wh05InventoryAdjust` / `Wh06ReleaseSchedule` / `Wh07PdaRelease` / `Wh08TransactionHistory`

**WH007-TC-012 대표 Location 차단 기준**: `LocationLevel=1`은 Location 스캔 즉시 HTTP 400·「Location Scan Failed」와 `Primary locations cannot hold stock. Scan a detail location.`으로 거부한다. CONFIRM은 불가하고 Box는 미적재 상태를 유지한다. 저장 경로에서도 같은 제한을 다시 검증한다.

#### FG (완성품 출하) — 10화면
`Fg01Stocking` / `Fg02Inventory` / `Fg03ShipmentOrder` / `Fg04FifoPicking` / `Fg05Loading`
`Fg06DeliveryNote` / `Fg07DayEndClose` / `Fg08ShipmentHistory` / `Fg09Dashboard` / `FgRtnReturn`

**FG 시나리오 기준(10-29 사용자 결정)** — 개편 전 화면명·행 수·팝업 제목을 되살리지 않고 다음 기준으로 대조한다.

- **ASSY Ready**(`FG001-TC-002/003/007/009`): `WH_Inventory`에 등록된 위치 미지정 ASSY를 팔렛별로 묶고 비팔렛 LOT도 포함한다. Groups는 묶음 수이며 동일 팔렛에 LOT가 추가돼도 묶음 수는 늘지 않는다. 팔렛의 가장 이른 등록 시각으로 Over 1/5/10 Days를 중첩 집계한다. 카드 머리는 팔렛/LOT 번호, 내부는 품번·품명·합계 수량·단위·LOT 수다. 빈 목록 문구는 `No registered ASSY LOTs are waiting for location assignment.`이며 모든 집계는 0이다.
- **메뉴·초기 화면**(`FG001-TC-015`, `FG002-TC-001`): Set Location은 Warehouse 메뉴 하나로 통합한다(`/wh/putaway`, `/fg/02`도 같은 화면). Shipping Unit·Unit Item·LOT·Delivery Note를 공통 스캔하며, Delivery Note는 조회용이다. CLEAR·CONFIRM과 필수 스캔 검증은 유지한다.
- **위치 지정 표시**(`FG002-TC-002/008/022`): FG 대기 LOT의 `ReadyAt`은 `READY DATE`·`MM/dd/yyyy`로 표시한다. WH 박스 날짜는 기존 형식을 유지한다. 팔렛은 `ASSY PALLET` 머리와 pending/stored 수, PENDING ITEMS의 LOT·품번·품명·수량으로 확인한다. 위치 속성은 AREA·AISLE·BAY·FLOOR·STOCK QTY·LOAD CAPACITY이며 용량 미설정은 `-`다.
- **위치 지정 알럿**(`FG002-TC-009/011/012/015/016/020/027`): 성공은 `Location Saved`·`ASSY stock was assigned successfully.`. 위치 미스캔은 `Set Location Check`·`Scan a valid location first.`. 공통 경로의 미등록 재고는 `Barcode Scan Failed`·`Received inventory was not found.`. 생산 미완료·기적재·기출고 LOT는 구체적 사유와 `Set Location Blocked`로 막는다. 위치 미등록·비FG·용량 부족·대표 위치는 스캔 단계에서 `Location Scan Failed`로 막고 저장 단계에서도 재검사한다.
- **검사·불량 차단**(`FG002-TC-026`, `FG004-TC-028`, `FG006-TC-018`): 검사 대기 반품의 출고 스캔·확정은 위치 누락보다 awaiting inspection 안내가 우선한다. 위치 지정은 검사 대기·불량 판정을 먼저 안내한다. `FG_DEFECT`/`INV_DEFECT` 거래의 `QtyAfter=0`인 LOT는 스캔과 저장 모두 `This LOT was rejected as defective. Put-away is not allowed.`로 막고 팔렛 전체도 진행하지 못한다. 부분 불량의 양수 잔량은 이 전량 불량 조건에 해당하지 않는다.
- **출하 목록**(`FG102-TC-001/005/009`, `FG004-TC-026`, `E2E-TC-002/004`): DCR별 1행(없으면 개별 출하)이며 펼치면 팔렛×품번·품명·수량, 품번을 누르면 LOT·출발 위치를 확인한다. 출하 KPI는 묶인 행 수, LOT·수량은 내부 합계로 대조한다. 기존 Shipment ID 열이나 LOT별 기본 행을 요구하지 않는다. 저장은 계속 `FG_Shipment`와 LOT별 OUT 거래(`SourceID=ShipmentID`)로 대조한다.
- **이력**(`FG104-TC-001/003/004`, `E2E-TC-002/004`): SHIPPED는 DCR별 1행(없으면 개별 출하), Reference는 DCR 번호/출고 바코드, Details는 팔렛·LOT 수 요약이다. 품번·위치로 검색한 묶음도 목록의 품번·위치 칸은 비어 있을 수 있으며 펼침에서 내부를 확인한다. Shipment ID 검색을 요구하지 않는다. PUT AWAY는 거래 ID, RETURN은 Return ID 기준을 유지한다. 기본 기간은 서버 날짜 전후 1개월이고 Events·Shipped는 표시 행 단위다.
- **트레일러 미리보기**(`FG101-TC-009`): 이동·추가는 화면 메모리 적재안만 바꾼다. 같은 페이지에서 닫았다 다시 열면 편집 상태를 유지하며 조회 조건 변경·새로고침 시 재계산한다. DCR·오더·재고 DB는 바꾸지 않는다.
- **미출하 KPI**(`FG105-TC-001`): 표시 오더 중 `Open`＋`Loading`이며 DCR 배정 여부와 무관하다. `Shipped`는 제외한다. DCR 배정 KPI는 별도이므로 두 KPI는 중복될 수 있다.
- **위치별 고객 혼적 허용**(`FG002-TC-019`, 10-29 사용자 결정): 같은 FG 위치에 다른 고객 제품을 적재할 수 있다. FG 상세 위치·활성·용량 검사는 유지하며, 출고 Unit의 고객 혼재 검사는 별개로 유지한다.
- **FG FIFO는 권고·확인 후 진행**(10-29 사용자 결정): 동일 고객·품번의 양수 재고를 `WH_Inventory.ReceivedAt` 순으로 비교한다. 활성 FG 상세 위치의 가용 LOT만 추천하며 검사 대기·불량·위치 미지정·다른 DCR 적재 재고와 내용물이 부적합한 팔렛은 제외한다. 생산 고객을 판별할 수 없는 후보를 다른 고객 재고로 임의 간주하지 않는다. PDA Outbound 스캔에 오래된 팔렛/LOT·위치·등록일을 표시하고, 출고 확정 및 DCR 적재·출하 확정 시 서버가 다시 조회해 FIFO Warning의 CONTINUE/CANCEL로 확인한다. CONTINUE는 FIFO만 예외 진행하며 고객·내용물·수량·중복·검사 차단은 유지한다. 반품 재입고·수리 양품은 재등록 시각을 사용하고 기존 재고의 등록일을 소급 보정하지 않는다.
- **FG 미출하 납기 위험은 웹 조회**(10-29 사용자 결정): Shipment Orders의 Delivery Risk와 Create Order의 배지로 표시한다. 출하 오더 미생성·미배정 수주도 포함하며 `PP_CustomerOrder.SoID`별 `RequestedDeliveryDate`와 `OrderQty-ShippedQty`가 기준이다. DB의 오늘보다 이전이면 Overdue, 오늘부터 다음 공장 근무일(`SYS_FactoryCalendar`/`WorkdayCalendar`, 미등록일은 기존 평일 규칙)까지 Due Soon, 이후 Normal이다. 취소·마감·출하 완료·잔량 0은 제외하고 납기·수량·고객·품번 누락은 Check Data다. 오더 생성·DCR 배정·적재만으로 출하 누계를 줄이지 않으며 수주 미연결 개별 출고를 동일 품번 수주에 임의 배분하지 않는다. 조회/새로고침 시 판정하며 PUSH·자동 재시도·추가 Worker는 만들지 않는다.
- **재작업 수리 양품 재고 복구**(`FG001-TC-012`, 10-29 사용자 결정): POP 수리 완료와 같은 트랜잭션에서 활성 ASSY의 `WH_Inventory`를 `PART`·1 EA·팔렛/부모/위치 미지정으로 등록한다. 기존 0수량 행은 복구하고 양수 행은 중복 증가시키지 않는다. `IN` 거래(`SourceType='PROD_REWORK'`, `SourceID=LotID`, 위치 NULL)를 남기며 실패 시 생산 실적·LOT 확정·재고·판정을 모두 롤백한다. ASSY Ready는 이 등록 재고를 기존 기준으로 표시하고 Set Location에서 FG 상세 위치를 지정한다. 비ASSY의 종전 재작업 재고 동작은 변경하지 않는다. 기존 수리 완료 LOT를 소급 등록하는 DB 작업은 별도 승인 전 실행하지 않는다.

---

### AMES.Api — REST API

Minimal API, Bearer Token 인증. 기본 포트: `https://localhost:7xxx`

| 그룹 | prefix | 설명 |
|------|--------|------|
| Auth | `/api/auth` | 로그인, 토큰 발급 |
| WH | `/api/wh` | 창고 (PDA용) |
| FG | `/api/fg` | 완성품 출하 |
| PP | `/api/pp` | 생산계획 |
| MNT | `/api/mnt` | 설비보전 |
| RPT | `/api/rpt` | 보고서 |
| SYS | `/api/sys` | 시스템 관리 |

Health check: `GET /api/health`

---

- **출하 오더 재고 검사는 「적재 가능 재고」 기준**(11-18 사용자 결정): 오더 생성·수정의 재고 = `WH_Inventory` 양수 수량 중 등록된 활성 FG 상세 위치(레벨 2)에 지정된 것(반품 검사 대기 제외, `FinishedGoodsRepository.LoadableLocation`). 위치 미지정(Set Location 전) 재고는 PDA 적재(52006)가 막으므로 세지 않는다 — 생성 팝업 재고 칸에 「+N 위치 미지정」으로만 보인다(`ListShippableStock`). PP-003·APS 의 재고 요약(`PpRepository.ListFgStockSummary`)은 위치 무관 기준 그대로. 재고 부족 승인(override)은 그대로 쓴다.
- **DCR 해제**(11-18): FG-03 DCR 상세 팝업의 「DCR 해제」(권한 `fg/shipping-orders` E, 사유 필수, 감사 `FG-03/RELEASE`) → `FinishedGoodsRepository.ReleaseShippingDelivery`. 아직 하나도 싣지 않은(Open·`FG_DeliveryLoad` 0건) DCR 만 가능 — 오더 `DeliveryID` 를 비워 미배정 Open 으로 되돌리고 DCR 은 `Cancelled`. 실은 팔레트가 있으면(Loading) 막으니 PDA 에서 내린 뒤 해제. 출하 완료는 되돌리지 않는다. 해제된 DCR 번호로는 PDA 적재가 거부된다.
