# SRM 발주·출하 테스트 API

기존 AMES 솔루션과 참조 관계가 없는 독립 .NET 10 API입니다.
기존 API 소스 및 업무 테이블을 수정하지 않습니다.

## 실행

```powershell
& D:\github\a_mes\test-api\Start-TestApi.ps1 -BindAddress 192.168.1.68 -Port 5220
```

`SRM_TEST_API_KEY` 환경 변수가 없으면 실행 시 키를 안전한 입력창으로 받습니다.
키를 소스나 설정 파일에 저장하지 않습니다. ASP.NET 요청 URL 로그도 억제합니다.
DB 접속 정보는 기존 API appsettings.json에서 읽고, 실행 프로세스의 연결 주소를
`192.168.1.100:1433 / AMES_DEV`로 지정합니다. 기존 설정 파일은 수정하지 않습니다.
기본 실행 주소는 http://192.168.1.68:5220 이며 IP 변경 시 BindAddress를 바꿔 실행하세요.
종료는 실행 터미널에서 Ctrl+C입니다.

## 발주 조회

```http
GET /Service/WEBSRV_INQUERY_PO.ashx?APIKEY=<설정한키>&CORCD=7700&BIZCD=7710&PURC_ORG=1A7700&VENDCD=310471&PO_DATE_BEG=2026-08-01&PO_DATE_TO=2026-09-30
```

- 위 7개 매개변수 모두 필수입니다. 같은 매개변수를 중복 지정할 수 없습니다.
- `/api/purchase-orders`도 동일한 인증·조건을 사용하는 별칭입니다.
- 구매조직(PURC_ORG), 거래처(VENDCD)는 일치 검색합니다.
- 날짜는 yyyy-MM-dd 형식이며 발주일(PO_DATE) 기준 시작일/종료일을 모두 포함합니다.
- 회사/사업장은 원본 JSON 및 테이블에 컬럼이 없어, 샘플 전체를 7700/7710에 속한 것으로 취급합니다.
  실행 환경 SRM_TEST_CORCD, SRM_TEST_BIZCD로 변경할 수 있습니다.
- 조건 불일치 또는 조회 결과가 없으면 HTTP 200과 빈 배열 []을 반환합니다.
- 키 누락/불일치는 401, 필수 조건 누락·잘못된 날짜·지원하지 않는 매개변수는 400입니다.
- DB 조회 실패는 503입니다. 위 오류 규칙은 이 테스트 API의 규칙이며 외부 서비스와 같다고 보장하지 않습니다.
- `/api/health`는 DB 연결과 테스트 테이블 건수를 확인합니다.

## 응답과 DB

`dbo.TEST_SRM_PurchaseOrderResponse`에서 매 요청마다 SELECT합니다.
발주 항목 45개만 반환하며 data 래퍼 없이 JSON 배열을 반환합니다.
관리 컬럼 SourceRowNo, LoadedAtUtc는 응답에서 제외합니다.
대문자 필드명, 정수, null, 날짜 문자열을 유지하며 SourceRowNo 순서로 반환합니다.
샘플의 SD_DELI_DATE 값 0000-00-00도 그대로 유지합니다.
DB에 저장된 데이터가 바뀌면 다음 조회에 반영됩니다. 발주 조회 API는 읽기 전용이며,
출하 수신 API는 아래의 TEST_SRM_Shipment / TEST_SRM_ShipmentItem 테이블에 저장합니다.

샘플: SrmMockApi/Data/PO_7700_310471_EN_data.json (426건 / 45개 필드)
2026-08-01~2026-09-30 조회 시 전체 샘플 426건이 반환됩니다.
사용자가 제공한 2026-06-01~2026-07-02 조건은 샘플에 해당 발주가 없어 []입니다.

Initialize-TestDatabase.ps1은 신규 테스트 테이블 생성 및 최초 적재용입니다.
동명 테이블이 있으면 중단하며 덮어쓰거나 삭제하지 않습니다.
테이블은 세션 종료 후에도 유지되는 일반 테스트 테이블입니다.

## 검증

SRM_TEST_API_KEY 환경 변수를 설정한 셸에서 다음을 실행합니다.

```powershell
& D:\github\a_mes\test-api\Verify-TestApi.ps1
```

전체 응답과 원본의 필드별 일치, 날짜 경계, 검색 조건, 인증 및 잘못된 입력을 확인합니다.
검증은 원본 샘플이 그대로 적재된 상태를 기준으로 합니다.

## 출하 수신 — SRM_MM22001.Save() 모의 처리

`POST /api/shipments`는 이번에 추가한 **테스트 전용 규격**입니다.
발주처에 원래 존재하던 API 주소나 Web Forms DirectMethod 규격이 아닙니다.
인증은 기존 `SRM_TEST_API_KEY`를 `X-API-KEY` 헤더에 넣습니다.
Swagger의 출하 수신 항목에서 예제 본문으로 바로 호출할 수 있습니다.

```http
POST /api/shipments
X-API-KEY: <설정한키>
Content-Type: application/json

{
  "REQUEST_ID": "SHIP-TEST-001",
  "CORCD": "7700",
  "BIZCD": "7710",
  "VENDCD": "310471",
  "PURC_ORG": "1A7700",
  "PURC_PO_TYPE": "1KMA",
  "DELI_DATE": "2026-09-17",
  "ARRIV_DATE": "2026-09-18",
  "ARRIV_TIME": "0930",
  "TRUCK_NO": "TEST-TRUCK",
  "USER_ID": "AMES_TEST",
  "ITEMS": [
    {
      "PONO": "TEST-PO-001",
      "PONO_SEQ": "00010",
      "UNIT_PACK_QTY": 10,
      "DELI_QTY": 100,
      "VEND_LOTNO": "LOT-TEST-001",
      "PRDT_DATE": "2026-09-16",
      "CHANGE_4M": ""
    }
  ]
}
```

`TEST-PO-001`은 임의의 테스트 발주번호입니다. 발주 존재 여부를 검사하지 않습니다.
발주 조회 샘플에는 `PONO_SEQ`가 없어 샘플과의 항번/잔량 검증을 구현하지 않았습니다.
`PONO_SEQ`는 선행 0을 보존하는 문자열입니다. 수량은 JSON 숫자로 전송합니다.
필드명은 위 대문자 그대로 사용하며 미정의 필드는 400으로 거절합니다.

### 원본과의 대응 및 차이

| 원본 처리 | 테스트 API |
|---|---|
| 로그인 정보 및 화면 컨트롤 참조 | 회사·사업장·업체·일시 등을 JSON으로 수신 |
| 납품/포장수량, 제조일, 도착일 검증 | 필수값, 양수 수량, 날짜·시간, 도착일 순서 검사 |
| `SAVE_SAPIF_4` → SAP 호출 → `SAVE_SUCCESS_4` | SAP 성공으로 모의하여 수신 기록을 저장하고 완료 응답 |
| 구매유형 `1K10`의 `SAVE_GOODS` | `SAP_STATUS=NOT_REQUIRED`로 기록 |
| 납품서 출력 | 수행하지 않음. `printYN` 필드는 받지 않음 |
| DB 발주 잔량·업체별 권한 검사 | 수행하지 않음. 구성된 회사/사업장만 검사 |

박스수량(납품수량/포장수량) 상한 1000은 원본 검사를 따릅니다(1000/1A1100 조합 제외).
시간은 원본보다 엄격하게 `0000`~`2359`를 받습니다. 0 수량은 원본 Save처럼 생략하지 않고 거절합니다.
요청 본문은 최대 1 MiB, 품목은 1~1000건, 수량은 0 초과 10억 이하로 제한합니다.
`USER_ID`는 테스트용 송신자 표시값이며 실제 사용자 인증으로 사용하지 않습니다.
반입 확인, 취소·수정, 실제 SAP 호출, 발주/재고 테이블 갱신은 이 API의 처리 범위에 포함되지 않습니다.

### 성공 응답 및 재전송

```json
{
  "success": true,
  "duplicate": false,
  "data": {
    "REQUEST_ID": "SHIP-TEST-001",
    "DELI_NOTE": "TEST-<고유값>",
    "STATUS": "COMPLETED",
    "SAP_STATUS": "SIMULATED_SUCCESS",
    "SAP_SIMULATED": true,
    "RECEIVED_AT_UTC": "2026-09-17T00:00:00+00:00",
    "ITEM_COUNT": 1,
    "TOTAL_DELI_QTY": 100
  }
}
```

- `REQUEST_ID`는 우리 쪽 출하 건에 부여한 고유 전송번호(영문·숫자·하이픈·밑줄 1~100자)입니다.
- 동일 ID와 같은 내용으로 재전송하면 HTTP 200, `duplicate=true`, **기존 납품서 번호**를 반환합니다.
- 같은 ID의 다른 내용은 409입니다. 품목 배열 순서도 내용 비교에 포함됩니다.
- 최초 저장과 재전송 모두 200이며, 저장이 완료된 뒤 성공을 반환합니다.
- `GET /api/shipments/{requestId}`에 같은 인증 헤더를 넣으면 결과와 저장된 요청 본문을 조회합니다.
- 인증 실패 401, 입력 오류 400, 미존재 조회 404, 크기 초과 413, Content-Type 오류 415, 저장소 오류 503입니다.

기록은 기본적으로 발주 조회와 같은 SQL Server / AMES_DEV에 저장됩니다.

| 테이블 | 저장 내용 |
|---|---|
| dbo.TEST_SRM_Shipment | 요청번호, 회사·사업장·업체, 일시, 차량, 처리 결과, 총수량, 요청·응답 JSON |
| dbo.TEST_SRM_ShipmentItem | 요청별 품목 순번, 발주번호·항번, 포장수량·납품수량, LOT, 제조일, 4M 변경 |

이는 연결 종료 시 사라지는 #임시 테이블이 아닌, 테스트용 일반 테이블입니다.
`Initialize-ShipmentDatabase.ps1`로 생성하며 기존 테이블과 데이터는 덮어쓰지 않습니다.
수량은 decimal(28,6)이며 API도 소수점 이하 6자리까지 받습니다.
헤더와 모든 품목은 한 트랜잭션으로 저장합니다. REQUEST_ID 고유키와 DB 잠금으로
여러 API 인스턴스에서도 중복 요청을 직렬화합니다. 항번은 문자열로 보존합니다.

기존 로컬 JSON 기록은 자동 이관하지 않습니다.
DB 없이 검증할 때만 `SRM_TEST_SHIPMENT_STORAGE=File`을 지정하면 이전 파일 저장소를 사용합니다.
이 경우 `SRM_TEST_SHIPMENT_PATH`로 경로를 지정할 수 있으며, 기본은 App_Data/shipments입니다.
이전 파일 기록을 DB에 옮기기 전에는 같은 요청을 재전송하면 새 DB 기록으로 생성됩니다.
### 출하 API 검증

```powershell
& D:\github\a_mes\test-api\Verify-Shipments.ps1
```

기본 검증은 별도 Release 출력 경로로 빌드하고 127.0.0.1:15229에서 일회성 키와 격리된 파일 저장소를 사용합니다.
공유 SQL DB에는 접속하지 않습니다. 인증, 입력 검증, 저장, 중복/충돌, 동시 재전송,
상품매출 분기, 프로세스 재시작 후 기록 유지, Swagger 및 기존 발주 조회 입력 검사를 확인합니다.
테스트 프로세스는 종료하고 검증 기록은 `App_Data/check-*`에 남깁니다.
실행 중인 기존 5220 프로세스에 코드를 반영하려면 종료 후 `Start-TestApi.ps1`로 다시 실행하세요.

실제 DB 저장 검증:

```powershell
& D:\github\a_mes\test-api\Initialize-ShipmentDatabase.ps1
& D:\github\a_mes\test-api\Verify-Shipments.ps1 -Database
```

DB 검증은 고유 VERIFY- 요청번호로 헤더·품목 저장, 동시 재전송, 재시작 후 조회를 확인합니다.
검증 종료 시 그 실행에서 생성한 요청번호의 데이터만 삭제합니다. 실제 수신 기록은 삭제하지 않습니다.

수신 데이터 확인:

```sql
SELECT REQUEST_ID, DELI_NOTE, VENDCD, DELI_DATE, STATUS, ITEM_COUNT, TOTAL_DELI_QTY
FROM AMES_DEV.dbo.TEST_SRM_Shipment ORDER BY RECEIVED_AT_UTC DESC;
SELECT REQUEST_ID, ITEM_NO, PONO, PONO_SEQ, DELI_QTY, UNIT_PACK_QTY, VEND_LOTNO
FROM AMES_DEV.dbo.TEST_SRM_ShipmentItem ORDER BY REQUEST_ID, ITEM_NO;
```
