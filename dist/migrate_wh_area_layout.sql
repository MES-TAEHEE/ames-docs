-- =====================================================================
--  migrate_wh_area_layout.sql
--  Warehouse Location Map area layout coordinates
--
--  The location master remains SIS_TEST.WMS1040. This table stores only
--  the web map position of each area so Location Map can render a factory
--  top-view layout.
--
--  Apply:
--    sqlcmd -S .\SQLEXPRESS -d AMES_DEV -U ames_app -P !Dev2026 -i dist\migrate_wh_area_layout.sql
-- =====================================================================
SET NOCOUNT ON;

IF OBJECT_ID(N'SIS_TEST.WH_AREA_LAYOUT', N'U') IS NULL
BEGIN
    CREATE TABLE SIS_TEST.WH_AREA_LAYOUT (
        AREACD NVARCHAR(20) NOT NULL CONSTRAINT PK_WH_AREA_LAYOUT PRIMARY KEY,
        X_PCT DECIMAL(5,2) NOT NULL,
        Y_PCT DECIMAL(5,2) NOT NULL,
        W_PCT DECIMAL(5,2) NOT NULL,
        H_PCT DECIMAL(5,2) NOT NULL,
        MODIFIED_BY NVARCHAR(80) NULL,
        MODIFIED_TS DATETIME2 NOT NULL CONSTRAINT DF_WH_AREA_LAYOUT_MODIFIED_TS DEFAULT SYSDATETIME()
    );

    PRINT 'Created SIS_TEST.WH_AREA_LAYOUT';
END
ELSE
BEGIN
    PRINT 'SIS_TEST.WH_AREA_LAYOUT already exists';
END;
