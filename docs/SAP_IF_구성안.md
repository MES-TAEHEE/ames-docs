# SAP B1 ↔ AMES 인터페이스 구성안

기준 문서: `EOS_SAP_B1_Interface_Definition_V1.2.xlsx` (WJ-B1-IFD-001, 2026-11-06 제정 · 11-12 개정)
작성일: 2026-11-13 · 상태: **구성안(미구현)** — 아래 코드·SQL 은 설계용 초안이며 아직 소스·DB 에 적용하지 않았다.

---

## 0. 한눈에 보기

| 항목 | 내용 |
|---|---|
| 경계 | REST API 가 아니라 **`EOS_IF` DB 테이블**(웅진 ↔ AMES) |
| 접속 | AMES 가 `EOS-DB01`(172.29.3.21, 기본 인스턴스) `EOS_IF` 에 `ames_if_svc` 로 **직접 접속** (11-13 읽기 접속·권한 확인 완료) |
| 실행 주체 | `AMES.Api` 의 `ScheduledWorker` 2개 — `SAPIN`(수신) · `SAPOUT`(송신) |
| 홀드 방지 | 현장 트랜잭션은 AMES DB 의 **아웃박스에 INSERT 만** 하고 커밋. EOS_IF 쓰기는 Worker 가 비동기로 수행. 원격 쿼리와 로컬 트랜잭션을 한 트랜잭션으로 묶지 않는다 |
| 로그 | AMES DB 에 `SAP_IfLog`(행 단위 이력) · `SAP_IfOutbox`(송신 큐 겸 이력) + 기존 `SYS_InterfaceMonitor`(요약) |
| 접속 정보 | 소스·appsettings·공통코드에 **넣지 않음**. 환경변수 `ConnectionStrings__EOS_IF` |

### 데이터 흐름

```
[수신] SAP B1 → (웅진) → EOS_IF.IF_MD_* / IF_TX_PO_* / IF_TX_MRP_PLAN_*
                                   │  SAPIN Worker (5분, N 행 읽기)
                                   ▼
                          AMES MD_* / 구매발주 / 포털 계획   (로컬 트랜잭션)
                                   │  Y / E 되쓰기 (IF_STATUS·IF_MSG·PROC_TS)
                                   ▼
                              EOS_IF (상태 갱신)

[송신] 현장 이벤트(WO·실적·입고·출하…) → AMES SAP_IfOutbox INSERT (같은 DB, 즉시 커밋)
                                   │  SAPOUT Worker (짧은 배치)
                                   ▼
                          EOS_IF.IF_TX_* INSERT (N) → (웅진) DI API → B1 문서
                                   ▲  DocEntry·Y/E 를 웅진이 되씀 → SAPOUT 이 읽어 아웃박스에 반영
```

---

## 1. 확인된 사실 (11-13 읽기 전용 점검)

- 접속: `eos-db01` / `EOS_IF` / `ames_if_svc` / SQL Server 2022(16.0) / 콜레이션 `SQL_Latin1_General_CP1_CI_AS` / 역할 `db_datareader`.
- 권한(정의서와 일치):
  - 수신 대상(`IF_MD_*`, `IF_TX_PO_HDR`, `IF_TX_MRP_PLAN_HDR`): `IF_STATUS`·`IF_MSG`·`PROC_TS` 컬럼 UPDATE.
  - 송신 대상(`IF_TX_PRODORDER/PRODISSUE/PRODRECEIPT/ADJUST` 및 `*_HDR/_LINE`): INSERT + 헤더 `IF_STATUS` UPDATE(재처리).
  - `IF_CONTROL`·`IF_LOG`·`IF_CODE_MAP` 은 읽기 전용(웅진 영역).
- 데이터: `IF_MD_UOM` 5건(EA·SHT·G·KG·RACK, 전부 `U`/`N`)만 존재. 나머지 `IF_MD_*`·`IF_TX_*` 는 0건. 웅진 서비스는 M01~M06 을 5분마다 실행 중.
- 웅진 쪽 FAIL 로그: IF-M06 창고 `01` "WH_AREA/LOC_TYPE 변환표 미등록", IF-M01 `Manual` "UOM 변환표 미등록".
- **컬럼 길이가 정의서와 다르다**: `IF_MD_UOM.IF_MSG` nvarchar(2000, 정의서 1000), `UOMName` nvarchar(60, MES 30), `ConvFactor` decimal(9,?)(정의서 decimal(18,8)). → 전 테이블 구조 대조 필요(2단계).
- EOS_IF 는 **운영 DB** 이고 웅진 서비스가 동작 중이다(별도 테스트 DB 없음).

---

## 2. 접속 정보 취급

1. 비밀번호는 **리포지토리 어디에도 넣지 않는다**(소스·appsettings·공통코드·커밋·스크립트·로그).
2. 환경변수 `ConnectionStrings__EOS_IF` 로 전체 접속문자열을 받는다(`ConnectionStrings__AMES` 와 같은 방식).
   - 개발: 현재 PowerShell 프로세스에서만 설정 후 실행. 셸 기록에 비밀번호가 남는 명령을 쓰지 않는다.
   - 서버: `AMES.Api` 앱풀 환경변수(IIS 설정에 보관, 재게시해도 유지).
3. 접속문자열 형식(값은 환경에서만):
   ```
   Server=172.29.3.21;Database=EOS_IF;User ID=ames_if_svc;Password=<환경변수에서>;
   Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=AMES.Api.SapIf
   ```
   - `Connect Timeout` 을 5 로 낮추지 않는다(TLS 사전 로그인 핸드셰이크 5초 초과 — CLAUDE.md 기존 주의).
   - `TrustServerCertificate` 는 인증서 체인 정비 전 임시 — EOS IT 확인 후 제거 여부 결정.
4. 변수가 비어 있으면 Worker 는 **소스 0개로 동작**(예외 없음, 모니터에 "미설정" 기록). PoSync 와 같은 방식.
5. 대화에 평문으로 노출된 비밀번호(`CHANGE_ME…` 형태)는 정식 가동 전에 **EOS IT 에 교체 요청**.

---

## 3. 단계별 구성

### 1단계 — 읽기 전용 점검 (완료)

접속·권한·데이터 확인. 재실행용 스크립트(비밀번호는 환경변수로만):

```powershell
# 현재 프로세스에만 설정 — 이 줄을 스크립트 파일·기록에 남기지 말 것
$env:EOSIF_PW = Read-Host -AsSecureString | ConvertFrom-SecureString -AsPlainText   # PS7. 5.1 은 아래 대안
$cs = "Server=172.29.3.21;Database=EOS_IF;User ID=ames_if_svc;Password=$($env:EOSIF_PW);Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;Application Name=AMES-IF-Probe"
Add-Type -AssemblyName System.Data
$c = New-Object System.Data.SqlClient.SqlConnection $cs
try {
  $c.Open()
  function Q($sql){ $cmd=$c.CreateCommand(); $cmd.CommandText=$sql; $cmd.CommandTimeout=15
    $t=New-Object System.Data.DataTable; $t.Load($cmd.ExecuteReader()); $t }
  Q "SELECT @@SERVERNAME, DB_NAME(), SUSER_NAME()" | Format-Table -AutoSize
  Q "SELECT IF_ID, ENABLED, LAST_RUN_TS, LAST_STATUS, LAST_ROWS FROM IF_CONTROL ORDER BY RUN_ORDER" | Format-Table -AutoSize
  Q "SELECT TOP 20 * FROM V_IF_QUEUE" | Format-Table -AutoSize
} finally { $c.Dispose(); Remove-Item Env:EOSIF_PW -ErrorAction SilentlyContinue }
```

> Windows PowerShell 5.1 에서는 `Read-Host -AsSecureString` 결과를 `[Runtime.InteropServices.Marshal]::PtrToStringBSTR(...)` 로 풀어 쓴다.

### 2단계 — 구조 대조 (읽기 전용)

목적: EOS_IF 실제 DDL 과 정의서 매핑 시트·AMES `MD_*` 컬럼을 대조해 **불일치 목록** 작성.

```sql
-- EOS_IF 에서 실행 (읽기). 테이블별 컬럼·타입·길이·NULL
SELECT t.name AS TableName, c.column_id, c.name AS Col, TYPE_NAME(c.user_type_id) AS Typ,
       c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
ORDER BY t.name, c.column_id;

-- UNIQUE·인덱스 (정의서: IF_TX_* 는 (MES 키, IF_ACTION) UNIQUE, (IF_STATUS, IF_SEQ) 인덱스)
SELECT t.name AS TableName, i.name AS IndexName, i.is_unique,
       STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyCols
FROM sys.tables t
JOIN sys.indexes i ON i.object_id = t.object_id AND i.index_id > 0
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
GROUP BY t.name, i.name, i.is_unique ORDER BY t.name, i.name;

-- 변환표(웅진이 적용하는 코드 변환) — 공통코드 정비 대상 확인
SELECT * FROM IF_CODE_MAP ORDER BY 1, 2;
```

산출물: `docs/SAP_IF_구조대조.md`(불일치·AMES 측 길이 검사 필요 컬럼·공통코드 보강 목록).

### 3단계 — AMES 로그·아웃박스 DDL

파일: `dist/migrate_sap_if_log.sql` (재실행 안전, `sqlcmd -f 65001 -I -b`). 프로젝트 규칙 준수: FK 없음, 한글은 nvarchar, `COLLATE DATABASE_DEFAULT`, `…By` 는 varchar(20), 감사 컬럼 맨 뒤 `CreatedBy → CreatedTS → ModifiedBy → ModifiedTS`.

```sql
/* ============================================================
   migrate_sap_if_log.sql — SAP B1 인터페이스 로그·아웃박스
   재실행 안전. 적용: sqlcmd -f 65001 -I -b -d <DB> -i dist/migrate_sap_if_log.sql
   ============================================================ */
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SAP_IfLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SAP_IfLog (
        LogID        bigint IDENTITY(1,1) NOT NULL,
        RunID        uniqueidentifier     NOT NULL,                       -- Worker 1회 실행 묶음
        IfID         varchar(10)          NOT NULL,                       -- IF-M01 … IF-T09
        Direction    varchar(3)           NOT NULL,                       -- IN / OUT
        KeyValue     varchar(60)          NULL,                           -- 품번·WO번호 등 업무 키
        IfSeq        bigint               NULL,                           -- EOS_IF.IF_SEQ
        Action       varchar(2)           NULL,                           -- I/U/D, C/U/L/X
        Result       varchar(5)           NOT NULL,                       -- OK / WARN / FAIL / SKIP
        Message      nvarchar(1000)       NULL,
        RowsAffected int                  NULL,
        DurationMs   int                  NULL,
        LoggedAt     datetime2(0)         NOT NULL CONSTRAINT DF_SAP_IfLog_LoggedAt DEFAULT SYSDATETIME(),
        CreatedBy    varchar(20)          NULL,
        CreatedTS    datetime2(0)         NULL CONSTRAINT DF_SAP_IfLog_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy   varchar(20)          NULL,
        ModifiedTS   datetime2(0)         NULL,
        CONSTRAINT PK_SAP_IfLog PRIMARY KEY CLUSTERED (LogID),
        CONSTRAINT CK_SAP_IfLog_Result CHECK (Result IN ('OK','WARN','FAIL','SKIP')),
        CONSTRAINT CK_SAP_IfLog_Dir    CHECK (Direction IN ('IN','OUT'))
    );
    CREATE INDEX IX_SAP_IfLog_If_Time ON dbo.SAP_IfLog (IfID, LoggedAt DESC);
    CREATE INDEX IX_SAP_IfLog_Key     ON dbo.SAP_IfLog (KeyValue) INCLUDE (IfID, Result, LoggedAt);
    CREATE INDEX IX_SAP_IfLog_Run     ON dbo.SAP_IfLog (RunID);
END
GO

IF OBJECT_ID(N'dbo.SAP_IfOutbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SAP_IfOutbox (
        OutboxID     bigint IDENTITY(1,1) NOT NULL,
        IfID         varchar(10)          NOT NULL,                       -- IF-T01 … IF-T09
        MesKey       varchar(60)          NOT NULL,                       -- MES_WO_NO / MES_RESULT_NO …
        Action       varchar(2)           NOT NULL,                       -- C/U/L/X
        PayloadJson  nvarchar(max)        NOT NULL,                       -- 헤더+라인 스냅샷(이벤트 시점 값 고정)
        EventAt      datetime2(0)         NOT NULL CONSTRAINT DF_SAP_IfOutbox_EventAt DEFAULT SYSDATETIME(), -- MES_TS
        Status       char(1)              NOT NULL CONSTRAINT DF_SAP_IfOutbox_Status DEFAULT 'N', -- N 대기 / P 전송중 / Y 전달완료 / E 오류
        RetryCount   int                  NOT NULL CONSTRAINT DF_SAP_IfOutbox_Retry DEFAULT 0,
        NextTryAt    datetime2(0)         NULL,
        LastError    nvarchar(1000)       NULL,
        EosIfSeq     bigint               NULL,                           -- 넣은 EOS_IF.IF_SEQ (헤더)
        B1Status     char(1)              NULL,                           -- 웅진이 되쓴 IF_STATUS (N/P/Y/E)
        B1DocEntry   int                  NULL,
        B1DocNum     int                  NULL,
        B1Msg        nvarchar(1000)       NULL,
        SentAt       datetime2(0)         NULL,
        CreatedBy    varchar(20)          NULL,
        CreatedTS    datetime2(0)         NULL CONSTRAINT DF_SAP_IfOutbox_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy   varchar(20)          NULL,
        ModifiedTS   datetime2(0)         NULL,
        CONSTRAINT PK_SAP_IfOutbox PRIMARY KEY CLUSTERED (OutboxID),
        CONSTRAINT CK_SAP_IfOutbox_Status CHECK (Status IN ('N','P','Y','E')),
        CONSTRAINT UQ_SAP_IfOutbox_Key UNIQUE (IfID, MesKey, Action)      -- 멱등성(중복 이벤트 방지)
    );
    CREATE INDEX IX_SAP_IfOutbox_Queue ON dbo.SAP_IfOutbox (Status, NextTryAt, OutboxID);
END
GO

/* 보존 정리: SAP_IfLog 90일(OK·SKIP 만), 아웃박스는 완료(Y) 90일. E 는 지우지 않는다. */
CREATE OR ALTER PROCEDURE dbo.usp_Cleanup_SAP_If
    @KeepDays int = 90
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut datetime2(0) = DATEADD(DAY, -@KeepDays, SYSDATETIME());
    WHILE 1 = 1
    BEGIN
        DELETE TOP (5000) FROM dbo.SAP_IfLog WHERE LoggedAt < @cut AND Result IN ('OK','SKIP');
        IF @@ROWCOUNT = 0 BREAK;
    END
    WHILE 1 = 1
    BEGIN
        DELETE TOP (5000) FROM dbo.SAP_IfOutbox WHERE Status = 'Y' AND ModifiedTS < @cut;
        IF @@ROWCOUNT = 0 BREAK;
    END
END
GO
```

- 같은 변경을 `dist/AMES_Schema.sql` 에 반영하고 `rebuild_db.sh` 에 등록한다(감사 컬럼 순서 스크립트는 이미 맨 뒤라 영향 없음).
- 정리 작업은 SQL Agent 작업 `[AMES] SAP IF Cleanup`(매일 04:30, `EXEC dbo.usp_Cleanup_SAP_If`)로 등록 — 기존 `setup_job_*.sql` 패턴을 따른다.
- **DB 적용(개발·운영)은 승인 후** — 확인사항 파일의 "공통 로그용 DB 변경은 적용 전 별도 확인" 방침.

### 4단계 — 공통 기반 (AMES.Data)

| 구성요소 | 위치 | 역할 |
|---|---|---|
| `EosIfConnectionFactory` | `AMES.Data/Connection` | 환경변수 `ConnectionStrings__EOS_IF` 로 연결, 비어 있으면 비활성. 연결 타임아웃 15초·명령 타임아웃 30초 |
| `SapIfRules` | `AMES.Data/Services/SapIf` | 순수 규칙: 길이 절단/검사, 액션 해석, 처리 순서, 재시도 간격 |
| `SapIfLogRepository` | `AMES.Data/Repositories` | `SAP_IfLog` 기록(실패해도 예외를 던지지 않음) |
| `SapIfOutboxRepository` | `AMES.Data/Repositories` | 아웃박스 `Enqueue`(현장용, 세이브포인트 호출 전제) / `Claim` / `MarkSent` / `MarkError` / `Requeue` |
| 테스트 | `AMES.Data.Tests` | 규칙은 DB 없이, 리포지토리는 `AMES_TEST_CONN` 있을 때만 |

접속 팩토리 골격:

```csharp
public sealed class EosIfConnectionFactory
{
    private readonly string? _cs =
        Environment.GetEnvironmentVariable("ConnectionStrings__EOS_IF");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_cs);

    public SqlConnection Open()
    {
        if (!IsConfigured) throw new InvalidOperationException("EOS_IF 접속문자열이 설정되지 않았습니다.");
        var c = new SqlConnection(_cs);
        c.Open();
        return c;
    }
}
```

### 5단계 — 수신 Worker `SAPIN`

`AMES.Api/Workers/SapIn/SapInWorker.cs`, `ScheduledWorker<TTarget,TResult>` 상속(`Code="SAPIN"`). 대상 키 = IF ID(M01~M06, T04, T08). 주기·사용 여부는 공통코드 그룹 `SW_SAPIN`(INTERVAL 5, BATCH 100)로 관리(그룹명 ≤ 20자 규칙 준수).

처리 순서와 규칙:

1. 대상은 UOM → Item → BOM → 고객 → 공급사 → 로케이션 → PO → MRP 순(선행 테이블에 `E` 행이 있으면 후행은 다음 주기).
2. EOS_IF 에서 `IF_STATUS='N'` 을 `IF_SEQ` 순으로 `BATCH` 건씩 읽는다(짧은 쿼리, `READPAST`).
3. AMES 로컬 트랜잭션으로 `MD_*` MERGE(헤더+라인은 한 트랜잭션). **원격 쿼리와 같은 트랜잭션으로 묶지 않는다.**
4. 성공 시 EOS_IF 에 `Y`+`PROC_TS`, 실패 시 `E`+`IF_MSG`(≤1000자). 되쓰기가 실패해도 MERGE 가 멱등이라 다음 주기에 안전하게 재처리.
5. 항상 `SAP_IfLog` 한 줄(RunID 묶음) + `SYS_InterfaceMonitor` `SAPIN-{IF}` 요약 갱신.
6. `IF_ACTION='D'` 는 삭제가 아니라 `ActiveFlag=0`(고객은 `Status`). BOM 은 헤더 버전의 기존 `MD_Bom` 중 IF 라인에 없는 것을 `ActiveFlag=0`.
7. 반영 값: `SAPFlag=1`, 신규 `CreatedBy='SAP-IF'`, 수정 `ModifiedBy='SAP-IF'`/`ModifiedTS=SYSDATETIME()`. IF 에 없는 MES 전용 컬럼은 건드리지 않는다.
8. AMES 는 코드 변환을 하지 않는다(웅진이 변환 완료) — 단, 길이 초과·NULL 위반은 AMES 가 검사해 `E`.

Worker 골격:

```csharp
public sealed class SapInWorker : ScheduledWorker<string, SapInResult>
{
    public override string Code => "SAPIN";
    // LoadPlan: 공통코드 SW_SAPIN 에서 INTERVAL/BATCH 읽기 (캐시 없음)
    // KeyOf / IntervalMinOf / Skipped / IsOk / RecordConfigError: 베이스 계약 구현
    protected override async Task<SapInResult> RunTargetAsync(string ifId, CancellationToken ct)
    {
        if (!_eos.IsConfigured) return SapInResult.NotConfigured(ifId);
        var runId = Guid.NewGuid();
        // 1) 선행 IF 에 E 가 남았으면 SKIP(+log)
        // 2) rows = ReadPending(ifId, batch)  // IF_STATUS='N' ORDER BY IF_SEQ
        // 3) foreach row: try { await _apply[ifId].ApplyAsync(row); await MarkAsync(row.Seq,'Y',null); }
        //                 catch (Exception ex) { await MarkAsync(row.Seq,'E',Trunc(ex.Message,1000)); log FAIL }
        // 4) monitor upsert (SYS_InterfaceMonitor, INBOUND, SAPIN-{ifId})
    }
}
```

UOM 반영 SQL 예(IF-M01 → `MD_Uom`, 키 `UOMCode`):

```sql
-- @UOMCode,@UOMName,@UOMCategory,@BaseFlag,@BaseUOM,@ConvFactor,@ActiveFlag 는 EOS_IF 행에서 바인딩
MERGE dbo.MD_Uom WITH (HOLDLOCK) AS t
USING (SELECT @UOMCode AS UOMCode) AS s ON t.UOMCode = s.UOMCode
WHEN MATCHED THEN UPDATE SET
     UOMName     = LEFT(@UOMName, 30),     -- MES nvarchar(30), 초과는 절단 + WARN 로그
     UOMCategory = @UOMCategory, BaseFlag = @BaseFlag, BaseUOM = @BaseUOM,
     ConvFactor  = @ConvFactor,  ActiveFlag = @ActiveFlag,
     SAPFlag = 1, ModifiedBy = 'SAP-IF', ModifiedTS = SYSDATETIME()
     -- DecimalPrec·Symbol 은 MES 전용 → 건드리지 않음
WHEN NOT MATCHED THEN INSERT
     (UOMCode, UOMName, UOMCategory, BaseFlag, BaseUOM, ConvFactor, ActiveFlag, SAPFlag, CreatedBy)
     VALUES (@UOMCode, LEFT(@UOMName,30), @UOMCategory, @BaseFlag, @BaseUOM, @ConvFactor, @ActiveFlag, 1, 'SAP-IF');
```

EOS_IF 되쓰기:

```sql
UPDATE dbo.IF_MD_UOM
   SET IF_STATUS = @Status,            -- 'Y' / 'E'
       IF_MSG    = @Msg,               -- 'Y' 이면 NULL, 'E' 이면 ≤1000자
       PROC_TS   = SYSDATETIME()
 WHERE IF_SEQ = @IfSeq AND IF_STATUS = 'N';   -- 이미 처리된 행은 건드리지 않음
```

구현 순서(위험 낮은 것부터): **M01 UOM → M06 로케이션 → M04/M05 거래처 → M02 품목 → M03 BOM → T04 PO → T08 MRP.** 각 IF 는 먼저 **DryRun**(읽기 + 로컬 MERGE 롤백 + EOS_IF 되쓰기 없음)으로 검증하고, 되쓰기는 웅진과 시점을 맞춰 UOM 1건부터 실제 `Y` 처리한다.

선행 정비(정의서가 AMES 몫으로 명시): 공통코드 `WH_AREA`(WIP·Defect 추가), `LOCATION_TYPE` 정비, `VENDOR_TYPE`(LOCAL·CKD·SUPPLIER 정리) — IF-M06 FAIL 해소를 위해 M06 전에 확정한다.

### 6단계 — 송신 Worker `SAPOUT` + 아웃박스

현장 코드는 **아웃박스 INSERT 한 줄만**. 같은 DB·같은 트랜잭션 안의 **세이브포인트** 로 감싸 실패해도 확정은 커밋한다(BOM 자동 차감 블록과 같은 방식).

```csharp
// 예: 실적 확정 트랜잭션(InjLotRepository.ConfirmByLotCode 등) 끝부분
tx.Save("sap_outbox");
try
{
    SapIfOutboxRepository.Enqueue(conn, tx,
        ifId: "IF-T03", mesKey: resultNo, action: "C",
        payload: SapPayload.ProdReceipt(result, lot));   // 이벤트 시점 값을 JSON 으로 고정
}
catch (Exception ex)
{
    tx.Rollback("sap_outbox");              // 확정은 계속 커밋
    PopLog.Warn("SAP outbox enqueue failed", ex);
}
```

`SAPOUT` Worker(`Code="SAPOUT"`, 공통코드 `SW_SAPOUT`):

```sql
-- Claim: 처리할 행을 잡고 P 로 (동시 실행 방지)
;WITH q AS (
    SELECT TOP (@Batch) * FROM dbo.SAP_IfOutbox WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE Status = 'N' AND (NextTryAt IS NULL OR NextTryAt <= SYSDATETIME())
    ORDER BY OutboxID)
UPDATE q SET Status = 'P', ModifiedBy = 'SAPOUT', ModifiedTS = SYSDATETIME()
OUTPUT inserted.*;
```

전송(EOS_IF) — 헤더 1행 + 라인 N행을 **EOS_IF 트랜잭션 하나**로 INSERT(AMES 트랜잭션과 분리):

```sql
-- 예: IF-T03 생산실적 (업무 컬럼은 정의서 IF-T03 시트 기준)
INSERT dbo.IF_TX_PRODRECEIPT (IF_ACTION, MES_TS, MES_RESULT_NO /* …정의서 컬럼… */)
SELECT @Action, @EventAt, @MesKey /* … */
WHERE NOT EXISTS (SELECT 1 FROM dbo.IF_TX_PRODRECEIPT WHERE MES_RESULT_NO = @MesKey AND IF_ACTION = @Action);
-- 이미 있으면 SKIP(멱등) → 기존 IF_SEQ 를 조회해 아웃박스에 기록
```

- 성공: 아웃박스 `Status='Y'`, `EosIfSeq`, `SentAt`. 실패: `Status='N'`, `RetryCount+1`, `NextTryAt` = 지수 백오프(최대 30분), 상한(예: 8회) 초과 시 `E`.
- **웅진 결과 회수**: 주기마다 `Y` 이지만 `B1Status<>'Y'` 인 행의 EOS_IF `IF_STATUS/B1_DOCENTRY/B1_DOCNUM/IF_MSG` 를 읽어 `B1Status/B1DocEntry/B1DocNum/B1Msg` 에 복사. 웅진이 `E` 를 되쓰면 화면(후속)에서 조치 후 재처리 → EOS_IF `IF_STATUS` 를 `N` 으로 되돌리거나(헤더 UPDATE 권한 있음) 새 키로 재적재.
- 순서 보장: T01 생성(C) → T02 → T03 → T01 종료(L). 같은 WO 의 후행 행은 선행 행이 `B1Status='Y'` 일 때만 전송(그렇지 않으면 `NextTryAt` 연기).

이벤트 연결 대상(위험도 순, **가장 나중에**):

| IF | 연결 지점 | 비고 |
|---|---|---|
| T01 생산오더 | PP-003·PP-APS WO 생성, PP-004 릴리스·취소·강제 마감 | 확정·변경·종료·취소 시 |
| T03 생산실적 | `InjLotRepository`·`ImgLotRepository`·`PntLotRepository` `ConfirmByLotCode`, 재작업 수리 | 불량은 Reject 라인 — 역분개와의 관계 정의 필요 |
| T02 자재투입 | BOM 자동 차감(`BackflushRepository.Apply`) 거래 | MES 에 투입 스캔이 없어 **차감 거래 → 투입 실적 변환** 여부 결정 필요 |
| T05 PO 입고 | PDA 입고 스캔(WH) | PO OpenQty 초과 → E |
| T06 재고이동 | B1 창고(1차 로케이션)가 바뀌는 이동만 | 2차 위치 간 이동 제외 |
| T07 출하 | FG 출하 확정(트럭 출발) | 수주는 MES 관리, B1 Delivery 단독 |
| T09 재고조정 | 승인 완료 Adjust | MD-026 재고 조정 화면은 현재 숨김 상태 |

### 7단계 — 모니터링·알림·운영

- `SYS_InterfaceMonitor` 행: `SAPIN-M01`~`M06`·`SAPIN-T04`·`SAPIN-T08`(INBOUND), `SAPOUT-T01`~`T09`(OUTBOUND). 상태는 OK/ERROR, 건수·메시지·재시도 횟수 기록.
- 상세 이력: `SAP_IfLog`(RunID 로 묶어 조회).
- 정의서 2.3 알림 요구("`E` 발생 또는 `N` 행 30분 이상 체류"): 확인사항 파일은 `SAP_SYNC_FAIL` PUSH 를 구현하지 않는다고 되어 있어 **결정 필요**. 결정 전에는 모니터 ERROR 기록까지만.
- 화면(SYS-006 확장: 로그 조회·`E` 행 재처리 버튼)은 확인사항 "추후" 항목에 따라 후속.
- 월 마감 체크리스트에 "미처리 `E` 0건 확인" 추가(정의서 2.4 전기일자).

### 8단계 — IIS·배포·테스트

1. 마이그레이션(`migrate_sap_if_log.sql`) → 정리 작업 등록 → 신 `AMES.Api` 게시 순서(구 바이너리는 새 테이블을 쓰지 않으므로 안전).
2. `AMES.Api` 앱풀 환경변수:
   ```powershell
   # 서버에서 관리자 PowerShell. 비밀번호가 든 명령을 셸 기록에 남기지 않도록 프롬프트로 입력
   $cs = Read-Host "EOS_IF 접속문자열"
   & "$env:windir\System32\inetsrv\appcmd.exe" set config -section:system.applicationHost/applicationPools `
     "/+[name='AMES.Api'].environmentVariables.[name='ConnectionStrings__EOS_IF',value='$cs']" /commit:apphost
   ```
3. 테스트
   - `AMES.Data.Tests`: `SapIfRules`(길이·순서·백오프), MERGE 매핑(DB 없이 DTO 단위), 아웃박스 멱등(`AMES_TEST_CONN`).
   - EOS_IF 에 쓰는 테스트는 **운영 DB 라서 자동 테스트에 넣지 않는다.** 수동 시나리오(UOM 1건 `Y`, 시험 WO 1건 T01)로만 웅진과 합동 진행.
   - 정의서 2.5: 통합 테스트 2회 + MES 연동 테스트 1회, 오픈 전 `IF_MD_*`·`IF_TX_*` TRUNCATE + Watermark 초기화 후 Full Load.

---

## 4. 일정(안)

| 시기 | 내용 |
|---|---|
| 11월 3~4주 | 2단계 구조 대조, 3단계 DDL(적용 승인 후), 4단계 공통 기반, M01 UOM 수신 DryRun → 실반영 |
| 12월 1주 | M06·M04·M05 수신, 공통코드 정비(WH_AREA·LOCATION_TYPE·VENDOR_TYPE) |
| 12월 2주 | M02 품목·M03 BOM 수신, T04 PO 수신 |
| 12월 3주 | 아웃박스·SAPOUT, T01·T03 송신 |
| 12월 4주 | T05~T07·T09, T08 MRP, 통합 테스트 |
| 2027-01 | B1 오픈(정의서 기준) |

---

## 5. 결정이 필요한 항목

1. **T02 자재투입**: BOM 자동 차감 거래를 투입 실적으로 변환할지, 별도 투입 스캔을 만들지.
2. **수신 알림**: `E`/`N` 30분 체류 알림을 이번 범위에 넣을지(PUSH), 모니터 기록만 할지.
3. **T03 불량 처리**: 불량 등록 역분개(−1)와 B1 Reject 입고의 대응 방식.
4. **TrustServerCertificate**: EOS IT 와 인증서 체인 정비 여부.
5. **EOS_IF 비밀번호 교체**(대화에 노출됨) 시점.
6. **개발 중 쓰기 테스트 범위**: 운영 EOS_IF 에 `Y` 되쓰기·시험 INSERT 를 어느 시점에 웅진과 맞춰 할지.
7. 정의서 쪽 미결(별도 `OpenIssues_V1.2.xlsx`): B1 BP Group 목록, B1 창고 코드, `U_ItemClass` UDF, MRP↔PO 권고 추적, 사유별 G/L 계정.

## 5-1. 구현 현황 (11-13)

| 항목 | 상태 |
|---|---|
| 2단계 구조 대조 | 완료(읽기 전용). EOS_IF 실제 DDL 확인 — 아래 "대조 결과" |
| 3단계 DDL | `dist/migrate_sap_if_log.sql` **개발 DB(`172.29.3.21\MES`/`AMES`) 적용 완료(11-13)**. `dist/migrate_sap_if_po_mrp.sql` 은 미적용(PO·MRP 수신 코드가 생길 때). `AMES_Schema.sql`·`rebuild_db.sh` 반영은 아직 |
| 4단계 공통 기반 | 완료 — `EosIfConnectionFactory`, `SapIfRules`, `SapIfLogRepository` |
| 5단계 수신 Worker | **M01 단위 · M02 품목 · M03 BOM · M04 고객 · M05 공급사 · M06 로케이션** 구현(`SapInRunner`, `SapInBom`, `SapInWorker`). DryRun 기본 ON. 품목은 사출품 여부(InjFlag)를 SUB 일 때만 유지하고 기본 단위가 바뀌면 BOM 라인 단위를 동기화, BOM 은 헤더+라인 한 트랜잭션·IF 에 없는 라인 비활성 |
| 5단계 나머지 | 미구현: T04 PO, T08 MRP |
| 6단계 송신 | 미구현: 아웃박스 Enqueue·`SAPOUT` Worker·현장 연결 |
| 7단계 | 모니터(`SYS_InterfaceMonitor` `SAPIN-IF-Mxx`)는 수신 Worker 에 포함. 알림·화면은 후속 |
| 테스트 | `SapIfRulesTests` 17건 통과. Api 빌드 오류 0. MERGE 실반영은 DB 적용 뒤 검증 필요 |

수신 Worker 동작: 환경변수 `ConnectionStrings__EOS_IF` 가 없으면 조용히 멈춘다. 있으면 공통코드 `SW_SAPIN` 의 `INTERVAL`(기본 5분)·`BATCH`(100)·`DRYRUN`(**기본 1**)을 읽는다. DryRun 이면 MD_* 반영을 롤백하고 EOS_IF 에 `Y`/`E` 를 되쓰지 않는다. 실반영은 웅진과 시점을 맞춘 뒤 `SW_SAPIN.DRYRUN` 의 Attribute1 을 `0` 으로 바꾼다.

### 비교 정책 (11-13 사용자 결정)

SAP(EOS_IF)와 AMES 마스터를 키로 비교했을 때의 처리. 수신 Worker 가 행마다 AMES 기존 값을 읽어 아래대로 처리하고 `SAP_IfLog` 에 남긴다.

| 경우 | 처리 | 로그(SAP_IfLog.Message) |
|---|---|---|
| ① 양쪽에 있고 값이 다름 | **SAP 값으로 덮어쓴다(SAP 우선).** SAP 값이 NULL 이면 NULL 로 덮어쓴다(`ActiveFlag` 만 NULL → 1) | `변경(SAP 우선) 컬럼: 기존 → 새값; …` |
| ② SAP 에만 있음 | **SAP 값으로 새로 등록한다.** | `신규 등록(SAP 값)` |
| ③ AMES 에만 있음 | **우선 그대로 둔다**(삭제·비활성 없음, 현재 정책). 정책을 바꾸기 전까지 목록만 기록 | Key `AMES-ONLY`, Result `SKIP`, `AMES 에만 있음(그대로 둠) n건: …`(최대 50건) |
| 양쪽 동일 | 반영하되 변경 없음(`SAPFlag` 만 1 로) | `동일(변경 없음)` |

- 기존 행이 MES 관리(`SAPFlag=0`)였다면 로그에 `※ MES 관리 행 → SAP 행으로 전환` 이 붙는다.
- 실행 요약은 모니터(`SYS_InterfaceMonitor`)에 `신규 a·변경 b·동일 c·AMES 단독(유지) d` 로 남는다.
- DryRun 에서는 반영을 롤백하므로 위 로그만 쌓인다 → **실반영 전에 이 로그로 덮어쓰기 대상을 확인한다.**
- `IF_ACTION='D'` 는 삭제가 아니라 비활성(`ActiveFlag=0`, 고객은 `Status='CLOSED'`)이며 로그에 `비활성 처리` 로 남는다.

### 대조 결과 (2단계, 11-13 EOS_IF 실제 구조)

- 첫 점검(11-13 오전)과 달리 `IF_MD_UOM` 은 정의서와 일치한다(`IF_MSG` 1000, `UOMName` 30, `ConvFactor` 18,8) — 웅진이 그사이 수정.
- `IF_MD_ITEM`·`IF_MD_BOM_*`·`IF_MD_CUSTOMER`·`IF_MD_VENDOR`·`IF_MD_LOCATION` 의 업무 컬럼명·타입은 MES `MD_*` 와 같다(품목은 `ItemNameEN`·`InjFlag`·`ProcureType` 등 포함).
- EOS_IF 의 UNIQUE 는 정의서(MES 키 + IF_ACTION)와 다르다: `UQ_IF_TX_PRODRECEIPT`(MES_RESULT_NO), `UQ_IF_TX_PRODISSUE`(MES_ISSUE_NO), `UQ_IF_TX_GRPO_HDR`(MES_GR_NO), `UQ_IF_TX_TRANSFER_HDR`(MES_TR_NO), `UQ_IF_TX_DELIVERY_HDR`(MES_SHIP_NO), `UQ_IF_TX_ADJUST`(MES_ADJ_NO)는 **MES 키 단독**이고, `UQ_IF_TX_PRODORDER` 만 (MES_WO_NO, IF_ACTION, CREATED_TS)다. → 송신 시 같은 MES 키로 후속 액션을 넣을 수 없는 테이블이 있다(실적·투입·입고·이동·출하·조정은 키당 1행). T01 은 액션별 행이 가능.
- `IF_TX_PRODRECEIPT` 는 정의서와 달리 양품/불량이 한 행(`CompleteQty`·`RejectQty`)이고, `IF_TX_PRODISSUE`·`GRPO`·`TRANSFER`·`DELIVERY` 는 헤더+라인이다.
- 웅진 `IF_CODE_MAP` 에는 `WH_AREA`(RM→MAT_AREA, FG→FG_AREA, WIP→WIP_AREA, DEFECT→DEFECT_AREA)·`LOC_TYPE`(RM→STORAGE, FG→FG, WIP→LINE, DEFECT→BLOCKED)·`VENDOR_TYPE`(MATERIAL·SERVICE·MRO·TOOLING) 값이 있어, AMES 공통코드에 `WIP_AREA`·`DEFECT_AREA`·`BLOCKED`·`TOOLING`·`MRO` 가 있어야 M05·M06 반영이 의미 있다(공통코드 정비 필요 — 미확인).

## 6. 하지 않는 것

- EOS_IF 에 DDL(테이블·인덱스·뷰) 생성, 업무 컬럼 UPDATE, DELETE.
- `IF_CONTROL`·`IF_LOG`·`IF_CODE_MAP` 쓰기(웅진 영역).
- 현장 확정 트랜잭션 안에서 EOS_IF 직접 접근.
- 접속 비밀번호의 소스·설정·로그 기록.
