-- =====================================================================
--  migrate_wh_inventory_setting.sql
--  Warehouse Inventory Setting
--
--  Min/Max quantities are stored on dbo.MD_Item.
--  Color rules and Safety Qty are intentionally not managed by WH-005.
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\migrate_wh_inventory_setting.sql
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.MD_Item
       SET MinStock = CASE ItemNo
            WHEN 'MAT-001' THEN 130
            WHEN 'MAT-002' THEN 90
            WHEN 'MAT-003' THEN 40
            WHEN 'INB-MAT-002' THEN 5
            ELSE MinStock
           END,
           MaxStock = CASE ItemNo
            WHEN 'MAT-001' THEN 220
            WHEN 'MAT-002' THEN 180
            WHEN 'MAT-003' THEN 50
            WHEN 'INB-MAT-002' THEN 40
            ELSE MaxStock
           END,
           ModifiedBy = N'wh-inventory-setting-seed',
           ModifiedTS = SYSDATETIME()
     WHERE ItemNo IN ('MAT-001', 'MAT-002', 'MAT-003', 'INB-MAT-002');
END;

IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL
BEGIN
    MERGE dbo.SYS_Screen AS tgt
    USING (
        VALUES
            ('WH-002', 'WEB', 'WH', N'Picking Orders', N'Picking Orders', 'wh/picking-orders', 'WH-002', 1, 1),
            ('WH-003', 'WEB', 'WH', N'Location Map', N'Location Map', 'wh/location-map', 'WH-003', 2, 1),
            ('WH-004', 'WEB', 'WH', N'Transaction Logs', N'Transaction Logs', 'wh/transactions', 'WH-004', 3, 1),
            ('WH-005', 'WEB', 'WH', N'Inventory Setting', N'Inventory Setting', 'wh/inventory-setting', 'WH-005', 4, 1)
    ) AS src (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible)
    ON tgt.ScreenCode = src.ScreenCode
    WHEN MATCHED THEN UPDATE SET
        ModuleCode = src.ModuleCode,
        ProcessCode = src.ProcessCode,
        ScreenName = src.ScreenName,
        ScreenNameEn = src.ScreenNameEn,
        HRef = src.HRef,
        LidLabel = src.LidLabel,
        SortOrder = src.SortOrder,
        IsVisible = src.IsVisible,
        ModifiedBy = N'wh-inventory-setting-seed',
        ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (ScreenCode, ModuleCode, ProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES
        (src.ScreenCode, src.ModuleCode, src.ProcessCode, src.ScreenName, src.ScreenNameEn, src.HRef, src.LidLabel, src.SortOrder, src.IsVisible, N'wh-inventory-setting-seed', SYSDATETIME());
END;

IF OBJECT_ID(N'dbo.SYS_RolePermission', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminRoleId nvarchar(450);
    SELECT @AdminRoleId = Id FROM dbo.AspNetRoles WHERE NormalizedName = 'ADMIN';

    IF @AdminRoleId IS NOT NULL
    BEGIN
        MERGE dbo.SYS_RolePermission AS tgt
        USING (
            SELECT ScreenCode, ModuleCode
            FROM dbo.SYS_Screen
            WHERE ProcessCode = 'WH'
        ) AS src
        ON tgt.RoleName = 'Admin' AND tgt.ScreenCode = src.ScreenCode
        WHEN MATCHED THEN UPDATE SET
            RoleID = @AdminRoleId,
            PermissionLevel = 'REA',
            IsSystemRole = 1,
            ModifiedBy = N'wh-inventory-setting-seed',
            ModifiedTS = SYSDATETIME()
        WHEN NOT MATCHED THEN INSERT
            (RoleID, RoleName, ModuleCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
        VALUES
            (@AdminRoleId, 'Admin', src.ModuleCode, src.ScreenCode, 'REA', 1, SYSDATETIME(), N'wh-inventory-setting-seed', SYSDATETIME());
    END
END;

IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetRoles', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NOT NULL
BEGIN
    DECLARE @AdminUserId nvarchar(450);
    DECLARE @AdminRoleId2 nvarchar(450);

    SELECT @AdminUserId = Id FROM dbo.AspNetUsers WHERE NormalizedEmail = 'ADMIN@AMES.LOCAL';
    SELECT @AdminRoleId2 = Id FROM dbo.AspNetRoles WHERE NormalizedName = 'ADMIN';

    IF @AdminUserId IS NOT NULL
       AND @AdminRoleId2 IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @AdminUserId AND RoleId = @AdminRoleId2)
    BEGIN
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
        VALUES (@AdminUserId, @AdminRoleId2);
    END
END;

IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    SELECT 'MD_Item inventory setting rows' AS CheckName,
           COUNT(*) AS DataRows
    FROM dbo.MD_Item
    WHERE MinStock IS NOT NULL OR MaxStock IS NOT NULL;
END;
