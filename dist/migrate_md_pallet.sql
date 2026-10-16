-- ════════════════════════════════════════════════════════════════════════
-- migrate_md_pallet.sql  (10-17)
-- 팔레트 마스터(MD-034)와 사출 조건 항목 화면(MD-035) — 사용자 요청
--
--  1) MD_Pallet — 실물 팔레트 마스터. 키 PalletNo varchar(50)(라벨 바코드 번호), 용도 PalletUse(공통코드 PALLET_USE:
--     INTERNAL 내부용 · EXTERNAL 외부 출입용), 외부용만 CustomerID(값 = 그 고객 전용, NULL = 공용), 출고됨 ShippedFlag,
--     마지막 출고 시각·고객 ShippedAt·ShippedCustomerID, 마지막 회수 시각 ReturnedAt(값은 출고·회수 처리가 관리, MD-034 는 보기만).
--     위치·유형·수량·회수 이력은 두지 않는다(수량은 MD_Item.PalletQty·MaxPalletQty, 사용자 결정).
--  2) 공통코드 PALLET_USE — 없는 행만 넣는다(MD-030 에서 고친 이름은 보존).
--  3) WH_Inventory.CaseNo → PalletNo 로 이름 변경(값·위치 그대로 — SCM 납품 케이스 번호가 들어 있던 행은 그 값이 그대로 남는다),
--     인덱스 IX_WH_Inventory_CaseNo → IX_WH_Inventory_PalletNo, 그 컬럼을 읽던 옛 PDA 프로시저 4개도 새 이름으로 고친다.
--  4) FG_Shipment.PalletNo varchar(50) NULL 을 UnitType 다음에 — 컬럼 순서 때문에 테이블을 다시 만든다
--     (행·IDENTITY·기본값·인덱스·컬럼 설명 보존). 출고 처리(PdaFinishedGoodsSql.OutboundComplete)가 출고 단위 재고 행의
--     PalletNo 를 남기고 그 팔레트의 MD_Pallet.ShippedFlag 를 켠다.
--  5) SYS_Screen MD-034(md/ql/pallet, MD-017 다음) · MD-035(md/rp/inj-cond-item, RP 그룹 마지막) + Admin 권한.
--     뒤 화면들의 SortOrder 는 한 칸씩 민다.
--
-- 전제: migrate_fg_shipment_no_foreign_keys.sql(FG_Shipment 에 걸린 FK 두 개 제거) — FG_Shipment 에 FK 가 남아 있으면 중단한다.
-- 재실행 안전. 신 Web·Api 와 함께 반영한다 — 이 마이그레이션 뒤 구 Api 는 WH_Inventory.CaseNo 를 찾지 못해 PDA 입고·재고 조회가
-- `Invalid column name 'CaseNo'` 로 실패하고, 마이그레이션 없이 신 Web·Api 를 올리면 같은 곳이 'PalletNo' 로 실패한다.
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\migrate_md_pallet.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'dbo.FG_Shipment') OR referenced_object_id = OBJECT_ID(N'dbo.FG_Shipment'))
    THROW 51900, N'FG_Shipment 에 외래키가 남아 있습니다. dist/migrate_fg_shipment_no_foreign_keys.sql 을 먼저 적용하세요.', 1;
GO

-- ── 1) MD_Pallet ─────────────────────────────────────────────────────────
-- 출고 시각·출고 고객·회수 시각(ShippedAt·ShippedCustomerID·ReturnedAt)은 10-17 사용자 요청으로 ShippedFlag 다음에 둔다 —
-- 값은 출고·회수 처리 쪽이 넣고 지운다(MD-034 는 목록에 보이기만). 초판(이 세 컬럼 없음)으로 만든 DB 는 아래 1-b 가 행을 보존해 다시 만든다.
IF OBJECT_ID(N'dbo.MD_Pallet', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MD_Pallet (
        PalletNo          varchar(50)   NOT NULL,
        PalletUse         varchar(10)   NOT NULL,
        CustomerID        varchar(20)   NULL,
        ShippedFlag       bit           NOT NULL CONSTRAINT DF_MD_Pallet_ShippedFlag DEFAULT (0),
        ShippedAt         datetime2(7)  NULL,
        ShippedCustomerID varchar(20)   NULL,
        ReturnedAt        datetime2(7)  NULL,
        Remark            nvarchar(200) NULL,
        ActiveFlag        bit           NOT NULL CONSTRAINT DF_MD_Pallet_ActiveFlag  DEFAULT (1),
        CreatedBy         varchar(20)   NOT NULL,
        CreatedTS         datetime2(7)  NOT NULL CONSTRAINT DF_MD_Pallet_CreatedTS   DEFAULT (SYSDATETIME()),
        ModifiedBy        varchar(20)   NULL,
        ModifiedTS        datetime2(7)  NULL,
        CONSTRAINT PK_MD_Pallet PRIMARY KEY CLUSTERED (PalletNo)
    );
    CREATE INDEX IX_MD_Pallet_Customer ON dbo.MD_Pallet (CustomerID);
    PRINT N'✓ MD_Pallet 생성';
END
ELSE PRINT N'· MD_Pallet 이미 있음';
GO

-- ── 1-b) 초판 MD_Pallet → 세 컬럼을 ShippedFlag 다음에(행 보존 재생성) ─────
IF COL_LENGTH(N'dbo.MD_Pallet', N'ReturnedAt') IS NULL
   OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.MD_Pallet')
       AND column_id < COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Pallet'), N'ReturnedAt', 'ColumnId')) <> 6
BEGIN
    BEGIN TRAN;
    CREATE TABLE dbo.MD_Pallet_new (
        PalletNo          varchar(50)   NOT NULL,
        PalletUse         varchar(10)   NOT NULL,
        CustomerID        varchar(20)   NULL,
        ShippedFlag       bit           NOT NULL CONSTRAINT DF_MD_Pallet_new_ShippedFlag DEFAULT (0),
        ShippedAt         datetime2(7)  NULL,
        ShippedCustomerID varchar(20)   NULL,
        ReturnedAt        datetime2(7)  NULL,
        Remark            nvarchar(200) NULL,
        ActiveFlag        bit           NOT NULL CONSTRAINT DF_MD_Pallet_new_ActiveFlag  DEFAULT (1),
        CreatedBy         varchar(20)   NOT NULL,
        CreatedTS         datetime2(7)  NOT NULL CONSTRAINT DF_MD_Pallet_new_CreatedTS   DEFAULT (SYSDATETIME()),
        ModifiedBy        varchar(20)   NULL,
        ModifiedTS        datetime2(7)  NULL,
        CONSTRAINT PK_MD_Pallet_new PRIMARY KEY CLUSTERED (PalletNo)
    );
    DECLARE @has bit = CASE WHEN COL_LENGTH(N'dbo.MD_Pallet', N'ShippedAt') IS NULL THEN 0 ELSE 1 END;
    DECLARE @copy nvarchar(max) = N'
        INSERT dbo.MD_Pallet_new (PalletNo, PalletUse, CustomerID, ShippedFlag, ShippedAt, ShippedCustomerID, ReturnedAt,
                                  Remark, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT PalletNo, PalletUse, CustomerID, ShippedFlag, '
        + CASE WHEN @has = 1 THEN N'ShippedAt, ShippedCustomerID, ReturnedAt' ELSE N'NULL, NULL, NULL' END
        + N', Remark, ActiveFlag, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS FROM dbo.MD_Pallet;';
    EXEC (@copy);
    DROP TABLE dbo.MD_Pallet;   -- 컬럼 설명은 같이 지워진다 — 아래 1-c 가 다시 붙인다
    EXEC sp_rename N'dbo.MD_Pallet_new', N'MD_Pallet';
    EXEC sp_rename N'dbo.PK_MD_Pallet_new', N'PK_MD_Pallet', N'OBJECT';
    EXEC sp_rename N'dbo.DF_MD_Pallet_new_ShippedFlag', N'DF_MD_Pallet_ShippedFlag', N'OBJECT';
    EXEC sp_rename N'dbo.DF_MD_Pallet_new_ActiveFlag', N'DF_MD_Pallet_ActiveFlag', N'OBJECT';
    EXEC sp_rename N'dbo.DF_MD_Pallet_new_CreatedTS', N'DF_MD_Pallet_CreatedTS', N'OBJECT';
    CREATE INDEX IX_MD_Pallet_Customer ON dbo.MD_Pallet (CustomerID);
    COMMIT;
    PRINT N'✓ MD_Pallet 재생성 — ShippedAt·ShippedCustomerID·ReturnedAt 을 ShippedFlag 다음에';
END
GO

-- ── 1-c) 컬럼 설명(없는 것만) ────────────────────────────────────────────
DECLARE @d TABLE (Col sysname NULL, Txt nvarchar(400));
INSERT @d VALUES
    (N'PalletNo',          N'PK · 팔레트 번호(라벨 바코드) — 등록할 때 EOS-PLT-yyMM-nnnn 자동 발행(수동 입력 없음) · varchar(50)'),
    (N'PalletUse',         N'용도 — 공통코드 PALLET_USE(INTERNAL 내부용 · EXTERNAL 외부 출입용) · varchar(10)'),
    (N'CustomerID',        N'전용 고객(MD_Customer) — 외부용만, NULL = 공용 · varchar(20)'),
    (N'ShippedFlag',       N'출고됨 — FG 출고가 켠다. MD-034 마스터 화면에서는 바꾸지 않음 · bit'),
    (N'ShippedAt',         N'마지막 출고 시각 — 출고 처리가 넣는다(MD-034 는 보기만) · datetime2'),
    (N'ShippedCustomerID', N'마지막 출고 고객(MD_Customer) — 출고 처리가 넣는다(MD-034 는 보기만) · varchar(20)'),
    (N'ReturnedAt',        N'마지막 회수 시각 — 회수 처리가 넣는다(MD-034 는 보기만) · datetime2'),
    (N'Remark',            N'비고(사용 안 함 사유 등) · nvarchar(200)'),
    (N'ActiveFlag',        N'사용 여부 — 분실·폐기는 0 · bit'),
    (N'CreatedBy',         N'등록자 사번 · varchar(20)'),
    (N'CreatedTS',         N'등록 시각 · datetime2'),
    (N'ModifiedBy',        N'최종 수정자 사번 · varchar(20)'),
    (N'ModifiedTS',        N'최종 수정 시각 · datetime2'),
    (NULL,                 N'팔레트 마스터 (MD-034)');
DECLARE @c sysname, @t nvarchar(400);
DECLARE dc CURSOR LOCAL FAST_FORWARD FOR SELECT Col, Txt FROM @d;
OPEN dc; FETCH NEXT FROM dc INTO @c, @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND name = N'MS_Description'
                   AND major_id = OBJECT_ID(N'dbo.MD_Pallet')
                   AND minor_id = ISNULL(COLUMNPROPERTY(OBJECT_ID(N'dbo.MD_Pallet'), @c, 'ColumnId'), 0))
    BEGIN
        IF @c IS NULL
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @t,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Pallet';
        ELSE
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @t,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_Pallet',
                 @level2type = N'COLUMN', @level2name = @c;
    END
    FETCH NEXT FROM dc INTO @c, @t;
END
CLOSE dc; DEALLOCATE dc;
GO

-- ── 2) 공통코드 PALLET_USE ───────────────────────────────────────────────
MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'PALLET_USE' AS GroupCode, N'팔레트 용도' AS GroupName, N'Pallet Use' AS GroupNameEn,
              N'MD_Pallet.PalletUse — INTERNAL 내부용 · EXTERNAL 외부 출입용(고객 전용 또는 공용)' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('PALLET_USE_INTERNAL', 'INTERNAL', N'내부용',      N'Internal', 10, N'공장 안에서만 쓰는 팔레트'),
        ('PALLET_USE_EXTERNAL', 'EXTERNAL', N'외부 출입용', N'External', 20, N'고객사로 나가는 팔레트 — 고객 전용 또는 공용')
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, Descr)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'PALLET_USE', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, s.Descr, 'system', SYSDATETIME());
GO

-- ── 3) WH_Inventory.CaseNo → PalletNo ────────────────────────────────────
IF COL_LENGTH(N'dbo.WH_Inventory', N'CaseNo') IS NOT NULL AND COL_LENGTH(N'dbo.WH_Inventory', N'PalletNo') IS NULL
BEGIN
    EXEC sp_rename N'dbo.WH_Inventory.CaseNo', N'PalletNo', N'COLUMN';
    PRINT N'✓ WH_Inventory.CaseNo → PalletNo';
END
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WH_Inventory') AND name = N'IX_WH_Inventory_CaseNo')
    EXEC sp_rename N'dbo.WH_Inventory.IX_WH_Inventory_CaseNo', N'IX_WH_Inventory_PalletNo', N'INDEX';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WH_Inventory') AND name = N'IX_WH_Inventory_PalletNo')
    CREATE INDEX IX_WH_Inventory_PalletNo ON dbo.WH_Inventory (PalletNo);

DECLARE @pd nvarchar(200) = N'팔레트 번호(MD_Pallet) — SCM 납품 케이스로 입고된 박스는 케이스 번호 · varchar(50)';
IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND name = N'MS_Description'
           AND major_id = OBJECT_ID(N'dbo.WH_Inventory') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.WH_Inventory'), N'PalletNo', 'ColumnId'))
    EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = @pd,
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'WH_Inventory',
         @level2type = N'COLUMN', @level2name = N'PalletNo';
ELSE
    EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @pd,
         @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'WH_Inventory',
         @level2type = N'COLUMN', @level2name = N'PalletNo';
GO

-- 옛 PDA 프로시저(앱은 직접 SQL 로 옮겨 쓰지 않지만 DB 에 남아 있다) — 이 네 개의 CaseNo 는 모두 WH_Inventory 컬럼이다
DECLARE @name sysname, @def nvarchar(max);
DECLARE pc CURSOR LOCAL FAST_FORWARD FOR
    SELECT o.name, m.definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id
    WHERE o.name IN (N'FG_PDA_OUTBOUND_TEST_RESET', N'WH_PDA_TRANSACTION_LIST', N'WH_PDA_ADJUST_SCAN_STOCK', N'FG_PDA_ADJUST_SCAN_STOCK')
      AND m.definition COLLATE Latin1_General_100_BIN2 LIKE N'%CaseNo%';
OPEN pc; FETCH NEXT FROM pc INTO @name, @def;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @def = REPLACE(@def COLLATE Latin1_General_100_BIN2, N'CaseNo', N'PalletNo');
    SET @def = STUFF(@def, CHARINDEX(N'CREATE', @def), 6, N'ALTER');
    EXEC (@def);
    PRINT N'✓ 프로시저 ' + @name + N' — CaseNo → PalletNo';
    FETCH NEXT FROM pc INTO @name, @def;
END
CLOSE pc; DEALLOCATE pc;
GO

-- ── 4) FG_Shipment.PalletNo (UnitType 다음) ─────────────────────────────
IF COL_LENGTH(N'dbo.FG_Shipment', N'PalletNo') IS NULL
   OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.FG_Shipment')
       AND column_id < COLUMNPROPERTY(OBJECT_ID(N'dbo.FG_Shipment'), N'PalletNo', 'ColumnId')) <> 3
BEGIN
    BEGIN TRAN;
    CREATE TABLE dbo.FG_Shipment_new (
        ShipmentID      int           IDENTITY(1,1) NOT NULL,
        OutboundBarcode nvarchar(50)  NOT NULL,
        UnitType        varchar(10)   NOT NULL,
        PalletNo        varchar(50)   NULL,
        CustomerID      varchar(20)   NOT NULL,
        ShippedAt       datetime2(7)  NOT NULL CONSTRAINT DF_FG_Shipment_new_ShippedAt DEFAULT (SYSDATETIME()),
        ShippedBy       varchar(20)   NOT NULL,
        CONSTRAINT PK_FG_Shipment_new PRIMARY KEY CLUSTERED (ShipmentID)
    );
    DECLARE @copy nvarchar(max) = N'
        SET IDENTITY_INSERT dbo.FG_Shipment_new ON;
        INSERT dbo.FG_Shipment_new (ShipmentID, OutboundBarcode, UnitType, PalletNo, CustomerID, ShippedAt, ShippedBy)
        SELECT ShipmentID, OutboundBarcode, UnitType, '
        + CASE WHEN COL_LENGTH(N'dbo.FG_Shipment', N'PalletNo') IS NULL THEN N'NULL' ELSE N'PalletNo' END
        + N', CustomerID, ShippedAt, ShippedBy FROM dbo.FG_Shipment;
        SET IDENTITY_INSERT dbo.FG_Shipment_new OFF;';
    EXEC (@copy);

    -- 컬럼 설명은 옮겨 두었다가 새 테이블에 다시 붙인다
    DECLARE @desc TABLE (Col sysname NULL, Txt sql_variant);
    INSERT @desc SELECT c.name, ep.value FROM sys.extended_properties ep
        LEFT JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
        WHERE ep.class = 1 AND ep.name = N'MS_Description' AND ep.major_id = OBJECT_ID(N'dbo.FG_Shipment');

    DROP TABLE dbo.FG_Shipment;
    EXEC sp_rename N'dbo.FG_Shipment_new', N'FG_Shipment';
    EXEC sp_rename N'dbo.PK_FG_Shipment_new', N'PK_FG_Shipment', N'OBJECT';
    EXEC sp_rename N'dbo.DF_FG_Shipment_new_ShippedAt', N'DF_FG_Shipment_ShippedAt', N'OBJECT';
    CREATE INDEX IX_FG_Shipment_ShippedAt ON dbo.FG_Shipment (ShippedAt DESC, ShipmentID DESC)
        INCLUDE (CustomerID, OutboundBarcode, UnitType, ShippedBy);

    IF NOT EXISTS (SELECT 1 FROM @desc WHERE Col = N'PalletNo')
        INSERT @desc VALUES (N'PalletNo', N'출고 단위 재고 행의 팔레트 번호(출고 시점 스냅샷) · varchar(50)');
    DECLARE @dc sysname, @dt sql_variant;
    DECLARE sc CURSOR LOCAL FAST_FORWARD FOR SELECT Col, Txt FROM @desc;
    OPEN sc; FETCH NEXT FROM sc INTO @dc, @dt;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @dc IS NULL
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @dt,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'FG_Shipment';
        ELSE
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @dt,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'FG_Shipment',
                 @level2type = N'COLUMN', @level2name = @dc;
        FETCH NEXT FROM sc INTO @dc, @dt;
    END
    CLOSE sc; DEALLOCATE sc;
    COMMIT;
    PRINT N'✓ FG_Shipment 재생성 — PalletNo 를 UnitType 다음에';
END
ELSE PRINT N'· FG_Shipment.PalletNo 이미 UnitType 다음';
GO

-- ── 5) 화면 등록 ─────────────────────────────────────────────────────────
-- MD 는 SortOrder 하나로 그룹 순서까지 정하므로 끼워 넣을 자리 뒤의 MD 화면을 한 칸씩 민다
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-034')
BEGIN
    DECLARE @after int = (SELECT SortOrder FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-017');
    IF @after IS NULL SET @after = ISNULL((SELECT MAX(SortOrder) FROM dbo.SYS_Screen WHERE ProcessCode = 'MD' AND SubProcessCode = 'QL'), 0);
    UPDATE dbo.SYS_Screen SET SortOrder = SortOrder + 1 WHERE ProcessCode = 'MD' AND SortOrder > @after;
    INSERT dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel,
                           PermissionCriteria, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES ('MD-034', 'WEB', 'MD', 'QL', N'팔레트 관리', N'Pallet Master', 'md/ql/pallet', 'MD-034',
            '__X', @after + 1, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen MD-034 (md/ql/pallet)';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SYS_Screen WHERE ScreenCode = 'MD-035')
BEGIN
    DECLARE @after int = ISNULL((SELECT MAX(SortOrder) FROM dbo.SYS_Screen WHERE ProcessCode = 'MD' AND SubProcessCode = 'RP'), 0);
    UPDATE dbo.SYS_Screen SET SortOrder = SortOrder + 1 WHERE ProcessCode = 'MD' AND SortOrder > @after;
    INSERT dbo.SYS_Screen (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel,
                           PermissionCriteria, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES ('MD-035', 'WEB', 'MD', 'RP', N'사출 조건 항목 관리', N'Injection Condition Item Master', 'md/rp/inj-cond-item', 'MD-035',
            '__X', @after + 1, 1, 'seed', SYSDATETIME());
    PRINT N'✓ SYS_Screen MD-035 (md/rp/inj-cond-item)';
END
GO

-- Admin = 시스템 역할 고정 ID(dist/migrate_system_roles.sql). 승인 기능이 없는 화면이라 REX
DECLARE @AdminRoleName varchar(40) = ISNULL((SELECT Name FROM dbo.AspNetRoles WHERE Id = N'ROLE-SYSADMIN'), 'System Administrator');
INSERT dbo.SYS_RolePermission (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
SELECT N'ROLE-SYSADMIN', @AdminRoleName, 'WEB', s.ScreenCode, 'REX', 1, SYSDATETIME(), 'seed', SYSDATETIME()
FROM (VALUES ('MD-034'), ('MD-035')) s (ScreenCode)
WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleID = N'ROLE-SYSADMIN' AND p.ScreenCode = s.ScreenCode);
GO

-- ── 확인 ─────────────────────────────────────────────────────────────────
SELECT c.column_id, c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'dbo.FG_Shipment') ORDER BY c.column_id;
SELECT COUNT(*) AS PalletNoRows FROM dbo.WH_Inventory WHERE PalletNo IS NOT NULL;
SELECT ScreenCode, SubProcessCode, HRef, SortOrder FROM dbo.SYS_Screen WHERE ProcessCode = 'MD' AND SubProcessCode IN ('QL', 'RP') ORDER BY SortOrder;
GO
