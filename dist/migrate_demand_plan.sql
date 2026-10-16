-- ══════════════════════════════════════════════════════════════════════
--  migrate_demand_plan.sql
--  고객사 SRM MM30011 일별 구매계획(JIT) — 저장 테이블 + APS 실행 플래그 + 자동수집 Worker 설정
--  1) PP_DemandPlan(고객×품번×일자 예정량) · PP_DemandPlanBatch(업로드/수집 1건 헤더)
--  2) PP_ApsRun.IncludeDailyPlan bit NOT NULL DEFAULT 0 — 기존 실행은 계획 없이 만든 것(0)
--  3) 공통코드 SW_DPSYNC(전역 INTERVAL=60) · SW_DPSYNC_SOURCE · SW_DPSYNC_URL · SW_DPSYNC_AUTH (PO Sync 와 같은 형식, WINDOW 없음)
--  선행: migrate_aps.sql(PP_ApsRun), migrate_md_codeitem_widen.sql(URL·토큰 길이). 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -I -b -i dist/migrate_demand_plan.sql
-- ══════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.PP_ApsRun', 'U') IS NULL
BEGIN
    THROW 50000, N'migrate_aps.sql 을 먼저 적용할 것 (PP_ApsRun 없음)', 1;
END
GO

-- §1 PP_DemandPlanBatch ────────────────────────────────────────────────
IF OBJECT_ID('dbo.PP_DemandPlanBatch', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_DemandPlanBatch (
        Batch          varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CustomerID     varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        Source         varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,   -- UPLOAD | SRM
        SourceKey      varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        FileName       nvarchar(200) COLLATE Korean_Wansung_CI_AS NULL,
        DateFrom       date          NOT NULL,
        DateTo         date          NOT NULL,
        ItemCount      int           NOT NULL DEFAULT ((0)),
        [RowCount]     int           NOT NULL DEFAULT ((0)),   -- 예약어(SET ROWCOUNT) 라 대괄호 필수
        UnmatchedItems int           NOT NULL DEFAULT ((0)),
        PackMismatch   int           NOT NULL DEFAULT ((0)),
        ImportedAt     datetime2(7)  NOT NULL DEFAULT (sysdatetime()),
        ImportedBy     varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedBy      varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedTS      datetime2(7)  NOT NULL DEFAULT (sysdatetime()),
        CONSTRAINT PK_PP_DemandPlanBatch PRIMARY KEY CLUSTERED (Batch)
    );
    CREATE INDEX IX_PP_DemandPlanBatch_Cust_At ON dbo.PP_DemandPlanBatch (CustomerID, ImportedAt DESC);
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'일별 구매계획 업로드/수집 1건 — 날짜 창·건수·미등록·포장 불일치 집계',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_DemandPlanBatch';
    PRINT N'✓ PP_DemandPlanBatch 생성';
END
ELSE PRINT N'· PP_DemandPlanBatch 이미 존재';
GO

-- §2 PP_DemandPlan ─────────────────────────────────────────────────────
IF OBJECT_ID('dbo.PP_DemandPlan', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PP_DemandPlan (
        PlanID       int           IDENTITY(1,1) NOT NULL,
        CustomerID   varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        ItemNo       varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        PlanDate     date          NOT NULL,
        ScheduledQty decimal(14,3) NOT NULL,
        PoQty        decimal(14,3) NULL,
        PackQty      decimal(14,3) NULL,
        PartName     nvarchar(100) COLLATE Korean_Wansung_CI_AS NULL,
        Unit         varchar(10)   COLLATE Korean_Wansung_CI_AS NULL,
        Batch        varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        Source       varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedBy    varchar(20)   COLLATE Korean_Wansung_CI_AS NOT NULL,
        CreatedTS    datetime2(7)  NOT NULL DEFAULT (sysdatetime()),
        ModifiedBy   varchar(20)   COLLATE Korean_Wansung_CI_AS NULL,
        ModifiedTS   datetime2(7)  NULL,
        CONSTRAINT PK_PP_DemandPlan PRIMARY KEY CLUSTERED (PlanID),
        CONSTRAINT FK_PP_DemandPlan_Batch FOREIGN KEY (Batch) REFERENCES dbo.PP_DemandPlanBatch (Batch)
    );
    CREATE UNIQUE INDEX UX_PP_DemandPlan_Cust_Item_Date ON dbo.PP_DemandPlan (CustomerID, ItemNo, PlanDate);
    CREATE INDEX IX_PP_DemandPlan_Date ON dbo.PP_DemandPlan (PlanDate) INCLUDE (CustomerID, ItemNo, ScheduledQty);
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'고객사 일별 납입 예정량(SRM MM30011 Scheduled Qty). 0 은 저장하지 않는다 — 날짜 창 단위로 교체된다',
        @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'PP_DemandPlan';
    PRINT N'✓ PP_DemandPlan 생성';
END
ELSE PRINT N'· PP_DemandPlan 이미 존재';
GO

-- §3 PP_ApsRun.IncludeDailyPlan ───────────────────────────────────────
IF COL_LENGTH('dbo.PP_ApsRun', 'IncludeDailyPlan') IS NULL
BEGIN
    ALTER TABLE dbo.PP_ApsRun ADD IncludeDailyPlan bit NOT NULL CONSTRAINT DF_PP_ApsRun_IncludeDailyPlan DEFAULT ((0));
    PRINT N'✓ PP_ApsRun.IncludeDailyPlan 추가';
END
ELSE PRINT N'· PP_ApsRun.IncludeDailyPlan 이미 존재';
GO

-- §4 공통코드 ─────────────────────────────────────────────────────────
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('SW_DPSYNC',        N'ScheduledWorker · 일별계획 자동수집 설정',   N'ScheduledWorker · Demand Plan Sync Settings',
     N'INTERVAL: Attribute1=수집 주기(분, 0=전체 중지). 선택 TICK_SEC·STARTUP_DELAY_SEC·TIMEOUT_SEC(초). 날짜 창은 서버 응답(dates)이 정한다'),
    ('SW_DPSYNC_SOURCE', N'ScheduledWorker · 일별계획 자동수집 고객사', N'ScheduledWorker · Demand Plan Sync Sources',
     N'CodeValue=소스 키(13자 이하). Attribute1=귀속 MD_Customer.CustomerID. Description="CORCD=;BIZCD=;VENDCD=;PURC_ORG=" 필수'),
    ('SW_DPSYNC_URL',    N'ScheduledWorker · 일별계획 자동수집 URL',    N'ScheduledWorker · Demand Plan Sync Endpoints',
     N'CodeValue=소스 키. Description=엔드포인트 절대 URL'),
    ('SW_DPSYNC_AUTH',   N'ScheduledWorker · 일별계획 자동수집 인증',   N'ScheduledWorker · Demand Plan Sync Auth',
     N'CodeValue=소스 키. Attribute1=Query:{매개변수이름} | Bearer | Basic | Header:{헤더이름}. Description=키 | 토큰 | user:pw | 헤더값. 수동 실행 서비스 키는 SW_POSYNC_AUTH/AMES_SERVICE_KEY 를 같이 쓴다')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'migrate', SYSDATETIME());
PRINT 'SW_DPSYNC* code groups merged';

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('SW_DPSYNC_INTERVAL', 'SW_DPSYNC', 'INTERVAL', N'수집 주기(분)', N'Interval (min)', N'60', 1)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'SW_DPSYNC INTERVAL merged';
GO

SELECT 'PP_DemandPlan' AS obj, COUNT(*) AS n FROM dbo.PP_DemandPlan
UNION ALL SELECT 'PP_DemandPlanBatch', COUNT(*) FROM dbo.PP_DemandPlanBatch
UNION ALL SELECT 'SW_DPSYNC items', COUNT(*) FROM dbo.MD_CodeItem WHERE GroupCode LIKE 'SW[_]DPSYNC%';
GO
