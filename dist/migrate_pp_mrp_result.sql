-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_mrp_result.sql
--  PP-005 MRP 결과 스냅샷 + 품목 조달 리드타임
--
--  PP_MRPLog 는 실행 헤더만 남겼다. PP-005 화면이 "최근 실행 결과" 를 보여주고 부족 행에서
--  PR 을 만들어 연결하려면 실행마다 자재별 결과(소요·재고·발주중·부족·발주 기한·영향 WO)를
--  저장해야 한다. 발주 기한 = 영향 WO 최단 납기 − MD_Item.LeadTimeDays.
--
--  컬럼·테이블 추가뿐 — 적용 순서 무관, 재실행 안전(멱등). FK 없음(스키마 관례).
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -b -i dist/migrate_pp_mrp_result.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.MD_Item', 'LeadTimeDays') IS NULL
    ALTER TABLE dbo.MD_Item ADD LeadTimeDays INT NULL;  -- 조달 리드타임(일). NULL 이면 MRP 발주 기한 미산정
GO

IF OBJECT_ID(N'dbo.PP_MRPResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_MRPResult (
      [MrpRunID]     INT            NOT NULL,  -- FK -> PP_MRPLog.MrpRunID
      [ItemNo]       VARCHAR(20)    NOT NULL,  -- FK -> MD_Item.ItemNo (BOM leaf 자재)
      [RequiredQty]  DECIMAL(14,3)  NOT NULL,  -- 열린 WO 잔량 × BOM 분해 소요
      [StockQty]     DECIMAL(14,3)  NOT NULL,  -- WH_Inventory OnHand − Reserved 합
      [OnOrderQty]   DECIMAL(14,3)  NOT NULL,  -- PO 미입고 + PO 미전환 PR 수량 (PR 생성 시 가산)
      [ShortageQty]  DECIMAL(14,3)  NOT NULL,  -- Required − Stock − OnOrder (양수 = 부족)
      [LeadTimeDays] INT                NULL,  -- 실행 시점 MD_Item.LeadTimeDays 스냅샷
      [OrderDue]     DATE               NULL,  -- 영향 WO 최단 납기 − LeadTimeDays (부족일 때만)
      [PrID]         INT                NULL,  -- 이 행에서 만든 PP_PurchaseRequest.PrID
      [CreatedBy]    VARCHAR(50)    NOT NULL,
      [CreatedTS]    DATETIME2          NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]   NVARCHAR(450)      NULL,
      [ModifiedTS]   DATETIME2          NULL,
      CONSTRAINT PK_PP_MRPResult PRIMARY KEY CLUSTERED ([MrpRunID], [ItemNo])
    );
END
GO

IF OBJECT_ID(N'dbo.PP_MRPResultWo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_MRPResultWo (
      [MrpRunID]     INT            NOT NULL,  -- FK -> PP_MRPLog.MrpRunID
      [ItemNo]       VARCHAR(20)    NOT NULL,  -- FK -> PP_MRPResult.ItemNo
      [WoID]         INT            NOT NULL,  -- FK -> PP_WorkOrder.WoID (이 자재를 소요하는 WO)
      [RequiredQty]  DECIMAL(14,3)  NOT NULL,  -- 그 WO 몫의 소요
      CONSTRAINT PK_PP_MRPResultWo PRIMARY KEY CLUSTERED ([MrpRunID], [ItemNo], [WoID])
    );
END
GO

PRINT 'migrate_pp_mrp_result: done';
GO
