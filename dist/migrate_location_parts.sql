-- Multiple assigned parts and a separate minimum per detail location/part.
-- Keeps legacy columns and copies existing assignments; does not change stock.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MD_LocationPart',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MD_LocationPart
    (
        LocationID varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        PartNo varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        MinimumQty decimal(18,3) NOT NULL CONSTRAINT DF_MD_LocationPart_MinimumQty DEFAULT(0),
        CreatedBy varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_MD_LocationPart_CreatedTS DEFAULT(SYSDATETIME()),
        ModifiedBy varchar(20) COLLATE DATABASE_DEFAULT NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT PK_MD_LocationPart PRIMARY KEY(LocationID,PartNo),
        CONSTRAINT CK_MD_LocationPart_MinimumQty CHECK(MinimumQty>=0 AND MinimumQty<=99999999999)
    );
END;
INSERT dbo.MD_LocationPart(LocationID,PartNo,MinimumQty,CreatedBy)
        SELECT L.LocationID,L.PartNo,COALESCE(L.MinimumQty,0),'migration'
        FROM dbo.MD_Location L
        WHERE L.LocationLevel=2 AND NULLIF(L.PartNo,'') IS NOT NULL
          AND NOT EXISTS(SELECT 1 FROM dbo.MD_LocationPart A WHERE A.LocationID=L.LocationID AND A.PartNo=L.PartNo);
COMMIT TRANSACTION;
