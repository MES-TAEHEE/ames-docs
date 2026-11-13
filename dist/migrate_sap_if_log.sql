-- ════════════════════════════════════════════════════════════════════════
--  migrate_sap_if_log.sql
--  SAP B1 인터페이스(EOS_IF) 로그·아웃박스 + Worker 설정 (11-13)
--
--  1) SAP_IfLog     — 수신/송신 행 단위 이력(RunID 로 한 번의 실행을 묶는다)
--  2) SAP_IfOutbox  — MES → B1 송신 큐. 현장 트랜잭션은 여기에 INSERT 만 하고 커밋하고,
--                     SAPOUT Worker 가 EOS_IF.IF_TX_* 로 옮긴다(현장 경로에서 EOS_IF 에 접근하지 않는다).
--  3) usp_Cleanup_SAP_If — 보존 정리(SAP_IfLog OK·SKIP 와 완료된 아웃박스 90일). E·FAIL 은 지우지 않는다.
--  4) 공통코드 그룹 SW_SAPIN · SW_SAPOUT + 전역 기본값(INTERVAL 5분)
--
--  규칙: 외래키 없음 · 한글은 nvarchar · 행위자 컬럼은 varchar(20) · 감사 컬럼은 맨 뒤 CreatedBy→CreatedTS→ModifiedBy→ModifiedTS.
--  재실행 안전, 순서 무관(공통코드 MERGE 는 MD_CodeGroup·MD_CodeItem 이 있어야 한다).
--  적용:  sqlcmd -S <서버> -d <DB> -f 65001 -I -b -i dist/migrate_sap_if_log.sql
--  ※ 개발·운영 DB 적용은 별도 승인 후.
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1. SAP_IfLog ─────────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.SAP_IfLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SAP_IfLog (
        LogID        bigint IDENTITY(1,1) NOT NULL,
        RunID        uniqueidentifier     NOT NULL,                       -- Worker 1회 실행 묶음
        IfID         varchar(10)          NOT NULL,                       -- IF-M01 … IF-T09
        Direction    varchar(3)           NOT NULL,                       -- IN / OUT
        KeyValue     varchar(60)          NULL,                           -- 품번·WO 번호 등 업무 키
        IfSeq        bigint               NULL,                           -- EOS_IF.IF_SEQ
        Action       varchar(2)           NULL,                           -- I/U/D · C/U/L/X
        Result       varchar(5)           NOT NULL,                       -- OK / WARN / FAIL / SKIP
        Message      nvarchar(1000)       NULL,
        RowsAffected int                  NULL,
        DurationMs   int                  NULL,
        LoggedAt     datetime2(0)         NOT NULL CONSTRAINT DF_SAP_IfLog_LoggedAt DEFAULT SYSDATETIME(),
        CreatedBy    varchar(20)          NULL,
        CreatedTS    datetime2(7)         NULL CONSTRAINT DF_SAP_IfLog_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy   varchar(20)          NULL,
        ModifiedTS   datetime2(7)         NULL,
        CONSTRAINT PK_SAP_IfLog PRIMARY KEY CLUSTERED (LogID),
        CONSTRAINT CK_SAP_IfLog_Result CHECK (Result IN ('OK','WARN','FAIL','SKIP')),
        CONSTRAINT CK_SAP_IfLog_Dir    CHECK (Direction IN ('IN','OUT'))
    );
    PRINT 'SAP_IfLog created';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_IfLog_If_Time' AND object_id = OBJECT_ID(N'dbo.SAP_IfLog'))
    CREATE INDEX IX_SAP_IfLog_If_Time ON dbo.SAP_IfLog (IfID, LoggedAt DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_IfLog_Key' AND object_id = OBJECT_ID(N'dbo.SAP_IfLog'))
    CREATE INDEX IX_SAP_IfLog_Key ON dbo.SAP_IfLog (KeyValue) INCLUDE (IfID, Result, LoggedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_IfLog_Run' AND object_id = OBJECT_ID(N'dbo.SAP_IfLog'))
    CREATE INDEX IX_SAP_IfLog_Run ON dbo.SAP_IfLog (RunID);
GO

-- ── 2. SAP_IfOutbox ──────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.SAP_IfOutbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SAP_IfOutbox (
        OutboxID     bigint IDENTITY(1,1) NOT NULL,
        IfID         varchar(10)          NOT NULL,                       -- IF-T01 … IF-T09
        MesKey       varchar(60)          NOT NULL,                       -- MES_WO_NO / MES_RESULT_NO …
        Action       varchar(2)           NOT NULL,                       -- C/U/L/X
        PayloadJson  nvarchar(max)        NOT NULL,                       -- 헤더+라인 스냅샷(이벤트 시점 값 고정)
        EventAt      datetime2(0)         NOT NULL CONSTRAINT DF_SAP_IfOutbox_EventAt DEFAULT SYSDATETIME(),   -- MES_TS
        Status       char(1)              NOT NULL CONSTRAINT DF_SAP_IfOutbox_Status  DEFAULT 'N',             -- N 대기 / P 전송중 / Y 전달완료 / E 오류
        RetryCount   int                  NOT NULL CONSTRAINT DF_SAP_IfOutbox_Retry   DEFAULT 0,
        NextTryAt    datetime2(0)         NULL,
        LastError    nvarchar(1000)       NULL,
        EosIfSeq     bigint               NULL,                           -- 넣은 EOS_IF.IF_SEQ (헤더)
        B1Status     char(1)              NULL,                           -- 웅진이 되쓴 IF_STATUS (N/P/Y/E)
        B1DocEntry   int                  NULL,
        B1DocNum     int                  NULL,
        B1Msg        nvarchar(1000)       NULL,
        SentAt       datetime2(0)         NULL,
        CreatedBy    varchar(20)          NULL,
        CreatedTS    datetime2(7)         NULL CONSTRAINT DF_SAP_IfOutbox_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy   varchar(20)          NULL,
        ModifiedTS   datetime2(7)         NULL,
        CONSTRAINT PK_SAP_IfOutbox PRIMARY KEY CLUSTERED (OutboxID),
        CONSTRAINT CK_SAP_IfOutbox_Status CHECK (Status IN ('N','P','Y','E')),
        CONSTRAINT UQ_SAP_IfOutbox_Key UNIQUE (IfID, MesKey, Action)      -- 멱등성: 같은 이벤트는 한 번만
    );
    PRINT 'SAP_IfOutbox created';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SAP_IfOutbox_Queue' AND object_id = OBJECT_ID(N'dbo.SAP_IfOutbox'))
    CREATE INDEX IX_SAP_IfOutbox_Queue ON dbo.SAP_IfOutbox (Status, NextTryAt, OutboxID);
GO

-- ── 3. 보존 정리 ─────────────────────────────────────────────────────
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

-- ── 4. 공통코드: Worker 설정 그룹 ────────────────────────────────────
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('SW_SAPIN',  N'SAP 수신 설정', N'SAP Inbound Settings',
     N'INTERVAL: Attribute1=수신 주기(분, 0=전체 중지) / BATCH: Attribute1=한 번에 처리할 최대 행 수 / 선택 TICK_SEC·STARTUP_DELAY_SEC: Attribute1=초. EOS_IF 접속문자열은 Api 환경변수 ConnectionStrings__EOS_IF'),
    ('SW_SAPOUT', N'SAP 송신 설정', N'SAP Outbound Settings',
     N'INTERVAL: Attribute1=송신 주기(분, 0=전체 중지) / BATCH: Attribute1=한 번에 처리할 최대 건수 / MAX_RETRY: Attribute1=재시도 상한')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'migrate', SYSDATETIME());
PRINT 'SW_SAPIN / SW_SAPOUT code groups merged';
GO

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('SW_SAPIN_INTERVAL',  'SW_SAPIN',  'INTERVAL',  N'수신 주기(분)',     N'Inbound interval (min)', N'5',   1),
    ('SW_SAPIN_BATCH',     'SW_SAPIN',  'BATCH',     N'수신 배치 크기',    N'Inbound batch size',     N'100', 2),
    -- 1 이면 DryRun: MD_* 반영은 롤백하고 EOS_IF 에 Y/E 를 되쓰지 않는다(운영 EOS_IF 검증용). 웅진과 시점을 맞춘 뒤 0 으로 바꾼다.
    ('SW_SAPIN_DRYRUN',    'SW_SAPIN',  'DRYRUN',    N'수신 DryRun(1=반영 안 함)', N'Inbound dry run (1=no write)', N'1', 3),
    ('SW_SAPOUT_INTERVAL', 'SW_SAPOUT', 'INTERVAL',  N'송신 주기(분)',     N'Outbound interval (min)', N'1',  1),
    ('SW_SAPOUT_BATCH',    'SW_SAPOUT', 'BATCH',     N'송신 배치 크기',    N'Outbound batch size',    N'50',  2),
    ('SW_SAPOUT_MAX_RETRY','SW_SAPOUT', 'MAX_RETRY', N'송신 재시도 상한',  N'Outbound max retry',     N'8',   3)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'SW_SAPIN / SW_SAPOUT defaults merged';
GO
