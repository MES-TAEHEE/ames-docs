# WH PDA Implementation Summary

작성일: 2026-07-07

최근 업데이트: 2026-07-13

대상 프로젝트: `C:\Users\Young\Desktop\Seoyon\Jackson\ames-docs\src\05_Pda\AMES.Pda`

주요 구현 파일:

- `Components/Pages/Login.razor`
- `Components/Pages/Wh/Wh01InboundSchedule.razor`
- `Components/Pages/Wh/Wh02PdaInbound.razor`
- `Components/Pages/Wh/Wh03InventoryStatus.razor`
- `Components/Pages/Wh/Wh04LocationMap.razor`
- `Components/Pages/Wh/Wh06ReleaseSchedule.razor`
- `Components/Pages/Wh/Wh07PdaRelease.razor`
- `Components/Pages/Wh/Wh08TransactionHistory.razor`
- `Components/Pages/Wh/WhHome.razor`
- `Services/PdaApi.cs`
- `wwwroot/css/pda.css`
- `..\..\04_Api\AMES.Api\Endpoints\AuthEndpoints.cs`
- `..\..\04_Api\AMES.Api\Endpoints\WhEndpoints.cs`
- `..\..\..\dist\pda\README.md`
- `..\..\..\dist\pda\migrate_pda_wh_schedule.sql`
- `..\..\..\dist\pda\seed_pda_wh_demo_data.sql`
- `..\..\..\dist\pda\migrate_pda_wh_inbound.sql`
- `..\..\..\dist\pda\seed_pda_wh_inbound_demo_data.sql`
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

## 2026-07-13 최신 변경 요약

### 화면 표시 번호 정리

현재 사용자가 보는 Warehouse 업무 번호는 아래처럼 정리했다. 실제 route와 component 이름은 기존 구현과 링크 호환 때문에 일부 다르다.

| 표시 화면 | 메뉴명 | 실제 route | 실제 component |
| --- | --- | --- | --- |
| WH001 | Schedule | `/wh/01` | `Wh01InboundSchedule.razor` |
| WH002 | Inbound | `/wh/02` | `Wh02Receive.razor` |
| WH003 | Release | `/wh/07` | `Wh07PdaRelease.razor` |
| WH004 | Inventory | `/wh/03` | `Wh03InventoryStatus.razor` |
| WH005 | Adjust | `/wh/03?tab=adjust` | `Wh03InventoryStatus.razor` |
| WH006 | Transactions | `/wh/08` | `Wh08TransactionHistory.razor` |

`/wh/06`은 별도 Release Schedule 화면으로 쓰지 않고, 기존 링크 호환용 redirect로 유지한다. 열리면 WH001 Schedule의 Release 탭(`/wh/01?tab=release`)으로 이동한다.

### Release 최신 보강

- Release 화면 헤더는 WH002와 같은 크기 체계로 맞췄고, 표시 타이틀은 `WH-003 RELEASE`로 변경했다.
- Pick Slip 입력 placeholder는 `Scan Pick Slip No`, LOT 입력 placeholder는 `Scan Lot No`로 통일했다.
- LOT 입력 영역 제목은 `LOT NO`로 단순화했다.
- `LOAD`, `SCAN`, `PICK` 버튼이 비활성화 상태일 때도 PDA에서 식별되도록 색을 더 진하게 조정했다.
- Pick Slip을 load하기 전에 `GET /api/wh/release/schedule/{pickSlipNo}/status`로 존재 여부, close 여부, line 수를 먼저 확인한다.
- Pick Slip이 없거나, closed 상태이거나, line이 없거나, status 조회 API가 실패하면 커스텀 알럿으로 작업을 막는다.

### Pick Slip 생성 흐름

출고 요청은 PDA에서 새로 만드는 것이 아니라 기존 SIS 흐름에서 생성된 Pick Slip을 PDA가 스캔/처리하는 구조로 잡았다.

1. SCM/SAP/MRP 쪽 자재소요계획이 인터페이스로 들어온다.
2. SIS `AMM1020`에 MIP Material Requirement Planning 데이터가 쌓인다.
3. SIS `WM20233` 화면이 `APG_WM20233.INQUERY`로 `AMM1020`의 D0/D1/D2 소요량을 조회한다.
4. `WM20233`에서 선택한 항목을 `SAVE_PICKSLIP`/`SAVE`로 저장하면 `WMS3050`에 Pick Slip 출고 요청 라인이 생성된다.
5. PDA Release는 `WMS3050`의 Pick Slip No와 line을 기준으로 `WMS2020` 현재 재고 LOT을 FIFO 순서로 검증하고 출고 처리한다.

참고한 기존 화면/프로시저:

- SIS `WM20233`: Pick Slip 요청 생성 화면
- SIS package `APG_WM20233`: `INQUERY`, `SAVE_PICKSLIP`, `SAVE`
- SIS table `AMM1020`: MIP 자재소요계획
- SIS table `WMS3050`: 현장 자재출고요청/Pick Slip
- SIS table `WMS2020`: 현재 재고 LOT, Location, 출고 상태
- SCM/SRM `SRM_MM30001`, `SRM_MM30013`: `AMM1020` 자재소요계획 조회 계열
- SIS `XM30413`: `ZMMT0360` 일별 자재소요계획 SCM 전송/인터페이스 계열

## 전체 창고 플로우 요약

현재 Warehouse PDA는 `Schedule`, `Inbound`, `Release`, `Inventory`, `Adjust`, `Transactions` 중심으로 정리되어 있다. 화면에 보여주는 업무 번호는 작업 흐름에 맞춰 다시 정리했고, 일부 route/component 이름은 기존 코드 호환을 위해 그대로 둔다. `Location Search`와 `Locating` 코드는 유지하되, 현재 대메뉴에서는 잠시 숨겨 둔 상태다.

1. SCM/SIS PO 데이터가 생성된다.
2. WH001 `Schedule`에서 Inbound 탭으로 PO 입고 예정 및 입고 진행 상태를 조회한다.
3. WH001 `Schedule`에서 Release 탭으로 출고 예정 Pick Slip과 진행 상태를 조회한다.
4. WH002 `Inbound`에서 작업자가 LOT No와 Location No를 스캔하고 입고 처리한다.
5. 입고 처리 결과가 `WMS2010`, `WMS2020`, `AMM2010`에 반영된다.
6. WH003 `Release`에서 Pick Slip을 불러오고 LOT No를 스캔해 FIFO 기준으로 출고 처리한다. 실제 route/component는 기존 호환 때문에 `/wh/07`, `Wh07PdaRelease.razor`를 사용한다.
7. WH004 `Inventory`에서 현재 재고 기준 테이블인 `WMS2000`을 기준으로 자재별 재고 상태를 조회한다. 실제 route/component는 `/wh/03`, `Wh03InventoryStatus.razor`를 사용한다.
8. WH005 `Adjust`에서 이미 입고된 LOT을 스캔해 수량 조정을 저장한다. 실제 route/component는 `/wh/03?tab=adjust`, `Wh03InventoryStatus.razor`를 사용한다.
9. WH006 `Transactions`에서 입고, 출고, 수량 조정 이력을 LOT, 품번, 작업자, 날짜 범위, 조정 사유 기준으로 조회한다. 실제 route/component는 `/wh/08`, `Wh08TransactionHistory.razor`를 사용한다.

테스트 구현에서는 기존 Oracle SIS/PDA 구조를 SQL Server `SIS_TEST` 스키마에 필요한 범위만 복사/재현해서 연결했다.

## Warehouse Menu

### 화면 목적

Warehouse 대메뉴는 PDA 창고 업무의 진입점이다. 기존에는 기능명이 단순 버튼처럼 나열되어 있었고, 화면 코드(`WH001`, `WH002`) 중심으로 보일 수 있어 실제 작업자가 업무를 고르기 애매했다.

현재는 메뉴명을 작업 기준으로 정리하고 카드형 UI로 바꿨다.

- `Schedule`: WH001 입고 예정과 출고 예정 통합 조회
- `Inbound`: WH002 LOT/Location 스캔 기반 입고 처리
- `Release`: WH003 Pick Slip 기반 출고 피킹
- `Inventory`: WH004 자재별 현재 재고와 Min/Max 상태
- `Adjust`: WH005 LOT 스캔 기반 수량 조정
- `Transactions`: WH006 입출고/재고 변경 이력

### 구현 변경점

- `Location Search`는 삭제하지 않고 기능은 유지하되, 현재 대메뉴에서는 숨겨 두었다.
- 별도 `Release Schedule` 메뉴는 없애고 WH001 `Schedule`의 Release 탭으로 합쳤다.
- WH006은 기존 링크 호환을 위해 `/wh/01?tab=release`로 보내는 redirect 화면으로 남겼다.
- WH001/WH002/WH003 같은 코드명은 대메뉴 카드에 직접 노출하지 않고, 사용자가 이해하기 쉬운 업무명으로 표시했다. 단, 각 화면 헤더에는 현재 정리된 업무 번호와 제목을 표시한다.
- Radzen icon을 붙인 흰색 카드형 UI로 변경해 PDA 앱 화면처럼 보이도록 했다.
- 각 카드에는 짧은 설명을 추가해 신규 개발자나 작업자가 화면 역할을 빠르게 이해할 수 있게 했다.

## WH001 Schedule

### 화면 목적

`WH-001 SCHEDULE`은 입고 예정과 출고 예정 스케줄을 한 화면에서 탭으로 나눠 보여주는 화면이다.

Inbound 탭은 기획 문서의 `WH-01 Inbound Schedule` 역할에 맞춰, 기존 SIS의 PO/back order 조회 화면인 `WM40120`을 PDA 화면에 맞게 축약했다. Release 탭은 별도 `WH006 Release Schedule` 메뉴로 두지 않고 이 화면에 합쳐 Pick Slip 기준 출고 예정과 진행 상태를 보여준다.

### 현재 보여주는 정보

Inbound 탭의 각 PO 카드에 아래 정보를 표시한다.

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

Inbound 탭의 하단 고정 버튼은 처음 `SCAN`이었으나, 실제 역할에 맞춰 `RECEIVE`로 변경했고 누르면 WH002로 이동한다.

Release 탭의 각 카드에는 아래 정보를 표시한다.

- Pick Slip No
- Destination / Request Location
- ETA D-Day
- Status: `Open`, `Partial`, `Picked`, `Late`
- First Material No
- First Material Desc
- Line count
- FIFO suggested location
- Requested Boxes
- Picked Boxes

Release 탭 상단에는 `RELEASE STATUS` 요약을 표시한다.

- Total
- Open
- Partial
- Picked

Release 탭에서는 카드를 선택한 뒤 하단 고정 `PICK` 버튼을 누르면 WH003 Release 화면으로 이동한다. 실제 route는 기존 호환 때문에 `/wh/07?pickSlipNo=...` query string을 사용한다.

### 사용하는 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.Wh001ScheduleInboundAsync()`
- 내부 DB 호출: `dbo.WH_PDA_SCHEDULE_INBOUND_LIST`
- 원천 테이블: `dbo.WH_PurchaseOrder`
- `PdaApi.Wh001ScheduleReleaseAsync()`
- API 호출: `GET /api/wh/schedule/release`
- fallback 호환 route: `GET /api/wh/release/schedule`

PDA DB 스크립트 관리 기준:

- Schedule부터 PDA DB 변경사항은 화면별 `docs/sql/WH001_*.sql` 파일로 나누지 않고 `dist/pda/migrate_pda_wh_schedule.sql`에 통합 관리한다.
- 새 프로시저는 화면번호가 아닌 업무 기준 이름을 사용한다. 예: `dbo.WH_PDA_SCHEDULE_INBOUND_LIST`, `dbo.WH_PDA_SCHEDULE_RELEASE_LIST`.
- PDA가 직접 관리하거나 demo seed로 채우는 Warehouse 업무 테이블은 기존 AMES 명명 규칙에 맞춰 `dbo.WH_...` 형식을 사용한다.
- Schedule Inbound demo data는 `dbo.WH_PurchaseOrder`, Release demo data는 `dbo.WH_ReleaseSchedule`을 사용한다.
- demo seed는 `dist/pda/seed_pda_wh_demo_data.sql`에 분리해 둔다.

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

Release 탭이 참고하는 주요 테이블:

- `dbo.WH_ReleaseSchedule`: Pick Slip 역할의 출고 요청 라인 기준
- `dbo.WH_Inventory`: 현재 재고, FIFO 추천 Location
- `dbo.tbl_Lot`: LOT No, 생산일, 입고일 기반 FIFO 정렬 보조
- `dbo.MD_Location`: Location master와 Zone
- `dbo.MD_Item`: 자재 마스터, Unit, 자재명
- `dbo.WH_PDA_SCHEDULE_RELEASE_LIST`: WH001 Release 탭 조회 프로시저

Release 주요 출력 컬럼:

- `PICK_SLIPNO`
- `REQ_LOCATION`
- `REQ_DATE`
- `LINE_COUNT`
- `REQ_BOX_QTY`
- `PICKED_BOX_QTY`
- `FIRST_PARTNO`
- `FIRST_PARTNM`
- `SUGGESTED_LOCATION`
- `STATUS`

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
- 별도 WH006 Release Schedule 화면을 만들지 않고 WH001 Schedule의 Release 탭으로 통합했다.
- PO Create Date, Arr.Date, Non-deliver Qty는 카드에서 제거하고 핵심 수량인 `PO Qty`, `GR Qty`만 남겼다.
- `ETA D-Day`를 각 카드에 표시하도록 추가했다.
- `Last Updated`와 1시간 자동 갱신을 추가했다.
- 하단 고정 `RECEIVE` 버튼으로 WH002 Inbound 화면 이동을 추가했다.
- Release 탭에서는 선택한 Pick Slip을 하단 고정 `PICK` 버튼으로 WH003 Release에 넘긴다. 실제 route는 `/wh/07`이다.

## WH002 Inbound

### 화면 목적

`WH-002 INBOUND`는 LOT No를 스캔해서 PO/입고 대상 정보를 조회하고, Location No를 스캔해서 실제 입고 처리하는 화면이다.

기존 MAUIPDA의 `Material > Incoming` 흐름을 기반으로 만들었고, 현재 화면 역할은 입고 처리와 입고 취소에 집중한다. 수량 조정은 Warehouse 대메뉴의 `Adjust`로 분리했고, 위치 변경/Locating 코드는 유지하되 현재 메뉴에서는 숨겨 둔 상태다.

### 현재 구현된 기능

작업 모드:

- `LOCAL`
- `CKD`

기본 입고 플로우:

1. `LOCAL` 또는 `CKD`를 선택한다.
2. `LOT NO SCAN`에 LOT No 또는 관련 바코드를 입력/스캔한다.
3. API가 `SIS_TEST` 프로시저를 호출해 PO/입고 대상 정보를 조회한다.
4. LOT이 PO 리스트에 없거나 입고 대상이 아니면 중앙 카드형 커스텀 알럿을 표시한다.
5. LOT이 입고 대상이면 품번, 품명, PO No, PO Seq, Qty + Unit, Vendor 등 정보를 카드로 보여준다.
6. LOT 정보가 조회된 뒤에만 Location No 스캔이 활성화된다.
7. Location No를 `WMS1040` 기준으로 검증한다.
8. Location의 WH, Area, Zone, Rack X/Y/Z, Stock Lines, Stock Qty를 표시한다.
9. LOT와 Location이 모두 유효하면 `RECEIVE` 버튼으로 입고 처리한다.
10. 입고 완료 후 `Receive Complete` 알럿을 띄우고 화면을 초기화한다.

이미 입고된 LOT을 다시 스캔한 경우:

- 현재 LOT 상태와 현재 Location을 보여준다.
- WH002에서는 `CANCEL INCOMING`만 직접 제공한다.
- 수량 조정은 `Adjust` 메뉴에서 처리하도록 안내한다.
- Location 변경/Locating은 코드와 API는 유지하지만 현재 대메뉴에서는 숨겨 둔 상태다.

UI/상태 관련 구현:

- 기본 브라우저 알럿이 아니라 중앙 카드형 커스텀 모달 사용
- 모든 문구는 영어 기준으로 정리
- `LOCAL`, `CKD` 탭 상태를 각각 유지
- LOT No가 없으면 Location No 스캔 비활성화
- LOT 또는 Location이 유효하지 않으면 `RECEIVE` 비활성화
- Qty는 `540 EA`처럼 수량과 Unit을 같이 표시
- `Source`, 하단의 `Location OK`, `Scan OK` 같은 개발용 문구 제거
- 테스트 버튼은 최종 UI에서 제거
- PDA에서 SQL Server에 직접 붙지 않고 API를 통해 조회/저장하도록 변경해, WebView/PDA 쪽 네트워크 timeout 메시지가 화면에 그대로 뜨는 문제를 줄였다.

### 사용하는 API, 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.WhScanInboundAsync(mode, barcode)`
- `PdaApi.WhReceiveInboundAsync(body)`
- `PdaApi.WhCancelInboundAsync(body)`
- `PdaApi.WhScanLocationAsync(locationId)`

API route:

- `GET /api/wh/inbound/scan`
- `POST /api/wh/inbound/receive-lot`
- `POST /api/wh/inbound/receive-sis` (compatibility alias)
- `POST /api/wh/inbound/move-location`
- `POST /api/wh/inbound/cancel`
- `GET /api/wh/location/scan`

프로시저:

- `dbo.WH_PDA_INBOUND_SCAN_LOT`
- `dbo.WH_PDA_INBOUND_RECEIVE_LOT`
- `dbo.WH_PDA_INBOUND_MOVE_LOCATION`
- `dbo.WH_PDA_INBOUND_CANCEL_RECEIPT`

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

현재 화면에서는 직접 노출하지 않지만, WH005 Adjust와 숨겨 둔 Locating 계열에서 재사용하는 API와 프로시저:

- `PdaApi.WhMoveInboundLocationAsync(body)`
- `PdaApi.WhAdjustInboundQtyAsync(body)`
- `POST /api/wh/inbound/move-location`
- `POST /api/wh/inbound/adjust-qty`
- `dbo.WH_PDA_INBOUND_MOVE_LOCATION`
- `SIS_TEST.PDA_WH002_ADJUST_QTY`

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
- `WMS1150 Incoming Cancel`
  - `MES.PKG_PDA_WMS1150.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1150.SET_IN_CANCEL`

SIS:

- Location master는 SIS에서 관리하고 PDA에서는 스캔/검증만 하는 구조로 판단했다.
- Location No, WH, Area, Zone, Rack 정보는 `WMS1040` 및 `WMS1010/1020/1030` 계층 테이블에서 가져오는 구조를 참고했다.

### 현재 카피/재현한 것

SQL Server `AMES_DEV`의 `dbo` 스키마에 아래 테이블 및 프로시저를 준비했다.

- `dbo.tbl_Lot`
- `dbo.WH_PurchaseOrder`
- `dbo.WH_Receiving`
- `dbo.WH_Inventory`
- `dbo.MD_Location`
- `dbo.WH_PDA_INBOUND_SCAN_LOT`
- `dbo.WH_PDA_INBOUND_RECEIVE_LOT`
- `dbo.WH_PDA_INBOUND_MOVE_LOCATION`
- `dbo.WH_PDA_INBOUND_CANCEL_RECEIPT`
- `dist/pda/migrate_pda_wh_inbound.sql`
- `dist/pda/seed_pda_wh_inbound_demo_data.sql`

Inbound test data:

- Ready local LOT: `LOT-LOCAL-001` -> PO `INB2607001`, Part `INB-MAT-001`, Qty `72 EA`
- Ready CKD LOT: `LOT-CKD-001` -> PO `INB2607002`, Part `INB-MAT-003`, Qty `100 EA`
- Already received LOT: `LOT-LOCAL-RECV` -> PO `INB2607003`, Part `INB-MAT-002`, Qty `40 EA`, Location `WH010101`
- Location No: `WH010101`, `WH010201`, `WH020101`
- Scan validation: `dbo.tbl_Lot.ProcessCode` must match the selected tab (`LOCAL` or `CKD`). For example, scanning `LOT-LOCAL-001` on the CKD tab returns `LOT receive mode does not match the selected tab.`

### 기존과 달라진 점

- 기존 MAUIPDA는 Local Receive, CKD Receive, Incoming Cancel이 화면별로 나뉘어 있었지만, WH002에서는 Local/CKD 입고와 입고 취소를 한 화면에서 처리한다.
- Local/CKD 선택 상태를 탭처럼 두고, 각 탭의 입력 상태를 유지하도록 했다.
- 실제 PDA 스캐너 대신 웹/PDA 테스트가 가능하도록 텍스트 입력 + 버튼 스캔 방식으로 구현했다.
- Location 선택 드롭다운은 제거하고 Location No 스캔 검증 방식으로 변경했다.
- 기본 알럿 대신 중앙 카드형 모달을 사용했다.
- 입고 완료 후 자동 초기화와 Clear 버튼을 추가했다.
- WH002에서 수량 조정 버튼은 제거하고, 별도 `Adjust` 메뉴로 분리했다.

## WH004 Inventory Status

### 화면 목적

`WH-004 INVENTORY`는 현재 재고 기준으로 자재별 재고 상태를 보여주는 화면이다. 실제 route/component는 기존 코드 호환을 위해 `/wh/03`, `Wh03InventoryStatus.razor`를 사용한다.

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

WH004 Inventory는 현재 날짜 필터 중심으로만 동작한다. 위치 조건으로 재고를 찾는 `Location Search` 코드는 유지하지만, 현재 대메뉴에서는 숨겨 두었다.

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

이 Location detail은 자재 카드에서 해당 자재가 어느 위치들에 나뉘어 있는지 확인하는 보조 상세다. 위치를 먼저 고르고 그 위치의 품목을 찾는 기능은 `Location Search` 코드에 유지되어 있다.

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

WH004 Inventory는 현재 재고 화면이므로 `SUM(QTY) > 0`인 현재 재고만 표시한다. 자재 마스터에는 있지만 실제 재고가 없는 품목은 카드에 표시하지 않는다.

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
- 처음에는 Inventory 안에 Date/Location 탭을 같이 두는 안도 검토했지만, Inventory와 Location Search의 역할이 겹쳐서 Inventory에서는 Location 필터를 제거했다.
- WH004 Inventory의 날짜 입력 영역은 버튼과 입력칸이 너무 붙어 보이지 않도록 `CLEAR`, `APPLY` 간격과 버튼 높이를 조정했다.
- 기존 SIS 그리드 방식 대신 PDA 카드 + 모달 방식으로 변경했다.
- 날짜 필터는 `<input type="date">`를 쓰면 WebView/OS locale 때문에 한국어 날짜 UI가 뜨는 문제가 있어, `YYYY-MM-DD` 텍스트 입력으로 바꿨다.
- 모든 화면 문구는 영어 기준으로 정리했다.

## WH005 Adjust

### 화면 목적

`WH-005 ADJUST`는 이미 입고되어 현재 재고에 존재하는 LOT을 스캔한 뒤, 수량 차이를 사유와 함께 저장하는 화면이다.

코드 파일은 `Wh03InventoryStatus.razor`를 같이 사용하지만, Warehouse 대메뉴에서는 `Inventory`와 별도 카드인 `Adjust`로 진입한다. 화면 표시 번호는 WH005이고, 실제 경로는 `/wh/03?tab=adjust`다.

### 현재 구현된 기능

기본 플로우:

1. `LOT NO SCAN`에 LOT No를 입력/스캔한다.
2. Local/CKD 선택 버튼 없이, 내부적으로 Local 스캔 후 CKD 스캔을 시도해 자동 감지한다.
3. 스캔 결과가 현재 재고에 있는 LOT이면 LOT, Part, Current Qty, Location 정보를 보여준다.
4. LOT이 현재 재고에 없거나 이미 입고 상태가 아니면 커스텀 알럿을 표시한다.
5. LOT이 유효하면 `ADJUSTMENT` 카드가 나타난다.
6. Before Qty, Change, After Qty를 확인한다.
7. Reason Code, Adjust Qty, Supervisor PIN, Note를 입력한다.
8. `SAVE`를 누르면 조정 전/후 수량과 사유를 저장한다.
9. 저장 성공 시 최신 LOT 정보를 다시 조회하고 조정 입력값을 초기화한다.

UI/상태 관련 구현:

- `Auto Detect` 모드 버튼은 제거했다.
- LOT 입력 placeholder는 `Scan Lot No`로 정리했다.
- Note 입력칸의 `Optional Note` placeholder는 제거했다.
- `SAVE` 버튼은 초록색이며, 입력이 부족해도 버튼 자체는 항상 보이게 두고 검증은 알럿으로 처리한다.
- 변경 수량이 `0`이면 `No changes to save.` 알럿을 표시한다.
- Supervisor PIN이 4자리 미만이면 저장하지 않는다.
- 조정 후 수량이 음수가 되면 저장하지 않는다.

Reason Code:

- `COUNT_DIFF`: cycle count 또는 실사 차이
- `DAMAGED`: 파손
- `LOST`: 분실
- `FOUND`: 발견
- `OTHER`: 기타

### 사용하는 API, 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.WhScanInboundAsync(mode, barcode)`
- `PdaApi.WhAdjustInboundQtyAsync(body)`

API route:

- `GET /api/wh/inbound/scan`
- `POST /api/wh/inbound/adjust-qty`

프로시저:

- `SIS_TEST.PDA_WH002_SCAN_LOCAL`
- `SIS_TEST.PDA_WH002_SCAN_CKD`
- `SIS_TEST.PDA_WH002_ADJUST_QTY`

수량 조정 시 갱신/기록되는 테이블:

- `SIS_TEST.WMS2020.QTY`: LOT별 Location 현재 수량 갱신
- `SIS_TEST.WMS2000.QTY`: WH004 Inventory에서 보는 현재 재고 수량 동기화
- `SIS_TEST.PDA_WH002_ADJUST_AUDIT`: 수량 조정 감사 이력 기록

`PDA_WH002_ADJUST_AUDIT` 주요 컬럼:

- `RECEIVE_TYPE`: `LOCAL` 또는 `CKD`
- `LOTNO`, `BARCODE`, `PARTNO`, `LOCATION_NO`
- `BEFORE_QTY`, `DELTA_QTY`, `AFTER_QTY`
- `REASON_CODE`, `REASON_NOTE`
- `SUPERVISOR_PIN_MASK`: Supervisor PIN 원문이 아닌 마스킹 값
- `WORK_DATE`, `WORK_TIME`, `USER_ID`, `INSERT_DATE`

### 기존 프로그램에서 참고한 점

MAUIPDA Inventory:

- `WMS1330 Discrepancy Adjust`
  - `MES.PKG_PDA_WMS1330.GET_LOT_INFO`
  - `MES.PKG_PDA_WMS1330.SET_LOT_SAVE`

`WMS1330 Discrepancy Adjust`는 LOT 정보를 조회한 뒤 변경 수량을 입력하고 조정 전/후 값을 저장하는 흐름을 가진다. 신규 PDA에서는 처음에 WH002 안에 넣었다가, 현재는 Warehouse 대메뉴의 WH005 `Adjust`로 분리했다.

### 기존과 달라진 점

- 별도 component를 만들지 않고 WH004 Inventory와 같은 `Wh03InventoryStatus.razor`의 Adjust 모드로 구현했다. 화면 표시 번호와 메뉴 역할은 WH005 `Adjust`다.
- 작업자는 Local/CKD를 직접 고르지 않고 LOT 스캔 결과로 감지한다.
- 저장 버튼은 비활성화로 막기보다, 눌렀을 때 부족한 입력값을 커스텀 알럿으로 안내한다.
- 조정 이력은 WH006 `Transactions`에서 `ADJ` 카드로 조회할 수 있다.

## Hidden Location Search (Legacy WH004)

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

현재 Location Search는 별도 저장 프로시저를 만들지 않고 `PdaApi.cs`에서 SQL Server `SIS_TEST` 테이블을 직접 조회한다. 운영 반영 시에는 SIS 표준에 맞춰 프로시저로 분리하는 것을 권장한다.

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

## WH003 Release

### 화면 목적

`WH-003 RELEASE`는 WH001 Schedule의 Release 탭에서 선택한 Pick Slip을 기준으로 출고 피킹을 처리하는 화면이다. 실제 route/component는 기존 코드 호환을 위해 `/wh/07`, `Wh07PdaRelease.razor`를 사용한다.

기획 문서의 WH006 `Release Schedule`과 WH007 `PDA Release Picking` 역할을 나눠 보면, WH006은 "출고해야 할 목록을 보는 화면"이고 WH007은 "실제로 LOT을 스캔해서 출고 처리하는 화면"이다. 현재 구현에서는 WH006 목록 역할을 WH001 Release 탭에 합쳤고, 실제 피킹 작업은 WH003 `Release` 화면에서 처리한다.

WH006 route(`/wh/06`)는 기존 링크 호환용으로만 남아 있으며, 열리면 `/wh/01?tab=release`로 이동한다.

### 현재 구현된 기능

기본 플로우:

1. WH001 Release 탭에서 Pick Slip 카드를 선택한다.
2. 하단 고정 `PICK` 버튼을 누르면 `/wh/07?pickSlipNo=...`로 이동한다.
3. WH003 Release에서 Pick Slip No를 자동 로드하거나 직접 입력 후 `LOAD`한다.
4. Pick Slip line별 요청 품번, 요청 박스 수량, 이미 picked 된 박스 수량, FIFO 추천 위치를 보여준다.
5. 작업자가 `LOT NO`에 LOT No를 입력/스캔한다.
6. API가 해당 LOT이 Pick Slip에 포함된 품번인지, 아직 출고 가능한 상태인지, FIFO 순서에 맞는지 검증한다.
7. 잘못된 품번, 이미 완료된 품번, closed Pick Slip, FIFO 위반이면 `Blocked` 상태와 알럿 메시지를 보여준다.
8. 검증된 LOT이면 `PICK`으로 출고 처리한다.
9. 성공 시 `WMS2020`의 LOT 상태를 출고 상태로 바꾸고 Pick Slip No를 기록한다.

### 주요 용어

- `Pick Slip`: 이번 출고 작업 묶음 번호다. 여러 품번/라인이 하나의 Pick Slip으로 묶일 수 있다.
- `Requested Boxes`: Pick Slip에서 요구한 박스 수량이다. 현재 PDA 테스트 구현에서는 `dbo.WH_ReleaseSchedule.DemandQty`를 사용한다.
- `Picked Boxes`: 이미 피킹 완료된 박스 수량이다. 현재 PDA 테스트 구현에서는 `dbo.WH_ReleaseSchedule.PickedQty`와 `dbo.WH_ReleasePicking` 기록을 기준으로 본다.
- `Lines`: Pick Slip 안에 들어 있는 품번 라인 수다. 같은 Pick Slip에 품번이 여러 개 있으면 여러 line으로 표시된다.
- `FIFO`: 먼저 입고된 LOT부터 먼저 출고하는 규칙이다. 현재 구현에서는 `RCV_DATE`, 없으면 `PROD_DATE`, 그 다음 `LOCATION_NO`, `LOTNO` 순서로 가장 오래된 LOT을 추천한다.

### Pick Slip 데이터 구조

운영 SIS 기준 Pick Slip 요청은 `WMS3050`에 저장되는 현장 자재출고요청 데이터다. 현재 PDA 테스트 DB에서는 이 구조를 `dbo.WH_ReleaseSchedule`로 재명명해서 사용한다. PDA는 Pick Slip No를 스캔한 뒤, 해당 번호에 묶인 line들을 불러오고 LOT No 스캔이 line의 품번/수량/FIFO 조건과 맞는지 검증한다.

주요 컬럼:

- `PICK_SLIPNO`: 출고 작업 묶음 번호
- `REQ_DATE`: 요청일
- `REQ_LOCATION`: 요청 위치 또는 라인 위치
- `PARTNO`: 요청 품번
- `REQ_BOX_QTY`: 요청 박스 수량
- `CLOSE_YN`: 요청 마감 여부
- `CLOSE_DATE`: 요청 마감일
- `CREATE_DATE`, `CREATE_ID`, `UPDATE_DATE`, `UPDATE_ID`: 생성/수정 감사 정보

관련 원천:

- `AMM1020`: MIP 자재소요계획. `REQ_DATE`, `VENDCD`, `LINECD`, `MIP_PARTNO`, `PARTNO`, `ASSY_PLAN_QTY`, `MAT_REQ_QTY`, `PO_UNIT`, `ZMMT0360_IFDATE_IFTIME` 등을 기준으로 소요량을 제공한다.
- `WM20233`: `AMM1020`의 소요량을 조회해서 Pick Slip 요청으로 저장하는 SIS 화면이다.
- `APG_WM20233.INQUERY`: 자재소요계획 조회
- `APG_WM20233.SAVE_PICKSLIP`/`SAVE`: 선택한 요청을 `WMS3050` Pick Slip line으로 저장

### 사용하는 API, 프로시저와 테이블

PDA 코드 호출:

- `PdaApi.Wh001ScheduleReleaseAsync()`
- `PdaApi.WhReleaseSlipStatusAsync(pickSlipNo)`
- `PdaApi.WhReleaseLinesAsync(pickSlipNo)`
- `PdaApi.WhReleaseLotAsync(pickSlipNo, lotNo)`
- `PdaApi.WhPickAsync(body)`

API route:

- `GET /api/wh/schedule/release`
- `GET /api/wh/release/schedule` (compatibility alias)
- `GET /api/wh/release/schedule/{pickSlipNo}/status`
- `GET /api/wh/release/schedule/{pickSlipNo}/lines`
- `GET /api/wh/release/lot`
- `POST /api/wh/release/pick`

현재 PDA 테스트 DB 우선 호출 프로시저:

- `dbo.WH_PDA_RELEASE_SLIP_STATUS`: Pick Slip 존재 여부, 마감 여부, 요청일 확인
- `dbo.WH_PDA_RELEASE_PICK_LINES`: Pick Slip의 요청 품번, 요청 수량, picked 수량, FIFO 추천 위치 조회
- `dbo.WH_PDA_RELEASE_SCAN_LOT`: LOT No 스캔 시 품번/상태/FIFO 검증
- `dbo.WH_PDA_RELEASE_PICK_LOT`: 검증된 LOT을 출고 처리하고 재고/이력 갱신

Pick Slip load 전 검증:

- `dbo.WH_ReleaseSchedule`에 Pick Slip이 없으면 `Pick Slip Not Found` 알럿을 표시한다.
- `Status`가 `Closed` 또는 `Canceled`이면 `Pick Slip Closed` 알럿을 표시한다.
- line 수가 0이면 `No Lines Found` 알럿을 표시한다.
- status API 조회 자체가 실패하면 `Load Failed` 알럿을 표시하고 출고 작업을 막는다.

주요 기준 테이블:

- `dbo.WH_ReleaseSchedule`: Pick Slip 역할의 출고 요청 header/line
- `dbo.WH_Inventory`: 현재 재고 LOT, Location, 출고 가능 상태
- `dbo.tbl_Lot`: LOT No, 생산일, LOT 잔량/상태
- `dbo.MD_Item`: 품번명, Unit
- `dbo.MD_Location`: Location master와 Zone
- `dbo.WH_ReleasePicking`: WH003 Release 출고 피킹 실행 이력
- `dbo.WH_TransactionHistory`: OUT transaction audit log

출고 처리 시 갱신/기록:

- `dbo.WH_Inventory.OnHandQty = 0`, `Status = Released`
- `dbo.tbl_Lot.RemainingQty = 0`, `Status = Released`, `CurrentLocationID = NULL`
- `dbo.WH_ReleaseSchedule.PickedQty` 증가, 필요 시 `Status = Partial/Picked`
- `dbo.WH_ReleasePicking`에 Pick Slip, LOT, Item, Qty, Location, 작업자, 단말 정보 기록
- `dbo.WH_TransactionHistory`에 `TxnType = OUT`, `ReasonCode = RELEASE_PICK` 기록

### 기존 프로그램에서 참고한 점

SIS/WMS:

- 출고 요청 생성 흐름은 `WM20233` 화면과 `APG_WM20233` 패키지를 기준으로 분석했다. `WM20233`은 `AMM1020` 자재소요계획을 조회하고, 선택된 요구량을 `WMS3050` Pick Slip 요청으로 저장한다.
- 출고 요청 묶음과 라인 구조는 `WMS3050`의 Pick Slip 데이터를 기준으로 잡았다.
- 실제 출고 가능한 LOT과 위치 추천은 현재 재고 테이블인 `WMS2020`을 기준으로 잡았다.
- FIFO 추천은 현재 재고 LOT의 입고일/생산일 기반으로 정렬하는 방식으로 구현했다.

기획 문서:

- WH006 `Release Schedule`: 출고 예정 목록, PP/FG 요청 통합, Pick 버튼으로 피킹 화면 이동
- WH007 `PDA Release Picking`: PDA에서 LOT 스캔, FIFO 검증, 잘못된 품번 차단, 출고 처리

### 기존과 달라진 점

- WH006은 별도 화면으로 유지하지 않고 WH001 Schedule의 Release 탭에 통합했다.
- WH001 Release 카드의 개별 버튼은 없애고, WH001 하단 고정 `PICK` 버튼으로 WH003 Release에 진입한다. 실제 route는 `/wh/07`이다.
- WH003 Release는 전체 Pick Slip을 먼저 로드하고, line별 진행률과 FIFO 추천 위치를 본 뒤 LOT을 스캔하는 구조다.
- WH003 Release 네비게이션과 입력 UI는 WH002와 같은 크기 체계로 조정했다.
- Pick Slip placeholder는 `Scan Pick Slip No`, LOT placeholder는 `Scan Lot No`로 정리했다.
- 비활성화된 `LOAD`, `SCAN`, `PICK` 버튼도 PDA에서 보이도록 색 대비를 높였다.
- 현재 테스트 구현에서는 부분 LOT split은 지원하지 않고, LOT 전체 수량 기준으로 Pick한다.

## WH006 Transactions

### 화면 목적

`WH-006 TRANSACTIONS`는 창고 입고, 출고, 수량 조정 이력을 조회하는 화면이다. 실제 route/component는 기존 코드 호환을 위해 `/wh/08`, `Wh08TransactionHistory.razor`를 사용한다.

처음에는 WH002에서 이미 입고된 LOT을 스캔했을 때 접히는 Transaction Log를 보여주는 방식으로 검토했지만, WH002는 입고 작업 화면으로 유지하는 편이 더 명확했다. 그래서 이력 조회는 Warehouse 메뉴의 `Transactions`인 WH006으로 이동했다.

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

현재 WH006의 `Supervisor` 표시는 PIN 값이 아니라 실제 작업자 기준으로 `USER_ID`를 사용한다. WH005 Adjust에서는 Supervisor PIN을 입력받지만, WH006 조회 화면에서는 사용자에게 PIN/masked PIN을 보여주는 것보다 실제 조정 작업자를 보여주는 편이 이해하기 쉽기 때문이다.

Excel export는 한때 CSV 저장 방식으로 검토했지만, PDA 환경에서 파일 저장 위치와 사용 흐름이 애매해서 제거했다.

### 사용하는 API, 테이블

PDA 코드 호출:

- `PdaApi.WhInboundTransactionLogsAsync(lotNo, dateFrom, dateTo)`

현재 테스트 DB 기준 조회 방식:

- `SIS_TEST.WMS2030`이 존재하면 우선 사용한다.
- 현재 테스트 DB에는 `WMS2030`이 없으므로 fallback으로 아래 테이블을 조합한다.

fallback 참조 테이블:

- `SIS_TEST.WMS2010`: 입고 이력 성격의 row. WH006에서는 `IN`으로 표시한다.
- `SIS_TEST.WMS2020`: 현재 재고 row. `WMS2010` 입고 이력이 없는 LOT에 한해서 보조 이력처럼 표시한다. 입고 이력이 이미 있는 LOT은 중복 표시를 피하기 위해 제외한다.
- `SIS_TEST.PDA_WH002_ADJUST_AUDIT`: WH005 Adjust에서 저장한 수량 조정 감사 이력. WH006에서는 `ADJ`로 표시한다.

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

기존 Lotno Status 화면은 LOT 기준으로 `Date`, `Location`, `Qty`, `Status`를 보여준다. WH006은 이 개념을 PDA 신규 Warehouse 메뉴의 전체 Transaction History 화면으로 확장했다.

SIS/DB:

- 현재 재고 기준: `WMS2020`, `WMS2000`
- 입고 이력 기준: `WMS2010`
- 테스트 수량 조정 감사 이력: `PDA_WH002_ADJUST_AUDIT`

### 기존과 달라진 점

- WH002 Inbound 화면 안에 Transaction Log를 넣지 않고 WH006 Transactions로 분리했다.
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

답변: 실제 재고 테이블은 LOT/Location 단위로 여러 row를 가진다. SIS 상세 화면은 여러 row로 보여주고, 요약 화면은 품번별로 합산한다. WH004 Inventory는 PDA에 맞춰 품번별 하나의 카드로 합산하고, 위치 상세는 모달에서 보여준다.

### 재고가 있는데 입고 이력이 없는 경우

질문: 재고가 있다면 입고/출고 이력이 있어야 하지 않는가?

답변: 맞다. 실제 현재 재고라면 입고, 조정, 기초재고 등 어떤 이력/근거가 있어야 한다. 그래서 WH004 Inventory는 자재 마스터 기준 0재고까지 보여주는 방식이 아니라 `WMS2000` 현재 재고가 있는 데이터만 보여주도록 수정했다.

### Adjust 수량 변경 이력 위치

질문: 수량 변경한 것은 어디에 기록되는가?

답변: 현재 구조에서는 WH005 `Adjust` 메뉴에서 수량 변경을 저장한다. 실제 현재 수량은 `SIS_TEST.WMS2020.QTY`와 `SIS_TEST.WMS2000.QTY`에 반영된다. 누가, 언제, 왜, 몇 개를 조정했는지에 대한 감사 이력은 `SIS_TEST.PDA_WH002_ADJUST_AUDIT`에 남긴다. 이 테이블에는 조정 전 수량, 조정 수량, 조정 후 수량, Reason Code, Note, 작업자, Supervisor PIN 마스킹 값이 저장된다.

### Location Map과 Inventory 관계

질문: Inventory와 Location Search는 기능을 합칠 수 있지 않은가?

답변: 일부 기능은 겹친다. WH004 Inventory는 자재를 먼저 보고 해당 자재가 어느 위치에 있는지 확인하는 흐름이고, Location Search는 위치를 먼저 고르고 그 위치에 어떤 자재/LOT가 있는지 확인하는 흐름이다. 따라서 Inventory 안에 위치 탭을 넣는 대신 Location Search 코드는 유지하되, 현재 대메뉴에서는 숨겼다.

질문: Location Map을 격자형으로 보여주면 PDA에서 괜찮은가?

답변: 실제 Location master에는 Area, Column, Row, Level 조합이 많아 PDA 화면에서 격자 전체를 표현하면 너무 복잡해진다. 그래서 격자형 맵은 제외하고 Area/Level/Column/Row 드롭다운으로 조건을 좁힌 뒤 Location 카드와 해당 Location의 품목을 보여주는 방식으로 바꿨다.

### Release Schedule과 Pick Slip

질문: WH006 Release Schedule은 별도 화면으로 있어야 하는가?

답변: 현재는 별도 화면보다 WH001 Schedule 안에 Inbound/Release 탭을 두는 구조가 더 자연스럽다고 판단했다. 입고 예정과 출고 예정 모두 "오늘/앞으로 해야 할 작업 목록"이므로 한 화면에서 탭으로 전환하는 방식이 PDA에 맞다. 그래서 WH006은 `/wh/01?tab=release`로 보내는 redirect로 남겼다.

질문: Pick Slip, Requested Boxes, Picked Boxes, Lines, FIFO는 무슨 뜻인가?

답변: Pick Slip은 출고 작업 묶음 번호다. Requested Boxes는 그 Pick Slip에서 요청한 박스 수량이고, Picked Boxes는 이미 스캔해서 출고 처리된 박스 수량이다. Lines는 Pick Slip 안에 포함된 품번 라인 수다. FIFO는 먼저 입고된 LOT부터 먼저 출고하라는 규칙이며, WH003 Release는 `WMS2020`의 입고일/생산일 기준으로 가장 오래된 LOT을 먼저 요구한다.

질문: Pick Slip 요청은 어디서 등록되는가?

답변: PDA에서 새로 등록하는 것이 아니라 SIS `WM20233`에서 등록한다. `WM20233`은 `AMM1020` 자재소요계획을 읽고, 선택한 소요량을 `WMS3050` Pick Slip 요청 라인으로 저장한다. 이후 PDA Release는 이 Pick Slip No를 스캔해 line별 품번/수량/FIFO 조건을 검증하고 출고 처리한다.

### WH006 Transaction History 이동

질문: WH002에서 이미 입고된 LOT을 스캔했을 때 Transaction Log를 같이 보여주는 게 맞는가?

답변: 처음에는 WH002 안에 접히는 Transaction Log를 넣었지만, WH002는 작업 화면이고 이력 조회는 별도 화면이 더 적합하다. 그래서 Transaction Log는 WH002에서 제거하고 Warehouse 메뉴의 `Transactions`, 즉 WH006으로 옮겼다. 실제 route는 `/wh/08`이다.

질문: ADJ 이력은 카드에 전부 펼쳐서 보여줘야 하는가?

답변: ADJ도 기본 카드에서는 IN/OUT과 같은 일반 이력처럼 보이게 하고, `DETAIL`을 눌렀을 때만 Before/Change/After, Reason, Note, Supervisor를 보여주도록 바꿨다. 이렇게 해야 리스트 스캔성이 좋아지고 조정 상세는 필요할 때만 확인할 수 있다.

질문: WH006에서 Excel export가 필요한가?

답변: CSV 저장 방식으로 검토했지만 PDA 환경에서는 파일 저장 위치와 후속 사용 흐름이 애매해서 제거했다. Export가 필요하면 Web PC 화면 또는 별도 리포트 화면에서 처리하는 편이 더 자연스럽다.

## 현재 구현 시 주의할 점

- `SIS_TEST`는 테스트용 스키마다. 운영 반영 시에는 실제 SIS/MES DB 스키마와 권한, 프로시저 배포 방식이 별도로 필요하다.
- WH002의 입고/취소 로직은 테스트 구현이므로 실제 운영에서는 기존 Oracle 패키지의 예외 처리, 트랜잭션, 인터페이스 테이블 반영 범위를 더 확인해야 한다.
- WH004 Inventory의 `WMS2000`은 테스트 DB에 맞춰 생성/시드했다. 운영 DB에 실제 `WMS2000`이 있다면 해당 구조에 맞춰 프로시저를 다시 정렬해야 한다.
- 숨겨 둔 `Location Search`는 현재 `PdaApi.cs`에서 직접 SQL로 조회한다. 운영 반영 시에는 `PDA_WH04_LOCATION_SEARCH`, `PDA_WH04_LOCATION_ITEMS` 같은 프로시저로 분리하는 편이 유지보수에 좋다.
- 숨겨 둔 `Location Search`는 SIS_TEST 직접 조회가 실패하면 API `/api/wh/locations`로 fallback한다. fallback 테이블인 `dbo.MD_Location`은 로컬 개발 DB 기준이므로 실제 운영 Location master와 혼동하지 않아야 한다.
- WH001의 PO 조회는 `WM40120` 기준으로 만들었고, SCM에서 PO가 신규 생성되는 원천 화면/배치까지 완전히 대체한 것은 아니다.
- `GRN_QTY`는 PO schedule에 저장되는 값이라기보다 GRN 실적을 합산해 계산하는 값이다. 운영에서는 GRN cancellation, return, reversal까지 반영해야 한다.
- WH005 Adjust의 Supervisor PIN은 현재 테스트 구현에서 최소 길이 검증과 마스킹 저장만 한다. 운영에서는 실제 승인자 계정/권한 검증과 감사 로그 보관 정책을 추가해야 한다.
- WH003 Release의 운영 분석 기준은 `WMS3050` Pick Slip과 `WMS2020` 현재 재고지만, 현재 PDA 테스트 구현은 `dbo.WH_ReleaseSchedule`, `dbo.WH_Inventory`, `dbo.tbl_Lot`, `dbo.WH_ReleasePicking`, `dbo.WH_TransactionHistory`와 `dbo.WH_PDA_RELEASE_*` 프로시저로 재명명해 연결했다. 실제 route/component는 `/wh/07`, `Wh07PdaRelease.razor`다.
- WH006은 현재 redirect 화면이다. 운영에서 별도 Release Schedule 화면이 다시 필요해지면 WH001 Release 탭의 `Wh001ScheduleReleaseAsync()` 호출부와 카드 UI를 분리해서 재사용할 수 있다.
- WH006 Transactions는 `WMS2030`이 있으면 우선 사용하도록 만들었지만, 현재 테스트 DB에는 `WMS2030`이 없어 `WMS2010`, `WMS2020`, `PDA_WH002_ADJUST_AUDIT`를 조합한다. 운영 반영 시에는 실제 Transaction History 표준 테이블/프로시저 기준으로 재정렬해야 한다. 실제 route/component는 `/wh/08`, `Wh08TransactionHistory.razor`다.
- WH005 Adjust는 별도 component를 만들지 않고 WH004 Inventory와 같은 `Wh03InventoryStatus.razor`의 Adjust 모드로 구현했다. 운영 정책상 완전한 독립 화면이 필요하면 현재 `PDA_WH002_ADJUST_QTY` 호출부를 재사용해 별도 component로 분리할 수 있다.

## 다음 개발 요청용 프롬프트

새 Warehouse/PDA 기능을 이어서 개발할 때는 아래 내용을 같이 주면 좋다. 그러면 기존 SIS/PDA/MES 흐름, 테스트 DB, UI 기준을 한 번에 맞춰서 분석하고 구현하기 쉽다.

```text
현재 작업 repo는 C:\Users\Young\Desktop\Seoyon\Jackson\ames-docs 이고,
PDA 프로젝트는 src/05_Pda/AMES.Pda,
API 프로젝트는 src/04_Api/AMES.Api 기준으로 봐줘.

Warehouse PDA 현재 표시 화면은 아래 기준이야.
- WH001 Schedule: /wh/01
- WH002 Inbound: /wh/02
- WH003 Release: /wh/07
- WH004 Inventory: /wh/03
- WH005 Adjust: /wh/03?tab=adjust
- WH006 Transactions: /wh/08

새 기능을 만들 때는 먼저 기존 SIS, MAUIPDA, MES에서 비슷한 화면/프로시저/테이블이 있는지 찾아보고,
그 근거를 화면명, package/procedure, table 단위로 정리해줘.

테스트 구현은 SQL Server AMES_DEV의 SIS_TEST 스키마를 기준으로 하고,
운영 원본은 Oracle SIS/MES 구조를 참고하되 테스트 DB에 필요한 테이블/프로시저만 복사 또는 재현해줘.

UI는 WH001/WH002 기준의 밝은 PDA 스타일과 Radzen 컴포넌트를 우선 사용해줘.
화면 문구와 알럿은 영어로 작성하고, 기본 alert 대신 중앙 카드형 커스텀 알럿을 사용해줘.

구현 후에는 아래 내용을 docs/WH_PDA_IMPLEMENTATION_SUMMARY.md에 업데이트해줘.
- 어떤 기존 화면/프로시저/테이블을 참고했는지
- 어떤 테스트 DB table/procedure/API를 추가했는지
- 현재 구현된 기능과 기존 프로그램과 달라진 점
- 테스트 방법과 주의할 점
- 내가 질문한 업무 개념과 답변 요약
```

작업할 때 추가로 알려주면 좋은 사항:

- 새 화면이 기존 화면 번호를 유지해야 하는지, 아니면 메뉴 표시 번호를 새로 정리해도 되는지
- 실제 운영 DB 기준으로 맞춰야 하는지, `SIS_TEST` 테스트 구현이면 되는지
- 기존 SIS/MAUIPDA/MES 중 어느 프로그램 흐름을 우선 기준으로 볼지
- 스캔 대상이 LOT No, Location No, Pick Slip No, Box/Case No 중 무엇인지
- 저장 시 실제 운영 프로시저까지 찾아야 하는지, 테스트용 프로시저를 먼저 만들어도 되는지
- 화면 문구는 영어 기준으로 유지할지, 업무 설명 문서만 한국어로 둘지
- 커밋을 화면별로 나눌지, DB/API/UI/docs 단위로 나눌지
