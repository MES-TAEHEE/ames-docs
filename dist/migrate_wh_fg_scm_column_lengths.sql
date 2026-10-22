-- WH / FG / SCM identifier lengths. Tablet reads these same WH/MD keys.
-- Run against the selected database; never recreates a database or deletes rows.
-- Oversized values are rejected. NULL clearing, if approved, is a separate operation.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
BEGIN TRY
BEGIN TRANSACTION;
CREATE TABLE #Targets(TableName sysname COLLATE DATABASE_DEFAULT,ColumnName sysname COLLATE DATABASE_DEFAULT,Width int, PRIMARY KEY(TableName,ColumnName)); -- tempdb collation may differ from the database (Docker replica)
INSERT #Targets VALUES
('WH_Inventory','PartNo',20),
('WH_Inventory','LocationNo',20),
('WH_Inventory','CaseNo',50),
('WH_Inventory','DeliveryNoteNo',30),
('WH_InventoryTransaction','PartNo',20),
('WH_InventoryTransaction','LocationNo',20),
('WH_InventoryTransaction','OperatorID',20),
('WH_InventoryTransaction','ApproverID',20),
('WH_PickSlip','ReqLocation',20),
('WH_PickSlip','ReqUserId',20),
('WH_PickSlip','CloseUserId',20),
('WH_ReleaseSchedule','ReqLocation',20),
('WH_ReleaseSchedule','ReqUserId',20),
('WH_ReleaseSchedule','CloseUserId',20),
('WH_ReleaseSchedule','CreatedBy',20),
('WH_ReleaseSchedule','ModifiedBy',20),
('FG_Shipment','ShippedBy',20),
('FG_CustomerReturn','ReceivedBy',20),
('FG_CustomerReturn','ClosedBy',20),
('SCM_DeliveryCase','CreatedBy',20),
('SCM_DeliveryCase','PoNumber',20),
('SCM_DeliveryBox','UnitCode',10);
DELETE T FROM #Targets T JOIN sys.columns C ON C.object_id=OBJECT_ID(N'dbo.'+T.TableName) AND C.name=T.ColumnName
WHERE TYPE_NAME(C.user_type_id)='varchar' AND C.max_length=T.Width AND C.collation_name='Korean_Wansung_CI_AS';
DECLARE @Table sysname,@Column sysname,@Width int,@Sql nvarchar(max),@Null nvarchar(10);
DECLARE targets CURSOR LOCAL FAST_FORWARD FOR
SELECT T.TableName,T.ColumnName,T.Width,CASE WHEN C.is_nullable=1 THEN N' NULL' ELSE N' NOT NULL' END
FROM #Targets T JOIN sys.columns C ON C.object_id=OBJECT_ID(N'dbo.'+T.TableName) AND C.name=T.ColumnName;
OPEN targets;
FETCH NEXT FROM targets INTO @Table,@Column,@Width,@Null;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @Sql=N'IF EXISTS (SELECT 1 FROM dbo.'+QUOTENAME(@Table)+N' WITH(TABLOCKX,HOLDLOCK) WHERE DATALENGTH(CONVERT(varchar(max),'+QUOTENAME(@Column)+N'))>'+CONVERT(varchar(10),@Width)+N'
        OR CONVERT(varbinary(max),CONVERT(nvarchar(max),'+QUOTENAME(@Column)+N'))<>CONVERT(varbinary(max),CONVERT(nvarchar(max),CONVERT(varchar(max),'+QUOTENAME(@Column)+N'))))
        THROW 52100,''Length or character conversion would lose data: '+@Table+N'.'+@Column+N'. Nothing changed.'',1;';
    EXEC sys.sp_executesql @Sql;
    FETCH NEXT FROM targets INTO @Table,@Column,@Width,@Null;
END;
CLOSE targets;
-- Preserve all affected nonclustered indexes; reject unusual dependencies rather than dropping them.
IF EXISTS(SELECT 1 FROM sys.indexes I JOIN sys.index_columns IC ON IC.object_id=I.object_id AND IC.index_id=I.index_id
    JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
    JOIN #Targets T ON OBJECT_ID(N'dbo.'+T.TableName)=C.object_id AND T.ColumnName=C.name
    WHERE I.is_primary_key=1 OR I.is_unique_constraint=1 OR I.type<>2)
    THROW 52101,'A target column has an unsupported constraint/index. Nothing changed.',1;
CREATE TABLE #Indexes(DropSql nvarchar(max),CreateSql nvarchar(max),DisableSql nvarchar(max));
INSERT #Indexes
SELECT N'DROP INDEX '+QUOTENAME(I.name)+N' ON dbo.'+QUOTENAME(OBJECT_NAME(I.object_id))+N';',
    N'CREATE '+CASE WHEN I.is_unique=1 THEN N'UNIQUE ' ELSE N'' END+N'NONCLUSTERED INDEX '+QUOTENAME(I.name)+N' ON dbo.'+QUOTENAME(OBJECT_NAME(I.object_id))+N' ('+K.Keys+N')'
    +CASE WHEN Inc.Columns IS NOT NULL THEN N' INCLUDE ('+Inc.Columns+N')' ELSE N'' END
    +CASE WHEN I.has_filter=1 THEN N' WHERE '+I.filter_definition ELSE N'' END
    +N' WITH ('+CASE WHEN I.fill_factor>0 THEN N'FILLFACTOR='+CONVERT(varchar(3),I.fill_factor)+N', ' ELSE N'' END
    +N'ALLOW_ROW_LOCKS='+CASE WHEN I.allow_row_locks=1 THEN N'ON' ELSE N'OFF' END
    +N', ALLOW_PAGE_LOCKS='+CASE WHEN I.allow_page_locks=1 THEN N'ON' ELSE N'OFF' END+N') ON '+QUOTENAME(DS.name)+N';',
    CASE WHEN I.is_disabled=1 THEN N'ALTER INDEX '+QUOTENAME(I.name)+N' ON dbo.'+QUOTENAME(OBJECT_NAME(I.object_id))+N' DISABLE;' END
FROM sys.indexes I JOIN sys.data_spaces DS ON DS.data_space_id=I.data_space_id
CROSS APPLY(SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)+CASE WHEN IC.is_descending_key=1 THEN N' DESC' ELSE N' ASC' END),N',') WITHIN GROUP(ORDER BY IC.key_ordinal) Keys
    FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
    WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id AND IC.key_ordinal>0) K
OUTER APPLY(SELECT STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(C.name)),N',') WITHIN GROUP(ORDER BY IC.index_column_id) Columns
    FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
    WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id AND IC.is_included_column=1) Inc
WHERE EXISTS(SELECT 1 FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
    JOIN #Targets T ON C.object_id=OBJECT_ID(N'dbo.'+T.TableName) AND C.name=T.ColumnName
    WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id);
IF EXISTS(SELECT 1 FROM sys.indexes I JOIN sys.data_spaces D ON D.data_space_id=I.data_space_id WHERE D.type<>'FG'
    AND EXISTS(SELECT 1 FROM sys.index_columns IC JOIN sys.columns C ON C.object_id=IC.object_id AND C.column_id=IC.column_id
    JOIN #Targets T ON C.object_id=OBJECT_ID(N'dbo.'+T.TableName) AND C.name=T.ColumnName WHERE IC.object_id=I.object_id AND IC.index_id=I.index_id))
    THROW 52102,'Partitioned target index requires a separate migration. Nothing changed.',1;
DECLARE indexes CURSOR LOCAL FAST_FORWARD FOR SELECT DropSql FROM #Indexes;
OPEN indexes; FETCH NEXT FROM indexes INTO @Sql;
WHILE @@FETCH_STATUS=0 BEGIN EXEC sys.sp_executesql @Sql; FETCH NEXT FROM indexes INTO @Sql; END;
CLOSE indexes; DEALLOCATE indexes;
OPEN targets; FETCH NEXT FROM targets INTO @Table,@Column,@Width,@Null;
WHILE @@FETCH_STATUS=0
BEGIN
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Table)+N' ALTER COLUMN '+QUOTENAME(@Column)+N' varchar('+CONVERT(varchar(10),@Width)+N') COLLATE Korean_Wansung_CI_AS'+@Null+N';';
    EXEC sys.sp_executesql @Sql;
    FETCH NEXT FROM targets INTO @Table,@Column,@Width,@Null;
END;
CLOSE targets; DEALLOCATE targets;
DECLARE rebuild_indexes CURSOR LOCAL FAST_FORWARD FOR SELECT CreateSql+COALESCE(DisableSql,N'') FROM #Indexes;
OPEN rebuild_indexes; FETCH NEXT FROM rebuild_indexes INTO @Sql;
WHILE @@FETCH_STATUS=0 BEGIN EXEC sys.sp_executesql @Sql; FETCH NEXT FROM rebuild_indexes INTO @Sql; END;
CLOSE rebuild_indexes; DEALLOCATE rebuild_indexes;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
