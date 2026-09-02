-- =====================================================================
--  migrate_wh_area_layout.sql
--  Warehouse location master hierarchy and map layout
--
--  Logical hierarchy:
--    WH -> Area -> Zone -> Location
--
--  Existing physical source:
--    dbo.MD_Location.PlantCode = WH Code
--    dbo.MD_Location.ZoneCode = Area Code
--    dbo.MD_Location.LocationType = Zone Code
--
--  Apply:
--    sqlcmd -S .\SQLEXPRESS -d AMES_DEV -U ames_app -P !Dev2026 -C -i dist\migrate_wh_area_layout.sql
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaLayout (
        AREACD NVARCHAR(80) NOT NULL CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY,
        X_PCT DECIMAL(5,2) NOT NULL,
        Y_PCT DECIMAL(5,2) NOT NULL,
        W_PCT DECIMAL(5,2) NOT NULL,
        H_PCT DECIMAL(5,2) NOT NULL,
        MODIFIED_BY NVARCHAR(80) NULL,
        MODIFIED_TS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_LAYOUT_MODIFIED_TS DEFAULT SYSDATETIME()
    );

    PRINT 'Created dbo.WH_AreaLayout';
END
ELSE
BEGIN
    PRINT 'dbo.WH_AreaLayout already exists';
END;
GO

IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.WH_AreaLayout', N'AREACD') < 160
BEGIN
    DECLARE @pkName sysname;
    SELECT @pkName = kc.name
    FROM sys.key_constraints kc
    WHERE kc.parent_object_id = OBJECT_ID(N'dbo.WH_AreaLayout')
      AND kc.[type] = 'PK';

    IF @pkName IS NOT NULL
    BEGIN
        DECLARE @dropSql nvarchar(max) = N'ALTER TABLE dbo.WH_AreaLayout DROP CONSTRAINT ' + QUOTENAME(@pkName);
        EXEC sys.sp_executesql @dropSql;
    END;

    ALTER TABLE dbo.WH_AreaLayout ALTER COLUMN AREACD NVARCHAR(80) NOT NULL;
    ALTER TABLE dbo.WH_AreaLayout ADD CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY (AREACD);

    PRINT 'Expanded dbo.WH_AreaLayout.AREACD to NVARCHAR(80)';
END;
GO

IF OBJECT_ID(N'dbo.WH_WarehouseMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_WarehouseMaster (
        WhCode VARCHAR(20) NOT NULL CONSTRAINT PK_WH_WAREHOUSE_MASTER PRIMARY KEY,
        WhName NVARCHAR(120) NULL,
        ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_WAREHOUSE_MASTER_ACTIVE DEFAULT 1,
        CreatedBy NVARCHAR(80) NULL,
        CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_WAREHOUSE_MASTER_CREATED_TS DEFAULT SYSDATETIME(),
        ModifiedBy NVARCHAR(80) NULL,
        ModifiedTS DATETIME2 NULL
    );

    PRINT 'Created dbo.WH_WarehouseMaster';
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    MERGE dbo.WH_WarehouseMaster AS tgt
    USING (
        SELECT DISTINCT CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode
        FROM dbo.MD_Location
        WHERE COALESCE(NULLIF(PlantCode, ''), 'WH') IS NOT NULL
    ) AS src ON tgt.WhCode = src.WhCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, WhName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.WhCode, 1, 'system', SYSDATETIME());
END;
GO

IF OBJECT_ID(N'dbo.WH_AreaMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaMaster (
        WhCode VARCHAR(20) NULL,
        AreaCode VARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_MASTER PRIMARY KEY,
        AreaName NVARCHAR(120) NULL,
        ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_AREA_MASTER_ACTIVE DEFAULT 1,
        CreatedBy NVARCHAR(80) NULL,
        CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_MASTER_CREATED_TS DEFAULT SYSDATETIME(),
        ModifiedBy NVARCHAR(80) NULL,
        ModifiedTS DATETIME2 NULL
    );

    PRINT 'Created dbo.WH_AreaMaster';
END;
GO

IF COL_LENGTH(N'dbo.WH_AreaMaster', N'WhCode') IS NULL
BEGIN
    ALTER TABLE dbo.WH_AreaMaster ADD WhCode VARCHAR(20) NULL;
    PRINT 'Added dbo.WH_AreaMaster.WhCode';
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    UPDATE A
       SET WhCode = COALESCE(NULLIF(A.WhCode, ''), X.WhCode, A.AreaCode)
    FROM dbo.WH_AreaMaster A
    OUTER APPLY (
        SELECT TOP (1) L.PlantCode AS WhCode
        FROM dbo.MD_Location L
        WHERE COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = A.AreaCode
          AND NULLIF(L.PlantCode, '') IS NOT NULL
        ORDER BY L.PlantCode
    ) X
    WHERE NULLIF(A.WhCode, '') IS NULL;

    MERGE dbo.WH_AreaMaster AS tgt
    USING (
        SELECT DISTINCT
            CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode,
            CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)) AS AreaCode
        FROM dbo.MD_Location
        WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') IS NOT NULL
    ) AS src ON tgt.AreaCode = src.AreaCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, AreaCode, AreaName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.AreaCode, src.AreaCode, 1, 'system', SYSDATETIME());
END;
GO

IF OBJECT_ID(N'dbo.WH_AreaSection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaSection (
        WhCode VARCHAR(20) NULL,
        AreaCode VARCHAR(20) NOT NULL,
        SectionCode VARCHAR(20) NOT NULL,
        SectionName NVARCHAR(120) NULL,
        ActiveFlag BIT NOT NULL CONSTRAINT DF_WH_AREA_SECTION_ACTIVE DEFAULT 1,
        CreatedBy NVARCHAR(80) NULL,
        CreatedTS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_SECTION_CREATED_TS DEFAULT SYSDATETIME(),
        ModifiedBy NVARCHAR(80) NULL,
        ModifiedTS DATETIME2 NULL,
        CONSTRAINT PK_WH_AREA_SECTION PRIMARY KEY (AreaCode, SectionCode)
    );

    PRINT 'Created dbo.WH_AreaSection';
END;
GO

IF COL_LENGTH(N'dbo.WH_AreaSection', N'WhCode') IS NULL
BEGIN
    ALTER TABLE dbo.WH_AreaSection ADD WhCode VARCHAR(20) NULL;
    PRINT 'Added dbo.WH_AreaSection.WhCode';
END;
GO

IF OBJECT_ID(N'dbo.MD_Location', N'U') IS NOT NULL
BEGIN
    UPDATE S
       SET WhCode = COALESCE(NULLIF(S.WhCode, ''), A.WhCode, X.WhCode)
    FROM dbo.WH_AreaSection S
    LEFT JOIN dbo.WH_AreaMaster A
           ON A.AreaCode = S.AreaCode
    OUTER APPLY (
        SELECT TOP (1) L.PlantCode AS WhCode
        FROM dbo.MD_Location L
        WHERE COALESCE(NULLIF(L.ZoneCode, ''), L.PlantCode) = S.AreaCode
          AND COALESCE(NULLIF(L.LocationType, ''), 'DEFAULT') = S.SectionCode
          AND NULLIF(L.PlantCode, '') IS NOT NULL
        ORDER BY L.PlantCode
    ) X
    WHERE NULLIF(S.WhCode, '') IS NULL;

    MERGE dbo.WH_AreaSection AS tgt
    USING (
        SELECT DISTINCT
            CAST(COALESCE(NULLIF(PlantCode, ''), 'WH') AS varchar(20)) AS WhCode,
            CAST(COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') AS varchar(20)) AS AreaCode,
            CAST(COALESCE(NULLIF(LocationType, ''), 'DEFAULT') AS varchar(20)) AS SectionCode
        FROM dbo.MD_Location
        WHERE COALESCE(NULLIF(ZoneCode, ''), PlantCode, 'WH') IS NOT NULL
    ) AS src
       ON tgt.AreaCode = src.AreaCode
      AND tgt.SectionCode = src.SectionCode
    WHEN NOT MATCHED THEN INSERT
        (WhCode, AreaCode, SectionCode, SectionName, ActiveFlag, CreatedBy, CreatedTS)
    VALUES
        (src.WhCode, src.AreaCode, src.SectionCode, src.SectionCode, 1, 'system', SYSDATETIME());
END;
GO

PRINT 'Warehouse hierarchy migration completed';
