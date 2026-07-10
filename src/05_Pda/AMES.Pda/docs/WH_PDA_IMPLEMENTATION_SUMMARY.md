# WH PDA Implementation Summary

작성일: 2026-07-07

최근 업데이트: 2026-07-10

대상 프로젝트: `C:\Users\Young\Desktop\Seoyon\Jackson\ames-docs\src\05_Pda\AMES.Pda`

주요 구현 파일:

- `Components/Pages/Login.razor`
- `Components/Pages/Wh/Wh01InboundSchedule.razor`
- `Components/Pages/Wh/Wh02PdaInbound.razor`
- `Components/Pages/Wh/Wh03InventoryStatus.razor`
- `Components/Pages/Wh/Wh04LocationMap.razor`
- `Components/Pages/Wh/Wh08TransactionHistory.razor`
- `Components/Pages/Wh/WhHome.razor`
- `Services/PdaApi.cs`
- `wwwroot/css/pda.css`
- `..\..\04_Api\AMES.Api\Endpoints\AuthEndpoints.cs`
- `docs/sql/WH002_ADJUST_QTY.sql`

테스트 DB 기준:

- Database: `AMES_DEV`
- Schema: `SIS_TEST`
- 주요 기준 코드: WH Location 쪽은 `CORCD = 5010`, `BIZCD = 5011`
- WH001 PO 조회 쪽은 기존 `WM40120` 기준에 맞춰 `CORCD = 1000`, `BIZCD = 5011`로 호출

## 공통 Login / PIN Auth

### 화면 목적

PDA 로그인은 작업자가 Employee No와 4자리 PIN을 입력해 PDA 세션을 생성하는 공통 진입 화면이다.

### 현재 구현된 기능

- 기본 Employee No는 개발 테스트용 `E001`이다.
- PIN은 4자리 숫자만 유효하다.
- 4번째 PIN 입력 시 자동으로 로그인 요청을 보낸다.
- 로그인 요청 중에는 Employee No 입력과 키패드를 비활성화해 중복 submit을 막는다.
- `OK` 버튼을 눌러도 현재 PIN 값으로 같은 검증을 수행한다.
- 실패 시 PIN을 초기화하고 영어 메시지를 표시한다.

### 사용하는 API, DB, Table

PDA 코드 호출:

- `PdaApi.LoginAsync(employeeNo, pin, terminalId, lineId, shiftCode)`

API 코드:

- `AMES.Api.Endpoints.AuthEndpoints.MapAuth()`
- `POST /api/auth/login`
- `GET /api/auth/me`

DB/Table:

- `dbo.AspNetUsers`: 사용자 계정 및 PIN hash
- `dbo.SYS_UserProfile`: Employee No, 이름, 라인 권한, 계정 상태, 실패 횟수
- `dbo.PR_PopSession`: 로그인 성공 시 세션 생성
- `dbo.PR_PopAuthLog`: 로그인 성공/실패 감사 로그

### 이번 변경점

- 기존에는 PIN 4자리 자동 submit을 fire-and-forget 방식으로 실행해, 빠른 입력이나 `OK` 중복 클릭 시 PIN 길이 검증과 실제 입력 상태가 엇갈릴 수 있었다.
- 현재는 4자리 PIN 값을 snapshot으로 잡은 뒤 `await` 처리하고, submit 중 키패드를 잠가 중복 호출을 막는다.
- API가 DB 연결 오류 등으로 예외를 던져도 `HTTP 500` stack trace가 PDA에 그대로 보이지 않도록 API와 PDA client 양쪽에서 메시지를 정리했다.
- `PdaApi.LoginAsync()`는 `HTTP 500`, invalid JSON, `/me` 실패를 사용자용 메시지로 변환한다.
- API `AuthEndpoints`는 login 내부 예외를 잡아 `Authentication service unavailable` 사유를 반환한다.

### 참고 및 주의

- 현재 로컬 개발 DB는 `.\SQLEXPRESS`의 `AMES_DEV` 기준으로 확인했다.
- 개발 테스트용으로 `E001 / 1234` 계정 profile을 로컬 DB에 추가했다.
- 운영 반영 시에는 실제 계정 생성/권한 정책과 PIN hash 배포 방식을 별도로 정해야 한다.

## 전체 창고 플로우 요약

현재 Warehouse PDA는 WH001, WH002, WH003, WH004가 아래 흐름으로 연결된다.

1. SCM/SIS PO 데이터가 생성된다.
2. WH001에서 PO 입고 예정 및 입고 진행 상태를 조회한다.
3. WH002에서 작업자가 LOT No와 Location No를 스캔하고 입고 처리한다.
4. 입고 처리 결과가 `WMS2010`, `WMS2020`, `AMM2010`에 반영된다.
5. 이미 입고된 LOT을 WH002에서 다시 스캔하면 입고 취소, 위치 변경, 수량 조정을 처리할 수 있다.
6. WH003에서 현재 재고 기준 테이블인 `WMS2000`을 기준으로 자재별 재고 상태를 조회한다.
7. 특정 창고 위치 기준으로 재고를 찾고 싶을 때는 WH004 `Location Search`에서 Area/Level/Column/Row 조건으로 위치와 해당 위치의 품목을 조회한다.
8. 입고, 출고, 수량 조정 이력은 WH008 `Transactions`에서 LOT, 품번, 작업자, 날짜 범위, 조정 사유 기준으로 조회한다.

테스트 구현에서는 기존 Oracle SIS/PDA 구조를 SQL Server `SIS_TEST` 스키마에 필요한 범위만 복사/재현해서 연결했다.

## Warehouse Menu

### 화면 목적

Warehouse 대메뉴는 PDA 창고 업무의 진입점이다. 기존에는 기능명이 단순 버튼처럼 나열되어 있었고, 화면 코드(`WH001`, `WH002`) 중심으로 보일 수 있어 실제 작업자가 업무를 고르기 애매했다.

현재는 메뉴명을 작업 기준으로 정리하고 카드형 UI로 바꿨다.

- `Schedule`: WH001 입고 예정/PO 진행 현황
- `Scan`: WH002 LOT/Location 스캔, 입고, 위치 변경, 수량 조정
- `Inventory`: WH003 자재별 현재 재고와 Min/Max 상태
- `Location Search`: WH004 위치 조건별 재고 검색
- `Release Schedule`: 출고 예정
- `Release`: 출고 처리
- `Transactions`: 입출고/재고 변경 이력

### 구현 변경점

- `Location Map`은 삭제하지 않고 기능은 유지하되, 메뉴명과 화면 타이틀을 `Location Search`로 변경했다.
- WH001/WH002/WH003 같은 코드명은 대메뉴 카드에 직접 노출하지 않고, 사용자가 이해하기 쉬운 업무명으로 표시했다.
- Radzen icon을 붙인 흰색 카드형 UI로 변경해 PDA 앱 화면처럼 보이도록 했다.
- 각 카드에는 짧은 설명을 추가해 신규 개발자나 작업자가 화면 역할을 빠르게 이해할 수 있게 했다.

## WH001 Inbound Schedule

### 화면 목적

`WH-001 INBOUND`는 PO 기준 입고 예정 목록과 진행 상태를 PDA 카드 형태로 보여주는 화면이다.

기획 문서의 `WH-01 Inbound Schedule` 역할에 맞춰, 기존 SIS의 PO/back order 조회 화면인 `WM40120`을 PDA 화면에 맞게 축약했다.

### 현재 보여주는 정보

각 PO 카드에 아래 정보를 표시한다.

- PO No
- Unit
- ETA D-Day
- Status: `In Progress`, `Complete`, `Late`
- Material No
- Material Desc
- Car
- PO Qty
- GR Qty

상단에는 `PO RECEIPT STATUS` 요약을 표시한다.

- Total
- In Progress
- Complete
- Late

헤더에는 `Last Updated HH:mm:ss`를 표시하고, `REFRESH` 버튼으로 수동 갱신할 수 있다. 자동 갱신 주기는 테스트 중 1분으로 두었다가 최종적으로 1시간으로 변경했다.

하단 고정 버튼은 처음 `SCAN`이었으나, 실제 역할에 맞춰 `RECEIVE`로 변경했고 누르면 WH002로 이동한다.

### 사용하는 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.WhInboundScheduleAsync()`
- 내부 DB 호출: `SIS_TEST.APG_WM40120_INQUERY_VENDER_BACK_ORDER`

프로시저 입력:

- `@IN_CORCD = 1000`
- `@IN_BIZCD = 5011`
- `@IN_YYYY`
- `@IN_QUATER`
- `@IN_VENDCD`
- `@IN_LANG_SET = EN`

프로시저가 참고하는 주요 테이블:

- `SIS_TEST.AMM1040`: PO 라인 정보
- `SIS_TEST.AMM2010`: GRN 입고 실적
- `SIS_TEST.ACD0020`: 자재 마스터, 차종 정보
- `SIS_TEST.ACD0020L`: 자재명 다국어 정보
- `SIS_TEST.ACD0070L`: 업체명 다국어 정보

주요 출력 컬럼:

- `PONO`
- `PONO_SEQ`
- `PARTNO`
- `PARTNM`
- `VINCD`
- `PO_DELI_DATE`
- `PO_DATE`
- `PO_UNIT`
- `PO_QTY`
- `GRN_QTY`
- `NON_DELI_QTY`
- `VENDCD`
- `VENDNM`

### 기존 프로그램에서 참고한 점

SIS:

- 화면: `WM40120`
- 역할: 업체별 PO/back order 조회
- 기존 패키지: `APG_WM40120`
- 참고한 핵심 컬럼: `PONO`, `PONO_SEQ`, `PARTNO`, `PARTNM`, `VINCD`, `PO_DELI_DATE`, `PO_DATE`, `PO_UNIT`, `PO_QTY`, `GRN_QTY`, `NON_DELI_QTY`

기존 SIS는 그리드 형태로 많은 컬럼을 가로로 보여준다. PDA 화면에서는 같은 데이터를 그대로 긴 테이블로 보여주면 사용성이 떨어지므로 카드형 UI로 변경했다.

### 현재 카피/재현한 것

SQL Server `SIS_TEST`에 아래 프로시저와 관련 테이블 데이터를 준비했다.

- `SIS_TEST.APG_WM40120_INQUERY_VENDER_BACK_ORDER`
- `SIS_TEST.AMM1040`
- `SIS_TEST.AMM2010`
- `SIS_TEST.ACD0020`
- `SIS_TEST.ACD0020L`
- `SIS_TEST.ACD0070L`

### 상태 기준

WH001 상태는 아래 기준으로 계산한다.

- `Complete`: `NON_DELI_QTY <= 0`
- `Late`: 완료가 아니고 `PO_DELI_DATE < 오늘`
- `In Progress`: 완료도 아니고 지연도 아닌 상태

ETA D-Day는 `PO_DELI_DATE` 기준이다.

- D-3 이하: 노란색 경고
- D-0 또는 지난 일정: 빨간색 계열 강조
- 완료 건: `Done`

### 기존과 달라진 점

- SIS의 긴 그리드 UI를 PDA 카드 UI로 변경했다.
- PO Create Date, Arr.Date, Non-deliver Qty는 카드에서 제거하고 핵심 수량인 `PO Qty`, `GR Qty`만 남겼다.
- `ETA D-Day`를 각 카드에 표시하도록 추가했다.
- `Last Updated`와 1시간 자동 갱신을 추가했다.
- 하단 고정 `RECEIVE` 버튼으로 WH002 Scan 화면 이동을 추가했다.

## WH002 Scan

### 화면 목적

`WH-002 SCAN`은 LOT No를 스캔해서 PO/입고 대상 정보를 조회하고, Location No를 스캔해서 실제 입고 처리하는 화면이다.

기존 MAUIPDA의 `Material > Incoming` 흐름을 기반으로 만들었다.

### 현재 구현된 기능

작업 모드:

- `LOCAL`
- `CKD`

기본 플로우:

1. `LOCAL` 또는 `CKD`를 선택한다.
2. `LOT NO SCAN`에 LOT No 또는 관련 바코드를 입력/스캔한다.
3. DB에서 PO/입고 대상 정보를 조회한다.
4. LOT이 PO 리스트에 없으면 커스텀 알럿을 표시한다.
5. LOT이 입고 대상이면 품번, 품명, PO No, PO Seq, Qty, Vendor 등 정보를 카드로 보여준다.
6. LOT 정보가 조회된 뒤에만 Location No 스캔이 활성화된다.
7. Location No를 `WMS1040` 기준으로 검증한다.
8. Location의 WH, Area, Zone, Rack X/Y/Z, Stock Lines, Stock Qty를 표시한다.
9. LOT와 Location이 모두 유효하면 `RECEIVE` 버튼이 활성화된다.
10. 입고 완료 후 `Receive Complete` 알럿을 띄우고 화면을 초기화한다.

이미 입고된 LOT을 다시 스캔한 경우:

- `CANCEL INCOMING` 버튼 표시
- `CHANGE LOCATION` 버튼 표시
- `ADJUST QTY` 버튼 표시
- 현재 위치 표시

Location 변경 플로우:

1. 이미 입고된 LOT을 스캔한다.
2. `CHANGE LOCATION`을 누른다.
3. 기존 아이템 정보는 숨기고 현재 위치 정보와 새 위치 스캔 영역만 보여준다.
4. 기존 위치와 같은 Location No를 넣으면 `Same Location` 알럿을 표시한다.
5. 새 Location No가 유효하면 `CONFIRM`으로 위치 변경한다.
6. 변경 전/후 Location을 알럿으로 보여준 뒤 화면을 초기화한다.

Cancel Incoming 플로우:

1. 이미 입고된 LOT을 스캔한다.
2. `CANCEL INCOMING`을 누른다.
3. 확인 모달 후 입고 취소 프로시저를 호출한다.
4. 성공 시 입고 상태가 취소된다.

Inventory Adjustment 플로우:

1. 이미 입고된 LOT을 스캔한다.
2. `ADJUST QTY`를 누른다.
3. 현재 LOT, Location, Part No, Current Qty를 확인한다.
4. Reason Code, +/- 조정 수량, Supervisor PIN, Note를 입력한다.
5. `POST ADJUST`를 누르면 수량 조정 프로시저를 호출한다.
6. 조정 전 수량, 조정 수량, 조정 후 수량을 이력 테이블에 기록한다.
7. 성공 시 `Adjustment Complete` 알럿을 띄우고 최신 수량으로 화면을 다시 조회한다.

Reason Code:

- `COUNT_DIFF`: cycle count 또는 실사 차이
- `DAMAGED`: 파손
- `LOST`: 분실
- `FOUND`: 발견
- `OTHER`: 기타

UI/상태 관련 구현:

- 기본 브라우저 알럿이 아니라 중앙 카드형 커스텀 모달 사용
- 모든 문구는 영어 기준으로 정리
- `LOCAL`, `CKD` 탭 상태를 각각 유지
- LOT No가 없으면 Location No 스캔 비활성화
- LOT 또는 Location이 유효하지 않으면 `RECEIVE` 비활성화
- Qty는 `540 EA`처럼 수량과 Unit을 같이 표시
- `Source`, 하단의 `Location OK`, `Scan OK` 같은 개발용 문구 제거
- 테스트 버튼은 최종 UI에서 제거
- WH005의 별도 Inventory Adjustment 화면은 만들지 않고, WH002의 이미 입고된 LOT 후속 작업으로 통합

### 사용하는 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.WhScanInboundAsync(mode, barcode)`
- `PdaApi.WhReceiveInboundAsync(body)`
- `PdaApi.WhMoveInboundLocationAsync(body)`
- `PdaApi.WhCancelInboundAsync(body)`
- `PdaApi.WhAdjustInboundQtyAsync(body)`
- `PdaApi.WhScanLocationAsync(locationId)`

프로시저:

- `SIS_TEST.PDA_WH002_SCAN_LOCAL`
- `SIS_TEST.PDA_WH002_SCAN_CKD`
- `SIS_TEST.PDA_WH002_RECEIVE`
- `SIS_TEST.PDA_WH002_MOVE_LOCATION`
- `SIS_TEST.PDA_WH002_CANCEL`
- `SIS_TEST.PDA_WH002_ADJUST_QTY`

LOCAL 스캔 기준 테이블:

- `SIS_TEST.AMM9011`: Local 박스/LOT 바코드 상세
- `SIS_TEST.AMM9010`: Local 납품서/헤더 정보
- `SIS_TEST.AMM1040`: PO 정보
- `SIS_TEST.WMS2020`: 이미 입고됐는지, 현재 Location이 있는지 확인

CKD 스캔 기준 테이블:

- `SIS_TEST.AMF1030`: CKD 박스/케이스/인보이스/컨테이너 정보
- `SIS_TEST.AMM1040`: PO 정보
- `SIS_TEST.WMS2020`: 이미 입고됐는지, 현재 Location이 있는지 확인

Location 검증 기준 테이블:

- `SIS_TEST.WMS1040`: Location master
- `SIS_TEST.WMS1010`: Warehouse master
- `SIS_TEST.WMS1020`: Area master
- `SIS_TEST.WMS1030`: Zone master
- `SIS_TEST.WMS2020`: 해당 Location의 현재 stock line/qty 집계

입고 처리 시 쓰는 테이블:

- `SIS_TEST.WMS2010`: LOT 입고/재고 헤더 성격
- `SIS_TEST.WMS2020`: LOT별 Location 재고 상세
- `SIS_TEST.AMM2010`: GRN 실적
- `SIS_TEST.WMS2000`: 현재 재고 기준 테이블

입고 취소 시 삭제되는 테이블:

- `SIS_TEST.WMS2020`
- `SIS_TEST.WMS2010`
- `SIS_TEST.AMM2010`

Location 변경 시 갱신되는 테이블:

- `SIS_TEST.WMS2020.LOCATION_NO`, `WHCD`, 작업일시
- `SIS_TEST.WMS2010.WHCD`, 수정일시

수량 조정 시 갱신/기록되는 테이블:

- `SIS_TEST.WMS2020.QTY`: LOT별 Location 현재 수량 갱신
- `SIS_TEST.WMS2000.QTY`: WH003 Current Stock에서 보는 현재 재고 수량 동기화
- `SIS_TEST.PDA_WH002_ADJUST_AUDIT`: 수량 조정 감사 이력 기록

`PDA_WH002_ADJUST_AUDIT` 주요 컬럼:

- `RECEIVE_TYPE`: `LOCAL` 또는 `CKD`
- `LOTNO`, `BARCODE`, `PARTNO`, `LOCATION_NO`
- `BEFORE_QTY`, `DELTA_QTY`, `AFTER_QTY`
- `REASON_CODE`, `REASON_NOTE`
- `SUPERVISOR_PIN_MASK`: Supervisor PIN 원문이 아닌 마스킹 값
- `WORK_DATE`, `WORK_TIME`, `USER_ID`, `INSERT_DATE`

### 기존 프로그램에서 참고한 점

MAUIPDA:

- 메뉴 구조: `Material > Incoming`
- `WMS1111 Local Receiving(SCM)`
  - `MES.PKG_PDA_WMS1111.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1111.SET_LOT_SAVE`
- `WMS1120 CKD Receiving`
  - `MES.PKG_PDA_WMS1120.GET_CASE_INFO`
  - `MES.PKG_PDA_WMS1120.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1120.LOT_SAVE`
- `WMS1130 Locating / Relocating`
  - `MES.PKG_PDA_WMS1130.GET_LOCATION_NO`
  - `MES.PKG_PDA_WMS1130.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1130.SET_ILOT_MOVE`
- `WMS1150 Incoming Cancel`
  - `MES.PKG_PDA_WMS1150.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1150.SET_IN_CANCEL`

MAUIPDA Inventory 쪽 참고:

- `WMS1330 Discrepancy Adjust`
  - `MES.PKG_PDA_WMS1330.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1330.SET_LOT_SAVE`
- `WMS1340 Location Information`
  - `MES.PKG_PDA_WMS1340.GET_LOCATION_NO`

`WMS1330 Discrepancy Adjust`는 LOT 정보를 조회한 뒤 변경 수량을 입력하고 조정 전/후 값을 저장하는 흐름을 가진다. WH002에서는 이를 별도 WH005 화면으로 분리하지 않고, 이미 입고된 LOT을 스캔했을 때 가능한 후속 작업으로 통합했다.

SIS:

- Location master는 SIS에서 관리하고 PDA에서는 스캔/검증만 하는 구조로 판단했다.
- Location No, WH, Area, Zone, Rack 정보는 `WMS1040` 및 `WMS1010/1020/1030` 계층 테이블에서 가져오는 구조를 참고했다.

### 현재 카피/재현한 것

SQL Server `SIS_TEST`에 아래 테이블 및 프로시저를 준비했다.

- `AMM9010`
- `AMM9011`
- `AMF1030`
- `AMM1040`
- `AMM2010`
- `WMS1010`
- `WMS1020`
- `WMS1030`
- `WMS1040`
- `WMS2010`
- `WMS2020`
- `WMS2000`
- `PDA_WH002_ADJUST_AUDIT`
- `PDA_WH002_SCAN_LOCAL`
- `PDA_WH002_SCAN_CKD`
- `PDA_WH002_RECEIVE`
- `PDA_WH002_MOVE_LOCATION`
- `PDA_WH002_CANCEL`
- `PDA_WH002_ADJUST_QTY`

### 기존과 달라진 점

- 기존 MAUIPDA는 여러 화면으로 나뉘어 있던 Local Receive, CKD Receive, Locating, Incoming Cancel 기능을 WH002 한 화면에 통합했다.
- Local/CKD 선택 상태를 탭처럼 두고, 각 탭의 입력 상태를 유지하도록 했다.
- 실제 PDA 스캐너 대신 웹/PDA 테스트가 가능하도록 텍스트 입력 + 버튼 스캔 방식으로 구현했다.
- Location 선택 드롭다운은 제거하고 Location No 스캔 검증 방식으로 변경했다.
- 기본 알럿 대신 중앙 카드형 모달을 사용했다.
- 입고 완료 후 자동 초기화, Clear 버튼, Change Location confirm 플로우를 추가했다.
- WH005로 따로 만들 예정이던 Inventory Adjustment를 WH002의 `ADJUST QTY` 모달로 통합했다.
- 수량 조정 시 현재 재고(`WMS2020`, `WMS2000`)와 감사 이력(`PDA_WH002_ADJUST_AUDIT`)을 함께 갱신하도록 했다.

## WH003 Inventory Status

### 화면 목적

`WH-003 INVENTORY`는 현재 재고 기준으로 자재별 재고 상태를 보여주는 화면이다.

기획 문서의 `WH03`에서 말한 재고 부족/상태 표시를 기존 SIS Current Stock 화면들과 Min/Max 기준 화면을 참고해 PDA 카드 형태로 구현했다.

### 현재 보여주는 정보

상단 기능:

- Material search
- Stock Date From
- Stock Date To
- Clear
- Apply
- Refresh
- Last Updated

WH003은 현재 날짜 필터 중심으로만 동작한다. 위치 조건으로 재고를 찾는 기능은 WH004 `Location Search`로 분리했다.

상태 요약 카드:

- Total
- Below Min
- Low Stock
- Over Max
- Normal

요약 카드를 누르면 해당 상태로 필터링된다. 별도 하단 필터 버튼은 화면에서 가려지는 문제가 있어 제거했다.

자재 카드:

- Material No
- Material Name
- Car
- Unit
- Status badge
- On Hand
- Min Qty
- Max Qty
- Main Location
- Locations count

`Locations`를 누르면 위치별 재고 상세 모달을 표시한다.

Location detail:

- Location No
- Qty
- Location Name
- Warehouse
- Zone
- Area
- Rack

이 Location detail은 자재 카드에서 해당 자재가 어느 위치들에 나뉘어 있는지 확인하는 보조 상세다. 위치를 먼저 고르고 그 위치의 품목을 찾는 화면은 WH004에서 처리한다.

### 사용하는 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.WhInventoryAsync(q, dateFrom, dateTo)`
- `PdaApi.WhInventoryLocationsAsync(itemNo, dateFrom, dateTo)`

프로시저:

- `SIS_TEST.PDA_WH03_INVENTORY_STATUS`
- `SIS_TEST.PDA_WH03_INVENTORY_LOCATIONS`

`PDA_WH03_INVENTORY_STATUS` 주요 기준:

- `SIS_TEST.WMS2000`: 현재 재고 기준
- `SIS_TEST.WMS2010`: LOT 입고일/생산일 보조 정보
- `SIS_TEST.ACD0020`: 자재 마스터, `MIN_INV_DAY`, `MIN_INV_QTY`, `MAX_INV_DAY`, `MAX_INV_QTY`, `VINCD`, `UNIT`
- `SIS_TEST.ACD0020L`: 자재명

`PDA_WH03_INVENTORY_LOCATIONS` 주요 기준:

- `SIS_TEST.WMS2000`: 자재별 Location 재고 합계
- `SIS_TEST.WMS2010`: Stock date 필터 보조
- `SIS_TEST.WMS1040`: Location master
- `SIS_TEST.WMS1010`: Warehouse master
- `SIS_TEST.WMS1020`: Area master
- `SIS_TEST.WMS1030`: Zone master

WH003은 현재 재고 화면이므로 `SUM(QTY) > 0`인 현재 재고만 표시한다. 자재 마스터에는 있지만 실제 재고가 없는 품목은 카드에 표시하지 않는다.

### 상태 기준

`PDA_WH03_INVENTORY_STATUS`에서 아래 기준으로 상태를 계산한다.

- `BELOW_MIN`: `SUM_QTY < MIN_INV_QTY`
- `LOW_STOCK`: `SUM_QTY < MIN_INV_QTY * 1.3`
- `OVER_MAX`: `SUM_QTY > MAX_INV_QTY`
- `NO_BASELINE`: Min/Max 기준이 없는 자재
- `NORMAL`: 위 조건에 걸리지 않는 정상 상태

처음에는 기획 문서의 `MD 09/10 시 노란색` 문구가 애매했기 때문에, 실제 SIS DB에서 재고 기준을 찾았다. 그 결과 `ACD0020.MIN_INV_QTY`, `ACD0020.MAX_INV_QTY`와 기존 `WM30910` 계열의 min/max 재고 상태 기준이 더 직접적인 기준이라고 판단했다.

### 기존 프로그램에서 참고한 점

SIS Current Stock 계열:

- `WM30130`: 현재고 상세 조회
  - 기존 패키지: `APG_WM30130`
  - 주요 개념: LOT/Location 단위 현재고 상세
  - 주요 테이블: `WMS2000`, `WMS2010`, `WMS2020`, `ACD0020L`
- `WM30140`: 위치별 재고 조회
  - 기존 패키지: `APG_WM30140`
  - 주요 개념: Location 기준 재고
- `WM30290`: 현재고 요약 조회
  - 기존 패키지: `APG_WM30290`
  - 주요 개념: 품번별 합산 재고, location count, total qty
- `WM30260`, `WM30265`: 위치별 재고 및 LOT 상세
  - 기존 패키지: `APG_WM30260`, `APG_WM30265`
  - 주요 개념: 위치를 먼저 보고 double click/detail로 LOT 목록 확인
- `WM30910`: Min/Max 재고 상태 참고
  - 기존 패키지: `APG_WM30910`
  - 주요 개념: `ACD0020`의 min/max 기준과 현재 재고를 비교해 부족/정상/초과 판단

MAUIPDA Inventory 계열:

- `WMS1310 Lotno Status`
- `WMS1320 Physical Inventory Check`
- `WMS1340 Location Information`
- `WMS1350 Same Part Tracking`

특히 Location 상세를 볼 때는 PDA의 `WMS1340 Location Information`과 SIS의 위치별 재고 조회 구조를 참고했다.

### 현재 카피/재현한 것

SQL Server `SIS_TEST`에 아래 항목을 준비했다.

- `WMS2000`: 현재 재고 기준 테이블로 생성/시드
- `WMS2010`
- `WMS2020`
- `WMS1040`
- `WMS1010`
- `WMS1020`
- `WMS1030`
- `ACD0020`
- `ACD0020L`
- `PDA_WH03_INVENTORY_STATUS`
- `PDA_WH03_INVENTORY_LOCATIONS`

### 기존과 달라진 점

- 처음에는 `ACD0020` 기준 자재까지 포함해 재고 0인 품목도 표시할 수 있었지만, 실제 Current Stock 화면 성격에 맞지 않아 `WMS2000` 현재 재고가 있는 품목만 표시하도록 변경했다.
- `Last Received`, `Lots`는 카드에서 제거했다.
- 같은 자재가 여러 Location에 있을 경우 하나의 자재 카드에 합산 재고를 표시하고, `Locations`를 눌러 위치별 상세를 보도록 바꿨다.
- 처음에는 WH003 안에 Date/Location 탭을 같이 두는 안도 검토했지만, Inventory와 Location Search의 역할이 겹쳐서 WH003에서는 Location 필터를 제거했다.
- WH003의 날짜 입력 영역은 버튼과 입력칸이 너무 붙어 보이지 않도록 `CLEAR`, `APPLY` 간격과 버튼 높이를 조정했다.
- 기존 SIS 그리드 방식 대신 PDA 카드 + 모달 방식으로 변경했다.
- 날짜 필터는 `<input type="date">`를 쓰면 WebView/OS locale 때문에 한국어 날짜 UI가 뜨는 문제가 있어, `YYYY-MM-DD` 텍스트 입력으로 바꿨다.
- 모든 화면 문구는 영어 기준으로 정리했다.

## WH004 Location Search

### 화면 목적

`LOCATION SEARCH`는 위치 조건을 기준으로 현재 재고가 있는 Location과 해당 Location의 품목을 조회하는 화면이다.

처음에는 `Location Map`이라는 이름으로 만들었지만, PDA 화면에서 실제 그리드 맵을 표현하기에는 X/Y/Level 조합이 너무 많고 화면이 복잡해졌다. 그래서 기능은 유지하되 사용 목적에 맞게 `Location Search`로 이름을 변경했다.

### 현재 보여주는 정보

상단 기능:

- Last Updated
- Refresh
- Location No Scan
- Scan button

검색 조건:

- Area
- Level
- Column
- Row

검색 결과 Location 카드:

- Area / Location No
- Zone text
- Qty

Location 카드를 누르면 해당 위치의 현재 재고 품목을 표시한다.

Location item detail:

- Item
- Item name
- Lot
- Work date/time
- Qty + Unit

Location No Scan 동작:

- Location No를 입력하거나 스캔하면 현재 로드된 Location 목록에서 먼저 자동 검색한다.
- 입력값이 Location No와 일치하면 해당 Location을 선택하고, Area/Level/Column/Row 필터도 자동으로 해당 위치 기준으로 변경한다.
- `Enter` 또는 `SCAN` 버튼을 눌러도 같은 검색을 수행한다.
- 현재 목록에 없으면 `PdaApi.WhScanLocationAsync(locationId)`로 DB/API 기준 Location을 다시 조회한다.
- Location 조회 성공 시 해당 Location 카드가 선택되고, 하단에 해당 위치의 item/LOT 목록을 로드한다.

### 사용하는 API, 테이블

PDA 코드 호출:

- `PdaApi.WhLocationMapAsync()`
- `PdaApi.WhLocationMapItemsAsync(locationId)`

`WhLocationMapAsync()` 주요 기준:

- `SIS_TEST.WMS1040`: Location master, Location No, Area, Zone, Rack X/Y/Z
- `SIS_TEST.WMS1010`: Warehouse name
- `SIS_TEST.WMS1020`: Area name
- `SIS_TEST.WMS1030`: Zone name
- `SIS_TEST.WMS2000`: Location별 현재 stock line count, total qty
- fallback: 직접 DB 조회 실패 시 `/api/wh/locations`를 호출한다.
- fallback Location에 Area/X/Y/Z가 없으면 `PdaApi`에서 Location No/Zone 기준으로 PDA 필터용 값을 보정한다.

`WhLocationMapItemsAsync(locationId)` 주요 기준:

- `SIS_TEST.WMS2000`: Location별 LOT/품번/수량
- `SIS_TEST.ACD0020`: Unit
- `SIS_TEST.ACD0020L`: Part name

현재 WH004는 별도 저장 프로시저를 만들지 않고 `PdaApi.cs`에서 SQL Server `SIS_TEST` 테이블을 직접 조회한다. 운영 반영 시에는 SIS 표준에 맞춰 프로시저로 분리하는 것을 권장한다.

로딩/예외 처리:

- Location 조회 중 예외가 발생해도 화면이 `Loading location map` 상태에 갇히지 않도록 `try/catch/finally`로 `_loading`을 반드시 해제한다.
- 조회 실패 시 `No locations found`와 함께 간단한 실패 메시지를 표시한다.
- API fallback까지 실패하면 빈 Location 리스트를 반환해 화면이 깨지지 않도록 했다.

### 기존 프로그램에서 참고한 점

SIS:

- Location master 구조: `WMS1040`
- Warehouse/Area/Zone 이름 구조: `WMS1010`, `WMS1020`, `WMS1030`
- 위치별 현재 재고 조회 개념: `WM30140`, `WM30260`, `WM30265`

MAUIPDA:

- `WMS1340 Location Information`
- Location No를 기준으로 위치와 해당 위치의 품목 상태를 확인하는 흐름

### 기존과 달라진 점

- 기획 문서의 Location Map처럼 전체 위치를 격자형으로 그리는 방식은 PDA 화면에 과밀해서 제외했다.
- Area/Level, Column/Row를 각각 한 줄의 드롭다운으로 배치해 작은 화면에서도 조건을 선택하기 쉽게 했다.
- 드롭다운 글씨가 잘리지 않도록 선택 라벨은 여러 줄 표시가 가능하게 스타일을 조정했다.
- `On Hand`, `Occupied`, `Selected Position`, `Inventory` 같은 혼동될 수 있는 보조 문구는 제거하고, Location과 Qty 중심으로 단순화했다.
- Location 카드 선택 시 그 위치에 어떤 품목과 LOT가 있는지 바로 확인할 수 있게 했다.
- Location No 스캔 입력을 추가해 드롭다운을 직접 고르지 않아도 위치를 바로 찾을 수 있게 했다.
- 스캔된 Location이 있으면 Area/Level/Column/Row 드롭다운이 자동으로 맞춰지도록 변경했다.

## WH008 Transactions

### 화면 목적

`WH-008 TRANSACTIONS`는 창고 입고, 출고, 수량 조정 이력을 조회하는 화면이다.

처음에는 WH002에서 이미 입고된 LOT을 스캔했을 때 접히는 Transaction Log를 보여주는 방식으로 검토했지만, WH002는 스캔/입고/위치 변경/수량 조정 작업 화면으로 유지하는 편이 더 명확했다. 그래서 이력 조회는 기존 Warehouse 메뉴의 `Transactions`인 WH008로 이동했다.

### 현재 구현된 기능

상단 요약:

- `ALL`: 현재 조건에 맞는 전체 이력 건수
- `IN`: 입고 성격의 이력 건수
- `OUT`: 출고 성격의 이력 건수
- `ADJ`: 수량 조정 이력 건수

요약 버튼을 누르면 해당 타입으로 카드 리스트가 필터링된다.

검색 조건:

- LOT No
- Item
- Worker
- Adj Reason
- Date Range: `FROM`, `TO`를 `YYYY-MM-DD` 형식으로 직접 입력

날짜 영역:

- 기본 기간은 최근 7일이다.
- `RESET`은 최근 7일 범위로 되돌린다.
- `APPLY`는 입력한 From/To 기간으로 DB를 다시 조회한다.
- OS/WebView locale 문제를 피하기 위해 WH003과 동일하게 `<input type="date">` 대신 `YYYY-MM-DD` 텍스트 입력을 사용한다.

카드 표시:

- `IN`, `OUT`, `ADJ`를 작은 배지만으로 구분하면 눈에 잘 들어오지 않아 카드 자체 색상과 왼쪽 컬러 라인을 다르게 표시한다.
- `IN`: 연녹색 카드
- `OUT`: 연노랑/주황 카드
- `ADJ`: 연하늘색 카드
- 기타 상태: 회색 카드

기본 카드에는 아래 정보를 표시한다.

- Date/Time
- LOT No
- Status badge
- Part No
- Location No

`ADJ` 카드만 `DETAIL` 버튼을 제공한다. 기본 상태에서는 일반 카드처럼 보이고, `DETAIL`을 누르면 아래 정보를 펼쳐서 보여준다.

- Before Qty
- Change Qty
- After Qty
- Reason
- Note
- Supervisor

현재 WH008의 `Supervisor` 표시는 PIN 값이 아니라 실제 작업자 기준으로 `USER_ID`를 사용한다. WH002 조정 모달에서는 Supervisor PIN을 입력받지만, WH008 조회 화면에서는 사용자에게 PIN/masked PIN을 보여주는 것보다 실제 조정 작업자를 보여주는 편이 이해하기 쉽기 때문이다.

Excel export는 한때 CSV 저장 방식으로 검토했지만, PDA 환경에서 파일 저장 위치와 사용 흐름이 애매해서 제거했다.

### 사용하는 API, 테이블

PDA 코드 호출:

- `PdaApi.WhInboundTransactionLogsAsync(lotNo, dateFrom, dateTo)`

현재 테스트 DB 기준 조회 방식:

- `SIS_TEST.WMS2030`이 존재하면 우선 사용한다.
- 현재 테스트 DB에는 `WMS2030`이 없으므로 fallback으로 아래 테이블을 조합한다.

fallback 참조 테이블:

- `SIS_TEST.WMS2010`: 입고 이력 성격의 row. WH008에서는 `IN`으로 표시한다.
- `SIS_TEST.WMS2020`: 현재 재고 row. `WMS2010` 입고 이력이 없는 LOT에 한해서 보조 이력처럼 표시한다. 입고 이력이 이미 있는 LOT은 중복 표시를 피하기 위해 제외한다.
- `SIS_TEST.PDA_WH002_ADJUST_AUDIT`: WH002 `ADJUST QTY`에서 저장한 수량 조정 감사 이력. WH008에서는 `ADJ`로 표시한다.

`PDA_WH002_ADJUST_AUDIT`에서 사용하는 주요 컬럼:

- `LOTNO`
- `PARTNO`
- `LOCATION_NO`
- `BEFORE_QTY`
- `DELTA_QTY`
- `AFTER_QTY`
- `REASON_CODE`
- `REASON_NOTE`
- `USER_ID`
- `WORK_DATE`
- `WORK_TIME`
- `INSERT_DATE`

### 기존 프로그램에서 참고한 점

MAUIPDA:

- `Material > Inventory > Lotno Status`
- 기존 서비스: `MaterialInventoryService`
- 기존 패키지: `MES.PKG_PDA_WMS1310.GET_LOT_INFO`
- 기존 이력 테이블: `MES.WMS2030`

기존 Lotno Status 화면은 LOT 기준으로 `Date`, `Location`, `Qty`, `Status`를 보여준다. WH008은 이 개념을 PDA 신규 Warehouse 메뉴의 전체 Transaction History 화면으로 확장했다.

SIS/DB:

- 현재 재고 기준: `WMS2020`, `WMS2000`
- 입고 이력 기준: `WMS2010`
- 테스트 수량 조정 감사 이력: `PDA_WH002_ADJUST_AUDIT`

### 기존과 달라진 점

- WH002 Scan 화면 안에 Transaction Log를 넣지 않고 WH008로 분리했다.
- 단순 `1D`, `7D`, `30D` 버튼 대신 From/To 날짜 범위를 직접 입력하도록 바꿨다.
- 상태 배지만으로 구분하지 않고 카드 자체 색상을 `IN`, `OUT`, `ADJ`별로 다르게 표시했다.
- `ADJ`는 기본 카드에서는 일반 이력처럼 보이고, `DETAIL` 버튼을 눌렀을 때만 조정 전/후 스냅샷과 사유 정보를 보여준다.
- 수량 조정 상세 하단의 `COUNT_DIFF +1.000` 같은 중복 note 표시는 제거했다.
- 일반 `IN/OUT` 카드의 `Qty / Worker` 보조 박스도 제거해 카드 정보를 단순화했다.
- Excel export는 PDA 사용성상 제거했다.

## 작업 중 주요 질문과 답변 요약

### PO와 SCM 시작점

질문: Warehouse flow는 PO 입고 데이터가 있어야 시작되는가?

답변: 맞다. WH001의 시작점은 SCM/SIS에서 넘어온 PO 및 입고 예정 데이터다. PO schedule, 입고 실적, 미입고 수량이 있어야 Inbound Schedule이 의미를 가진다.

### Forecast, MRP, PO 관계

질문: Forecast 단계에서 어떻게 PO가 되는가? MRP는 무엇인가?

답변: Forecast는 앞으로 필요한 생산/판매 수요이고, MRP는 그 수요를 만들기 위해 필요한 자재 수량과 시점을 계산하는 단계다. MRP 결과로 부족한 자재가 산출되면 구매 요청/구매 오더가 만들어지고, 이것이 PO가 된다.

### SIS에서 PO는 어디서 보는가

질문: SIS에서는 PO를 어느 화면에서 보여주는가?

답변: `WM40120`에서 PO/back order 목록을 보여준다. 이 화면의 `APG_WM40120` 로직을 참고해 WH001의 `SIS_TEST.APG_WM40120_INQUERY_VENDER_BACK_ORDER`를 만들었다.

### Non-deliver Qty 의미

질문: `WM40120`의 Non-deliver Qty는 무슨 뜻인가?

답변: 아직 납품/입고되지 않은 잔량이다. 개념적으로 `PO Qty - GR Qty`다.

### GRN Qty 갱신 시점

질문: GRN Qty는 언제 업데이트되는가?

답변: PO schedule 조회 화면에서 직접 갱신되는 값이 아니라 실제 입고/GRN 처리 시 실적 테이블에 쌓이고, WH001은 그 결과를 조회해 보여준다. WH002 테스트 구현에서는 입고 시 `AMM2010`에 GRN 성격 데이터를 기록하도록 했다.

### CKD와 Local 차이

질문: CKD와 Local 차이는 무엇인가?

답변: Local은 국내/로컬 납품 기반으로 납품서/박스 바코드 흐름을 탄다. CKD는 해외/수입성 포장 단위로 Case, Invoice, Container 정보가 붙는다. WH002에서는 Local은 `AMM9010/AMM9011`, CKD는 `AMF1030`을 기준으로 분리했다.

### Location master는 어디서 관리되는가

질문: Location No, WH, Zone, Rack 정보는 어디서 입력되는가? PDA에서 입력 가능한가?

답변: Location master는 SIS에서 관리하는 기준정보다. PDA는 Location No를 스캔하고 검증/사용할 뿐, WH/Zone/Rack master를 신규 등록하거나 수정하는 역할은 아니다. 기준 테이블은 `WMS1040`이고 WH/Area/Zone 이름은 `WMS1010/1020/1030`에서 가져온다.

### 같은 품번이 여러 위치에 있는 경우

질문: 같은 아이템이 서로 다른 위치에 있으면 하나의 row를 더 추가하는가?

답변: 실제 재고 테이블은 LOT/Location 단위로 여러 row를 가진다. SIS 상세 화면은 여러 row로 보여주고, 요약 화면은 품번별로 합산한다. WH003은 PDA에 맞춰 품번별 하나의 카드로 합산하고, 위치 상세는 모달에서 보여준다.

### 재고가 있는데 입고 이력이 없는 경우

질문: 재고가 있다면 입고/출고 이력이 있어야 하지 않는가?

답변: 맞다. 실제 현재 재고라면 입고, 조정, 기초재고 등 어떤 이력/근거가 있어야 한다. 그래서 WH003은 자재 마스터 기준 0재고까지 보여주는 방식이 아니라 `WMS2000` 현재 재고가 있는 데이터만 보여주도록 수정했다.

### WH002 수량 변경 이력 위치

질문: WH002에서 수량 변경한 것은 어디에 기록되는가?

답변: 실제 현재 수량은 `SIS_TEST.WMS2020.QTY`와 `SIS_TEST.WMS2000.QTY`에 반영된다. 누가, 언제, 왜, 몇 개를 조정했는지에 대한 감사 이력은 `SIS_TEST.PDA_WH002_ADJUST_AUDIT`에 남긴다. 이 테이블에는 조정 전 수량, 조정 수량, 조정 후 수량, Reason Code, Note, 작업자, Supervisor PIN 마스킹 값이 저장된다.

### Location Map과 Inventory 관계

질문: WH003 Inventory와 WH004 Location Map은 기능을 합칠 수 있지 않은가?

답변: 일부 기능은 겹친다. WH003은 자재를 먼저 보고 해당 자재가 어느 위치에 있는지 확인하는 흐름이고, WH004는 위치를 먼저 고르고 그 위치에 어떤 자재/LOT가 있는지 확인하는 흐름이다. 따라서 WH003 안에 위치 탭을 넣는 대신 WH004를 `Location Search`로 유지해 두 역할을 분리했다.

질문: Location Map을 격자형으로 보여주면 PDA에서 괜찮은가?

답변: 실제 Location master에는 Area, Column, Row, Level 조합이 많아 PDA 화면에서 격자 전체를 표현하면 너무 복잡해진다. 그래서 격자형 맵은 제외하고 Area/Level/Column/Row 드롭다운으로 조건을 좁힌 뒤 Location 카드와 해당 Location의 품목을 보여주는 방식으로 바꿨다.

### WH008 Transaction History 이동

질문: WH002에서 이미 입고된 LOT을 스캔했을 때 Transaction Log를 같이 보여주는 게 맞는가?

답변: 처음에는 WH002 안에 접히는 Transaction Log를 넣었지만, WH002는 작업 화면이고 이력 조회는 별도 화면이 더 적합하다. 그래서 Transaction Log는 WH002에서 제거하고 기존 Warehouse 메뉴의 `Transactions`, 즉 WH008로 옮겼다.

질문: ADJ 이력은 카드에 전부 펼쳐서 보여줘야 하는가?

답변: ADJ도 기본 카드에서는 IN/OUT과 같은 일반 이력처럼 보이게 하고, `DETAIL`을 눌렀을 때만 Before/Change/After, Reason, Note, Supervisor를 보여주도록 바꿨다. 이렇게 해야 리스트 스캔성이 좋아지고 조정 상세는 필요할 때만 확인할 수 있다.

질문: WH008에서 Excel export가 필요한가?

답변: CSV 저장 방식으로 검토했지만 PDA 환경에서는 파일 저장 위치와 후속 사용 흐름이 애매해서 제거했다. Export가 필요하면 Web PC 화면 또는 별도 리포트 화면에서 처리하는 편이 더 자연스럽다.

## 현재 구현 시 주의할 점

- `SIS_TEST`는 테스트용 스키마다. 운영 반영 시에는 실제 SIS/MES DB 스키마와 권한, 프로시저 배포 방식이 별도로 필요하다.
- WH002의 입고/취소 로직은 테스트 구현이므로 실제 운영에서는 기존 Oracle 패키지의 예외 처리, 트랜잭션, 인터페이스 테이블 반영 범위를 더 확인해야 한다.
- WH003의 `WMS2000`은 테스트 DB에 맞춰 생성/시드했다. 운영 DB에 실제 `WMS2000`이 있다면 해당 구조에 맞춰 프로시저를 다시 정렬해야 한다.
- WH004 `Location Search`는 현재 `PdaApi.cs`에서 직접 SQL로 조회한다. 운영 반영 시에는 `PDA_WH04_LOCATION_SEARCH`, `PDA_WH04_LOCATION_ITEMS` 같은 프로시저로 분리하는 편이 유지보수에 좋다.
- WH004는 SIS_TEST 직접 조회가 실패하면 API `/api/wh/locations`로 fallback한다. fallback 테이블인 `dbo.MD_Location`은 로컬 개발 DB 기준이므로 실제 운영 Location master와 혼동하지 않아야 한다.
- WH001의 PO 조회는 `WM40120` 기준으로 만들었고, SCM에서 PO가 신규 생성되는 원천 화면/배치까지 완전히 대체한 것은 아니다.
- `GRN_QTY`는 PO schedule에 저장되는 값이라기보다 GRN 실적을 합산해 계산하는 값이다. 운영에서는 GRN cancellation, return, reversal까지 반영해야 한다.
- WH002 수량 조정의 Supervisor PIN은 현재 테스트 구현에서 최소 길이 검증과 마스킹 저장만 한다. 운영에서는 실제 승인자 계정/권한 검증과 감사 로그 보관 정책을 추가해야 한다.
- WH008은 `WMS2030`이 있으면 우선 사용하도록 만들었지만, 현재 테스트 DB에는 `WMS2030`이 없어 `WMS2010`, `WMS2020`, `PDA_WH002_ADJUST_AUDIT`를 조합한다. 운영 반영 시에는 실제 Transaction History 표준 테이블/프로시저 기준으로 재정렬해야 한다.
- WH005 Inventory Adjustment는 별도 메뉴/화면으로 두지 않고 WH002에 통합했다. 운영 정책상 독립 메뉴가 필요하면 WH002의 `PDA_WH002_ADJUST_QTY` 호출부를 재사용해 별도 화면으로 분리할 수 있다.
