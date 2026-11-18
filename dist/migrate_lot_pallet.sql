-- ════════════════════════════════════════════════════════════════════════
-- migrate_lot_pallet.sql
-- LOT ↔ 팔레트 매핑 이력 PR_LotPallet — 공정(INJ·IMG·PNT)마다 POP 에서 LOT 을 팔레트에 실을 때 한 줄씩 남긴다.
-- 창고(WH)·출하(SHIP) 단계의 팔레트는 WH_Inventory·FG_Shipment 에서 읽으므로 여기에 쓰지 않는다. 토트(팔레트 스캔 없음)는 행이 없다.
-- 불량·폐기로 팔레트에서 내려지면 ReleasedTS 를 채운다(행은 남는다). 외래키는 두지 않는다.
-- 재실행 안전. 마이그레이션 → 신 바이너리 순서 권장(신 코드는 테이블이 없어도 확정은 실패하지 않고 매핑만 건너뛴다).
-- 적용: sqlcmd -S <서버> -d <DB> -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\migrate_lot_pallet.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.PR_LotPallet', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PR_LotPallet (
        MapID       bigint        IDENTITY(1,1) NOT NULL,
        LotCode     varchar(50)   COLLATE DATABASE_DEFAULT NOT NULL,
        PalletNo    varchar(50)   COLLATE DATABASE_DEFAULT NOT NULL,
        Stage       varchar(10)   COLLATE DATABASE_DEFAULT NOT NULL,
        ReleasedTS  datetime2(7)  NULL,
        CreatedBy   varchar(20)   NULL,
        CreatedTS   datetime2(7)  NOT NULL CONSTRAINT DF_PR_LotPallet_CreatedTS DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(20)   NULL,
        ModifiedTS  datetime2(7)  NULL,
        CONSTRAINT PK_PR_LotPallet PRIMARY KEY CLUSTERED (MapID)
    );
    CREATE INDEX IX_PR_LotPallet_Lot    ON dbo.PR_LotPallet (LotCode) INCLUDE (PalletNo, Stage, ReleasedTS);
    CREATE INDEX IX_PR_LotPallet_Pallet ON dbo.PR_LotPallet (PalletNo);
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'LOT↔팔레트 매핑 이력 — POP(INJ·IMG·PNT)이 LOT 을 팔레트에 실을 때 한 줄', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'PR_LotPallet';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'매핑 단계 — INJ · IMG · PNT · varchar(10)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'PR_LotPallet', @level2type=N'COLUMN',@level2name=N'Stage';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'팔레트에서 내려진 시각(불량·폐기) — NULL 이면 아직 실려 있음', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'PR_LotPallet', @level2type=N'COLUMN',@level2name=N'ReleasedTS';
    PRINT N'✓ PR_LotPallet 생성';
END
ELSE PRINT N'· PR_LotPallet 이미 있음';
GO
