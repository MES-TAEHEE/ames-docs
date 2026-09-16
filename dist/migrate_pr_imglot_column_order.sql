-- ════════════════════════════════════════════════════════════════════════
--  migrate_pr_imglot_column_order.sql
--  PR_ImgLot 컬럼 순서를 스키마(AMES_Schema.sql · migrate_img_lot.sql)와 같게 재정렬
--
--  초기 버전 테이블에 CustomerCode 를 ALTER ADD 로 붙인 DB(개발서버)는 그 컬럼이 맨 끝에 있어
--  스키마로 만든 DB(로컬)와 구조 서명이 달랐다. 데이터를 그대로 옮기며 재생성한다(LotID 는 IDENTITY 아님).
--  PK·FK(tbl_Lot)·IX_PR_ImgLot_Status 를 같은 이름으로 다시 만든다. PR_ImgLot 을 참조하는 FK 는 없다.
--  순서 무관, 재실행 안전(CustomerCode 가 7번째가 아닐 때만).
--
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -C -f 65001 -I -b -i dist/migrate_pr_imglot_column_order.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.PR_ImgLot', 'U') IS NOT NULL
   AND COLUMNPROPERTY(OBJECT_ID('dbo.PR_ImgLot'), 'CustomerCode', 'ColumnId') IS NOT NULL
   AND COLUMNPROPERTY(OBJECT_ID('dbo.PR_ImgLot'), 'CustomerCode', 'ColumnId') <> 7
BEGIN
    BEGIN TRANSACTION;

    CREATE TABLE dbo.PR_ImgLot_new (
      [LotID]                     INT                  NOT NULL,
      [EquipID]                   VARCHAR(20)              NULL,
      [ConfirmStatus]             VARCHAR(16)          NOT NULL CONSTRAINT DF_PR_ImgLot_ConfirmStatus_new DEFAULT 'RAW',
      [ConfirmedAt]               DATETIME2                NULL,
      [ConfirmedBy]               NVARCHAR(450)            NULL,
      [ConfirmedSessionID]        INT                      NULL,
      [CustomerCode]              VARCHAR(20)              NULL,
      [FabricRollLotID]           INT                      NULL,
      [FabricConsumedM]           DECIMAL(8,3)             NULL,
      [BondSetupID]               INT                      NULL,
      [PrintedCount]              INT                  NOT NULL CONSTRAINT DF_PR_ImgLot_PrintedCount_new DEFAULT 0,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL CONSTRAINT DF_PR_ImgLot_CreatedTS_new DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_PR_ImgLot_new PRIMARY KEY CLUSTERED ([LotID])
    );

    INSERT INTO dbo.PR_ImgLot_new
        (LotID, EquipID, ConfirmStatus, ConfirmedAt, ConfirmedBy, ConfirmedSessionID, CustomerCode,
         FabricRollLotID, FabricConsumedM, BondSetupID, PrintedCount, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT LotID, EquipID, ConfirmStatus, ConfirmedAt, ConfirmedBy, ConfirmedSessionID, CustomerCode,
           FabricRollLotID, FabricConsumedM, BondSetupID, PrintedCount, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.PR_ImgLot;

    DROP TABLE dbo.PR_ImgLot;
    EXEC sp_rename N'dbo.PR_ImgLot_new', N'PR_ImgLot', N'OBJECT';
    EXEC sp_rename N'dbo.PK_PR_ImgLot_new', N'PK_PR_ImgLot', N'OBJECT';
    EXEC sp_rename N'dbo.DF_PR_ImgLot_ConfirmStatus_new', N'DF_PR_ImgLot_ConfirmStatus', N'OBJECT';
    EXEC sp_rename N'dbo.DF_PR_ImgLot_PrintedCount_new',  N'DF_PR_ImgLot_PrintedCount',  N'OBJECT';
    EXEC sp_rename N'dbo.DF_PR_ImgLot_CreatedTS_new',     N'DF_PR_ImgLot_CreatedTS',     N'OBJECT';
    ALTER TABLE dbo.PR_ImgLot ADD CONSTRAINT FK_PR_ImgLot_Lot FOREIGN KEY ([LotID]) REFERENCES dbo.tbl_Lot([LotID]);
    CREATE INDEX IX_PR_ImgLot_Status ON dbo.PR_ImgLot([ConfirmStatus]);

    COMMIT TRANSACTION;
    PRINT 'PR_ImgLot rebuilt in schema column order';
END
ELSE
    PRINT 'PR_ImgLot column order already matches schema';
GO

SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.PR_ImgLot') ORDER BY c.column_id;
SELECT COUNT(*) AS ImgLots FROM dbo.PR_ImgLot;
GO
