-- ════════════════════════════════════════════════════════════════════════
--  migrate_sap_if_po_mrp.sql
--  SAP B1 → MES 구매 PO(IF-T04)·자재 소요계획(IF-T08) 수신 저장소 (11-13)
--
--  1) WH_PurchaseOrder.B1DocEntry · B1LineNum — B1 구매발주(OPOR/POR1) 키. 정의서: MES 구매발주 키 = B1_DOCENTRY, 라인 = LINENUM.
--     기존 PoNumber(varchar(20))에는 B1 문서번호(DocNum)를 문자열로 넣는다.
--     ALTER ADD 라 감사 컬럼 뒤로 가므로 적용 뒤 dist/migrate_audit_columns_last.sql 을 다시 돌린다.
--  2) SCM_MrpPlan — B1 MRP 스냅샷(품목 × 공급사 × 기간, 일 D / 주 W). 공급사 포털 계획 조회의 원천(화면은 후속).
--
--  규칙: 외래키 없음 · 필터 인덱스 없음(Msg 1934) · 감사 컬럼 맨 뒤. 재실행 안전.
--  적용:  sqlcmd -S <서버> -d <DB> -f 65001 -I -b -i dist/migrate_sap_if_po_mrp.sql
--  ※ 개발·운영 DB 적용은 별도 승인 후.
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.WH_PurchaseOrder', 'B1DocEntry') IS NULL
    ALTER TABLE dbo.WH_PurchaseOrder ADD B1DocEntry int NULL;
GO
IF COL_LENGTH('dbo.WH_PurchaseOrder', 'B1LineNum') IS NULL
    ALTER TABLE dbo.WH_PurchaseOrder ADD B1LineNum int NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WH_PurchaseOrder_B1' AND object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder'))
    CREATE INDEX IX_WH_PurchaseOrder_B1 ON dbo.WH_PurchaseOrder (B1DocEntry, B1LineNum);
GO

IF OBJECT_ID(N'dbo.SCM_MrpPlan', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SCM_MrpPlan (
        PlanID       bigint IDENTITY(1,1) NOT NULL,
        EosHdrSeq    bigint               NOT NULL,                       -- EOS_IF.IF_TX_MRP_PLAN_HDR.IF_SEQ (스냅샷 묶음)
        PlanDate     date                 NOT NULL,                       -- 기준일
        Bucket       char(1)              NOT NULL,                       -- D 일별 / W 주별
        HorizonFrom  date                 NOT NULL,
        HorizonTo    date                 NOT NULL,
        Scenario     nvarchar(50)         NULL,
        RunTS        datetime2(0)         NOT NULL,                       -- MRP 실행 시각(포털 '업데이트일시')
        ItemNo       varchar(20)          NOT NULL,
        VendorID     varchar(20)          NULL,                           -- NULL 이면 포털 미표시
        PeriodStart  date                 NOT NULL,
        PeriodLabel  varchar(12)          NULL,
        OpeningQty   decimal(19,6)        NULL,
        GrossReqQty  decimal(19,6)        NOT NULL,
        ScheduledQty decimal(19,6)        NOT NULL,
        PlannedQty   decimal(19,6)        NOT NULL,
        PackQty      decimal(19,6)        NULL,
        UOM          varchar(10)          NULL,
        OrderedQty   decimal(19,6)        NULL,
        CreatedBy    varchar(20)          NOT NULL CONSTRAINT DF_SCM_MrpPlan_CreatedBy DEFAULT 'SAP-IF',
        CreatedTS    datetime2(7)         NULL CONSTRAINT DF_SCM_MrpPlan_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy   varchar(20)          NULL,
        ModifiedTS   datetime2(7)         NULL,
        CONSTRAINT PK_SCM_MrpPlan PRIMARY KEY CLUSTERED (PlanID),
        CONSTRAINT CK_SCM_MrpPlan_Bucket CHECK (Bucket IN ('D','W'))
    );
    PRINT 'SCM_MrpPlan created';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SCM_MrpPlan_Vendor' AND object_id = OBJECT_ID(N'dbo.SCM_MrpPlan'))
    CREATE INDEX IX_SCM_MrpPlan_Vendor ON dbo.SCM_MrpPlan (Bucket, VendorID, ItemNo, PeriodStart);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SCM_MrpPlan_Hdr' AND object_id = OBJECT_ID(N'dbo.SCM_MrpPlan'))
    CREATE INDEX IX_SCM_MrpPlan_Hdr ON dbo.SCM_MrpPlan (EosHdrSeq);
GO
