-- ============================================================
-- 일별 발주 PP_DailyPo (SRM MM31007 "발주정보 조회 (납기일자별)") — 2026-11-03
-- 수주(PP_CustomerOrder, MM31006)·일별 구매계획(PP_DemandPlan, MM30011)과 대조하는 세 번째 출처.
-- 같은 수요를 세 출처가 가리키므로 값이 같아야 하며, 화면 「대조」 탭이 차이를 보여 준다.
-- 재실행 안전. 실행: sqlcmd -S <서버> -d AMES_DEV -E -f 65001 -I -b -i dist\migrate_pp_daily_po.sql
-- 순서 무관. 이 테이블이 없으면 PP-001 「일별 발주」 업로드·「대조」 탭이 Invalid object name 으로 실패한다.
-- 주의: 외래키 없음(프로젝트 원칙 — 고객·품번 정합은 리포지토리 책임).
-- ============================================================
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.PP_DailyPo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_DailyPo (
        DailyPoID     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PP_DailyPo PRIMARY KEY,
        CustomerID    VARCHAR(20)   NOT NULL,
        ItemNo        VARCHAR(20)   NOT NULL,
        PlanDate      DATE          NOT NULL,
        OrderQty      DECIMAL(14,3) NOT NULL CONSTRAINT DF_PP_DailyPo_OrderQty DEFAULT (0),
        DeliveredQty  DECIMAL(14,3) NOT NULL CONSTRAINT DF_PP_DailyPo_DeliveredQty DEFAULT (0),
        PackQty       DECIMAL(14,3) NULL,
        PartName      NVARCHAR(100) NULL,
        Unit          VARCHAR(10)   NULL,
        Batch         VARCHAR(20)   NOT NULL,
        FileName      NVARCHAR(200) NULL,
        ImportedAt    DATETIME2(7)  NOT NULL CONSTRAINT DF_PP_DailyPo_ImportedAt DEFAULT SYSDATETIME(),
        CreatedBy     VARCHAR(20)   NOT NULL,
        CreatedTS     DATETIME2(7)  NOT NULL CONSTRAINT DF_PP_DailyPo_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy    VARCHAR(20)   NULL,
        ModifiedTS    DATETIME2(7)  NULL,
        CONSTRAINT UX_PP_DailyPo UNIQUE (CustomerID, ItemNo, PlanDate)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'SRM MM31007 일별 발주(납기일자별) — 고객사 × 품번 × 납기일. 수주·일별 구매계획과 대조한다.', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'PP_DailyPo';
END
GO

PRINT 'migrate_pp_daily_po: done';
GO
