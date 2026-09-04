-- ════════════════════════════════════════════════════════════════════════
--  migrate_img_lot.sql
--  PR_ImgLot — IMG(래핑) 원천 LOT (1 LOT = 1 EA)
--
--  IMG-MAIN 은 INJ 와 같은 "라벨 발행 → 스캔 확정" 모델로 실적을 낸다.
--  라벨 발행 버튼이 tbl_Lot(ProcessCode='IMG', BatchSize 1) + PR_ImgLot(RAW) 를
--  만들고, 라벨 스캔이 ConfirmStatus 를 CONFIRMED 로 바꾸며 PR_ProductionResult
--  1 EA 를 남긴다. 확정 시 차감한 원단 롤·길이와 적용된 본딩 설정을 LOT 에 직접
--  기록해 개별 추적이 되게 한다.
--
--  PR_InjLot 을 쓰지 않는 이유: 금형·캐비티·샷카운트·로봇 NG·발행 클레임은
--  IMG 에 없고, 원단·본딩은 INJ 에 없다.
--
--  전제: migrate_lotno_rule.sql (LotNo 채번), migrate_inj_lot_line_created.sql
--        (IX_tbl_Lot_Line_Created — 오늘 LOT 조회) 적용 후. 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_img_lot.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.PR_ImgLot', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PR_ImgLot (
      [LotID]                     INT                  NOT NULL,  -- PK & FK -> tbl_Lot.LotID (1:1)
      [EquipID]                   VARCHAR(20)              NULL,  -- FK -> MD_Equipment.EquipID (라인 대표 설비)
      [ConfirmStatus]             VARCHAR(16)          NOT NULL DEFAULT 'RAW',  -- RAW / CONFIRMED
      [ConfirmedAt]               DATETIME2                NULL,
      [ConfirmedBy]               NVARCHAR(450)            NULL,
      [ConfirmedSessionID]        INT                      NULL,
      [CustomerCode]              VARCHAR(20)              NULL,  -- 발행 시점 열린 WO 의 수주처 MD_Customer.CustomerCode (라벨 V 토큰)
      [FabricRollLotID]           INT                      NULL,  -- FK -> tbl_Lot.LotID (확정 시 차감한 롤)
      [FabricConsumedM]           DECIMAL(8,3)             NULL,
      [BondSetupID]               INT                      NULL,  -- FK -> PR_BondSetup.BondSetupID
      [PrintedCount]              INT                  NOT NULL DEFAULT 0,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_PR_ImgLot PRIMARY KEY CLUSTERED ([LotID]),
      CONSTRAINT FK_PR_ImgLot_Lot FOREIGN KEY ([LotID]) REFERENCES dbo.tbl_Lot([LotID])
    );
    CREATE INDEX IX_PR_ImgLot_Status ON dbo.PR_ImgLot([ConfirmStatus]);
    PRINT 'PR_ImgLot created';
END
ELSE
    PRINT 'PR_ImgLot already exists';
GO

-- 초기 버전에는 없던 컬럼 — 먼저 만든 DB 에 재실행하면 여기서 붙는다.
IF COL_LENGTH('dbo.PR_ImgLot', 'CustomerCode') IS NULL
BEGIN
    ALTER TABLE dbo.PR_ImgLot ADD [CustomerCode] VARCHAR(20) NULL;
    PRINT 'PR_ImgLot.CustomerCode added';
END
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c
JOIN   sys.types   t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.PR_ImgLot')
ORDER  BY c.column_id;
GO
