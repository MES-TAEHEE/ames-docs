SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH('dbo.MD_Item','BoxContentUnit') IS NULL
        ALTER TABLE dbo.MD_Item ADD BoxContentUnit varchar(10) NULL;
    IF COL_LENGTH('dbo.MD_Item','BoxContentQty') IS NULL
        ALTER TABLE dbo.MD_Item ADD BoxContentQty decimal(18,3) NULL;

    -- Preserve existing BOX PO settings when every PO for the item agrees.
    IF COL_LENGTH('dbo.WH_PurchaseOrder','BoxContentUnit') IS NOT NULL
       AND COL_LENGTH('dbo.WH_PurchaseOrder','BoxContentQty') IS NOT NULL
    BEGIN
        EXEC(N'
            UPDATE i SET BoxContentUnit=x.BoxContentUnit,BoxContentQty=x.BoxContentQty
            FROM dbo.MD_Item i
            JOIN (SELECT ItemNo,MIN(BoxContentUnit) BoxContentUnit,MIN(BoxContentQty) BoxContentQty
                  FROM dbo.WH_PurchaseOrder
                  WHERE UnitCode=''BOX'' AND BoxContentUnit IS NOT NULL AND BoxContentQty>0
                  GROUP BY ItemNo
                  HAVING COUNT(DISTINCT CONCAT(BoxContentUnit,'':'',CONVERT(varchar(40),BoxContentQty)))=1) x ON x.ItemNo=i.ItemNo
            WHERE i.DefaultUOM=''BOX'' AND i.BoxContentUnit IS NULL AND i.BoxContentQty IS NULL;
        ');
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
