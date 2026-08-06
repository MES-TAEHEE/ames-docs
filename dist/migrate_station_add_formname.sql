-- ╔══════════════════════════════════════════════════════════════════════╗
-- ║  Migration: MD_Station +FormName VARCHAR(50) (OrderSeq 앞 위치)         ║
-- ║  Pop 화면 구분값. SQL Server는 위치 삽입 불가 → 테이블 재구성으로 처리.  ║
-- ║  (FK/비PK 인덱스 없음, 트랜잭션 보호. FormName 은 NULL 로 이관)          ║
-- ╚══════════════════════════════════════════════════════════════════════╝
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MD_Station', 'FormName') IS NULL
BEGIN
    BEGIN TRANSACTION;
    CREATE TABLE dbo.MD_Station_new (
      [StationCode]   VARCHAR(20)   NOT NULL,
      [StationName]   NVARCHAR(60)      NULL,
      [StationNameEn] NVARCHAR(60)      NULL,
      [LineID]        VARCHAR(20)   NOT NULL,  -- FK -> MD_Line.LineID
      [FormName]      VARCHAR(50)       NULL,  -- Pop 화면 구분
      [OrderSeq]      INT           NOT NULL,
      [Status]        VARCHAR(10)       NULL,
      [CreatedBy]     VARCHAR(50)   NOT NULL,
      [CreatedTS]     DATETIME2         NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]    NVARCHAR(450)     NULL,
      [ModifiedTS]    DATETIME2         NULL,
      CONSTRAINT PK_MD_Station_new PRIMARY KEY CLUSTERED ([StationCode])
    );
    INSERT INTO dbo.MD_Station_new (StationCode, StationName, StationNameEn, LineID, OrderSeq, Status, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT StationCode, StationName, StationNameEn, LineID, OrderSeq, Status, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.MD_Station;
    DROP TABLE dbo.MD_Station;
    EXEC sp_rename 'dbo.MD_Station_new', 'MD_Station';
    EXEC sp_rename 'dbo.PK_MD_Station_new', 'PK_MD_Station';
    COMMIT;
END
GO

PRINT 'MD_Station: FormName column added before OrderSeq.';
GO
