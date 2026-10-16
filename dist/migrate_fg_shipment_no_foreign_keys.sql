-- Removes only the two shipment FKs added contrary to the no-new-FK policy.
-- No table/column/row removal. Existing database application requires operator approval.
-- Deploy the API with PdaFinishedGoodsSql customer/latest-shipment validation first.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_Shipment_Customer'
        AND parent_object_id=OBJECT_ID(N'dbo.FG_Shipment'))
        ALTER TABLE dbo.FG_Shipment DROP CONSTRAINT FK_FG_Shipment_Customer;
    IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_FG_CustomerReturn_Shipment'
        AND parent_object_id=OBJECT_ID(N'dbo.FG_CustomerReturn'))
        ALTER TABLE dbo.FG_CustomerReturn DROP CONSTRAINT FK_FG_CustomerReturn_Shipment;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
