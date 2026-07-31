-- =====================================================================
--  migrate_wh_area_layout.sql
--  Warehouse Location Map area layout coordinates
--
--  The location master is dbo.MD_Location. This table stores only
--  the web map position of each area so Location Map can render a factory
--  top-view layout.
--
--  Apply:
--    sqlcmd -S .\SQLEXPRESS -d AMES_DEV -U ames_app -P !Dev2026 -i dist\migrate_wh_area_layout.sql
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.WH_AreaLayout', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WH_AreaLayout (
        AREACD NVARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY,
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
