-- =====================================================================
--  PDA_SEED.sql
--  Consolidated Warehouse and Finished Goods demo/test data for the PDA
--
--  Apply after dist/pda/PDA_SCHEMA.sql:
--    sqlcmd -S <server> -U <user> -C -b -d AMES_DEV -i dist\pda\PDA_SEED.sql
--
--  This file is rerunnable. It contains the current WH/FG PDA test set.
-- =====================================================================
-- Target database must be selected explicitly by sqlcmd -d or in SSMS.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO
IF COALESCE(TRY_CONVERT(int,SESSION_CONTEXT(N'AMES_DEVELOPMENT_SEED')),0)<>1
BEGIN
    RAISERROR('Development seed disabled. Explicit AMES_DEVELOPMENT_SEED session opt-in is required.',16,1);
    SET NOEXEC ON;
END;
GO

-- SCTEST2 / 0000: detailed scenario account.
-- SCTEST1 / 0000: simple PPT validation account.
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SYS_UserProfile', N'U') IS NOT NULL
BEGIN
    DECLARE @TestUserId nvarchar(450) = N'pda-detailed-scenario-user';
    DECLARE @TestPinHash nvarchar(200) = N'AQAAAAEAACcQAAAAEJFoD5NntyEZN/tZd1NHiMZtqlIJPCqGlrClvFmOcSGzPWghpal/Q1PscOkb3c9kyQ==';

    MERGE dbo.AspNetUsers AS T
    USING (SELECT @TestUserId AS Id) AS S ON T.Id = S.Id
    WHEN MATCHED THEN UPDATE SET
        UserName = N'SCTEST2', NormalizedUserName = N'SCTEST2',
        LockoutEnabled = 1, AccessFailedCount = 0
    WHEN NOT MATCHED THEN INSERT
        (Id, UserName, NormalizedUserName, SecurityStamp, ConcurrencyStamp,
         EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        (@TestUserId, N'SCTEST2', N'SCTEST2', REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
         REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 0, 0, 0, 1, 0);

    IF EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE EmployeeNo = 'SCTEST2')
        UPDATE dbo.SYS_UserProfile
           SET UserID = @TestUserId, EmployeeName = N'Detailed Scenario Test', Department = 'QA',
               PinHash = @TestPinHash, AccountStatus = 'Active',
               FailedLoginCount = 0, ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE EmployeeNo = 'SCTEST2';
    ELSE
        INSERT INTO dbo.SYS_UserProfile
            (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
             PinHash, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
        VALUES
            (@TestUserId, 'SCTEST2', N'Detailed Scenario Test', 'QA', 'SEH-US-01', 'DAY',
             @TestPinHash, 'Active', 0, 'pda-seed', SYSDATETIME());

    DECLARE @SimpleTestUserId nvarchar(450) = N'pda-simple-scenario-user';
    MERGE dbo.AspNetUsers AS T
    USING (SELECT @SimpleTestUserId AS Id) AS S ON T.Id = S.Id
    WHEN MATCHED THEN UPDATE SET
        UserName = N'SCTEST1', NormalizedUserName = N'SCTEST1',
        LockoutEnabled = 1, AccessFailedCount = 0
    WHEN NOT MATCHED THEN INSERT
        (Id, UserName, NormalizedUserName, SecurityStamp, ConcurrencyStamp,
         EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        (@SimpleTestUserId, N'SCTEST1', N'SCTEST1', REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''),
         REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 0, 0, 0, 1, 0);

    IF EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE EmployeeNo = 'SCTEST1')
        UPDATE dbo.SYS_UserProfile
           SET UserID = @SimpleTestUserId, EmployeeName = N'Simple Scenario Test', Department = 'QA',
               PinHash = @TestPinHash, AccountStatus = 'Active',
               FailedLoginCount = 0, ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
         WHERE EmployeeNo = 'SCTEST1';
    ELSE
        INSERT INTO dbo.SYS_UserProfile
            (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
             PinHash, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
        VALUES
            (@SimpleTestUserId, 'SCTEST1', N'Simple Scenario Test', 'QA', 'SEH-US-01', 'DAY',
             @TestPinHash, 'Active', 0, 'pda-seed', SYSDATETIME());

    -- Both scenario accounts validate administrator-only Adjust screens.
    -- 시스템 역할 고정 ID(ROLE-SYSADMIN) — 이름은 바뀔 수 있다(dist/migrate_system_roles.sql)
    DECLARE @AdminRoleId nvarchar(450) =
        (SELECT TOP (1) Id FROM dbo.AspNetRoles WHERE Id = N'ROLE-SYSADMIN');
    IF @AdminRoleId IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1 FROM dbo.AspNetUserRoles
           WHERE UserId = @SimpleTestUserId AND RoleId = @AdminRoleId
       )
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
        VALUES (@SimpleTestUserId, @AdminRoleId);
    IF @AdminRoleId IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1 FROM dbo.AspNetUserRoles
           WHERE UserId = @TestUserId AND RoleId = @AdminRoleId
       )
        INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
        VALUES (@TestUserId, @AdminRoleId);
END;
GO

-- PTEST / 0000: normal PDA operator, without scenario panels or admin rights.
-- BEGIN PTEST ACCOUNT
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @PtestUserId nvarchar(450) =
    (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedUserName = N'PTEST');
DECLARE @PtestPinHash nvarchar(200) = N'AQAAAAEAACcQAAAAEJFoD5NntyEZN/tZd1NHiMZtqlIJPCqGlrClvFmOcSGzPWghpal/Q1PscOkb3c9kyQ==';
IF @PtestUserId IS NULL
BEGIN
    SET @PtestUserId = CONVERT(nvarchar(36), NEWID());
    INSERT INTO dbo.AspNetUsers
        (Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, ConcurrencyStamp,
         EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
    VALUES
        (@PtestUserId, N'PTEST', N'PTEST', @PtestPinHash, CONVERT(nvarchar(36), NEWID()),
         CONVERT(nvarchar(36), NEWID()), 0, 0, 0, 1, 0);
END;
IF EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE EmployeeNo = 'PTEST' AND (UserID IS NULL OR UserID <> @PtestUserId))
    THROW 50000, 'PTEST employee number belongs to a different profile.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SYS_UserProfile WHERE EmployeeNo = 'PTEST')
    INSERT INTO dbo.SYS_UserProfile
        (UserID, EmployeeNo, EmployeeName, Department, PlantCode, DefaultShift,
         PinHash, AccountStatus, FailedLoginCount, CreatedBy, CreatedTS)
    VALUES
        (@PtestUserId, 'PTEST', N'PDA Operator Test', 'QA', 'SEH-US-01', 'DAY',
         @PtestPinHash, 'Active', 0, 'pda-seed', SYSDATETIME());
-- Operator = 기본 역할 고정 ID ROLE-OPERATOR(이름은 바뀔 수 있다 — dist/migrate_system_roles.sql)
DECLARE @PtestRoleId nvarchar(450) = (SELECT Id FROM dbo.AspNetRoles WHERE Id = N'ROLE-OPERATOR');
IF @PtestRoleId IS NULL
BEGIN
    -- Fresh rebuilds run before the application's role bootstrap.
    SET @PtestRoleId = N'ROLE-OPERATOR';
    INSERT INTO dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (@PtestRoleId, N'Operator', N'OPERATOR', CONVERT(nvarchar(36), NEWID()));
END;
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE UserId = @PtestUserId AND RoleId = @PtestRoleId)
    INSERT INTO dbo.AspNetUserRoles (UserId, RoleId) VALUES (@PtestUserId, @PtestRoleId);
COMMIT;
-- END PTEST ACCOUNT
GO

-- =====================================================================
--  WH / FG common codes used by the PDA
-- =====================================================================
IF OBJECT_ID(N'dbo.MD_CodeGroup', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.MD_CodeItem', N'U') IS NOT NULL
BEGIN
    DECLARE @PdaCodeGroups TABLE
    (
        GroupCode varchar(20) PRIMARY KEY,
        GroupName nvarchar(60),
        GroupNameEn nvarchar(60),
        Description nvarchar(200)
    );

    INSERT INTO @PdaCodeGroups VALUES
        ('WH_OUTGOING_TYPE', N'ì°½ê³  ì¶œê³  ìœ í˜•', N'Warehouse Outgoing Type', N'Warehouse release destination type'),
        ('INV_ADJUST_REASON', N'ìž¬ê³  ì¡°ì • ì‚¬ìœ ', N'Inventory Adjust Reason', N'Warehouse and finished goods quantity adjustment reason'),
        ('FG_STORAGE_METHOD', N'ì™„ì œí’ˆ ì ì¹˜ ë°©ì‹', N'FG Storage Method', N'Finished goods put-away storage method'),
        ('FG_RETURN_REASON', N'ì™„ì œí’ˆ ë°˜í’ˆ ì‚¬ìœ ', N'FG Return Reason', N'Finished goods customer return reason'),
        ('WH_INV_STATUS', N'ì°½ê³  ìž¬ê³  ìƒíƒœ', N'Warehouse Inventory Status', N'Warehouse LOT inventory lifecycle status'),
        ('FG_STOCK_STATUS', N'ì™„ì œí’ˆ ìž¬ê³  ìƒíƒœ', N'FG Stock Status', N'Finished goods stock lifecycle status'),
        ('FG_SHIP_STATUS', N'ì™„ì œí’ˆ ì¶œí•˜ ìƒíƒœ', N'FG Shipment Status', N'Finished goods shipment lifecycle status'),
        ('INV_TXN_TYPE', N'ìž¬ê³  íŠ¸ëžœìž­ì…˜ ìœ í˜•', N'Inventory Transaction Type', N'Warehouse and finished goods inventory transaction type');

    MERGE dbo.MD_CodeGroup AS T
    USING @PdaCodeGroups AS S
       ON S.GroupCode COLLATE DATABASE_DEFAULT = T.GroupCode COLLATE DATABASE_DEFAULT
    WHEN MATCHED THEN UPDATE SET
        GroupName = S.GroupName, GroupNameEn = S.GroupNameEn, Description = S.Description,
        UseFlag = 1, ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES
        (S.GroupCode, S.GroupName, S.GroupNameEn, S.Description, 1, 'pda-seed', SYSDATETIME());

    DECLARE @PdaCodeItems TABLE
    (
        GroupCode varchar(20),
        CodeValue varchar(20),
        CodeName nvarchar(60),
        CodeNameEn nvarchar(60),
        SortOrder int,
        Attribute1 nvarchar(40),
        Description nvarchar(120),
        PRIMARY KEY (GroupCode, CodeValue)
    );

    INSERT INTO @PdaCodeItems VALUES
        ('WH_OUTGOING_TYPE', 'PRODUCTION', N'ìƒì‚°ë¼ì¸ ì¶œê³ ', N'To Production Line', 10, N'TO_PRODUCTION_LINE', N'Issue material to production'),
        ('WH_OUTGOING_TYPE', 'OTHER', N'ê¸°íƒ€ ì¶œê³ ', N'Other Outgoing', 20, N'OTHER_OUTGOING', N'Other warehouse issue'),
        ('WH_OUTGOING_TYPE', 'DEFECT', N'ë¶ˆëŸ‰ ì¶œê³ ', N'Defect Outgoing', 30, N'DEFECT_OUTGOING', N'Issue defective material'),
        ('INV_ADJUST_REASON', 'COUNT_DIFF', N'ì‹¤ì‚¬ ì°¨ì´', N'Count Diff', 10, NULL, N'Physical count difference'),
        ('INV_ADJUST_REASON', 'DAMAGED', N'íŒŒì†', N'Damaged', 20, NULL, N'Damaged inventory'),
        ('INV_ADJUST_REASON', 'LOST', N'ë¶„ì‹¤', N'Lost', 30, NULL, N'Lost inventory'),
        ('INV_ADJUST_REASON', 'FOUND', N'ìž¬ê³  ë°œê²¬', N'Found', 40, NULL, N'Found inventory'),
        ('INV_ADJUST_REASON', 'OTHER', N'ê¸°íƒ€', N'Other', 50, NULL, N'Other adjustment reason'),
        ('FG_STORAGE_METHOD', 'BOX', N'ë°•ìŠ¤', N'Box', 10, NULL, N'Box storage'),
        ('FG_STORAGE_METHOD', 'PALLET', N'íŒ”ë ˆíŠ¸', N'Pallet', 20, NULL, N'Pallet storage'),
        ('FG_STORAGE_METHOD', 'RACK', N'ëž™', N'Rack', 30, NULL, N'Rack storage'),
        ('FG_STORAGE_METHOD', 'LOCATION', N'ë¡œì¼€ì´ì…˜', N'Location Only', 40, NULL, N'Direct location storage'),
        ('FG_RETURN_REASON', 'DEFECT', N'ë¶ˆëŸ‰', N'Defect', 10, NULL, N'Defective product'),
        ('FG_RETURN_REASON', 'WRONG_ITEM', N'ì˜¤ì¶œí•˜', N'Wrong Item', 20, NULL, N'Wrong item shipped'),
        ('FG_RETURN_REASON', 'DAMAGED_TRANSIT', N'ìš´ì†¡ ì¤‘ íŒŒì†', N'Damaged in Transit', 30, NULL, N'Damaged during transit'),
        ('FG_RETURN_REASON', 'CUSTOMER_CHANGE', N'ê³ ê° ìš”ì²­ ë³€ê²½', N'Customer Change', 40, NULL, N'Customer-requested change'),
        ('FG_RETURN_REASON', 'OTHER', N'ê¸°íƒ€', N'Other', 50, NULL, N'Other return reason'),
        ('WH_INV_STATUS', 'CREATED', N'ìƒì„±', N'Created', 10, NULL, N'LOT created before receipt'),
        ('WH_INV_STATUS', 'RECEIVED', N'ìž…ê³ ', N'Received', 20, NULL, N'Received into warehouse'),
        ('WH_INV_STATUS', 'STORED', N'ì ì¹˜', N'Stored', 30, NULL, N'Stored at a location'),
        ('WH_INV_STATUS', 'RELEASED', N'ì¶œê³ ', N'Released', 40, NULL, N'Released from warehouse'),
        ('WH_INV_STATUS', 'RECEIPT_CANCELLED', N'ìž…ê³  ì·¨ì†Œ', N'Receipt Cancelled', 50, NULL, N'Receipt was cancelled'),
        ('WH_INV_STATUS', 'RELEASE_CANCELLED', N'ì¶œê³  ì·¨ì†Œ', N'Release Cancelled', 60, NULL, N'Release was cancelled'),
        ('WH_INV_STATUS', 'RETURN_RECEIVED', N'ë°˜í’ˆ ìž…ê³ ', N'Return Received', 70, NULL, N'Returned inventory received'),
        ('WH_INV_STATUS', 'DEFECTIVE', N'ë¶ˆëŸ‰', N'Defective', 80, NULL, N'Defective inventory'),
        ('WH_INV_STATUS', 'DISPOSED', N'íê¸°', N'Disposed', 90, NULL, N'Disposed inventory'),
        ('FG_STOCK_STATUS', 'AVAILABLE', N'ê°€ìš©', N'Available', 10, NULL, N'Available finished goods stock'),
        ('FG_STOCK_STATUS', 'RESERVED', N'ì˜ˆì•½', N'Reserved', 20, NULL, N'Reserved for shipment'),
        ('FG_STOCK_STATUS', 'PICKED', N'í”¼í‚¹', N'Picked', 30, NULL, N'Picked for loading'),
        ('FG_STOCK_STATUS', 'LOADED', N'ìƒì°¨', N'Loaded', 40, NULL, N'Loaded onto truck'),
        ('FG_STOCK_STATUS', 'SHIPPED', N'ì¶œí•˜', N'Shipped', 50, NULL, N'Shipped finished goods'),
        ('FG_STOCK_STATUS', 'HOLD', N'ë³´ë¥˜', N'Hold', 60, NULL, N'Stock on hold'),
        ('FG_SHIP_STATUS', 'OPEN', N'ì˜¤í”ˆ', N'Open', 10, NULL, N'Shipment order opened'),
        ('FG_SHIP_STATUS', 'RELEASED', N'ë¦´ë¦¬ì¦ˆ', N'Released', 20, NULL, N'Shipment order released'),
        ('FG_SHIP_STATUS', 'READY', N'ì¶œí•˜ ì¤€ë¹„', N'Ready', 30, NULL, N'Ready for loading'),
        ('FG_SHIP_STATUS', 'PICKED', N'í”¼í‚¹ ì™„ë£Œ', N'Picked', 40, NULL, N'Products picked'),
        ('FG_SHIP_STATUS', 'LOADED', N'ìƒì°¨ ì™„ë£Œ', N'Loaded', 50, NULL, N'Products loaded'),
        ('FG_SHIP_STATUS', 'SHIPPED', N'ì¶œí•˜ ì™„ë£Œ', N'Shipped', 60, NULL, N'Shipment departed'),
        ('INV_TXN_TYPE', 'IN', N'ìž…ê³ ', N'Inbound', 10, NULL, N'Inventory receipt or put-away'),
        ('INV_TXN_TYPE', 'OUT', N'ì¶œê³ ', N'Outbound', 20, NULL, N'Warehouse inventory issue'),
        ('INV_TXN_TYPE', 'PICK', N'í”¼í‚¹', N'Picking', 30, NULL, N'Finished goods picking'),
        ('INV_TXN_TYPE', 'LOAD', N'ìƒì°¨', N'Loading', 40, NULL, N'Finished goods loading'),
        ('INV_TXN_TYPE', 'RETURN', N'ë°˜í’ˆ', N'Return', 50, NULL, N'Customer return receipt'),
        ('INV_TXN_TYPE', 'ADJ', N'ìˆ˜ëŸ‰ ì¡°ì •', N'Adjustment', 60, NULL, N'Inventory quantity adjustment');

    MERGE dbo.MD_CodeItem AS T
    USING @PdaCodeItems AS S
       ON T.CodeID COLLATE DATABASE_DEFAULT = CONCAT(S.GroupCode, '_', S.CodeValue) COLLATE DATABASE_DEFAULT
    WHEN MATCHED THEN UPDATE SET
        GroupCode = S.GroupCode, CodeValue = S.CodeValue, CodeName = S.CodeName,
        CodeNameEn = S.CodeNameEn, SortOrder = S.SortOrder, Attribute1 = S.Attribute1,
        UseFlag = 1, Description = S.Description,
        ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder,
         Attribute1, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES
        (CONCAT(S.GroupCode, '_', S.CodeValue), S.GroupCode, S.CodeValue,
         S.CodeName, S.CodeNameEn, S.SortOrder, S.Attribute1, 1,
         S.Description, 'pda-seed', SYSDATETIME());
END;
GO

-- =====================================================================
--  EOS spare-parts storage hierarchy
-- =====================================================================
IF OBJECT_ID(N'dbo.MD_CodeGroup', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.MD_CodeItem', N'U') IS NOT NULL
BEGIN
    MERGE dbo.MD_CodeGroup AS T
    USING (VALUES
        (CONVERT(varchar(20), 'WH_CODE'), CONVERT(nvarchar(60), N'ì°½ê³ '), CONVERT(nvarchar(60), N'Warehouse'), CONVERT(nvarchar(200), N'Warehouse code')),
        ('WH_AREA', N'ì°½ê³  êµ¬ì—­', N'Warehouse Area', N'Warehouse area by storage purpose'),
        ('MNT_ZONE', N'ë³´ì „ ìœ„ì¹˜', N'Maintenance Zone', N'Spare-parts storage zone'),
        ('MNT_SLOT', N'ë³´ì „ ëž™ ì¸µ', N'Maintenance Rack Level', N'Spare-parts rack level')
    ) AS S(GroupCode, GroupName, GroupNameEn, Description)
       ON T.GroupCode COLLATE DATABASE_DEFAULT = S.GroupCode COLLATE DATABASE_DEFAULT
    WHEN MATCHED THEN UPDATE SET
        GroupName = S.GroupName, GroupNameEn = S.GroupNameEn, Description = S.Description,
        UseFlag = 1, ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES
        (S.GroupCode, S.GroupName, S.GroupNameEn, S.Description, 1, 'pda-seed', SYSDATETIME());

    DECLARE @LocationCodes TABLE
    (
        GroupCode varchar(20), CodeValue varchar(20), CodeName nvarchar(60),
        CodeNameEn nvarchar(60), ParentCodeID varchar(41), SortOrder int,
        PRIMARY KEY (GroupCode, CodeValue)
    );

    INSERT INTO @LocationCodes VALUES
        ('WH_CODE', 'EOS', N'EOS', N'EOS', NULL, 10),
        ('WH_AREA', 'MAT_AREA', N'ìžìž¬ ë³´ê´€ êµ¬ì—­', N'Material Storage Area', 'WH_CODE_EOS', 10),
        ('WH_AREA', 'FG_AREA', N'ì™„ì œí’ˆ ë³´ê´€ êµ¬ì—­', N'Finished Goods Storage Area', 'WH_CODE_EOS', 20),
        ('MNT_ZONE', 'SP_CAB1', N'CAB1', N'CAB1', NULL, 10),
        ('MNT_ZONE', 'SP_CAB2', N'CAB2', N'CAB2', NULL, 20),
        ('MNT_ZONE', 'SP_A1', N'A1', N'A1', NULL, 30),
        ('MNT_ZONE', 'SP_A2', N'A2', N'A2', NULL, 40),
        ('MNT_ZONE', 'SP_A3', N'A3', N'A3', NULL, 50),
        ('MNT_ZONE', 'SP_B1', N'B1', N'B1', NULL, 60),
        ('MNT_ZONE', 'SP_B2', N'B2', N'B2', NULL, 70),
        ('MNT_ZONE', 'SP_B3', N'B3', N'B3', NULL, 80),
        ('MNT_ZONE', 'SP_C1', N'C1', N'C1', NULL, 90),
        ('MNT_ZONE', 'SP_C2', N'C2', N'C2', NULL, 100),
        ('MNT_ZONE', 'SP_C3', N'C3', N'C3', NULL, 110),
        ('MNT_ZONE', 'SP_D1', N'D1', N'D1', NULL, 120),
        ('MNT_ZONE', 'SP_D2', N'D2', N'D2', NULL, 130),
        ('MNT_ZONE', 'SP_D3', N'D3', N'D3', NULL, 140),
        ('MNT_ZONE', 'SP_E1', N'E1', N'E1', NULL, 150),
        ('MNT_ZONE', 'SP_E2', N'E2', N'E2', NULL, 160),
        ('MNT_ZONE', 'SP_E3', N'E3', N'E3', NULL, 170),
        ('MNT_ZONE', 'SP_F1', N'F1', N'F1', NULL, 180),
        ('MNT_ZONE', 'SP_F2', N'F2', N'F2', NULL, 190),
        ('MNT_ZONE', 'SP_F3', N'F3', N'F3', NULL, 200),
        ('MNT_ZONE', 'SP_R_RACKS_1', N'R/Racks-1', N'R/Racks-1', NULL, 210),
        ('MNT_ZONE', 'SP_R_RACKS_2', N'R/Racks-2', N'R/Racks-2', NULL, 220),
        ('MNT_ZONE', 'SP_C_RACK_1', N'C/Rack-1', N'C/Rack-1', NULL, 230),
        ('MNT_ZONE', 'SP_FL1', N'FL1', N'FL1', NULL, 240),
        ('MNT_ZONE', 'SP_FL2', N'FL2', N'FL2', NULL, 250),
        ('MNT_ZONE', 'SP_FL3', N'FL3', N'FL3', NULL, 260),
        ('MNT_ZONE', 'SP_FL4', N'FL4', N'FL4', NULL, 270),
        ('MNT_ZONE', 'SP_EXTRA', N'Extra', N'Extra', NULL, 280),
        ('MNT_SLOT', '01', N'1ì¸µ', N'Level 1', NULL, 10),
        ('MNT_SLOT', '02', N'2ì¸µ', N'Level 2', NULL, 20),
        ('MNT_SLOT', '03', N'3ì¸µ', N'Level 3', NULL, 30),
        ('MNT_SLOT', '04', N'4ì¸µ', N'Level 4', NULL, 40),
        ('MNT_SLOT', '05', N'5ì¸µ', N'Level 5', NULL, 50),
        ('MNT_SLOT', 'EX', N'Extra', N'Extra', NULL, 60);

    MERGE dbo.MD_CodeItem AS T
    USING @LocationCodes AS S
       ON T.CodeID COLLATE DATABASE_DEFAULT = CONCAT(S.GroupCode, '_', S.CodeValue) COLLATE DATABASE_DEFAULT
    WHEN MATCHED THEN UPDATE SET
        GroupCode = S.GroupCode, CodeValue = S.CodeValue, CodeName = S.CodeName,
        CodeNameEn = S.CodeNameEn, ParentCodeID = S.ParentCodeID,
        SortOrder = S.SortOrder, UseFlag = 1,
        ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID,
         SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES
        (CONCAT(S.GroupCode, '_', S.CodeValue), S.GroupCode, S.CodeValue,
         S.CodeName, S.CodeNameEn, S.ParentCodeID, S.SortOrder, 1,
         'pda-seed', SYSDATETIME());

    DELETE FROM dbo.MD_CodeItem
     WHERE GroupCode = 'WH_ZONE'
       AND ParentCodeID = 'WH_AREA_SPARE_PARTS_AREA';
END;
GO

IF OBJECT_ID(N'dbo.MD_CodeItem', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.MD_CodeItem SET ParentCodeID = NULL
    WHERE GroupCode = 'MNT_ZONE' AND ParentCodeID = 'WH_AREA_SPARE_PARTS_AREA';
    DELETE FROM dbo.MD_CodeItem WHERE CodeID = 'WH_AREA_SPARE_PARTS_AREA';
END;
GO

-- =====================================================================
--  Spare Parts PDA test master (production rows come from MD_SparePart)
-- =====================================================================
IF OBJECT_ID(N'dbo.MD_Vendor', N'U') IS NOT NULL
BEGIN
    MERGE dbo.MD_Vendor AS T
    USING (VALUES
        (CONVERT(varchar(20), 'SP-DEMO-V01'), CONVERT(nvarchar(80), N'Demo Spare Parts Supply')),
        ('V1007', N'Demo Local Supplier'),
        ('V2003', N'Demo CKD Supplier')
    ) AS S(VendorID, VendorName) ON T.VendorID = S.VendorID
    WHEN MATCHED THEN UPDATE SET
        VendorName = S.VendorName, VendorType = 'SUPPLIER', VendorCategory = N'Spare Parts',
        ActiveFlag = 1, ModifiedBy = N'pda-seed', ModifiedTS = SYSDATETIME()
    WHEN NOT MATCHED THEN INSERT
        (VendorID, VendorName, VendorType, VendorCategory, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (S.VendorID, S.VendorName, 'SUPPLIER', N'Spare Parts', 1, N'pda-seed', SYSDATETIME());
END;
GO

IF OBJECT_ID(N'dbo.MD_SparePart', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.MD_SparePart WHERE SparePartNo = 'EOS-SP-K9-269999')
BEGIN
    INSERT INTO dbo.MD_SparePart
        (SparePartNo, Category, ApplicableEquip, PartNo, PartName, Maker, UOM, OnHandQty,
         SafetyStock, SupplierID, ZoneCode, Slot, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        ('EOS-SP-K9-269999', 'K', '9', 'PDA-SP-TEST-001', N'PDA Spare Parts Test', N'DEMO INDUSTRIAL', 'EA', 0,
         1, 'SP-DEMO-V01', 'SP_EXTRA', 'EX', 1, N'pda-seed', SYSDATETIME());
END;

IF OBJECT_ID(N'dbo.MNT_SparePartItem', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.MNT_SparePartItem WHERE SerialNo='SPI-TEST-000001')
BEGIN
    INSERT dbo.MNT_SparePartItem(SerialNo,SparePartNo,StatusCode,CreatedBy,CreatedTS)
    VALUES('SPI-TEST-000001','EOS-SP-K9-269999','CREATED','pda-seed',SYSDATETIME());
END;

IF OBJECT_ID(N'dbo.MD_SparePart', N'U') IS NOT NULL
BEGIN
    DECLARE @SparePartSource TABLE (PartNo varchar(20), Maker nvarchar(100));
    INSERT INTO @SparePartSource VALUES
        ('SP-BRG-6204', N'DEMO MOTION'),
        ('SP-BRG-6206', N'DEMO MOTION'),
        ('SP-SEAL-32', N'DEMO MOTION'),
        ('SP-SEAL-50', N'DEMO MOTION'),
        ('SP-FLT-HYD', N'DEMO INDUSTRIAL'),
        ('SP-FLT-AIR', N'DEMO INDUSTRIAL'),
        ('SP-HTR-2KW', N'DEMO CONTROLS'),
        ('SP-SENS-PT100', N'DEMO CONTROLS'),
        ('SP-MOT-1HP', N'DEMO CONTROLS'),
        ('SP-OIL-46', N'DEMO INDUSTRIAL'),
        ('SP-GREASE-EP', N'DEMO INDUSTRIAL'),
        ('SP-FUSE-25A', N'DEMO CONTROLS'),
        ('PDA-SP-TEST-001', N'DEMO INDUSTRIAL');

    UPDATE P
       SET P.Maker = S.Maker,
           P.SupplierID = 'SP-DEMO-V01',
           P.ModifiedBy = N'pda-seed',
           P.ModifiedTS = SYSDATETIME()
      FROM dbo.MD_SparePart P
    JOIN @SparePartSource S
      ON S.PartNo COLLATE DATABASE_DEFAULT = P.PartNo COLLATE DATABASE_DEFAULT;
END;
GO

-- =====================================================================
--  WH unified demo data
--  Uses only WH_Inventory, WH_InventoryTransaction, WH_PurchaseOrder,
--  and WH_PickSlip. Inbound source rows are tbl_Lot or SCM delivery data.
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

MERGE dbo.MD_CodeItem AS T
USING (VALUES
    ('WH_CODE_EOS','WH_CODE','EOS',N'EOS Warehouse',NULL,10),
    ('WH_AREA_MAT_AREA','WH_AREA','MAT_AREA',N'Material Storage Area','WH_CODE_EOS',10),
    ('WH_AREA_FG_AREA','WH_AREA','FG_AREA',N'Finished Goods Storage Area','WH_CODE_EOS',20)
) S(CodeID,GroupCode,CodeValue,CodeName,ParentCodeID,SortOrder)
ON T.CodeID COLLATE DATABASE_DEFAULT=S.CodeID COLLATE DATABASE_DEFAULT
WHEN MATCHED THEN UPDATE SET GroupCode=S.GroupCode,CodeValue=S.CodeValue,CodeName=S.CodeName,
    ParentCodeID=S.ParentCodeID,SortOrder=S.SortOrder,UseFlag=1,ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(CodeID,GroupCode,CodeValue,CodeName,ParentCodeID,SortOrder,UseFlag,CreatedBy,CreatedTS)
    VALUES(S.CodeID,S.GroupCode,S.CodeValue,S.CodeName,S.ParentCodeID,S.SortOrder,1,'pda-seed',SYSDATETIME());

DECLARE @Locations table(LocationID varchar(20),LocationName nvarchar(120),ZoneCode varchar(20),Aisle varchar(5),Bay varchar(5),Slot varchar(5));
INSERT @Locations VALUES
 ('WH010101',N'Inbound Rack A-01-01','A1','01','01','01'),
 ('WH010201',N'Inbound Rack A-02-01','A1','02','01','01'),
 ('B0-08-A1',N'Rack 08 / Bay A / Level 1','B0','08','A','1'),
 ('B0-08-B1',N'Rack 08 / Bay B / Level 1','B0','08','B','1'),
 ('B0-08-C1',N'Rack 08 / Bay C / Level 1','B0','08','C','1'),
 ('B0-09-D2',N'Rack 09 / Bay D / Level 2','B0','09','D','2'),
 ('B0-10-A1',N'Rack 10 / Bay A / Level 1','B0','10','A','1'),
 ('B0-10-B1',N'Rack 10 / Bay B / Level 1','B0','10','B','1'),
 ('B0-12-B1',N'Rack 12 / Bay B / Level 1','B0','12','B','1'),
 ('REL010101',N'Release Rack 01 / Bay 01','REL','01','01','01'),
 ('REL010201',N'Release Rack 01 / Bay 02','REL','01','02','01'),
 ('REL020101',N'Release Rack 02 / Bay 01','REL','02','01','01');
MERGE dbo.MD_Location T USING @Locations S
ON T.LocationID COLLATE DATABASE_DEFAULT=S.LocationID COLLATE DATABASE_DEFAULT
WHEN MATCHED THEN UPDATE SET LocationName=S.LocationName,ZoneCode=S.ZoneCode,Aisle=S.Aisle,Bay=S.Bay,Slot=S.Slot,
    WhCode='EOS',AreaCode='MAT_AREA',ActiveFlag=1,ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(LocationID,LocationName,ZoneCode,Aisle,Bay,Slot,Capacity,LocationType,PlantCode,ActiveFlag,CreatedBy,CreatedTS,WhCode,AreaCode)
    VALUES(S.LocationID,S.LocationName,S.ZoneCode,S.Aisle,S.Bay,S.Slot,5000,'RACK','EOS',1,'pda-seed',SYSDATETIME(),'EOS','MAT_AREA');

-- Scenario lots use only existing canonical MD_Item rows; never create or rename masters.
IF EXISTS (SELECT 1 FROM (VALUES
 ('S82311-DW010'),('S82321-DW010'),('S82371-XA000'),('M0230-P8000RBQ'),
 ('M0230-P8010RBQ'),('M0240-P8000RBQ'),('S82381-XA000'),('S83371-XA000'),
 ('S83371-XA010'),('S83381-XA000'),('S83381-XA010')
) V(ItemNo) WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item I WHERE I.ItemNo=V.ItemNo))
    THROW 51000, 'PDA scenario requires canonical MD_Item seed first.', 1;

MERGE dbo.WH_PurchaseOrder T
USING (VALUES
 ('PPT-INBOUND',10,'V1007','S82311-DW010',100.000,'EA'),
 ('PPT-INBOUND',20,'V1007','S82321-DW010',100.000,'EA'),
 ('PPT-INBOUND',30,'V2003','S82371-XA000',100.000,'EA'),
 ('4100260903',90,'V1007','S82321-DW010',288.000,'EA')
) S(PoNumber,PoLineNo,VendorID,ItemNo,OrderQty,UnitCode)
ON T.PoNumber COLLATE DATABASE_DEFAULT=S.PoNumber COLLATE DATABASE_DEFAULT AND T.PoLineNo=S.PoLineNo
WHEN MATCHED THEN UPDATE SET VendorID=S.VendorID,ItemNo=S.ItemNo,OrderQty=S.OrderQty,UnitCode=S.UnitCode,
    Status=CASE WHEN COALESCE(T.ReceivedQty,0)>0 THEN T.Status ELSE 'Open' END,ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(PoNumber,PoLineNo,VendorID,ItemNo,OrderQty,ReceivedQty,UnitCode,OrderDate,DueDate,Status,CreatedBy,CreatedTS)
    VALUES(S.PoNumber,S.PoLineNo,S.VendorID,S.ItemNo,S.OrderQty,0,S.UnitCode,CONVERT(date,GETDATE()),DATEADD(day,1,CONVERT(date,GETDATE())),'Open','pda-seed',SYSDATETIME());

/* SCTEST1 inbound source: two shipped delivery notes with three boxes each. */
DECLARE @SimpleDeliveries table(ModeCode varchar(10),DeliveryNumber varchar(30),NoteNumber varchar(30),VendorID varchar(20));
INSERT @SimpleDeliveries VALUES
 ('LOCAL','PPT-WH-DL-LOCAL','PPT-WH-NOTE-LOCAL','V1007'),
 ('CKD','PPT-WH-DL-CKD','PPT-WH-NOTE-CKD','V2003');

MERGE dbo.SCM_Delivery T
USING @SimpleDeliveries S
ON T.DeliveryNumber COLLATE DATABASE_DEFAULT=S.DeliveryNumber COLLATE DATABASE_DEFAULT
WHEN MATCHED THEN UPDATE SET PoNumber='PPT-INBOUND',VendorID=S.VendorID,DeliveryDate=CONVERT(date,GETDATE()),
    Status='Shipped',ShipDate=CONVERT(date,GETDATE()),ShippedAt=SYSDATETIME(),ShippedBy='SCTEST1',
    ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(DeliveryNumber,RequestID,PoNumber,VendorID,DeliveryDate,Status,CreatedBy,CreatedUserID,CreatedTS,ShipDate,ShippedAt,ShippedBy)
    VALUES(S.DeliveryNumber,NEWID(),'PPT-INBOUND',S.VendorID,CONVERT(date,GETDATE()),'Shipped','pda-seed','SCTEST1',SYSDATETIME(),CONVERT(date,GETDATE()),SYSDATETIME(),'SCTEST1');

DECLARE @SimpleLines table(ModeCode varchar(10),ItemNo varchar(20),Qty decimal(18,3),PackingQty decimal(18,3));
INSERT @SimpleLines VALUES
 ('LOCAL','S82311-DW010',40,20),
 ('LOCAL','S82321-DW010',10,10),
 ('CKD','S82371-XA000',90,30);

MERGE dbo.SCM_DeliveryLine T
USING
(
    SELECT D.DeliveryID,P.PoID,L.Qty,L.PackingQty
    FROM @SimpleLines L
    JOIN @SimpleDeliveries S ON S.ModeCode=L.ModeCode
    JOIN dbo.SCM_Delivery D
      ON D.DeliveryNumber COLLATE DATABASE_DEFAULT=S.DeliveryNumber COLLATE DATABASE_DEFAULT
    CROSS APPLY
    (
        SELECT TOP (1) P.PoID
        FROM dbo.WH_PurchaseOrder P
        WHERE P.PoNumber='PPT-INBOUND'
          AND P.ItemNo COLLATE DATABASE_DEFAULT=L.ItemNo COLLATE DATABASE_DEFAULT
        ORDER BY P.PoLineNo,P.PoID
    ) P
) S ON T.DeliveryID=S.DeliveryID AND T.PoID=S.PoID
WHEN MATCHED THEN UPDATE SET Quantity=S.Qty,ReceivedQty=0,PackingQty=S.PackingQty,VendorLotNo=NULL,ProductionDate=CONVERT(date,GETDATE())
WHEN NOT MATCHED THEN INSERT(DeliveryID,PoID,Quantity,ReceivedQty,PackingQty,VendorLotNo,ProductionDate)
    VALUES(S.DeliveryID,S.PoID,S.Qty,0,S.PackingQty,NULL,CONVERT(date,GETDATE()));

/* Keep PDA test delivery notes printable: never seed an empty JSON snapshot. */
MERGE dbo.SCM_DeliveryNote T
USING
(
    SELECT S.NoteNumber,S.VendorID,
        (SELECT
            S.NoteNumber AS Number,
            D.PoNumber AS OrderNumber,
            S.VendorID AS Vendor,
            COALESCE(V.VendorName,S.VendorID) AS VendorName,
            N'EOS' AS Buyer,
            COALESCE((SELECT MAX(P.DeliveryDestination) FROM dbo.SCM_DeliveryLine DL JOIN dbo.WH_PurchaseOrder P ON P.PoID=DL.PoID WHERE DL.DeliveryID=D.DeliveryID),N'') AS Destination,
            D.ShipDate AS ShipDate,
            SYSDATETIME() AS IssuedAt,
            N'SCTEST1' AS IssuedBy,
            JSON_QUERY((
                SELECT ISNULL(P.PoLineNo,P.PoID) AS Line,P.ItemNo AS Item,ISNULL(I.ItemName,P.ItemNo) AS Name,
                    ISNULL(P.UnitCode,'') AS Unit,DL.Quantity,D.DeliveryNumber,D.PoNumber AS OrderNumber,
                    ISNULL(P.DeliveryDestination,'') AS Destination,D.ShipDate,DL.DeliveryLineID,P.PoID
                FROM dbo.SCM_DeliveryLine DL
                JOIN dbo.WH_PurchaseOrder P ON P.PoID=DL.PoID
                LEFT JOIN dbo.MD_Item I ON I.ItemNo=P.ItemNo
                WHERE DL.DeliveryID=D.DeliveryID
                ORDER BY DL.DeliveryLineID
                FOR JSON PATH
            )) AS Lines
         FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) AS Snapshot
    FROM @SimpleDeliveries S
    JOIN dbo.SCM_Delivery D
      ON D.DeliveryNumber COLLATE DATABASE_DEFAULT=S.DeliveryNumber COLLATE DATABASE_DEFAULT
    LEFT JOIN dbo.MD_Vendor V ON V.VendorID=S.VendorID
) S
ON T.NoteNumber COLLATE DATABASE_DEFAULT=S.NoteNumber COLLATE DATABASE_DEFAULT
WHEN MATCHED THEN UPDATE SET VendorID=S.VendorID,Snapshot=S.Snapshot,IssuedAt=SYSDATETIME(),IssuedBy='SCTEST1',IssuedUserID='SCTEST1'
WHEN NOT MATCHED THEN INSERT(NoteNumber,VendorID,Snapshot,IssuedAt,IssuedBy,IssuedUserID)
    VALUES(S.NoteNumber,S.VendorID,S.Snapshot,SYSDATETIME(),'SCTEST1','SCTEST1');

MERGE dbo.SCM_DeliveryNoteDelivery T
USING
(
    SELECT D.DeliveryID,N.NoteID
    FROM @SimpleDeliveries S
    JOIN dbo.SCM_Delivery D
      ON D.DeliveryNumber COLLATE DATABASE_DEFAULT=S.DeliveryNumber COLLATE DATABASE_DEFAULT
    JOIN dbo.SCM_DeliveryNote N
      ON N.NoteNumber COLLATE DATABASE_DEFAULT=S.NoteNumber COLLATE DATABASE_DEFAULT
) S ON T.DeliveryID=S.DeliveryID
WHEN MATCHED THEN UPDATE SET NoteID=S.NoteID
WHEN NOT MATCHED THEN INSERT(DeliveryID,NoteID) VALUES(S.DeliveryID,S.NoteID);

DECLARE @SimpleBoxes table(ModeCode varchar(10),BoxSeq int,BoxNumber varchar(64),ItemNo varchar(20),Qty decimal(18,3));
INSERT @SimpleBoxes VALUES
 ('LOCAL',1,'5011LL260908800001','S82311-DW010',20),
 ('LOCAL',2,'5011LL260908800002','S82311-DW010',20),
 ('LOCAL',1,'5011LL260908800003','S82321-DW010',10),
 ('CKD',1,'CKD260908800000001','S82371-XA000',30),
 ('CKD',2,'CKD260908800000002','S82371-XA000',30),
 ('CKD',3,'CKD260908800000003','S82371-XA000',30);

MERGE dbo.SCM_DeliveryBox T
USING
(
    SELECT DL.DeliveryLineID,B.BoxSeq,B.BoxNumber,B.ItemNo,I.ItemName,B.Qty
    FROM @SimpleBoxes B
    JOIN @SimpleDeliveries S ON S.ModeCode=B.ModeCode
    JOIN dbo.SCM_Delivery D
      ON D.DeliveryNumber COLLATE DATABASE_DEFAULT=S.DeliveryNumber COLLATE DATABASE_DEFAULT
    CROSS APPLY
    (
        SELECT TOP (1) P.PoID
        FROM dbo.WH_PurchaseOrder P
        WHERE P.PoNumber='PPT-INBOUND'
          AND P.ItemNo COLLATE DATABASE_DEFAULT=B.ItemNo COLLATE DATABASE_DEFAULT
        ORDER BY P.PoLineNo,P.PoID
    ) P
    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID=D.DeliveryID AND DL.PoID=P.PoID
    JOIN dbo.MD_Item I
      ON I.ItemNo COLLATE DATABASE_DEFAULT=B.ItemNo COLLATE DATABASE_DEFAULT
) S ON T.DeliveryLineID=S.DeliveryLineID AND T.BoxSeq=S.BoxSeq AND T.ActiveFlag=1
WHEN MATCHED THEN UPDATE SET DeliveryLineID=S.DeliveryLineID,BoxSeq=S.BoxSeq,ItemNo=S.ItemNo,ItemName=S.ItemName,
    UnitCode='EA',Quantity=S.Qty,ActiveFlag=1,VoidedTS=NULL
WHEN NOT MATCHED THEN INSERT(DeliveryLineID,BoxSeq,ItemNo,ItemName,UnitCode,Quantity,ActiveFlag,CreatedTS)
    VALUES(S.DeliveryLineID,S.BoxSeq,S.ItemNo,S.ItemName,'EA',S.Qty,1,SYSDATETIME());

DECLARE @Lots table(LotNo varchar(40),ItemNo varchar(20),Qty decimal(14,3),LocationNo varchar(20),ReceivedAt datetime2,SeedGroup varchar(20));
INSERT @Lots VALUES
 ('5011LL260908800001','S82311-DW010',20,NULL,DATEADD(day,-1,SYSDATETIME()),'INBOUND'),
 ('5011LL260908800002','S82321-DW010',20,NULL,DATEADD(day,-1,SYSDATETIME()),'INBOUND'),
 ('5011LL260908800003','S82311-DW010',20,NULL,DATEADD(day,-1,SYSDATETIME()),'INBOUND'),
 ('CKD260908800000001','S82371-XA000',20,NULL,DATEADD(day,-1,SYSDATETIME()),'INBOUND'),
 ('CKD260908800000002','S82371-XA000',20,NULL,DATEADD(day,-1,SYSDATETIME()),'INBOUND'),
 ('CKD260908800000003','S82371-XA000',20,NULL,DATEADD(day,-1,SYSDATETIME()),'INBOUND'),
 ('5011LL260701000001','S82321-DW010',4,'B0-10-A1','2026-07-01T08:00:00','RELEASE'),
 ('5011LL260715000002','S82321-DW010',4,'B0-10-B1','2026-07-15T08:00:00','RELEASE'),
 ('5011LL260801000003','S82321-DW010',2,'B0-09-D2','2026-08-01T08:00:00','RELEASE'),
 ('5011LL260601000004','S82321-DW010',8,'B0-08-B1','2026-06-01T08:00:00','RELEASE'),
 ('5011LL260820000010','S82321-DW010',24,'B0-10-A1','2026-08-20T08:00:00','RELEASE'),
 ('5011LL260101000018','S82321-DW010',6,'B0-08-C1','2026-01-01T08:00:00','RELEASE'),
 ('5011LL260908810001','S82381-XA000',20,'B0-10-A1','2026-09-01T08:00:00','PPT'),
 ('5011LL260908810002','S82381-XA000',10,'B0-10-B1','2026-09-02T08:00:00','PPT'),
 ('5011LL260908810003','S83371-XA000',16,'B0-09-D2','2026-09-01T08:00:00','PPT'),
 ('5011LL260908810004','S83371-XA000',8,'B0-08-B1','2026-09-03T08:00:00','PPT'),
 ('5011LL260908850001','S83371-XA010',12,NULL,DATEADD(day,-1,SYSDATETIME()),'PPT'),
 ('5011LL260908850002','S83371-XA010',8,NULL,DATEADD(day,-1,SYSDATETIME()),'PPT'),
 ('5011LL260908850003','S83371-XA010',15,NULL,DATEADD(day,-1,SYSDATETIME()),'PPT'),
 ('5011LL260908850004','S83371-XA010',6,'B0-10-A1',DATEADD(day,-2,SYSDATETIME()),'PPT'),
 ('5011LL260908820001','S83381-XA000',30,'B0-10-A1','2026-09-01T08:00:00','PPT'),
 ('5011LL260908820002','S83381-XA000',20,'B0-10-B1','2026-09-02T08:00:00','PPT'),
 ('5011LL260908820003','S83381-XA010',12,'B0-09-D2','2026-09-01T08:00:00','PPT'),
 ('5011LL260908830001','M0230-P8000RBQ',10,'B0-12-B1','2026-09-01T08:00:00','PPT'),
 ('5011LL260908840001','M0240-P8000RBQ',18,'B0-08-C1','2026-09-01T08:00:00','PPT');
MERGE dbo.tbl_Lot T USING @Lots S
ON T.LotCode COLLATE DATABASE_DEFAULT=S.LotNo COLLATE DATABASE_DEFAULT
WHEN MATCHED THEN UPDATE SET ItemNo=S.ItemNo,BatchSize=S.Qty,RemainingQty=CASE WHEN S.SeedGroup='INBOUND' THEN S.Qty ELSE T.RemainingQty END,
    ProducedAt=S.ReceivedAt,Status=CASE WHEN S.SeedGroup='INBOUND' THEN 'Open' ELSE 'Received' END,
    InventoryStatus=CASE WHEN S.SeedGroup='INBOUND' THEN 'CREATED' ELSE 'STORED' END,
    CurrentLocationID=CASE WHEN S.SeedGroup='INBOUND' THEN NULL ELSE S.LocationNo END,ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(LotCode,ItemNo,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,InventoryStatus,QualityFlag,CurrentLocationID,CreatedBy,CreatedTS)
    VALUES(S.LotNo,S.ItemNo,CASE WHEN LEFT(S.LotNo,3)='CKD' THEN 'CKD' ELSE 'WH' END,S.Qty,S.Qty,S.ReceivedAt,
      CASE WHEN S.SeedGroup='INBOUND' THEN 'Open' ELSE 'Received' END,CASE WHEN S.SeedGroup='INBOUND' THEN 'CREATED' ELSE 'STORED' END,
      'PASS',CASE WHEN S.SeedGroup='INBOUND' THEN NULL ELSE S.LocationNo END,'pda-seed',SYSDATETIME());

MERGE dbo.WH_Inventory T
USING (SELECT S.*,I.ItemName FROM @Lots S JOIN dbo.MD_Item I
    ON I.ItemNo COLLATE DATABASE_DEFAULT=S.ItemNo COLLATE DATABASE_DEFAULT
    WHERE S.SeedGroup<>'INBOUND') X
ON T.LotNo=X.LotNo
WHEN MATCHED THEN UPDATE SET UnitType='PART',PartNo=X.ItemNo,PartName=X.ItemName,LocationNo=X.LocationNo,Qty=X.Qty,
    ReceivedAt=X.ReceivedAt,UpdatedAt=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
    VALUES(X.LotNo,'PART',X.ItemNo,X.ItemName,X.LocationNo,X.Qty,X.ReceivedAt,SYSDATETIME(),SYSDATETIME());

UPDATE dbo.WH_Inventory
SET DeliveryNoteNo=CASE WHEN LotNo='5011LL260908850004' THEN 'PPT-WH-NOTE-PUTAWAY-RELOCATE' ELSE 'PPT-WH-NOTE-PUTAWAY' END,
    LocationNo=CASE WHEN LotNo='5011LL260908850004' THEN 'B0-10-A1' ELSE NULL END,
    UpdatedAt=SYSDATETIME()
WHERE LotNo LIKE '5011LL26090885%';

UPDATE dbo.tbl_Lot
SET CurrentLocationID=CASE WHEN LotCode='5011LL260908850004' THEN 'B0-10-A1' ELSE NULL END,
    InventoryStatus=CASE WHEN LotCode='5011LL260908850004' THEN 'STORED' ELSE 'RECEIVED' END,
    ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHERE LotCode LIKE '5011LL26090885%';

MERGE dbo.WH_PickSlip T
USING (VALUES
 ('PDA-REL-TEST-02','S82321-DW010',1.000,1,'PDA TEST'),
 ('PDA-REL-ROLLBACK-01','S82321-DW010',1.000,1,'TEST'),
 ('PS-PPT-WH-01','S82381-XA000',2.000,1,'SCTEST1'),
 ('PS-PPT-WH-01','S83371-XA000',1.000,2,'SCTEST1')
) S(PickSlipNo,ItemNo,DemandQty,ReqSeqNo,ReqUserId)
ON T.PickSlipNo=S.PickSlipNo AND T.ItemNo=S.ItemNo
WHEN MATCHED THEN UPDATE SET DemandQty=S.DemandQty,ReqSeqNo=S.ReqSeqNo,ReqUserId=S.ReqUserId,
    Status=CASE WHEN UPPER(COALESCE(T.Status,'OPEN')) IN('CLOSED','RELEASED') THEN T.Status ELSE 'Open' END,
    ModifiedBy='pda-seed',ModifiedTS=SYSDATETIME()
WHEN NOT MATCHED THEN INSERT(WoID,ItemNo,DemandQty,PickedQty,RequiredAt,Priority,Status,CreatedBy,CreatedTS,PickSlipNo,ReqLocation,ReqSeqNo,ReqUserId)
    VALUES(NULL,S.ItemNo,S.DemandQty,0,SYSDATETIME(),2,'Open','pda-seed',SYSDATETIME(),S.PickSlipNo,'LINE-A',S.ReqSeqNo,S.ReqUserId);

IF OBJECT_ID(N'dbo.WH_PDA_PPT_TEST_RESET',N'P') IS NOT NULL
BEGIN
 EXEC dbo.WH_PDA_PPT_TEST_RESET 'release';
 EXEC dbo.WH_PDA_PPT_TEST_RESET 'inventory';
 EXEC dbo.WH_PDA_PPT_TEST_RESET 'adjust';
 EXEC dbo.WH_PDA_PPT_TEST_RESET 'history';
END;

SELECT 'WH_Inventory' TableName,COUNT(*) DataRows FROM dbo.WH_Inventory
UNION ALL SELECT 'WH_InventoryTransaction',COUNT(*) FROM dbo.WH_InventoryTransaction
UNION ALL SELECT 'WH_PurchaseOrder',COUNT(*) FROM dbo.WH_PurchaseOrder
UNION ALL SELECT 'WH_PickSlip',COUNT(*) FROM dbo.WH_PickSlip;
GO
-- FG demos retired: actual outbound is the only shipment writer.
-- =====================================================================
--  WH Inventory Threshold Demo
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.MD_Item
       SET MinStock = CASE ItemNo
            WHEN 'M0230-P8000RBQ' THEN 130
            WHEN 'M0230-P8010RBQ' THEN 90
            WHEN 'M0240-P8000RBQ' THEN 40
            WHEN 'M0240-P8010RBQ' THEN 5
            ELSE MinStock
           END,
           MaxStock = CASE ItemNo
            WHEN 'M0230-P8000RBQ' THEN 220
            WHEN 'M0230-P8010RBQ' THEN 180
            WHEN 'M0240-P8000RBQ' THEN 50
            WHEN 'M0240-P8010RBQ' THEN 40
            ELSE MaxStock
           END,
           ModifiedBy = N'pda-seed',
           ModifiedTS = SYSDATETIME()
     WHERE ItemNo IN ('M0230-P8000RBQ', 'M0230-P8010RBQ', 'M0240-P8000RBQ', 'M0240-P8010RBQ');
END;
GO

-- =====================================================================
--  WH Legacy Location Menu Cleanup
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.SYS_RolePermission', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.SYS_RolePermission
    WHERE ScreenCode = 'WH-001';
END;

IF OBJECT_ID(N'dbo.SYS_Screen', N'U') IS NOT NULL
BEGIN
    DECLARE @DeleteWh001Sql nvarchar(max) = N'
        DELETE FROM dbo.SYS_Screen
        WHERE ScreenCode = N''WH-001''
          AND ModuleCode = N''WEB''';
    IF COL_LENGTH(N'dbo.SYS_Screen', N'ProcessCode') IS NOT NULL
        SET @DeleteWh001Sql += N' AND ProcessCode = N''WH''';
    EXEC sys.sp_executesql @DeleteWh001Sql;
END;
GO

-- =====================================================================
--  Web WH/FG Screen Registration and Role Permissions
--  Keep after legacy WH-001 cleanup. Existing screen codes and permissions
--  are preserved; all WH/FG seed data is maintained in this file.
-- =====================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- â”€â”€ 1) SYS_Screen upsert â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
MERGE dbo.SYS_Screen AS tgt
USING (VALUES
  ('WH-01', 'WH', N'ìž¬ê³  ì¡°íšŒ',     N'Inventory Search',  'wh/inventory',         1, 1),
  ('WH-02', 'WH', N'ë¡œì¼€ì´ì…˜ ë§µ',   N'Location Map',      'wh/location-map',      2, 1),
  ('WH-03', 'WH', N'ìž¬ê³  ì´ë ¥',     N'Inventory History', 'wh/log-history',       3, 1),
  ('WH-04', 'WH', N'í”¼í‚¹ ì˜¤ë”',     N'Picking Orders',    'wh/picking-orders',    4, 1),
  ('WH-05', 'WH', N'ìž¬ê³  ì„¤ì •',     N'Inventory Setting', 'wh/inventory-setting', 5, 1),
  ('FG-01', 'FG', N'ê³ ê°ì‚¬ ë¦¬í„´',   N'Customer Returns',  'fg/customer-returns',  1, 1),
  ('FG-02', 'FG', N'ì¶œí•˜ ê³„íš',     N'Shipment Plan',     'fg/shipment-plan',     2, 1),
  ('FG-03', 'FG', N'ì¶œí•˜ ëª©ë¡',     N'Shipments',         'fg/shipments',         3, 1),
  ('FG-04', 'FG', N'ìž‘ì—… ì´ë ¥',     N'History',           'fg/history',           4, 1)
) AS src (ScreenCode, ProcessCode, ScreenName, ScreenNameEn, HRef, SortOrder, IsVisible)
ON tgt.ScreenCode = src.ScreenCode
WHEN NOT MATCHED THEN
    INSERT (ScreenCode, ModuleCode, ProcessCode, SubProcessCode, ScreenName, ScreenNameEn, HRef, LidLabel, SortOrder, IsVisible, CreatedBy, CreatedTS)
    VALUES (src.ScreenCode, 'WEB', src.ProcessCode, NULL, src.ScreenName, src.ScreenNameEn, src.HRef, src.ScreenCode, src.SortOrder, src.IsVisible, 'seed', SYSDATETIME())
WHEN MATCHED THEN
    UPDATE SET tgt.ModuleCode = 'WEB', tgt.ProcessCode = src.ProcessCode, tgt.ScreenName = src.ScreenName, tgt.ScreenNameEn = src.ScreenNameEn,
               tgt.HRef = src.HRef, tgt.LidLabel = src.ScreenCode, tgt.SortOrder = src.SortOrder, tgt.IsVisible = src.IsVisible,
               tgt.ModifiedBy = 'seed', tgt.ModifiedTS = SYSDATETIME();
PRINT CONCAT(N'âœ“ SYS_Screen WH/FG upsert: ', @@ROWCOUNT, N'í–‰');
GO

-- â”€â”€ 2) êµ¬ ì½”ë“œ ê¶Œí•œ í–‰ â†’ ìƒˆ ì½”ë“œë¡œ ì´ê´€ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
DECLARE @map TABLE (OldCode VARCHAR(20), NewCode VARCHAR(20));
INSERT INTO @map VALUES
    ('WH-006','WH-01'), ('WH-003','WH-02'), ('WH-004','WH-03'), ('WH-002','WH-04'), ('WH-005','WH-05'),
    ('FG-003','FG-01'), ('FG-004','FG-03'), ('FG-005','FG-04');

UPDATE p
   SET p.ScreenCode = m.NewCode, p.ModifiedBy = 'seed', p.ModifiedTS = SYSDATETIME()
  FROM dbo.SYS_RolePermission p
    JOIN @map m ON m.OldCode COLLATE DATABASE_DEFAULT = p.ScreenCode COLLATE DATABASE_DEFAULT
    WHERE NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission x
        WHERE x.RoleName COLLATE DATABASE_DEFAULT = p.RoleName COLLATE DATABASE_DEFAULT
          AND x.ScreenCode COLLATE DATABASE_DEFAULT = m.NewCode COLLATE DATABASE_DEFAULT);
PRINT CONCAT(N'âœ“ êµ¬ ì½”ë“œ ê¶Œí•œ ì´ê´€: ', @@ROWCOUNT, N'í–‰');

DELETE p FROM dbo.SYS_RolePermission p JOIN @map m
  ON m.OldCode COLLATE DATABASE_DEFAULT = p.ScreenCode COLLATE DATABASE_DEFAULT;
PRINT CONCAT(N'âœ“ êµ¬ ì½”ë“œ ê¶Œí•œ ìž”ì—¬ ì‚­ì œ: ', @@ROWCOUNT, N'í–‰');

DELETE s FROM dbo.SYS_Screen s JOIN @map m
  ON m.OldCode COLLATE DATABASE_DEFAULT = s.ScreenCode COLLATE DATABASE_DEFAULT;
PRINT CONCAT(N'âœ“ êµ¬ 3ìžë¦¬ í™”ë©´ ì‚­ì œ: ', @@ROWCOUNT, N'í–‰');
GO

-- â”€â”€ 3) Admin FULL ê¶Œí•œ (ì—†ëŠ” í™”ë©´ë§Œ) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
-- Admin = 시스템 역할 고정 ID(ROLE-SYSADMIN), 권한 행은 RoleID 로 찾는다(이름은 바뀔 수 있다)
INSERT INTO dbo.SYS_RolePermission
    (RoleID, RoleName, ModuleCode, ProcessCode, ScreenCode, PermissionLevel, IsSystemRole, EffectiveTS, CreatedBy, CreatedTS)
SELECT r.Id, r.Name, 'WEB', s.ProcessCode, s.ScreenCode, 'REA', 1, SYSDATETIME(), 'seed', SYSDATETIME()
  FROM dbo.SYS_Screen s
  JOIN dbo.AspNetRoles r ON r.Id = N'ROLE-SYSADMIN'
 WHERE s.ProcessCode IN ('WH','FG')
   AND NOT EXISTS (SELECT 1 FROM dbo.SYS_RolePermission p WHERE p.RoleID = r.Id AND p.ScreenCode = s.ScreenCode);
PRINT CONCAT(N'âœ“ Admin/WHÂ·FG REA ì¶”ê°€: ', @@ROWCOUNT, N'í–‰');
GO

SELECT ScreenCode, ProcessCode, HRef, ScreenName, SortOrder, IsVisible FROM dbo.SYS_Screen WHERE ProcessCode IN ('WH','FG') ORDER BY ProcessCode DESC, SortOrder;
SELECT ScreenCode, RoleName, PermissionLevel FROM dbo.SYS_RolePermission WHERE ScreenCode LIKE 'WH-%' OR ScreenCode LIKE 'FG-%' ORDER BY ScreenCode, RoleName;
GO

-- No FG shipment or return seeding. Use actual PDA barcode operations.
