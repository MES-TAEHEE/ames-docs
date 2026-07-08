-- ════════════════════════════════════════════════════════════════════════
-- migrate_column_order.sql
-- 감사 컬럼 순서 정렬 및 구조 보정 (실제 DB 적용용)
-- 대상 DB : AMES_DEV
--
-- 선행 조건 : migrate_audit_columns.sql 먼저 실행 (CreatedAt 이름 변경 + ModifiedTS 추가)
--             migrate_add_processcode.sql 먼저 실행 (ProcessCode 추가)
--             (이 스크립트도 IF EXISTS 가드로 중복 실행 안전)
--
-- 처리 내용 :
--   Part 0 : 선행 마이그레이션 보정 (멱등)
--   Part 1 : 일반 테이블 133개 — ModifiedTS / ModifiedBy 순서 교체
--   Part 2 : MD_Bom        — 전체 재구성 (CreatedBy/TS + ModifiedBy/TS 모두 교체)
--   Part 3 : WH_PurchaseOrder — 전체 재구성 (IDENTITY, CreatedAt→TS)
--   Part 4 : SYS_NotificationRule — 전체 재구성 (IDENTITY, SourceModule→ModuleCode)
--   Part 5 : SYS_Config    — 전체 재구성 (IDENTITY)
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO

-- ════════════════════════════════════════════════════════════════════════
-- Part 0 : 선행 마이그레이션 보정 (멱등 — 이미 적용됐으면 스킵)
-- ════════════════════════════════════════════════════════════════════════

-- 0-a. WH_PurchaseOrder.CreatedAt → CreatedTS
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder') AND name = 'CreatedAt'
)
BEGIN
    EXEC sp_rename 'dbo.WH_PurchaseOrder.CreatedAt', 'CreatedTS', 'COLUMN';
    PRINT '✓ Part0-a : WH_PurchaseOrder.CreatedAt → CreatedTS';
END
GO

-- 0-b. 4개 캐시 테이블 ModifiedTS 누락 보완
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PR_DashTileCache')    AND name = 'ModifiedTS')
    ALTER TABLE dbo.PR_DashTileCache    ADD [ModifiedTS] DATETIME2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PR_DefectRateCache')  AND name = 'ModifiedTS')
    ALTER TABLE dbo.PR_DefectRateCache  ADD [ModifiedTS] DATETIME2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PNT_SeqAllocator')    AND name = 'ModifiedTS')
    ALTER TABLE dbo.PNT_SeqAllocator    ADD [ModifiedTS] DATETIME2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.PNT_StationStatsCache') AND name = 'ModifiedTS')
    ALTER TABLE dbo.PNT_StationStatsCache ADD [ModifiedTS] DATETIME2 NULL;
PRINT '✓ Part0-b : 캐시 테이블 ModifiedTS 확인 완료';
GO

-- 0-c. SYS_NotificationRule : SourceModule → ModuleCode
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.SYS_NotificationRule') AND name = 'SourceModule'
)
BEGIN
    EXEC sp_rename 'dbo.SYS_NotificationRule.SourceModule', 'ModuleCode', 'COLUMN';
    PRINT '✓ Part0-c : SYS_NotificationRule.SourceModule → ModuleCode';
END
GO

-- 0-d. SYS_NotificationRule / SYS_AuditLog : ProcessCode 컬럼 추가
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SYS_NotificationRule') AND name = 'ProcessCode')
BEGIN
    ALTER TABLE dbo.SYS_NotificationRule ADD [ProcessCode] VARCHAR(10) NULL;
    PRINT '✓ Part0-d : SYS_NotificationRule.ProcessCode 추가';
END
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SYS_AuditLog') AND name = 'ProcessCode')
BEGIN
    ALTER TABLE dbo.SYS_AuditLog ADD [ProcessCode] VARCHAR(10) NULL;
    PRINT '✓ Part0-d : SYS_AuditLog.ProcessCode 추가';
END
GO

-- ════════════════════════════════════════════════════════════════════════
-- Part 1 : 일반 테이블 — ModifiedTS / ModifiedBy 순서 교체
--          대상 : ModifiedTS.column_id < ModifiedBy.column_id 인 모든 테이블
--                 (4개 전체재구성 테이블 제외)
--          방법 : ADD [ModifiedTS_mig] → UPDATE 복사 → DROP 원본 → sp_rename
--                 (ModifiedTS_mig 가 테이블 끝에 추가되므로 ModifiedBy 뒤로 이동)
-- ════════════════════════════════════════════════════════════════════════

DECLARE @tbl  NVARCHAR(128);
DECLARE @sql  NVARCHAR(MAX);

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name
    FROM   sys.tables t
    WHERE  t.name NOT IN (
               'MD_Bom', 'WH_PurchaseOrder',
               'SYS_NotificationRule', 'SYS_Config'
           )
      AND  EXISTS (
               SELECT 1
               FROM   sys.columns c1
               JOIN   sys.columns c2
                   ON c2.object_id = c1.object_id
               WHERE  c1.object_id = t.object_id
                 AND  c1.name      = 'ModifiedTS'
                 AND  c2.name      = 'ModifiedBy'
                 AND  c1.column_id < c2.column_id   -- ModifiedTS 가 ModifiedBy 앞 → 잘못된 순서
           )
    ORDER  BY t.name;

OPEN cur;
FETCH NEXT FROM cur INTO @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        SET @sql = N'
ALTER TABLE dbo.' + QUOTENAME(@tbl) + N' ADD [ModifiedTS_mig] DATETIME2 NULL;
UPDATE dbo.' + QUOTENAME(@tbl) + N' SET [ModifiedTS_mig] = [ModifiedTS];
ALTER TABLE dbo.' + QUOTENAME(@tbl) + N' DROP COLUMN [ModifiedTS];
EXEC sp_rename N''dbo.' + REPLACE(@tbl, '''', '''''') + N'.[ModifiedTS_mig]'', N''ModifiedTS'', N''COLUMN'';
';
        EXEC sp_executesql @sql;
        PRINT N'✓ Part1 : ' + @tbl + N' — ModifiedTS 이동 완료';
    END TRY
    BEGIN CATCH
        PRINT N'✗ Part1 : ' + @tbl + N' — 오류: ' + ERROR_MESSAGE();
    END CATCH

    FETCH NEXT FROM cur INTO @tbl;
END
CLOSE cur;
DEALLOCATE cur;
GO

-- ════════════════════════════════════════════════════════════════════════
-- Part 2 : MD_Bom — 전체 재구성
--          원본 DB : CreatedTS → CreatedBy → ModifiedTS → ModifiedBy (모두 잘못됨)
--          목표     : CreatedBy → CreatedTS → ModifiedBy → ModifiedTS
-- ════════════════════════════════════════════════════════════════════════

-- Part1 커서가 MD_Bom을 이미 처리했을 경우도 고려해 현재 순서 체크
IF EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.MD_Bom')
      AND  c1.name = 'ModifiedTS'
      AND  c2.name = 'ModifiedBy'
      AND  c1.column_id < c2.column_id
)
   OR EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.MD_Bom')
      AND  c1.name = 'CreatedTS'
      AND  c2.name = 'CreatedBy'
      AND  c1.column_id < c2.column_id
)
BEGIN
    -- 1) 올바른 순서로 새 테이블 생성
    CREATE TABLE dbo.MD_Bom_new (
        [BOMID]         VARCHAR(24)    NOT NULL,
        [ParentItemNo]  VARCHAR(20)        NULL,
        [CompItemNo]    VARCHAR(20)        NULL,
        [BOMLevel]      INT                NULL,
        [QtyPer]        DECIMAL(12,4)      NULL,
        [UOM]           VARCHAR(10)        NULL,
        [ScrapPct]      DECIMAL(5,2)       NULL,
        [VersionID]     VARCHAR(24)        NULL,
        [Position]      INT                NULL,
        [Note]          NVARCHAR(120)      NULL,
        [ActiveFlag]    BIT                NULL DEFAULT 1,
        [CreatedBy]     VARCHAR(50)    NOT NULL DEFAULT 'system',
        [CreatedTS]     DATETIME2          NULL DEFAULT SYSDATETIME(),
        [ModifiedBy]    NVARCHAR(450)      NULL,
        [ModifiedTS]    DATETIME2          NULL,
        CONSTRAINT PK_MD_Bom_new PRIMARY KEY CLUSTERED ([BOMID])
    );

    -- 2) 데이터 복사 (컬럼명으로 참조 — 순서 무관)
    INSERT INTO dbo.MD_Bom_new
        (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, UOM,
         ScrapPct, VersionID, [Position], Note, ActiveFlag,
         CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT
        BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, UOM,
        ScrapPct, VersionID, [Position], Note, ActiveFlag,
        CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.MD_Bom;

    -- 3) 원본 삭제 후 이름 변경
    DROP TABLE dbo.MD_Bom;
    EXEC sp_rename N'dbo.MD_Bom_new',      N'MD_Bom',     N'OBJECT';
    EXEC sp_rename N'dbo.PK_MD_Bom_new',   N'PK_MD_Bom',  N'OBJECT';

    -- 4) 임시 DEFAULT 제거
    DECLARE @defName NVARCHAR(128);
    SELECT @defName = d.name
    FROM   sys.default_constraints d
    JOIN   sys.columns c ON c.default_object_id = d.object_id
    WHERE  d.parent_object_id = OBJECT_ID(N'dbo.MD_Bom')
      AND  c.name = 'CreatedBy';
    IF @defName IS NOT NULL
        EXEC(N'ALTER TABLE dbo.MD_Bom DROP CONSTRAINT [' + @defName + N']');

    PRINT N'✓ Part2 : MD_Bom 재구성 완료';
END
ELSE
    PRINT N'– Part2 : MD_Bom 이미 올바른 순서 (스킵)';
GO

-- ════════════════════════════════════════════════════════════════════════
-- Part 3 : WH_PurchaseOrder — 전체 재구성 (IDENTITY on PoID)
-- ════════════════════════════════════════════════════════════════════════

IF EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder')
      AND  c1.name = 'ModifiedTS'
      AND  c2.name = 'ModifiedBy'
      AND  c1.column_id < c2.column_id
)
   OR EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder')
      AND  c1.name = 'CreatedTS'
      AND  c2.name = 'CreatedBy'
      AND  c1.column_id < c2.column_id
)
BEGIN
    CREATE TABLE dbo.WH_PurchaseOrder_new (
        [PoID]          INT IDENTITY   NOT NULL,
        [PoNumber]      VARCHAR(20)        NULL,
        [PoLineNo]      INT                NULL,
        [VendorID]      VARCHAR(20)        NULL,
        [ItemNo]        VARCHAR(20)        NULL,
        [OrderQty]      DECIMAL(12,3)      NULL,
        [ReceivedQty]   DECIMAL(12,3)      NULL,
        [UnitCode]      VARCHAR(10)        NULL,
        [UnitPrice]     DECIMAL(14,4)      NULL,
        [Currency]      CHAR(3)            NULL,
        [OrderDate]     DATE               NULL,
        [DueDate]       DATE               NULL,
        [Status]        VARCHAR(20)        NULL,
        [SapSyncedAt]   DATETIME2          NULL,
        [CreatedBy]     VARCHAR(50)    NOT NULL DEFAULT 'system',
        [CreatedTS]     DATETIME2          NULL DEFAULT SYSDATETIME(),
        [ModifiedBy]    NVARCHAR(450)      NULL,
        [ModifiedTS]    DATETIME2          NULL,
        CONSTRAINT PK_WH_PurchaseOrder_new PRIMARY KEY CLUSTERED ([PoID])
    );

    SET IDENTITY_INSERT dbo.WH_PurchaseOrder_new ON;

    -- CreatedTS 컬럼명이 이전에 CreatedAt 이었을 수 있음 — 이름 체크 후 분기
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder') AND name = 'CreatedTS')
        INSERT INTO dbo.WH_PurchaseOrder_new
            (PoID, PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty,
             UnitCode, UnitPrice, Currency, OrderDate, DueDate, Status, SapSyncedAt,
             CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT
            PoID, PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty,
            UnitCode, UnitPrice, Currency, OrderDate, DueDate, Status, SapSyncedAt,
            CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM dbo.WH_PurchaseOrder;
    ELSE
        -- CreatedAt 으로 남아있는 경우 (Part0-a 미실행)
        INSERT INTO dbo.WH_PurchaseOrder_new
            (PoID, PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty,
             UnitCode, UnitPrice, Currency, OrderDate, DueDate, Status, SapSyncedAt,
             CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT
            PoID, PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty,
            UnitCode, UnitPrice, Currency, OrderDate, DueDate, Status, SapSyncedAt,
            CreatedBy, CreatedAt, ModifiedBy, ModifiedTS
        FROM dbo.WH_PurchaseOrder;

    SET IDENTITY_INSERT dbo.WH_PurchaseOrder_new OFF;

    DROP TABLE dbo.WH_PurchaseOrder;
    EXEC sp_rename N'dbo.WH_PurchaseOrder_new',      N'WH_PurchaseOrder',     N'OBJECT';
    EXEC sp_rename N'dbo.PK_WH_PurchaseOrder_new',   N'PK_WH_PurchaseOrder',  N'OBJECT';

    DECLARE @defName3 NVARCHAR(128);
    SELECT @defName3 = d.name
    FROM   sys.default_constraints d
    JOIN   sys.columns c ON c.default_object_id = d.object_id
    WHERE  d.parent_object_id = OBJECT_ID(N'dbo.WH_PurchaseOrder')
      AND  c.name = 'CreatedBy';
    IF @defName3 IS NOT NULL
        EXEC(N'ALTER TABLE dbo.WH_PurchaseOrder DROP CONSTRAINT [' + @defName3 + N']');

    PRINT N'✓ Part3 : WH_PurchaseOrder 재구성 완료';
END
ELSE
    PRINT N'– Part3 : WH_PurchaseOrder 이미 올바른 순서 (스킵)';
GO

-- ════════════════════════════════════════════════════════════════════════
-- Part 4 : SYS_NotificationRule — 전체 재구성 (IDENTITY on NotificationRuleID)
--          SourceModule → ModuleCode, ProcessCode 컬럼 동적 처리
-- ════════════════════════════════════════════════════════════════════════

IF EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.SYS_NotificationRule')
      AND  c1.name = 'ModifiedTS'
      AND  c2.name = 'ModifiedBy'
      AND  c1.column_id < c2.column_id
)
   OR EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.SYS_NotificationRule')
      AND  c1.name = 'CreatedTS'
      AND  c2.name = 'CreatedBy'
      AND  c1.column_id < c2.column_id
)
BEGIN
    CREATE TABLE dbo.SYS_NotificationRule_new (
        [NotificationRuleID]    INT IDENTITY   NOT NULL,
        [EventTypeCode]         VARCHAR(20)        NULL,
        [EventName]             NVARCHAR(60)       NULL,
        [ModuleCode]            VARCHAR(10)        NULL,
        [ProcessCode]           VARCHAR(10)        NULL,
        [TriggerCondition]      NVARCHAR(500)      NULL,
        [IsEnabled]             BIT                NULL DEFAULT 1,
        [ChannelsJSON]          NVARCHAR(200)      NULL,
        [RecipientRolesJSON]    NVARCHAR(500)      NULL,
        [CreatedBy]             VARCHAR(50)    NOT NULL DEFAULT 'system',
        [CreatedTS]             DATETIME2          NULL DEFAULT SYSDATETIME(),
        [ModifiedBy]            NVARCHAR(450)      NULL,
        [ModifiedTS]            DATETIME2          NULL,
        CONSTRAINT PK_SYS_NotificationRule_new PRIMARY KEY CLUSTERED ([NotificationRuleID])
    );

    SET IDENTITY_INSERT dbo.SYS_NotificationRule_new ON;

    -- ModuleCode 컬럼 원본 이름 동적 결정 (SourceModule → ModuleCode 상태 체크)
    DECLARE @srcModuleCol NVARCHAR(128) =
        CASE
            WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SYS_NotificationRule') AND name = 'ModuleCode')
            THEN N'ModuleCode'
            WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SYS_NotificationRule') AND name = 'SourceModule')
            THEN N'SourceModule'
            ELSE NULL
        END;

    DECLARE @hasProcessCode BIT =
        CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.SYS_NotificationRule') AND name = 'ProcessCode')
             THEN 1 ELSE 0 END;

    DECLARE @insSQL NVARCHAR(MAX) =
        N'INSERT INTO dbo.SYS_NotificationRule_new
            (NotificationRuleID, EventTypeCode, EventName, ModuleCode, ProcessCode,
             TriggerCondition, IsEnabled, ChannelsJSON, RecipientRolesJSON,
             CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
         SELECT
            NotificationRuleID, EventTypeCode, EventName, '
        + CASE WHEN @srcModuleCol IS NULL THEN N'NULL' ELSE QUOTENAME(@srcModuleCol) END
        + N', '
        + CASE WHEN @hasProcessCode = 1 THEN N'ProcessCode' ELSE N'NULL' END
        + N', TriggerCondition, IsEnabled, ChannelsJSON, RecipientRolesJSON,
            CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
         FROM dbo.SYS_NotificationRule;';

    EXEC sp_executesql @insSQL;

    SET IDENTITY_INSERT dbo.SYS_NotificationRule_new OFF;

    DROP TABLE dbo.SYS_NotificationRule;
    EXEC sp_rename N'dbo.SYS_NotificationRule_new',      N'SYS_NotificationRule',     N'OBJECT';
    EXEC sp_rename N'dbo.PK_SYS_NotificationRule_new',   N'PK_SYS_NotificationRule',  N'OBJECT';

    DECLARE @defName4 NVARCHAR(128);
    SELECT @defName4 = d.name
    FROM   sys.default_constraints d
    JOIN   sys.columns c ON c.default_object_id = d.object_id
    WHERE  d.parent_object_id = OBJECT_ID(N'dbo.SYS_NotificationRule')
      AND  c.name = 'CreatedBy';
    IF @defName4 IS NOT NULL
        EXEC(N'ALTER TABLE dbo.SYS_NotificationRule DROP CONSTRAINT [' + @defName4 + N']');

    PRINT N'✓ Part4 : SYS_NotificationRule 재구성 완료';
END
ELSE
    PRINT N'– Part4 : SYS_NotificationRule 이미 올바른 순서 (스킵)';
GO

-- ════════════════════════════════════════════════════════════════════════
-- Part 5 : SYS_Config — 전체 재구성 (IDENTITY on ConfigID)
-- ════════════════════════════════════════════════════════════════════════

IF EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.SYS_Config')
      AND  c1.name = 'ModifiedTS'
      AND  c2.name = 'ModifiedBy'
      AND  c1.column_id < c2.column_id
)
   OR EXISTS (
    SELECT 1
    FROM   sys.columns c1
    JOIN   sys.columns c2 ON c2.object_id = c1.object_id
    WHERE  c1.object_id = OBJECT_ID(N'dbo.SYS_Config')
      AND  c1.name = 'CreatedTS'
      AND  c2.name = 'CreatedBy'
      AND  c1.column_id < c2.column_id
)
BEGIN
    CREATE TABLE dbo.SYS_Config_new (
        [ConfigID]          INT IDENTITY   NOT NULL,
        [ConfigKey]         VARCHAR(60)        NULL,
        [ConfigType]        VARCHAR(15)        NULL,
        [Category]          VARCHAR(30)        NULL,
        [ConfigValue]       NVARCHAR(500)      NULL,
        [CodeName]          NVARCHAR(80)       NULL,
        [Unit]              VARCHAR(10)        NULL,
        [UsedByModulesJSON] NVARCHAR(500)      NULL,
        [SortOrder]         INT                NULL,
        [IsActive]          BIT                NULL DEFAULT 1,
        [CreatedBy]         VARCHAR(50)    NOT NULL DEFAULT 'system',
        [CreatedTS]         DATETIME2          NULL DEFAULT SYSDATETIME(),
        [ModifiedBy]        NVARCHAR(450)      NULL,
        [ModifiedTS]        DATETIME2          NULL,
        CONSTRAINT PK_SYS_Config_new PRIMARY KEY CLUSTERED ([ConfigID])
    );

    SET IDENTITY_INSERT dbo.SYS_Config_new ON;

    INSERT INTO dbo.SYS_Config_new
        (ConfigID, ConfigKey, ConfigType, Category, ConfigValue, CodeName,
         Unit, UsedByModulesJSON, SortOrder, IsActive,
         CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT
        ConfigID, ConfigKey, ConfigType, Category, ConfigValue, CodeName,
        Unit, UsedByModulesJSON, SortOrder, IsActive,
        CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM dbo.SYS_Config;

    SET IDENTITY_INSERT dbo.SYS_Config_new OFF;

    DROP TABLE dbo.SYS_Config;
    EXEC sp_rename N'dbo.SYS_Config_new',      N'SYS_Config',     N'OBJECT';
    EXEC sp_rename N'dbo.PK_SYS_Config_new',   N'PK_SYS_Config',  N'OBJECT';

    DECLARE @defName5 NVARCHAR(128);
    SELECT @defName5 = d.name
    FROM   sys.default_constraints d
    JOIN   sys.columns c ON c.default_object_id = d.object_id
    WHERE  d.parent_object_id = OBJECT_ID(N'dbo.SYS_Config')
      AND  c.name = 'CreatedBy';
    IF @defName5 IS NOT NULL
        EXEC(N'ALTER TABLE dbo.SYS_Config DROP CONSTRAINT [' + @defName5 + N']');

    PRINT N'✓ Part5 : SYS_Config 재구성 완료';
END
ELSE
    PRINT N'– Part5 : SYS_Config 이미 올바른 순서 (스킵)';
GO

-- ════════════════════════════════════════════════════════════════════════
-- 확인 쿼리
-- ════════════════════════════════════════════════════════════════════════
SELECT
    t.name    AS TableName,
    c.name    AS ColumnName,
    c.column_id AS ColOrder
FROM   sys.columns c
JOIN   sys.tables  t ON t.object_id = c.object_id
WHERE  c.name IN ('CreatedBy','CreatedTS','ModifiedBy','ModifiedTS')
ORDER  BY t.name, c.column_id;
GO
