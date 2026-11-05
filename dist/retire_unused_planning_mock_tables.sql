-- Exact 132/AMES_DEV cleanup only. Run a schema+data backup first; no rebuild is needed.
-- Default is preview. Applying also requires the verified backup manifest and directory.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 10000;
DECLARE @Apply bit=0;
DECLARE @BackupManifest nvarchar(max)=N'[]';
DECLARE @BackupDirectory nvarchar(260)=N'';
IF CONVERT(nvarchar(128),SERVERPROPERTY('ServerName'))<>N'DESKTOP-POEDIBS' OR DB_NAME()<>N'AMES_DEV'
    THROW 52091,'Select DESKTOP-POEDIBS / AMES_DEV explicitly. Nothing changed.',1;

DECLARE @Targets TABLE(Name sysname PRIMARY KEY);
INSERT @Targets VALUES(N'WH_ReleaseSchedule'),(N'PP_MaterialReservation'),
    (N'TEST_SRM_PurchaseOrderResponse'),(N'TEST_SRM_Shipment'),(N'TEST_SRM_ShipmentItem');
IF @Apply=0
BEGIN
    SELECT x.Name,CASE WHEN t.object_id IS NULL THEN N'ABSENT' ELSE N'PRESENT' END ObjectState,
        (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id=t.object_id AND p.index_id IN(0,1)) AS [Rows]
    FROM @Targets x LEFT JOIN sys.tables t ON t.name=x.Name AND t.schema_id=SCHEMA_ID(N'dbo');
    RETURN;
END;
IF ISJSON(@BackupManifest)<>1 OR NULLIF(@BackupDirectory,N'') IS NULL
    THROW 52091,'A verified schema/data backup manifest and directory are required.',1;
DECLARE @Backup TABLE(Name sysname PRIMARY KEY,ExpectedRows bigint NOT NULL);
INSERT @Backup SELECT Name,ExpectedRows FROM OPENJSON(@BackupManifest)
    WITH(Name sysname '$.Table',ExpectedRows bigint '$.Rows');
IF EXISTS(SELECT Name FROM @Targets EXCEPT SELECT Name FROM @Backup)
    OR EXISTS(SELECT Name FROM @Backup EXCEPT SELECT Name FROM @Targets)
    THROW 52091,'The backup manifest must contain exactly the five approved tables.',1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.WH_PickSlip',N'U') IS NULL
        THROW 52091,'The current WH_PickSlip table is missing.',1;
    IF EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d JOIN @Targets t ON d.referenced_id=OBJECT_ID(N'dbo.'+t.Name)
        WHERE NOT EXISTS(SELECT 1 FROM @Targets p WHERE OBJECT_ID(N'dbo.'+p.Name)=d.referencing_id)
          AND NOT EXISTS(SELECT 1 FROM sys.objects o JOIN @Targets p ON o.parent_object_id=OBJECT_ID(N'dbo.'+p.Name)
              WHERE o.object_id=d.referencing_id))
        OR EXISTS(SELECT 1 FROM sys.sql_modules m JOIN @Targets t ON CHARINDEX(t.Name,m.definition)>0)
        OR EXISTS(SELECT 1 FROM sys.synonyms s JOIN @Targets t ON CHARINDEX(t.Name,s.base_object_name)>0)
        THROW 52091,'A database module or synonym still refers to an approved table.',1;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys f JOIN @Targets t ON OBJECT_ID(N'dbo.'+t.Name)=f.referenced_object_id
        WHERE NOT EXISTS(SELECT 1 FROM @Targets p WHERE OBJECT_ID(N'dbo.'+p.Name)=f.parent_object_id))
        THROW 52091,'An external foreign key still refers to an approved table.',1;

    DECLARE @SourceBefore nvarchar(max)=(SELECT CodeID,UseFlag FROM dbo.MD_CodeItem WITH(UPDLOCK,HOLDLOCK)
        WHERE GroupCode='SW_POSYNC_SOURCE' AND CodeValue='SEMS' FOR JSON PATH);
    IF EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='SW_POSYNC_SOURCE' AND CodeValue='SEMS' AND UseFlag=1)
        AND NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='SW_POSYNC_URL' AND CodeValue='SEMS'
            AND Description LIKE 'http://192.168.1.68:5220/%')
        THROW 52091,'SEMS no longer targets the approved old mock endpoint.',1;
    DECLARE @Actor varchar(20)=LEFT(ORIGINAL_LOGIN(),20);
    UPDATE dbo.MD_CodeItem SET UseFlag=0,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
    WHERE GroupCode='SW_POSYNC_SOURCE' AND CodeValue='SEMS' AND UseFlag=1;
    DECLARE @SourceChanges int=@@ROWCOUNT;

    DECLARE @Name sysname,@Rows bigint,@Expected bigint,@Sql nvarchar(max);
    DECLARE targets_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT Name FROM @Targets ORDER BY Name;
    OPEN targets_cursor;
    FETCH NEXT FROM targets_cursor INTO @Name;
    WHILE @@FETCH_STATUS=0
    BEGIN
        IF OBJECT_ID(N'dbo.'+@Name,N'U') IS NOT NULL
        BEGIN
            SET @Rows=NULL;
            SET @Sql=N'SELECT @Count=COUNT_BIG(*) FROM dbo.'+QUOTENAME(@Name)+N' WITH(TABLOCKX,HOLDLOCK);';
            EXEC sys.sp_executesql @Sql,N'@Count bigint OUTPUT',@Rows OUTPUT;
            SELECT @Expected=ExpectedRows FROM @Backup WHERE Name=@Name;
            IF @Rows<>@Expected OR (@Name IN(N'WH_ReleaseSchedule',N'PP_MaterialReservation') AND @Rows<>0)
                THROW 52091,'Table contents changed after backup, or an empty-only table has rows.',1;
        END;
        FETCH NEXT FROM targets_cursor INTO @Name;
    END;
    CLOSE targets_cursor;
    DEALLOCATE targets_cursor;

    DROP TABLE IF EXISTS dbo.TEST_SRM_ShipmentItem;
    DROP TABLE IF EXISTS dbo.TEST_SRM_Shipment;
    DROP TABLE IF EXISTS dbo.TEST_SRM_PurchaseOrderResponse;
    DROP TABLE IF EXISTS dbo.WH_ReleaseSchedule;
    DROP TABLE IF EXISTS dbo.PP_MaterialReservation;
    INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ActionType,TargetEntity,TargetID,
        BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
    VALUES(SYSDATETIME(),@Actor,'SYS','RETIRE','DatabaseTables','legacy-mock-tables',
        @BackupManifest,N'{"retiredTables":5}',N'OK',@BackupDirectory,@Actor,SYSDATETIME());
    IF @SourceChanges>0
        INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ActionType,TargetEntity,TargetID,
            BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
        VALUES(SYSDATETIME(),@Actor,'SYS','UPDATE','MD_CodeItem','SW_POSYNC_SOURCE/SEMS',
            @SourceBefore,N'{"UseFlag":false}',N'OK',N'Approved mock integration retirement',@Actor,SYSDATETIME());
    COMMIT TRANSACTION;
    SELECT N'Five approved tables retired; mock source disabled. Inventory and supply plans unchanged.' AS Result;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local','targets_cursor')>=0 CLOSE targets_cursor;
    IF CURSOR_STATUS('local','targets_cursor')>=-1 DEALLOCATE targets_cursor;
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
