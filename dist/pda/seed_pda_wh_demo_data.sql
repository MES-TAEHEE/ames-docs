-- =====================================================================
--  seed_pda_wh_demo_data.sql
--  PDA Warehouse demo data
--
--  Uses existing AMES Warehouse tables with dbo.WH_* names.
--
--  Apply:
--    sqlcmd -S localhost,11433 -U ames_app -P "!Dev2026" -C -d AMES_DEV -i dist\pda\seed_pda_wh_demo_data.sql
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.MD_Item', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'TST-1221')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('TST-1221', N'WH01 AUTO SYNC TEST PART', 'RM', 'Warehouse Demo', 'MV1', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'MAT-001')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('MAT-001', N'SW ASSY-RR HTR LH', 'RM', 'Warehouse Demo', 'MV1A', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'MAT-002')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('MAT-002', N'ARMREST GARNISH-RR DR LH', 'RM', 'Warehouse Demo', 'LQ2', 'EA', 1, 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = 'MAT-003')
        INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ItemCategory, CarType, DefaultUOM, ActiveFlag, CreatedBy)
        VALUES ('MAT-003', N'COVER BLANKING', 'RM', 'Warehouse Demo', 'MQ4A', 'EA', 1, 'pda-seed');
END;

IF OBJECT_ID(N'dbo.MD_Vendor', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MD_Vendor WHERE VendorID = 'V-PDA')
        INSERT INTO dbo.MD_Vendor (VendorID, VendorName, VendorType, VendorCategory, ActiveFlag, CreatedBy)
        VALUES ('V-PDA', N'PDA Demo Supplier', 'LOCAL', N'Demo', 1, 'pda-seed');
END;

IF OBJECT_ID(N'dbo.WH_PurchaseOrder', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'PO2607001' AND PoLineNo = 10)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('PO2607001', 10, 'V-PDA', 'MAT-001', 540, 468, 'EA',
             CONVERT(date, DATEADD(day, -6, GETDATE())),
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             'Open', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'PO2607002' AND PoLineNo = 20)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('PO2607002', 20, 'V-PDA', 'MAT-002', 900, 900, 'EA',
             CONVERT(date, DATEADD(day, -5, GETDATE())),
             CONVERT(date, GETDATE()),
             'Complete', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'PO2607003' AND PoLineNo = 30)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('PO2607003', 30, 'V-PDA', 'MAT-003', 1200, 200, 'EA',
             CONVERT(date, DATEADD(day, -2, GETDATE())),
             CONVERT(date, DATEADD(day, 2, GETDATE())),
             'Open', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_PurchaseOrder WHERE PoNumber = 'T07061221' AND PoLineNo = 10)
        INSERT INTO dbo.WH_PurchaseOrder
            (PoNumber, PoLineNo, VendorID, ItemNo, OrderQty, ReceivedQty, UnitCode, OrderDate, DueDate, Status, CreatedBy)
        VALUES
            ('T07061221', 10, 'V-PDA', 'TST-1221', 1440, 0, 'EA',
             CONVERT(date, DATEADD(day, -1, GETDATE())),
             CONVERT(date, DATEADD(day, 3, GETDATE())),
             'Open', 'pda-seed');
END;

IF OBJECT_ID(N'dbo.WH_ReleaseSchedule', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ItemNo = 'MAT-001' AND CreatedBy = 'pda-seed')
        INSERT INTO dbo.WH_ReleaseSchedule (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy)
        VALUES ('MAT-001', 120, 0, DATEADD(day, 1, SYSDATETIME()), 1, 'Open', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ItemNo = 'MAT-002' AND CreatedBy = 'pda-seed')
        INSERT INTO dbo.WH_ReleaseSchedule (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy)
        VALUES ('MAT-002', 80, 40, DATEADD(day, 2, SYSDATETIME()), 2, 'Partial', 'pda-seed');

    IF NOT EXISTS (SELECT 1 FROM dbo.WH_ReleaseSchedule WHERE ItemNo = 'MAT-003' AND CreatedBy = 'pda-seed')
        INSERT INTO dbo.WH_ReleaseSchedule (ItemNo, DemandQty, PickedQty, RequiredAt, Priority, Status, CreatedBy)
        VALUES ('MAT-003', 60, 0, DATEADD(day, -1, SYSDATETIME()), 1, 'Open', 'pda-seed');
END;

SELECT 'WH_PurchaseOrder' AS TableName, COUNT(*) AS DataRows FROM dbo.WH_PurchaseOrder
UNION ALL
SELECT 'WH_ReleaseSchedule', COUNT(*) FROM dbo.WH_ReleaseSchedule;
