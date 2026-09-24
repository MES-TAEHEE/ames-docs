SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.SCM_Delivery','ShipDate') IS NULL
 ALTER TABLE dbo.SCM_Delivery ADD ShipDate date NULL, ShippedAt datetime2(7) NULL,
 ShippedBy varchar(20) NULL, ShippedUserID nvarchar(450) NULL;
DECLARE @Constraint sysname;
DECLARE status_checks CURSOR LOCAL FAST_FORWARD FOR
 SELECT name FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.SCM_Delivery') AND definition LIKE '%Status%';
OPEN status_checks;
FETCH NEXT FROM status_checks INTO @Constraint;
WHILE @@FETCH_STATUS=0
BEGIN
 EXEC('ALTER TABLE dbo.SCM_Delivery DROP CONSTRAINT '+QUOTENAME(@Constraint));
 FETCH NEXT FROM status_checks INTO @Constraint;
END;
CLOSE status_checks;
DEALLOCATE status_checks;
ALTER TABLE dbo.SCM_Delivery WITH CHECK ADD CONSTRAINT CK_SCM_Delivery_Status
 CHECK(Status IN ('Registered','Shipped','Received','Cancelled'));
COMMIT;
