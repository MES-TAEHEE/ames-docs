IF OBJECT_ID(N'SIS_TEST.PDA_WH002_ADJUST_AUDIT', N'U') IS NULL
BEGIN
    CREATE TABLE SIS_TEST.PDA_WH002_ADJUST_AUDIT
    (
        ADJUST_ID bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CORCD nvarchar(10) NOT NULL,
        BIZCD nvarchar(10) NOT NULL,
        RECEIVE_TYPE nvarchar(10) NOT NULL,
        LOTNO nvarchar(50) NOT NULL,
        BARCODE nvarchar(50) NOT NULL,
        PARTNO nvarchar(40) NULL,
        LOCATION_NO nvarchar(30) NULL,
        BEFORE_QTY decimal(18,3) NOT NULL,
        DELTA_QTY decimal(18,3) NOT NULL,
        AFTER_QTY decimal(18,3) NOT NULL,
        REASON_CODE nvarchar(30) NOT NULL,
        REASON_NOTE nvarchar(500) NULL,
        SUPERVISOR_PIN_MASK nvarchar(20) NOT NULL,
        WORK_DATE nvarchar(10) NOT NULL,
        WORK_TIME nvarchar(8) NOT NULL,
        USER_ID nvarchar(40) NOT NULL,
        INSERT_DATE datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
GO

CREATE OR ALTER PROCEDURE SIS_TEST.PDA_WH002_SCAN_LOCAL
    @IN_BARCODE nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        N'LOCAL' AS RECEIVE_TYPE,
        CASE WHEN S.LOTNO IS NULL THEN N'Y' ELSE N'N' END AS YN,
        B.BOX_BARCODE AS LOTNO,
        B.BOX_BARCODE AS BARCODE,
        N'AMM9010/AMM9011' AS SOURCE_TABLE,
        H.DELI_NOTE AS NOTENO,
        CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
        CAST(NULL AS nvarchar(30)) AS CASE_NO,
        CAST(NULL AS nvarchar(30)) AS INVOICE_NO,
        CAST(NULL AS nvarchar(30)) AS CONTAINER_NO,
        B.PARTNO,
        COALESCE(H.PARTNM, P.PARTNM) AS PARTNM,
        CASE
            WHEN S.LOTNO IS NOT NULL THEN COALESCE(S.QTY, 0)
            ELSE COALESCE(B.QTY, H.DELI_QTY, 0)
        END AS QTY,
        COALESCE(B.PO_UNIT, H.PO_UNIT, P.PO_UNIT) AS UNIT,
        COALESCE(B.PONO, H.PONO) AS PONO,
        COALESCE(B.PONO_SEQ, H.PONO_SEQ) AS PONO_SEQ,
        H.VENDCD,
        H.VENDCD AS VENDNM,
        B.PRDT_DATE AS PROD_DATE,
        H.DELI_DATE,
        H.ARRIV_DATE,
        CAST(NULL AS nvarchar(10)) AS SHIP_DATE,
        CAST(NULL AS nvarchar(10)) AS PACK_DATE,
        S.LOCATION_NO AS RECEIVED_LOCATION,
        S.INV_STATUS AS RECEIVED_STATUS
    FROM SIS_TEST.AMM9011 B
    LEFT JOIN SIS_TEST.AMM9010 H
        ON H.CORCD = B.CORCD
       AND H.BIZCD = B.BIZCD
       AND H.DELI_NOTE = B.DELI_NOTE
       AND H.DELI_NOTE_SEQ = B.DELI_NOTE_SEQ
    LEFT JOIN SIS_TEST.AMM1040 P
        ON P.CORCD = B.CORCD
       AND P.BIZCD = B.BIZCD
       AND P.PONO = B.PONO
       AND P.PONO_SEQ = B.PONO_SEQ
    LEFT JOIN SIS_TEST.WMS2020 S
        ON S.CORCD = B.CORCD
       AND S.BIZCD = B.BIZCD
       AND S.LOTNO = B.BOX_BARCODE
    WHERE B.BOX_BARCODE = @IN_BARCODE
       OR B.VEND_LOTNO = @IN_BARCODE
       OR B.DELI_NOTE = @IN_BARCODE;
END;
GO

CREATE OR ALTER PROCEDURE SIS_TEST.PDA_WH002_SCAN_CKD
    @IN_BARCODE nvarchar(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        N'CKD' AS RECEIVE_TYPE,
        CASE WHEN S.LOTNO IS NULL THEN N'Y' ELSE N'N' END AS YN,
        C.BOX_BARCODE AS LOTNO,
        C.BOX_BARCODE AS BARCODE,
        N'AMF1030' AS SOURCE_TABLE,
        C.CASE_BARCODE AS NOTENO,
        C.CASE_BARCODE,
        C.CASE_NO,
        C.INVOICE_NO,
        C.CONTAINER_NO,
        C.PARTNO,
        C.PARTNM,
        CASE
            WHEN S.LOTNO IS NOT NULL THEN COALESCE(S.QTY, 0)
            ELSE COALESCE(C.PACK_QTY, 0)
        END AS QTY,
        C.UNIT,
        C.PONO,
        C.PONO_SEQ,
        C.VENDCD,
        C.VENDCD AS VENDNM,
        C.PROD_DATE,
        CAST(NULL AS nvarchar(10)) AS DELI_DATE,
        CAST(NULL AS nvarchar(10)) AS ARRIV_DATE,
        C.SHIP_DATE,
        C.PACK_DATE,
        S.LOCATION_NO AS RECEIVED_LOCATION,
        S.INV_STATUS AS RECEIVED_STATUS
    FROM SIS_TEST.AMF1030 C
    LEFT JOIN SIS_TEST.WMS2020 S
        ON S.CORCD = C.CORCD
       AND S.BIZCD = C.BIZCD
       AND S.LOTNO = C.BOX_BARCODE
    WHERE C.BOX_BARCODE = @IN_BARCODE
       OR C.LOTNO = @IN_BARCODE
       OR C.CASE_BARCODE = @IN_BARCODE;
END;
GO

CREATE OR ALTER PROCEDURE SIS_TEST.PDA_WH002_ADJUST_QTY
    @IN_MODE nvarchar(10),
    @IN_BARCODE nvarchar(50),
    @IN_DELTA_QTY decimal(18,3),
    @IN_REASON_CODE nvarchar(30),
    @IN_REASON_NOTE nvarchar(500) = NULL,
    @IN_SUPERVISOR_PIN nvarchar(40),
    @IN_USERID nvarchar(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @MODE nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@IN_MODE, N''))));
    DECLARE @BARCODE nvarchar(50) = LTRIM(RTRIM(ISNULL(@IN_BARCODE, N'')));
    DECLARE @REASON nvarchar(30) = UPPER(LTRIM(RTRIM(ISNULL(@IN_REASON_CODE, N''))));
    DECLARE @NOTE nvarchar(500) = NULLIF(LTRIM(RTRIM(ISNULL(@IN_REASON_NOTE, N''))), N'');
    DECLARE @PIN nvarchar(40) = LTRIM(RTRIM(ISNULL(@IN_SUPERVISOR_PIN, N'')));
    DECLARE @USER nvarchar(40) = NULLIF(LTRIM(RTRIM(ISNULL(@IN_USERID, N''))), N'');

    IF @MODE NOT IN (N'LOCAL', N'CKD')
        THROW 51300, 'Receive mode must be LOCAL or CKD.', 1;
    IF @BARCODE = N''
        THROW 51301, 'Barcode is required.', 1;
    IF @IN_DELTA_QTY = 0
        THROW 51302, 'Adjustment quantity must not be zero.', 1;
    IF @REASON NOT IN (N'COUNT_DIFF', N'DAMAGED', N'LOST', N'FOUND', N'OTHER')
        THROW 51303, 'Reason code is invalid.', 1;
    IF LEN(@PIN) < 4
        THROW 51304, 'Supervisor PIN must be at least 4 digits.', 1;

    IF @USER IS NULL SET @USER = N'PDA';

    DECLARE
        @CORCD nvarchar(10),
        @BIZCD nvarchar(10),
        @LOTNO nvarchar(50),
        @PARTNO nvarchar(40);

    IF @MODE = N'LOCAL'
    BEGIN
        SELECT TOP (1)
            @CORCD = B.CORCD,
            @BIZCD = B.BIZCD,
            @LOTNO = B.BOX_BARCODE,
            @PARTNO = B.PARTNO
        FROM SIS_TEST.AMM9011 B
        WHERE B.BOX_BARCODE = @BARCODE
           OR B.VEND_LOTNO = @BARCODE
           OR B.DELI_NOTE = @BARCODE;
    END
    ELSE
    BEGIN
        SELECT TOP (1)
            @CORCD = C.CORCD,
            @BIZCD = C.BIZCD,
            @LOTNO = C.BOX_BARCODE,
            @PARTNO = C.PARTNO
        FROM SIS_TEST.AMF1030 C
        WHERE C.BOX_BARCODE = @BARCODE
           OR C.LOTNO = @BARCODE
           OR C.CASE_BARCODE = @BARCODE;
    END;

    IF @LOTNO IS NULL
        THROW 51305, 'Barcode was not found in inbound source tables.', 1;

    DECLARE
        @LOCATION nvarchar(30),
        @WHCD nvarchar(10),
        @INV_STATUS nvarchar(10),
        @BEFORE_QTY decimal(18,3),
        @AFTER_QTY decimal(18,3);

    SELECT TOP (1)
        @LOCATION = S.LOCATION_NO,
        @WHCD = S.WHCD,
        @INV_STATUS = S.INV_STATUS,
        @BEFORE_QTY = COALESCE(S.QTY, 0)
    FROM SIS_TEST.WMS2020 S
    WHERE S.CORCD = @CORCD
      AND S.BIZCD = @BIZCD
      AND S.LOTNO = @LOTNO;

    IF @INV_STATUS IS NULL
        THROW 51306, 'This LOT is not incoming yet.', 1;
    IF @INV_STATUS NOT IN (N'I0', N'I')
        THROW 51307, 'Quantity can be adjusted only for incoming stock.', 1;

    SET @AFTER_QTY = @BEFORE_QTY + @IN_DELTA_QTY;

    IF @AFTER_QTY < 0
        THROW 51308, 'After Qty cannot be below zero.', 1;

    DECLARE @NOW datetime2 = SYSUTCDATETIME();
    DECLARE @WORK_DATE nvarchar(10) = CONVERT(nvarchar(10), GETDATE(), 23);
    DECLARE @WORK_TIME nvarchar(8) = CONVERT(nvarchar(8), GETDATE(), 108);
    DECLARE @PIN_MASK nvarchar(20) = N'***' + RIGHT(@PIN, 2);

    BEGIN TRANSACTION;

    UPDATE SIS_TEST.WMS2020
       SET QTY = @AFTER_QTY,
           WORK_DATE = @WORK_DATE,
           WORK_TIME = @WORK_TIME,
           UPDATE_DATE = @NOW,
           UPDATE_ID = @USER
     WHERE CORCD = @CORCD
       AND BIZCD = @BIZCD
       AND LOTNO = @LOTNO;

    IF @AFTER_QTY = 0
    BEGIN
        DELETE FROM SIS_TEST.WMS2000
         WHERE CORCD = @CORCD
           AND BIZCD = @BIZCD
           AND LOTNO = @LOTNO;
    END
    ELSE
    BEGIN
        MERGE SIS_TEST.WMS2000 AS T
        USING
        (
            SELECT
                @CORCD AS CORCD,
                @BIZCD AS BIZCD,
                @LOTNO AS LOTNO,
                @PARTNO AS PARTNO,
                @AFTER_QTY AS QTY,
                @LOCATION AS LOCATION_NO,
                @INV_STATUS AS INV_STATUS,
                @WORK_DATE AS WORK_DATE,
                @WORK_TIME AS WORK_TIME,
                @USER AS USER_ID,
                @WHCD AS WHCD
        ) AS S
        ON T.CORCD = S.CORCD
           AND T.BIZCD = S.BIZCD
           AND T.LOTNO = S.LOTNO
        WHEN MATCHED THEN
            UPDATE SET
                PARTNO = S.PARTNO,
                QTY = S.QTY,
                LOCATION_NO = S.LOCATION_NO,
                INV_STATUS = S.INV_STATUS,
                WORK_DATE = S.WORK_DATE,
                WORK_TIME = S.WORK_TIME,
                USER_ID = S.USER_ID,
                WHCD = S.WHCD,
                UPDATE_DATE = @NOW,
                UPDATE_ID = @USER
        WHEN NOT MATCHED THEN
            INSERT
            (
                CORCD, BIZCD, LOTNO, PARTNO, QTY, LOCATION_NO, INV_STATUS,
                WORK_DATE, WORK_TIME, USER_ID, WHCD, INSERT_DATE, INSERT_ID
            )
            VALUES
            (
                S.CORCD, S.BIZCD, S.LOTNO, S.PARTNO, S.QTY, S.LOCATION_NO, S.INV_STATUS,
                S.WORK_DATE, S.WORK_TIME, S.USER_ID, S.WHCD, @NOW, @USER
            );
    END;

    INSERT INTO SIS_TEST.PDA_WH002_ADJUST_AUDIT
    (
        CORCD, BIZCD, RECEIVE_TYPE, LOTNO, BARCODE, PARTNO, LOCATION_NO,
        BEFORE_QTY, DELTA_QTY, AFTER_QTY, REASON_CODE, REASON_NOTE,
        SUPERVISOR_PIN_MASK, WORK_DATE, WORK_TIME, USER_ID, INSERT_DATE
    )
    VALUES
    (
        @CORCD, @BIZCD, @MODE, @LOTNO, @BARCODE, @PARTNO, @LOCATION,
        @BEFORE_QTY, @IN_DELTA_QTY, @AFTER_QTY, @REASON, @NOTE,
        @PIN_MASK, @WORK_DATE, @WORK_TIME, @USER, @NOW
    );

    COMMIT TRANSACTION;

    IF @MODE = N'LOCAL'
        EXEC SIS_TEST.PDA_WH002_SCAN_LOCAL @IN_BARCODE = @LOTNO;
    ELSE
        EXEC SIS_TEST.PDA_WH002_SCAN_CKD @IN_BARCODE = @LOTNO;
END;
GO
