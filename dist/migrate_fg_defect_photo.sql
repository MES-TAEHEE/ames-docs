-- PDA FG defect evidence. No inventory or production data is changed by this migration.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.FG_DefectPhoto',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_DefectPhoto(
        DefectID int NOT NULL CONSTRAINT PK_FG_DefectPhoto PRIMARY KEY,
        RequestID uniqueidentifier NOT NULL CONSTRAINT UX_FG_DefectPhoto_Request UNIQUE,
        ContentType varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        Photo varbinary(max) NOT NULL,
        CreatedBy varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
        CreatedTS datetime2 NOT NULL CONSTRAINT DF_FG_DefectPhoto_CreatedTS DEFAULT SYSDATETIME(),
        ModifiedBy varchar(20) COLLATE DATABASE_DEFAULT NULL,
        ModifiedTS datetime2 NULL,
        CONSTRAINT CK_FG_DefectPhoto_Type CHECK(ContentType IN('image/jpeg','image/png')),
        CONSTRAINT CK_FG_DefectPhoto_Size CHECK(DATALENGTH(Photo) BETWEEN 20 AND 3145728)
    );
END;
GO
