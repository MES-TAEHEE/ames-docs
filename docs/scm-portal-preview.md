# EOS 구매·발주 / 협력업체 포털 화면 미리보기

SCM-001/002는 기존 WH_PurchaseOrder의 실제 발주를 저장·조회한다. 포털은 실제 발주 조회, 수주 확인, 납품 예정 등록과 조회를 지원한다. 납품서 수정·취소도 DB에 반영한다. 출하 확정도 DB에 저장하며 입고·검수는 후속 구현이다. SAP 호출은 하지 않는다.
포털은 내부 Admin이면 발행된 전체 발주를 조회하고, 외부 계정은 SCM_PortalVendorUser에 연결된 활성 업체의 발주만 조회한다. 아직 실제 외부 계정 연결 데이터는 없다.
발주 한 건에 여러 품목 행을 추가할 수 있다. 업체·납기·납품장소는 발주 공통 정보이며, 품목별 수량·단가·금액과 합계를 표시한다. 수량 합계는 단위별로 구분하며, 납품 잔량도 품목 행별로 관리한다.

구매발주 작성의 품목 선택은 실제 MD_Item에서 ItemType='MATERIAL'인 항목만 조회한다.
품명·단위는 선택한 마스터에서 자동 적용하며, 저장과 발행 시 유형을 다시 확인한다.
품목 선택 시 단가는 MD_Item.UnitCost를 적용하고 NULL이면 0으로 표시한다.
SCM-003 발주품목 관리는 MATERIAL 품목과 MD_Vendor 업체를 연결하는 실제 DB 관리 화면이다. 단가는 MD_Item.UnitCost를 참고용으로 표시하고 NULL은 0으로 표시한다. 업체 연결 및 해제는 즉시 저장한다.

| 코드 | 메뉴 | 경로 |
|---|---|---|
| SCM-001 | 구매발주 관리 | /scm/purchase-orders |
| SCM-002 | 발주 진행 현황 | /scm/order-progress |
| SCM-003 | 발주품목 관리 | /scm/purchase-items |
| PORTAL-001 | 발주 조회·수주 확인 | /portal/orders |
| PORTAL-002 | 납기별 발주 현황 | /portal/due-orders |
| PORTAL-003 | 납품서 등록 | /portal/delivery-entry |
| PORTAL-004 | 납품서 조회·수정 | /portal/deliveries |
| PORTAL-005 | 입고·검수 결과 | /portal/receipts |

- EOS 내부: 임시 발주 생성/수정, 발행, 발주 취소, 진행 현황/납품 이력 조회.
- 포털: 실제 발행된 발주와 납기별 현황 조회. 수주 확인 및 여러 품목의 납품 예정 등록·조회.
- 한국어/영어/스페인어, 기존 메뉴·카드·그리드·테마 사용.
- 실제 DB 반영: dist/migrate_scm_portal_screens.sql.
  SYS_Screen의 SCM/PORTAL 화면과 PROCESS 코드. 이후 로그인 마이그레이션으로 SCM-004를 추가한다.
  기존 portal/shipment-plan 등록과 권한은 삭제하고, 포털 번호를 001~005로 정리한다.
- 내부 SCM은 화면 권한으로 관리한다. 포털은 PortalAccess 인증과 업체별 데이터 접근 검사를 사용한다.
  다른 내부 역할의 권한은 기존 SYS 역할/권한 관리 화면에서 별도로 지정한다.
- 스크립트는 트랜잭션으로 실행되며 코드/경로 충돌 시 중단한다.
  재실행 시 화면을 갱신하고 누락된 권한만 추가한다. 기존 권한은 덮어쓰지 않는다.

로컬 확인:
dotnet build src/06_Web/AMES.Web/AMES.Web.csproj -c Release --no-restore -m:1 -nr:false
dotnet run --project src/06_Web/AMES.Web/AMES.Web.csproj -c Release --no-build --no-launch-profile --urls http://localhost:5088

후속 작업은 EOS 입고·검수 연계와 납품서 출력이다.

## 발주번호 채번
- 신규 발주는 첫 저장에서 PO-YYYYMMDD-0001 형식으로 발급한다.
- DB 서버 날짜 기준, 날짜별 0001~9999. 한도 초과 시 발급을 거부한다.
- dbo.SCM_PurchaseOrderSequence에 마지막 순번을 저장하고 트랜잭션 잠금으로 동시 발급을 직렬화한다.
- 적용 스크립트: dist/migrate_scm_purchase_order_sequence.sql.
- 창 열기·화면 입력 검증 실패·기존 발주 수정에는 신규 번호를 사용하지 않는다. DB 검증 실패는 예약 번호를 소비할 수 있다.
- 이미 발급한 번호는 취소·화면 초기화 후에도 재사용하지 않는다.
- 발주 내용과 채번 기록은 DB에 영구 저장한다. 저장 트랜잭션 실패 시 발주 행은 롤백되지만 별도로 예약한 번호에는 공백이 생길 수 있다.


## SCM-003 품목·업체 연결

- `dist/migrate_scm_item_vendor.sql`로 `dbo.SCM_ItemVendor`를 생성한다. 초기 매칭 데이터는 넣지 않는다.
- 기본키 `(ItemNo, VendorID)`: 한 품목에 여러 업체, 한 업체에 여러 품목을 연결한다. 중복은 방지한다.
- ItemNo는 MD_Item, VendorID는 MD_Vendor에 외래키로 연결한다. 삭제 연쇄 동작은 없다.
- ActiveFlag, CreatedBy/TS, ModifiedBy/TS로 사용 상태와 등록·수정 정보를 남긴다. 해제는 ActiveFlag=0, 재연결은 기존 행을 활성화한다.
- 등록 시 MATERIAL 유형과 업체 사용 여부를 DB에서 확인한다. 업체 유형은 제한하지 않는다(현재 마스터에는 SUPPLIER/CKD/LOCAL이 존재).
- 화면에는 품목별 연결 업체, 단위, 참고 단가가 표시되며 품목코드·품목명·업체로 검색한다. 단가 수정 기능은 업체 매칭 기능으로 대체했다.
- SCM-003 편집 권한으로 연결·해제를 제한하고 SYS_AuditLog에 LINK/UNLINK 이력을 남긴다.
- SCM-001의 협력업체 선택은 MD_Vendor의 사용 중인 업체를 조회하고 VendorID를 보관한다. 조회 필터와 발주 목록/상세에는 업체코드와 업체명을 표시한다. 저장·발행 시 업체 사용 여부를 다시 검증한다. 발주 가능 품목은 선택한 업체의 활성 SCM_ItemVendor 매칭과 MATERIAL 유형을 모두 충족한 품목으로 제한한다. 업체 변경 시 기존 발주 품목 행을 초기화하고, 저장·발행 시 최신 매칭을 다시 확인한다. 미연결 업체는 품목 선택을 비활성화하고 SCM-003 연결 안내를 표시한다.
- 업체 선택은 스크롤 가능한 체크박스 목록이며 여러 업체를 한 번에 연결한다. 일괄 연결은 하나의 DB 트랜잭션으로 저장되어 실패 시 전체 롤백한다.



## WH_PurchaseOrder 실제 저장

- 새 헤더/품목 테이블을 만들지 않고 WH_PurchaseOrder에 동일 PoNumber와 품목별 PoLineNo로 저장한다.
- dist/migrate_scm_wh_purchase_order.sql: DeliveryDestination(nvarchar(200)), ScmRowVersion(rowversion)만 추가한다.
- 저장 버튼은 Draft(화면: 작성중) 상태를 DB에 저장한다. 발행은 Open, 취소는 Cancelled로 기존 테이블을 갱신한다. 기존 Partial은 입고 진행, Complete/Received는 마감으로 표시한다.
- 기존 발주도 조회한다. 수정은 Draft이고 입고수량/입고 패키지 연결이 없는 경우만 가능하다. 기존 품목 행의 PoID를 보존하며 삭제된 품목만 제거한다.
- 저장·상태 변경은 트랜잭션으로 처리하고 전체 품목 행의 rowversion으로 동시 수정·입고 충돌을 검출한다. 활성 업체/MATERIAL/활성 매칭과 단위를 DB에서 재검증한다.
- 수량은 decimal(12,3), 단가는 decimal(14,4) 범위를 검증한다. 신규 통화는 USD. 기존 발주 상세는 저장된 Currency를 표시한다.
- SCM 내부 진행 수량은 WH_PurchaseOrder.ReceivedQty를 사용한다. 포털도 실제 발주를 조회한다. 납품 예정은 SCM_Delivery/SCM_DeliveryLine에 저장하며 신규 입고 처리는 후속 구현이다.
- SYS_AuditLog에 생성·수정·발행·취소를 기록한다. 테스트 발주는 검증 후 제거하되 사용한 발주번호는 재사용하지 않는다.


## 포털 실제 발주 조회

- `dist/migrate_scm_portal_vendor_user.sql`: 사용자 ID와 VendorID를 연결하는 SCM_PortalVendorUser를 생성한다. 연결 데이터는 자동 생성하지 않는다.
- 외부 계정의 NameIdentifier와 활성 매칭/업체를 SQL 조건으로 확인한다. 미연결·해제 계정에는 발주가 보이지 않는다. 브라우저의 업체 선택값은 접근권한으로 사용하지 않는다.
- 전체 조회는 내부 Identity 인증의 Admin에게만 허용한다. 화면 접근에는 기존 화면 권한을 함께 적용한다.
- Draft는 포털에서 제외한다. Open/Partial/Complete/Received/Cancelled 발주만 조회하며 실제 입고수량도 표시한다.
- 데모 발주·납품 데이터를 제거했다. 수주 확인과 납품 예정은 DB에 저장한다. 입고·검수 처리는 아직 연결하지 않았다.

## 수주 확인

- PORTAL-001에서 발주 선택 → 수주 확인 → 확인 저장. 내부 Admin은 '대리 수주 확인'으로 표시하고 관리자 본인을 확인자로 기록한다.
- 기존 WH_PurchaseOrder에 SupplierConfirmedAt, SupplierConfirmedBy, SupplierConfirmedUserID를 추가한다. 동일 발주번호의 전체 품목에 한 트랜잭션으로 기록하며 기존 Status와 입고수량은 변경하지 않는다.
- Open/Partial만 확인 가능하다. 외부 계정은 SCM_PortalVendorUser의 활성·잠금 해제 계정 및 활성 업체 연결이 필요하며 저장 시 DB에서 재검증한다. 포털은 PortalAccess 인증 정책을 사용하며, 내부 SCM은 기존 화면 권한을 유지한다.
- 전체 품목 rowversion으로 동시 수정 충돌을 차단한다. 재확인은 기존 확인자·확인일을 덮어쓰지 않는다. SYS_AuditLog에는 CONFIRM 또는 CONFIRM_PROXY를 기록한다.
- 내부 구매발주/진행 화면과 포털에 확인 상태 및 상세 확인자·확인일을 표시한다. 계정과 업체 연결은 자동 생성하지 않는다.

## 납품 예정 등록

- PORTAL-003은 수주 확인 완료된 발주 중 추가 등록 가능한 잔량이 있는 발주만 표시한다. 여러 품목을 선택하여 수량(소수 3자리)과 납품 예정일을 입력한다.
- 기존 FG_DeliveryNote는 고객 출하용, WH_InboundPackage는 LOT/박스 필수 입고용이므로 SCM_Delivery 헤더와 SCM_DeliveryLine을 추가하고 기존 WH_PurchaseOrder.PoID로 연결한다.
- 납품번호는 DB 생성일과 전역 identity로 DN-yyyyMMdd-ID 형태로 발급한다. 등록 상태는 Registered(납품 예정)이며 출하 확정 및 EOS 입고는 하지 않는다.
- 등록 가능 수량 = 발주수량 - 기존 입고수량 - 활성 납품서의 미입고 수량. 추후 입고 구현은 발주 ReceivedQty와 납품품목 ReceivedQty를 같은 트랜잭션에서 갱신해야 한다.
- 발주 잠금/rowversion/요청 ID로 동시 등록, 초과 수량 및 중복 재시도를 막는다. 저장 시 포털 계정의 활성·잠금 상태와 업체 연결을 재검사한다. 관리자 대리 등록은 현재 관리자 ID를 기록한다.
- PORTAL-004는 납품서번호별 한 행으로 조회하고 상세에서 전체 품목의 수량·예정일을 수정한다. 납품번호/발주번호/품목, 납품 상태, 예정일로 검색한다.
- 수정은 기존 품목 구성에 대해 양수 수량(소수 3자리)만 허용한다. 다른 납품서의 예약수량을 제외해 잔량을 재검증한다. 품목 추가·삭제는 지원하지 않는다.
- 납품 예정 상태이며 입고수량/입고 패키지 연결이 없는 경우만 수정·전체 취소할 수 있다. 취소 시 이력은 보존하고 잔량을 복원한다. 출하 확정된 납품서의 수정도 거부한다.
- SCM_Delivery.Version과 발주 전체 rowversion을 검증하고 발주→납품서 순서로 잠근다. 변경자 ID·시간과 SYS_AuditLog 이력을 기록한다.
- 활성 납품서가 있는 발주는 취소할 수 없다. 납품서 전체 취소 후에는 발주 취소가 가능하다.
- 테스트 전용 발주로 복수 품목 저장, 수주 미확인·권한 없는 등록 거부, 중복 재시도, 초과 수량 롤백, 동시 변경, 취소 방지 및 UI 등록·재조회를 검증한다.

## 다른 환경에 적용할 때

앱 실행 전에 대상 DB를 확인하고 다음 마이그레이션을 순서대로 적용한다. 기존 데이터가 있는 DB에서 `AMES_Schema.sql` 전체를 실행하지 않는다.

1. `dist/migrate_scm_portal_screens.sql` — 화면/역할 권한
2. `dist/migrate_scm_purchase_order_sequence.sql` — 발주번호 순번
3. `dist/migrate_scm_item_vendor.sql` — 품목·업체 연결
4. `dist/migrate_scm_wh_purchase_order.sql` — 기존 발주 테이블 추가 컬럼
5. `dist/migrate_scm_portal_user_login.sql` — 새 포털 로그인 계정 및 SCM-004
6. `dist/migrate_scm_order_confirmation.sql` — 수주 확인자·확인일
7. `dist/migrate_scm_delivery.sql` — 납품 예정 헤더·품목
8. `dist/migrate_scm_delivery_edit.sql` — 납품서 동시 수정 방지 및 변경 이력
9. `dist/migrate_scm_delivery_ship.sql` — 실제 출하일·출하 처리자·출하 상태
10. `dist/migrate_scm_delivery_note.sql` — 딜리버리 노트 발행 스냅샷 및 발행 이력

소스 동기화는 업무 데이터나 DB 연결정보를 복사하지 않는다. 외부 포털 계정과 품목·업체 연결은 대상 환경에서 별도로 관리한다.

## 병합 후 인증·출하 처리

- GitHub의 SCM/포털 목록·모달 디자인, SCM-004 외부 사용자 관리, 로그인 및 DbClock.Today 날짜 기준을 유지한다.
- 포털은 SYS_RolePermission이나 ExternalCustomer 역할을 사용하지 않는다. 외부 사용자 식별자는 SCM_PortalVendorUser.UserID(이메일)이며 SQL에서 ActiveFlag, LockedFlag, VendorID를 재검증한다. 내부 대리 처리는 Admin만 허용한다.
- PORTAL-004에서 저장된 납품서의 전체 품목을 실제 출하일과 함께 확정한다. 수정 중인 값은 먼저 저장해야 한다. 미래 출하일·저장되지 않은 수량으로 확정할 수 없다.
- 출하 시 Shipped 상태와 ShipDate/ShippedAt/ShippedBy/ShippedUserID를 저장하고 이후 수정·취소·중복 출하를 차단한다. 발주 ReceivedQty 및 재고는 증가하지 않는다.
- 기존 납품 예정일은 유지하며 출하된 미입고 수량도 잔량 계산에 포함한다. EOS 입고 연계와 출하 취소는 미구현이다.


## 출하 후 딜리버리 노트

- PORTAL-004에서 출하 완료/입고 완료 납품서 선택 → **딜리버리 노트 발행 / 재출력** → **인쇄 / PDF 저장**. PDF는 브라우저 인쇄 대화상자에서 저장한다.
- 최초 발행 시 SCM_Delivery.NoteSnapshot(JSON), NoteIssuedAt, NoteIssuedBy, NoteIssuedUserID를 같은 트랜잭션에 저장한다. 기존 납품번호 DN을 문서번호로 사용한다.
- 공급업체명·코드, EOS, 납품처, 발주번호, 실제 출하일, 품목코드·명·단위·출하수량을 발행 시점 기준으로 보관한다. 재출력은 원본을 읽으며 마스터 변경이나 중복 클릭으로 내용·발행자가 바뀌지 않는다.
- `/portal/delivery-note/{Number}`는 이미 발행된 문서의 조회 전용 경로다. 최초 발행과 재조회 모두 활성·잠금 해제된 동일 업체 계정 또는 내부 Admin인지 DB에서 검증한다. 출하 전 발행은 거부한다.
- 입고수량/재고는 변경하지 않는다. 출하 취소·문서 정정 및 별도 바코드는 이번 범위에 포함하지 않는다.
- 개발 DB 통합검증: 복수 품목, 출하 전 거부, 무권한/관리자 위조/비활성/잠긴 계정 거부, 동시 발행 중복 방지, 납품처 변경 후 원본 유지, 입고수량 불변을 확인했다.


## 복수 납품서 딜리버리 노트 (2026-09-25)

- PORTAL-004 → **딜리버리 노트 발행·조회**에서 동일 협력업체의 미발행 출하 완료 납품서 여러 건(최대 100건)을 선택한다. 발주번호가 달라도 묶을 수 있다.
- `dist/migrate_scm_delivery_note_batch.sql`을 기존 note 마이그레이션 다음에 적용한다. `SCM_DeliveryNote`는 독립 문서 헤더·발행 원본 JSON·발행자/시간, `SCM_DeliveryNoteDelivery`는 포함 납품서 연결을 저장한다. DeliveryID 고유키로 중복 포함을 막는다.
- 신규 문서번호는 `DLN-yyyyMMdd-identity`이며 DB 날짜를 사용한다. 동일 번호를 Code 128 바코드로 인쇄한다. 납품서번호 DN과 구분된다.
- 원본 JSON 각 품목에는 DeliveryNumber, DeliveryLineID, OrderNumber, PoID, 수량, 단위, 품목명, 출하일과 납품처가 포함된다. 바코드 번호 → 문서 → 납품서 → 원본 품목 연결로 추후 입고 연계가 가능하다.
- 재출력은 최초 원본을 유지한다. 기존 SCM_Delivery.NoteSnapshot 단일 문서는 보존하고 목록에서 재출력하며 신규 묶음에 중복 포함하지 않는다.
- EOS 입고 화면, 입고 처리, 재고 테이블 및 발주/납품 입고수량은 변경하지 않았다. 바코드 스캔을 통한 EOS 입고 기능은 이번 작업에 포함하지 않는다.
- 검증: 복수 납품서·품목 묶음, 동시 발행의 동일 결과, 중복 포함 차단, 출하 전 발행 차단, 계정 권한, 원본 재조회, 입고수량 불변, UI 선택 발행 및 바코드 표시. 실제 스캐너 하드웨어 테스트는 별도다.

## 포털 입고 결과 조회 (SCM 수량 반영 전제)

- PORTAL-005는 SCM_DeliveryLine.ReceivedQty를 EOS가 반영한다는 전제로 조회한다. 납품 품목 수량과 입고수량으로 미입고/부분입고/입고완료 및 잔량을 계산한다. WH_PurchaseOrder의 누적 입고수량을 개별 납품서에 배분하지 않는다.
- 출하된 납품서 또는 입고수량이 있는 납품서를 조회하며 취소 건은 제외한다. 딜리버리 노트/납품서/발주/품목/업체 검색, 출하일·입고 상태 필터, 새로고침, 원본 문서 조회를 제공한다.
- 상세는 선택한 딜리버리 노트의 전체 품목을 표시한다. 합계는 단위별로 분리하고 문서 전체의 상태는 모든 품목의 입고 진행으로 계산한다.
- 활성·잠금 해제된 업체 계정만 자기 업체를 조회한다. 내부 Admin은 전체 조회한다. 상세를 열 때 DB에서 조회 권한과 수량을 다시 확인한다.
- 검수 판정·합격/불합격 수량·입고일시의 저장 계약은 아직 없으므로 미연계로 안내한다. 입고완료를 검수 합격으로 표시하지 않는다.
- EOS 입고 코드 및 DB 스키마는 수정하지 않았다. 검증용 독립 납품서의 입고수량만 테스트 후 정리한다.

## 딜리버리 노트 인쇄 호환성

- 인쇄 버튼은 사용자의 클릭에서 직접 window.print()를 호출한다. 서버 왕복 후 JS를 호출하던 경로를 제거했다.
- 인쇄창이 없는 내장 브라우저를 위해 인쇄용 HTML 다운로드를 제공한다. 화면에 이미 권한 검증 후 표시된 문서와 스타일·바코드만 포함하며, 앱 스크립트·쿠키·로그인 정보는 포함하지 않는다.
- 내려받은 HTML은 Chrome/Edge에서 열어 인쇄하거나 PDF로 저장한다. 다운로드 파일 자체는 PDF가 아니다.

### PORTAL-006 적입량 관리
- `/portal/packing-quantities`: 로그인 업체에 연결된 활성 MATERIAL 품목의 적입량을 행별 저장. 내부 Admin은 전체 업체 조회/수정 가능.
- `dist/migrate_scm_packing_qty.sql`: `SCM_ItemVendor.PackingQty decimal(18,3) NULL` 추가 및 화면 목록 등록. 미등록은 NULL, 입력은 양수/소수점 3자리까지 허용.
- 저장 시 업체 접근권한, 활성 연결 및 원래 적입량을 재검사하여 권한 없는 변경과 동시 수정 덮어쓰기를 방지한다.
- 납품서 수량 자동계산이나 EOS 입고 처리에는 아직 연결하지 않는다.
- 검증: 업체 범위, 저장/재조회, 미인증 저장 차단, 충돌 차단, 입력값 검증을 트랜잭션 롤백 방식으로 확인.

### 외부 포털 수량 표시
- 발주/납품/입고 목록·상세·합계, 적입량 입력 및 딜리버리 노트(인쇄/HTML 포함)의 수량은 `MD_Uom.DecimalPrec`를 따른다. 단위 설정이 없으면 3자리를 사용한다.
- 공통 `PortalQuantityFormat`은 단위 설정을 최대 1분 캐시하며, 표시만 반올림하고 원본 수량과 금액/단가 형식은 변경하지 않는다.

### 납품서 등록의 박스수량
- PORTAL-003 등록 품목에 해당 발주 업체의 적입량과 계산 박스수를 표시한다.
- 박스수 = Ceiling(납품수량 / 적입량). 올림한 총 박스수를 표시하며, 미등록/0 적입량은 납품서 등록을 차단한다.
- 화면 계산값이며 납품서 저장 구조/기존 수량/입고 처리는 변경하지 않는다.
- 원본 SCM 기준 보완: 등록 선택 품목에 활성 업체 매칭과 양수 적입량이 반드시 있어야 한다. 화면과 등록 트랜잭션 양쪽에서 검사하며, 박스수는 올림한 총수만 표시한다. 적입량/박스수의 납품서 스냅샷과 박스별 목록 저장은 아직 구현하지 않았다.

### 납품서 박스 저장·식별표 발행
- `dist/migrate_scm_delivery_boxes.sql`: SCM_DeliveryLine.PackingQty에 등록 당시 적입량을 보관하고 SCM_DeliveryBox에 박스번호(BOX-{BoxID}), 품목·품명·단위·실수량·순번을 저장한다.
- 신규 등록 트랜잭션에서 박스를 분할 생성한다. 품목당 최대 1,000박스이며 마지막 박스에는 잔량을 저장한다. 재시도는 같은 납품서/박스를 반환한다.
- 등록 상태 수량 수정 시 기존 적입량을 유지하고 해당 품목의 이전 박스를 무효화한 후 새 번호로 생성한다. 수량이 같으면 번호를 유지한다. 취소 시 활성 박스를 무효화한다. 출하 확정은 기존 박스번호를 유지한다.
- `/portal/delivery-boxes`에서 납품서/박스 목록 조회 및 선택 출력. 신규 등록 후 해당 납품서 목록으로 이동한다. `/portal/box-labels`는 선택된 활성 박스에 대해 업체 권한을 다시 확인하고 Code128 바코드 식별표를 A4 인쇄/PDF 및 HTML 다운로드로 제공한다. 별도 프린터 드라이버/ZPL 직접 출력은 포함하지 않는다.
- 기존 납품서에는 자동 소급 생성하지 않는다. 등록 상태의 기존 납품서를 수정 저장하면 현재 적입량으로 최초 생성한다. 출하/입고 이력은 변경하지 않는다.
- 검증: 별도 테스트 납품서로 분할·잔량·재조회·중복 재시도·업체 격리·상한 롤백·수정/무효화·동일수량 번호 유지·취소 차단을 확인했다.

### 납품 품목 LOT / 제조일자
- `migrate_scm_delivery_trace.sql`: SCM_DeliveryLine.VendorLotNo(nvarchar(30)), ProductionDate(date). 기존 빈 값은 납품일자로 보완.
- 납품 등록·수정에서 품목별 입력. 기본 LOT는 yyyyMMdd, 제조일자는 납품일자. 기본값과 동일한 값은 납품일 변경 시 따라가며 다른 입력값은 유지.
- 박스 식별표는 연결된 납품 품목의 저장 LOT와 제조일자(FIFO 표시)를 조회. 출하 후 수정 제한은 기존과 동일. EOS 입고 로직 변경 없음.

### 박스 식별표 양식
- 서연이화 SAV PDF 기준 Letter 세로, 동일 박스 좌우 2매 × 3행. 3박스마다 페이지 구분.
- 업체명, 납품일, 박스 바코드, 품번/품명, 제조일자(FIFO), 납품서번호, 수량/단위, 전체 박스 기준 순번, 납품장소, 업체 LOT, 품번 바코드, EOS/출력일시 표시.
- 회사명과 식별번호는 EOS 실제 값 사용. 원본의 납품차수/지역 워터마크는 대응 값이 없어 임의 생성하지 않음.
- 인쇄용 HTML 다운로드에도 동일한 용지/페이지 구분 적용.

### 기존 설치 업데이트 순서 (2026-09-25)
1. dist/migrate_scm_packing_qty.sql
2. dist/migrate_scm_delivery_boxes.sql
3. dist/migrate_scm_delivery_trace.sql

신규 생성용 dist/AMES_Schema.sql에도 위 변경과 PORTAL-006 등록을 포함합니다. 기존 데이터가 있는 환경에서는 전체 생성 스키마 대신 위 마이그레이션을 사용합니다.
