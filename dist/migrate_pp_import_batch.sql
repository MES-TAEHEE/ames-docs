-- ============================================================
-- 구매오더 업로드 이력 PP_ImportBatch — 2026-11-03
-- 업로드 이력 탭이 구매오더(MM31006) 업로드 1건을 보여 주기 위한 기록(신규·갱신·취소 건수).
-- 일별 계획(PP_DemandPlanBatch)·일별 발주(PP_DailyPo.Batch)·주간(PP_Forecast.ForecastBatch)은 기존 기록을 그대로 쓴다.
-- 재실행 안전. 실행: sqlcmd -S <서버> -d <DB> -E -f 65001 -I -b -i dist\migrate_pp_import_batch.sql
-- 순서 무관. 이 테이블이 없으면 구매오더 업로드 뒤 기록이 실패하고 업로드 이력 탭이 Invalid object name 으로 실패한다.
-- 외래키 없음(프로젝트 원칙).
-- ============================================================
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.PP_ImportBatch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_ImportBatch (
        ImportBatchID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PP_ImportBatch PRIMARY KEY,
        Batch         VARCHAR(30)   NOT NULL CONSTRAINT UX_PP_ImportBatch_Batch UNIQUE,
        CustomerID    VARCHAR(20)   NOT NULL,
        FileName      NVARCHAR(200) NULL,
        DateFrom      DATE          NULL,
        DateTo        DATE          NULL,
        [RowCount]    INT           NOT NULL CONSTRAINT DF_PP_ImportBatch_RowCount DEFAULT (0),
        ItemCount     INT           NOT NULL CONSTRAINT DF_PP_ImportBatch_ItemCount DEFAULT (0),
        Inserted      INT           NOT NULL CONSTRAINT DF_PP_ImportBatch_Inserted DEFAULT (0),
        Updated       INT           NOT NULL CONSTRAINT DF_PP_ImportBatch_Updated DEFAULT (0),
        Cancelled     INT           NOT NULL CONSTRAINT DF_PP_ImportBatch_Cancelled DEFAULT (0),
        ImportedAt    DATETIME2(7)  NOT NULL CONSTRAINT DF_PP_ImportBatch_ImportedAt DEFAULT SYSDATETIME(),
        ImportedBy    VARCHAR(20)   NOT NULL,
        CreatedBy     VARCHAR(20)   NOT NULL,
        CreatedTS     DATETIME2(7)  NOT NULL CONSTRAINT DF_PP_ImportBatch_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy    VARCHAR(20)   NULL,
        ModifiedTS    DATETIME2(7)  NULL
    );
END
GO

PRINT 'migrate_pp_import_batch: done';
GO
