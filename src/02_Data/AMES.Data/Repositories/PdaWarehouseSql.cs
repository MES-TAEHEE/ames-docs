namespace AMES.Data.Repositories;

// Parameterized batches owned by the application; no database procedure installation is required.
public static class PdaWarehouseSql
{
    public const string PutAwayInventory = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        SELECT Q.Mode, Q.Barcode, Q.LotNo, Q.PartNo, Q.PartName, Q.Qty,
               Q.Unit, Q.DeliveryNoteNo, Q.ReceivedAt, Q.LocationNo, Q.ParentLotNo
        FROM
        (
            SELECT CAST('LOCAL' AS varchar(10)) AS Mode,
                   W.LotNo AS Barcode,
                   W.LotNo, W.PartNo, W.PartName, W.Qty,
                   I.DefaultUOM AS Unit, W.DeliveryNoteNo, W.ReceivedAt, W.LocationNo, W.ParentLotNo
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = W.PartNo
            WHERE W.Qty > 0
              AND W.DeliveryNoteNo IS NOT NULL

            UNION ALL

            SELECT CAST(CASE WHEN UPPER(COALESCE(L.ProcessCode,'LOCAL'))='CKD' THEN 'CKD' ELSE 'LOCAL' END AS varchar(10)) AS Mode,
                   W.LotNo AS Barcode,
                   W.LotNo, W.PartNo, COALESCE(NULLIF(W.PartName,N''),I.ItemName) AS PartName,
                   W.Qty, I.DefaultUOM AS Unit,
                   CAST(NULL AS nvarchar(30)) AS DeliveryNoteNo,
                   W.ReceivedAt, W.LocationNo, W.ParentLotNo
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.tbl_Lot L ON L.LotCode=W.LotNo
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=W.PartNo
            WHERE W.Qty>0 AND W.DeliveryNoteNo IS NULL
        ) Q
        WHERE (@Barcode IS NULL OR UPPER(Q.Barcode) = UPPER(@Barcode)
            OR UPPER(Q.LotNo) = UPPER(@Barcode)
            OR UPPER(Q.DeliveryNoteNo) = UPPER(@Barcode)
            OR EXISTS(SELECT 1 FROM dbo.WH_Inventory C
                      WHERE C.LotNo=Q.LotNo AND UPPER(C.ParentLotNo)=UPPER(@Barcode)))
          AND (@OnlyUnassigned = 0 OR NULLIF(LTRIM(RTRIM(Q.LocationNo)), '') IS NULL)
        ORDER BY Q.ReceivedAt, Q.Barcode;
        """;

    public const string ScheduleInbound = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;

            SELECT TOP (100)
                P.PoNumber AS PurchaseOrderNo,
                P.PoLineNo AS PurchaseOrderLineNo,
                COALESCE(V.VendorName, P.VendorID) AS SupplierName,
                P.ItemNo AS MaterialNo,
                I.ItemName AS MaterialName,
                I.CarType AS CarCode,
                COALESCE(P.UnitCode, I.DefaultUOM) AS UnitOfMeasure,
                COALESCE(P.OrderQty, 0) AS PurchaseOrderQty,
                COALESCE(P.ReceivedQty, 0) AS ReceivedQty,
                CASE
                    WHEN COALESCE(P.OrderQty, 0) - COALESCE(P.ReceivedQty, 0) < 0 THEN 0
                    ELSE COALESCE(P.OrderQty, 0) - COALESCE(P.ReceivedQty, 0)
                END AS RemainingQty,
                P.DueDate AS ExpectedArrivalDate,
                P.OrderDate AS PurchaseOrderCreatedDate,
                CASE
                    WHEN COALESCE(P.OrderQty, 0) - COALESCE(P.ReceivedQty, 0) <= 0 THEN N'Complete'
                    WHEN P.DueDate < CONVERT(date, GETDATE()) THEN N'Late'
                    ELSE N'In Progress'
                END AS ReceiptStatus
            FROM dbo.WH_PurchaseOrder P
            LEFT JOIN dbo.MD_Item I
                   ON I.ItemNo = P.ItemNo
            LEFT JOIN dbo.MD_Vendor V
                   ON V.VendorID = P.VendorID
            WHERE (@SupplierCode IS NULL OR P.VendorID = @SupplierCode)
              AND (
                    P.DueDate IS NULL
                    OR (
                        YEAR(P.DueDate) = TRY_CONVERT(int, @ScheduleYear)
                        AND DATEPART(quarter, P.DueDate) = TRY_CONVERT(int, @ScheduleQuarter)
                    )
              )
            ORDER BY P.DueDate, P.PoNumber, P.PoLineNo;
        END;
        """;

    public const string InboundDocument = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
            DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@DocumentBarcode, N'')));
            DECLARE @NoteNumber nvarchar(50);

            IF @Mode NOT IN (N'LOCAL', N'CKD') THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;

            SELECT TOP (1) @NoteNumber = N.NoteNumber
            FROM dbo.SCM_DeliveryNote N
            LEFT JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
            LEFT JOIN dbo.SCM_Delivery D ON D.DeliveryID = ND.DeliveryID
            WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @Barcode
               OR D.DeliveryNumber COLLATE DATABASE_DEFAULT = @Barcode
            ORDER BY CASE WHEN N.NoteNumber COLLATE DATABASE_DEFAULT = @Barcode THEN 0 ELSE 1 END;

            IF @NoteNumber IS NULL
                THROW 51402, 'Barcode was not found in inbound source tables.', 1;
            IF EXISTS
            (
                SELECT 1 FROM dbo.SCM_DeliveryNote N
                JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
                JOIN dbo.SCM_Delivery D ON D.DeliveryID = ND.DeliveryID
                WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
                  AND D.Status NOT IN ('Shipped', 'Received')
            )
                THROW 51404, 'Only shipped delivery notes can be received.', 1;
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.SCM_DeliveryNote N
                JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
                JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = ND.DeliveryID
                JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
                WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
            )
                THROW 51405, 'The delivery note has no active box labels.', 1;

            SELECT
                @Mode AS RECEIVE_TYPE, N.NoteID AS INBOUND_DOCUMENT_ID,
                N.NoteNumber AS DOCUMENT_BARCODE, N.NoteNumber AS DOCUMENT_NO,
                N.VendorID AS VENDCD, COALESCE(V.VendorName, N.VendorID) AS VENDNM,
                CAST(NULL AS nvarchar(50)) AS CASE_NO,
                CAST(NULL AS nvarchar(50)) AS INVOICE_NO,
                CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
                MIN(D.ShipDate) AS SHIP_DATE, CAST(NULL AS date) AS PACK_DATE,
                MAX(D.DeliveryDate) AS DELI_DATE, MAX(D.DeliveryDate) AS ARRIV_DATE,
                COUNT(B.BoxID) AS TOTAL_BOXES,
                SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END) AS SCANNED_BOXES,
                CASE WHEN COUNT(B.BoxID) = SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END)
                     THEN N'Y' ELSE N'N' END AS YN
            FROM dbo.SCM_DeliveryNote N
            JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
            JOIN dbo.SCM_Delivery D ON D.DeliveryID = ND.DeliveryID
            JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = D.DeliveryID
            JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
            LEFT JOIN dbo.MD_Vendor V ON V.VendorID = N.VendorID
            LEFT JOIN dbo.WH_Inventory W
              ON W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
                                          AND W.LotNo = B.BoxNumber AND W.Qty > 0
            WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
            GROUP BY N.NoteID, N.NoteNumber, N.VendorID, V.VendorName;

            SELECT
                B.ItemNo AS PARTNO, MAX(B.ItemName) AS PARTNM,
                COUNT(B.BoxID) AS BOX_COUNT,
                SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END) AS SCAN_COUNT,
                SUM(B.Quantity) AS DELIVERED_QTY,
                SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE B.Quantity END) AS RECEIVED_QTY,
                SUM(CASE WHEN W.LotNo IS NULL THEN B.Quantity ELSE 0 END) AS REMAINING_QTY,
                MAX(B.UnitCode) AS UNIT,
                CASE WHEN COUNT(B.BoxID) = SUM(CASE WHEN W.LotNo IS NULL THEN 0 ELSE 1 END)
                     THEN N'Y' ELSE N'N' END AS YN
            FROM dbo.SCM_DeliveryNote N
            JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
            JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = ND.DeliveryID
            JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
            LEFT JOIN dbo.WH_Inventory W
              ON W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
                                          AND W.LotNo = B.BoxNumber AND W.Qty > 0
            WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
            GROUP BY B.ItemNo
            ORDER BY B.ItemNo;

            SELECT
                B.ItemNo AS PARTNO, B.BoxNumber AS BOX_BARCODE,
                COALESCE(NULLIF(DL.VendorLotNo, N''), B.BoxNumber) AS LOTNO,
                B.Quantity AS QTY, B.UnitCode AS UNIT,
                CASE WHEN W.LotNo IS NULL THEN N'N' ELSE N'Y' END AS YN
            FROM dbo.SCM_DeliveryNote N
            JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.NoteID = N.NoteID
            JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryID = ND.DeliveryID
            JOIN dbo.SCM_DeliveryBox B ON B.DeliveryLineID = DL.DeliveryLineID AND B.ActiveFlag = 1
            LEFT JOIN dbo.WH_Inventory W
              ON W.DeliveryNoteNo COLLATE DATABASE_DEFAULT = N.NoteNumber COLLATE DATABASE_DEFAULT
                                          AND W.LotNo = B.BoxNumber AND W.Qty > 0
            WHERE N.NoteNumber COLLATE DATABASE_DEFAULT = @NoteNumber
            ORDER BY B.ItemNo, B.BoxSeq;
        END;
        """;

    public const string InboundScan = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
            DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));

            IF @Mode NOT IN (N'LOCAL', N'CKD') THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
            IF @Barcode = N'' THROW 51401, 'Barcode is required.', 1;

            IF EXISTS (SELECT 1 FROM dbo.SCM_DeliveryBox WHERE BoxNumber = @Barcode AND ActiveFlag = 1)
            BEGIN
                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.SCM_DeliveryBox B
                    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
                    JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
                    WHERE B.BoxNumber = @Barcode AND B.ActiveFlag = 1
                      AND D.Status IN ('Shipped', 'Received')
                )
                    THROW 51404, 'Only boxes from a shipped delivery note can be received.', 1;

                SELECT TOP (1)
                    @Mode AS RECEIVE_TYPE, CASE WHEN W.LotNo IS NULL THEN N'Y' ELSE N'N' END AS YN,
                    COALESCE(NULLIF(DL.VendorLotNo, N''), B.BoxNumber) AS LOTNO,
                    B.BoxNumber AS BARCODE, N'dbo.SCM_DeliveryBox' AS SOURCE_TABLE,
                    N.NoteNumber AS NOTENO, CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
                    CAST(NULL AS nvarchar(50)) AS CASE_NO, PO.PoNumber AS INVOICE_NO,
                    CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
                    B.ItemNo AS PARTNO, B.ItemName AS PARTNM,
                    COALESCE(W.Qty, B.Quantity) AS QTY, B.UnitCode AS UNIT,
                    PO.PoNumber AS PONO, PO.PoLineNo AS PONO_SEQ,
                    D.VendorID AS VENDCD, COALESCE(V.VendorName, D.VendorID) AS VENDNM,
                    DL.ProductionDate AS PROD_DATE, D.DeliveryDate AS DELI_DATE,
                    D.DeliveryDate AS ARRIV_DATE, D.ShipDate AS SHIP_DATE,
                    CAST(NULL AS date) AS PACK_DATE, W.LocationNo AS RECEIVED_LOCATION,
                    CASE WHEN W.LotNo IS NULL THEN N'Open' ELSE N'Received' END AS RECEIVED_STATUS
                FROM dbo.SCM_DeliveryBox B
                JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
                JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
                JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID = D.DeliveryID
                JOIN dbo.SCM_DeliveryNote N ON N.NoteID = ND.NoteID
                LEFT JOIN dbo.WH_PurchaseOrder PO ON PO.PoID = DL.PoID
                LEFT JOIN dbo.MD_Vendor V ON V.VendorID = D.VendorID
                LEFT JOIN dbo.WH_Inventory W ON W.LotNo = B.BoxNumber AND W.Qty > 0
                WHERE B.BoxNumber = @Barcode AND B.ActiveFlag = 1
                ORDER BY N.NoteID DESC;
                RETURN;
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode COLLATE DATABASE_DEFAULT = @Barcode)
                THROW 51402, 'Barcode was not found in inbound source tables.', 1;

            SELECT TOP (1)
                @Mode AS RECEIVE_TYPE, CASE WHEN W.LotNo IS NULL THEN N'Y' ELSE N'N' END AS YN,
                L.LotCode AS LOTNO, L.LotCode AS BARCODE, N'dbo.tbl_Lot' AS SOURCE_TABLE,
                PO.PoNumber AS NOTENO, CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
                CAST(NULL AS nvarchar(50)) AS CASE_NO, PO.PoNumber AS INVOICE_NO,
                CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
                L.ItemNo AS PARTNO, I.ItemName AS PARTNM,
                COALESCE(W.Qty, NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), PO.OrderQty, 0) AS QTY,
                COALESCE(PO.UnitCode, I.DefaultUOM, 'EA') AS UNIT,
                PO.PoNumber AS PONO, PO.PoLineNo AS PONO_SEQ,
                PO.VendorID AS VENDCD, COALESCE(V.VendorName, PO.VendorID) AS VENDNM,
                CONVERT(date, L.ProducedAt) AS PROD_DATE, PO.DueDate AS DELI_DATE,
                PO.DueDate AS ARRIV_DATE, CAST(NULL AS date) AS SHIP_DATE,
                CAST(NULL AS date) AS PACK_DATE, W.LocationNo AS RECEIVED_LOCATION,
                CASE WHEN W.LotNo IS NULL THEN N'Open' ELSE N'Received' END AS RECEIVED_STATUS
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT = L.ItemNo COLLATE DATABASE_DEFAULT
            OUTER APPLY
            (
                SELECT TOP (1) P.* FROM dbo.WH_PurchaseOrder P
                WHERE P.ItemNo COLLATE DATABASE_DEFAULT = L.ItemNo COLLATE DATABASE_DEFAULT
                ORDER BY CASE WHEN COALESCE(P.OrderQty,0) > COALESCE(P.ReceivedQty,0) THEN 0 ELSE 1 END,
                         P.DueDate, P.PoID
            ) PO
            LEFT JOIN dbo.MD_Vendor V ON V.VendorID = PO.VendorID
            LEFT JOIN dbo.WH_Inventory W ON W.LotNo COLLATE DATABASE_DEFAULT = L.LotCode COLLATE DATABASE_DEFAULT AND W.Qty > 0
            WHERE L.LotCode COLLATE DATABASE_DEFAULT = @Barcode
            ORDER BY L.LotID DESC;
        END;
        """;

    public const string Inventory = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Q nvarchar(80)=NULLIF(LTRIM(RTRIM(@SearchText)),N'');
            ;WITH S AS
            (
                SELECT W.PartNo,MAX(COALESCE(NULLIF(W.PartName,N''),I.ItemName COLLATE DATABASE_DEFAULT)) PartName,
                    SUM(W.Qty) SumQty,MAX(W.ReceivedAt) LastReceivedDate,
                    COUNT(DISTINCT W.LotNo) LotCount,COUNT(DISTINCT W.LocationNo) LocationCount,
                    MAX(I.CarType) CarType,MAX(COALESCE(I.DefaultUOM,'EA')) Unit,
                    MAX(COALESCE(I.MinStock,0)) MinQty,MAX(COALESCE(I.MaxStock,0)) MaxQty
                FROM dbo.WH_Inventory W
                LEFT JOIN dbo.MD_Item I
                  ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo COLLATE DATABASE_DEFAULT
                LEFT JOIN dbo.MD_Location L
                  ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
                WHERE W.Qty>0 AND W.PartNo IS NOT NULL
                  AND NOT (UPPER(COALESCE(L.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
                  AND (@AreaCode IS NULL OR L.AreaCode=@AreaCode)
                  AND (@StockDateFrom IS NULL OR CONVERT(date,W.ReceivedAt)>=@StockDateFrom)
                  AND (@StockDateTo IS NULL OR CONVERT(date,W.ReceivedAt)<=@StockDateTo)
                  AND (@Q IS NULL OR W.PartNo LIKE N'%'+@Q+N'%' OR W.PartName LIKE N'%'+@Q+N'%'
                       OR W.LotNo LIKE N'%'+@Q+N'%' OR W.LocationNo LIKE N'%'+@Q+N'%'
                       OR I.ItemName LIKE N'%'+@Q+N'%')
                GROUP BY W.PartNo
            )
            SELECT ROW_NUMBER() OVER(ORDER BY S.PartNo) INVENTORY_ID,S.PartNo PARTNO,S.PartName PARTNM,
                P.LotNo LOTNO,COALESCE(P.LocationNo,N'-') PRIMARY_LOCATION,S.SumQty SUM_QTY,
                CAST(0 AS decimal(18,3)) RESERVED_QTY,S.LastReceivedDate LAST_RECEIVED_DATE,
                S.CarType VINCD,S.Unit UNIT,CAST(NULL AS decimal(18,3)) MIN_INV_DAY,S.MinQty MIN_INV_QTY,
                CAST(NULL AS decimal(18,3)) MAX_INV_DAY,S.MaxQty MAX_INV_QTY,S.LotCount LOT_COUNT,
                S.LocationCount LOCATION_COUNT,
                CASE WHEN S.MinQty>0 AND S.SumQty<S.MinQty THEN N'BELOW_MIN'
                     WHEN S.MaxQty>0 AND S.SumQty>S.MaxQty THEN N'OVER_MAX' ELSE N'NORMAL' END STATUS,
                CASE WHEN S.MinQty>0 AND S.SumQty<S.MinQty THEN N'Below Min'
                     WHEN S.MaxQty>0 AND S.SumQty>S.MaxQty THEN N'Over Max' ELSE N'Normal' END STATUSNM
            FROM S
            OUTER APPLY(SELECT TOP(1) W.LotNo,W.LocationNo FROM dbo.WH_Inventory W
              LEFT JOIN dbo.MD_Location PL
                ON PL.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
              WHERE W.PartNo COLLATE DATABASE_DEFAULT = S.PartNo COLLATE DATABASE_DEFAULT
                AND W.Qty>0
                AND NOT (UPPER(COALESCE(PL.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
              ORDER BY W.ReceivedAt,W.LotNo) P
            ORDER BY S.PartNo;
        END;
        """;

    public const string InventoryLookup = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;

            DECLARE @Scan nvarchar(80) = NULLIF(LTRIM(RTRIM(@ScanText)), N'');

            IF @Scan IS NULL
            BEGIN
                SELECT N'TEXT' AS SEARCH_KIND, N'' AS SEARCH_TEXT, CAST(NULL AS nvarchar(120)) AS DISPLAY_TEXT;
                RETURN;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM dbo.MD_Location L
                WHERE UPPER(L.LocationID) = UPPER(@Scan)
                  AND COALESCE(L.ActiveFlag, 1) = 1
            )
            BEGIN
                SELECT TOP (1)
                    N'LOCATION' AS SEARCH_KIND,
                    CAST(L.LocationID AS nvarchar(80)) AS SEARCH_TEXT,
                    L.LocationName AS DISPLAY_TEXT
                FROM dbo.MD_Location L
                WHERE UPPER(L.LocationID) = UPPER(@Scan)
                  AND COALESCE(L.ActiveFlag, 1) = 1;
                RETURN;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM dbo.tbl_Lot L
                WHERE UPPER(L.LotCode) = UPPER(@Scan)
                   OR UPPER(L.ItemNo) = UPPER(@Scan)
            )
            BEGIN
                SELECT TOP (1)
                    N'PART' AS SEARCH_KIND,
                    CAST(L.ItemNo AS nvarchar(80)) AS SEARCH_TEXT,
                    I.ItemName AS DISPLAY_TEXT
                FROM dbo.tbl_Lot L
                LEFT JOIN dbo.MD_Item I
                       ON I.ItemNo = L.ItemNo
                WHERE UPPER(L.LotCode) = UPPER(@Scan)
                   OR UPPER(L.ItemNo) = UPPER(@Scan)
                ORDER BY
                    CASE WHEN UPPER(L.LotCode) = UPPER(@Scan) THEN 0 ELSE 1 END,
                    L.LotID DESC;
                RETURN;
            END;

            IF EXISTS
            (
                SELECT 1
                FROM dbo.MD_Item I
                WHERE UPPER(I.ItemNo) = UPPER(@Scan)
                  AND COALESCE(I.ActiveFlag, 1) = 1
            )
            BEGIN
                SELECT TOP (1)
                    N'PART' AS SEARCH_KIND,
                    CAST(I.ItemNo AS nvarchar(80)) AS SEARCH_TEXT,
                    I.ItemName AS DISPLAY_TEXT
                FROM dbo.MD_Item I
                WHERE UPPER(I.ItemNo) = UPPER(@Scan)
                  AND COALESCE(I.ActiveFlag, 1) = 1;
                RETURN;
            END;

            SELECT N'TEXT' AS SEARCH_KIND, @Scan AS SEARCH_TEXT, CAST(NULL AS nvarchar(120)) AS DISPLAY_TEXT;
        END;
        """;

    public const string InventoryLocations = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SELECT ROW_NUMBER() OVER(ORDER BY COALESCE(L.LocationID COLLATE DATABASE_DEFAULT,W.LocationNo,N'-')) ROW_NO,
                W.PartNo PARTNO,COALESCE(L.LocationID COLLATE DATABASE_DEFAULT,W.LocationNo,N'-') LOCATION_NO,L.LocationName LOCATION_NM,
                L.WhCode WHCD,COALESCE(WC.CodeName,L.WhCode) WHNM,L.AreaCode AREACD,
                COALESCE(AC.CodeName,L.AreaCode) AREANM,L.ZoneCode ZONECD,L.ZoneCode ZONENM,
                L.Aisle RACK_X,L.Bay RACK_Y,L.Slot RACK_Z,SUM(W.Qty) SUM_QTY
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L
              ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo COLLATE DATABASE_DEFAULT
            LEFT JOIN dbo.MD_CodeItem WC ON WC.GroupCode='WH_CODE' AND WC.CodeValue=L.WhCode
            LEFT JOIN dbo.MD_CodeItem AC ON AC.GroupCode='WH_AREA' AND AC.CodeValue=L.AreaCode
            WHERE W.PartNo=@ItemNo AND W.Qty>0
              AND NOT (UPPER(COALESCE(L.AreaCode,''))='FG_AREA' OR UPPER(W.LocationNo) LIKE 'FG%')
              AND (@AreaCode IS NULL OR L.AreaCode=@AreaCode)
              AND (@StockDateFrom IS NULL OR CONVERT(date,W.ReceivedAt)>=@StockDateFrom)
              AND (@StockDateTo IS NULL OR CONVERT(date,W.ReceivedAt)<=@StockDateTo)
            GROUP BY W.PartNo,W.LocationNo,L.LocationID,L.LocationName,L.WhCode,WC.CodeName,
              L.AreaCode,AC.CodeName,L.ZoneCode,L.Aisle,L.Bay,L.Slot
            ORDER BY COALESCE(L.LocationID COLLATE DATABASE_DEFAULT,W.LocationNo,N'-');
        END;
        """;

    public const string Transactions = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;

            DECLARE @Search nvarchar(120) = NULLIF(LTRIM(RTRIM(@SearchText)), N'');
            DECLARE @Like nvarchar(130) = CASE WHEN @Search IS NULL THEN NULL ELSE N'%' + @Search + N'%' END;
            DECLARE @From date = COALESCE(@DateFrom, DATEADD(day, -30, CONVERT(date, SYSDATETIME())));
            DECLARE @To date = COALESCE(@DateTo, CONVERT(date, SYSDATETIME()));

            SELECT
                ROW_NUMBER() OVER (ORDER BY T.TransactionTime DESC, T.TransactionID DESC) AS ROW_NO,
                T.LotNo AS LOTNO,
                T.PartNo AS PARTNO,
                CONVERT(nvarchar(10), T.TransactionTime, 23) AS WDATE,
                CONVERT(nvarchar(8), T.TransactionTime, 108) AS WTIME,
                T.LocationNo AS LOCATION_NO,
                CASE WHEN T.TransactionType IN ('IN', 'OUT') THEN ABS(T.QtyChange)
                     ELSE T.QtyChange END AS QTY,
                I.DefaultUOM AS UNIT,
                CASE T.TransactionType
                    WHEN 'IN' THEN N'In'
                    WHEN 'OUT' THEN N'Out'
                    WHEN 'ADJ' THEN N'Adjust'
                    WHEN 'MOVE' THEN N'Location Change'
                    ELSE T.TransactionType
                END AS STATUS,
                T.TransactionType AS DIRECTION,
                T.OperatorID AS WORKER_ID,
                T.ReasonCode AS REASON_CODE,
                T.Note AS REASON_NOTE,
                T.ApproverID AS SUPERVISOR,
                T.QtyBefore AS BEFORE_QTY,
                T.QtyChange AS DELTA_QTY,
                T.QtyAfter AS AFTER_QTY,
                CASE WHEN T.TransactionType = 'ADJ' THEN N'QTY BEFORE' ELSE NULL END AS BEFORE_STATUS,
                CASE WHEN T.TransactionType = 'ADJ' THEN N'QTY AFTER' ELSE NULL END AS AFTER_STATUS,
                CASE WHEN T.TransactionType = 'MOVE' THEN
                    CASE WHEN T.Note LIKE N'PDA put-away % -> %' THEN NULLIF(SUBSTRING(T.Note, 14, CHARINDEX(N' -> ', T.Note) - 14), N'UNASSIGNED')
                         WHEN T.Note LIKE N'Moved from % to %' THEN NULLIF(SUBSTRING(T.Note, 12, CHARINDEX(N' to ', T.Note) - 12), N'(unassigned)')
                         ELSE NULL END
                    ELSE T.LocationNo END AS BEFORE_LOCATION,
                T.LocationNo AS AFTER_LOCATION,
                N'WH_InventoryTransaction' AS SOURCE,
                T.Note AS NOTE
            FROM dbo.WH_InventoryTransaction T
            LEFT JOIN dbo.MD_Item I
                   ON I.ItemNo COLLATE DATABASE_DEFAULT = T.PartNo COLLATE DATABASE_DEFAULT
            WHERE T.TransactionTime >= @From
              AND T.TransactionTime < DATEADD(day, 1, @To)
              AND (@Like IS NULL
                   OR T.LotNo COLLATE DATABASE_DEFAULT LIKE @Like
                   OR T.PartNo LIKE @Like
                   OR T.LocationNo LIKE @Like
                   OR EXISTS (
                       SELECT 1
                       FROM dbo.WH_Inventory W
                       WHERE W.LotNo COLLATE DATABASE_DEFAULT = T.LotNo COLLATE DATABASE_DEFAULT
                         AND (W.PalletNo LIKE @Like
                              OR W.InvoiceNo LIKE @Like
                              OR W.DeliveryNoteNo LIKE @Like)))
            ORDER BY T.TransactionTime DESC, T.TransactionID DESC;
        END;
        """;

    public const string AdjustScan = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;

            DECLARE @Scan nvarchar(100) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
            DECLARE @LotNo nvarchar(50);

            IF @Scan = N''
                THROW 51500, 'Barcode is required.', 1;

            IF EXISTS
               (SELECT 1
                FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo
                WHERE UPPER(W.LotNo) = UPPER(@Scan)
                  AND UPPER(COALESCE(L.AreaCode, N'')) = N'FG_AREA')
                THROW 51505, 'Finished goods cannot be adjusted in Warehouse Adjust.', 1;

            SELECT TOP (1)
                @LotNo = W.LotNo
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo
            WHERE UPPER(W.LotNo) = UPPER(@Scan)
              AND UPPER(COALESCE(L.AreaCode, N'')) <> N'FG_AREA'
            ORDER BY CASE WHEN W.Qty > 0 THEN 0 ELSE 1 END, W.ReceivedAt DESC, W.LotNo;

            IF @LotNo IS NULL
                THROW 51501, 'The specified barcode could not be found in Warehouse inventory.', 1;

            SELECT
                N'LOCAL' AS RECEIVE_TYPE,
                N'N' AS YN,
                W.LotNo AS LOTNO,
                W.LotNo AS BARCODE,
                N'dbo.WH_Inventory' AS SOURCE_TABLE,
                W.DeliveryNoteNo AS NOTENO,
                W.PalletNo AS CASE_BARCODE,
                W.PalletNo AS CASE_NO,
                W.InvoiceNo AS INVOICE_NO,
                W.ParentLotNo AS CONTAINER_NO,
                W.PartNo AS PARTNO,
                COALESCE(NULLIF(W.PartName, N''), I.ItemName COLLATE DATABASE_DEFAULT) AS PARTNM,
                W.Qty AS QTY,
                COALESCE(NULLIF(I.DefaultUOM, N''), N'EA') AS UNIT,
                W.InvoiceNo AS PONO,
                CAST(NULL AS int) AS PONO_SEQ,
                CAST(NULL AS nvarchar(30)) AS VENDCD,
                CAST(NULL AS nvarchar(100)) AS VENDNM,
                CAST(NULL AS date) AS PROD_DATE,
                CAST(NULL AS date) AS DELI_DATE,
                CONVERT(date, W.ReceivedAt) AS ARRIV_DATE,
                CAST(NULL AS date) AS SHIP_DATE,
                CAST(NULL AS date) AS PACK_DATE,
                W.LocationNo AS RECEIVED_LOCATION,
                N'Received' AS RECEIVED_STATUS
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo
            WHERE W.LotNo = @LotNo;
        END;
        """;

    public const string ReleaseStatus = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @Slip nvarchar(40)=UPPER(LTRIM(RTRIM(ISNULL(@PickSlipNo,N''))));
            SELECT @Slip PICK_SLIPNO,CASE WHEN COUNT(*)>0 THEN 1 ELSE 0 END EXISTS_FLAG,
              CASE WHEN COUNT(*)>0 AND SUM(CASE WHEN UPPER(COALESCE(Status,'OPEN')) IN('CLOSED','RELEASED','CANCELED','CANCELLED') THEN 1 ELSE 0 END)=COUNT(*) THEN 1 ELSE 0 END IS_CLOSED,
              COUNT(*) LINE_COUNT,MIN(ReqLocation) REQ_LOCATION,CONVERT(date,MAX(RequiredAt)) REQ_DATE,
              MAX(CloseDate) CLOSE_DATE,
              CASE WHEN COUNT(*)=0 THEN N'Pick Slip was not found.'
                   WHEN SUM(CASE WHEN UPPER(COALESCE(Status,'OPEN')) IN('CLOSED','RELEASED','CANCELED','CANCELLED') THEN 1 ELSE 0 END)=COUNT(*) THEN N'Pick Slip is already closed.'
                   ELSE N'Pick Slip is ready.' END MESSAGE
            FROM dbo.WH_PickSlip
            WHERE UPPER(COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID)))=@Slip;
        END;
        """;

    public const string FifoEarlier = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;

            DECLARE @ItemNo varchar(50), @ReceivedAt datetime2, @ResolvedLot nvarchar(50);
            SELECT TOP (1)
                @ResolvedLot = W.LotNo,
                @ItemNo = W.PartNo,
                @ReceivedAt = W.ReceivedAt
            FROM dbo.WH_Inventory W
            WHERE UPPER(W.LotNo) = UPPER(LTRIM(RTRIM(@LotNo)))
            ORDER BY CASE WHEN W.Qty > 0 THEN 0 ELSE 1 END,W.ReceivedAt,W.LotNo;

            SELECT
                CAST(NULL AS int) AS LOT_ID,
                W.LotNo AS LOTNO,
                W.PartNo AS PARTNO,
                W.LocationNo AS LOCATION_NO,
                W.Qty AS QTY,
                W.ReceivedAt AS RCV_DATE,
                CASE WHEN NULLIF(W.LocationNo,'') IS NULL THEN 'RECEIVED' ELSE 'STORED' END AS LOT_STATUS
            FROM dbo.WH_Inventory W
            WHERE W.PartNo = @ItemNo
              AND W.LotNo <> @ResolvedLot
              AND W.Qty > 0
              AND (W.ReceivedAt < @ReceivedAt OR (W.ReceivedAt = @ReceivedAt AND W.LotNo < @ResolvedLot))
            ORDER BY W.ReceivedAt,W.LotNo;
        END;
        """;

    public const string InboundReceive = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
            DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
            DECLARE @Location nvarchar(50) = NULLIF(LTRIM(RTRIM(ISNULL(@LocationId, N''))), N'');
            DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');

            IF @Mode NOT IN (N'LOCAL', N'CKD')
                THROW 51410, 'Receive mode must be LOCAL or CKD.', 1;
            IF @Barcode = N''
                THROW 51411, 'LOT barcode is required.', 1;

            IF @Location IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.MD_Location WHERE LocationID = @Location)
                THROW 51413, 'Location No was not found.', 1;
            IF @Location IS NOT NULL
               AND EXISTS
            (
                SELECT 1
                FROM dbo.MD_Location
                WHERE LocationID = @Location
                  AND
                  (
                      COALESCE(ActiveFlag, 1) = 0
                      OR UPPER(COALESCE(LocationType, N'')) IN (N'BLOCKED', N'HOLD', N'QUARANTINE', N'INACTIVE')
                  )
            )
                THROW 51418, 'Location is blocked for inbound.', 1;

            IF EXISTS
               (
                   SELECT 1
                   FROM dbo.SCM_DeliveryBox
                   WHERE BoxNumber = @Barcode
                     AND ActiveFlag = 1
               )
            BEGIN
                DECLARE
                    @PortalDeliveryLineID int,
                    @PortalItemNo varchar(20),
                    @PortalPartName nvarchar(200),
                    @PortalQty decimal(18,3),
                    @PortalPoID int,
                    @PortalPoNumber varchar(20),
                    @PortalNoteNo nvarchar(50);

                BEGIN TRANSACTION;

                SELECT TOP (1)
                    @PortalDeliveryLineID = DL.DeliveryLineID,
                    @PortalItemNo = B.ItemNo,
                    @PortalPartName = B.ItemName,
                    @PortalQty = B.Quantity,
                    @PortalPoID = DL.PoID,
                    @PortalPoNumber = PO.PoNumber,
                    @PortalNoteNo = N.NoteNumber
                FROM dbo.SCM_DeliveryBox B
                JOIN dbo.SCM_DeliveryLine DL WITH (UPDLOCK, HOLDLOCK)
                  ON DL.DeliveryLineID = B.DeliveryLineID
                LEFT JOIN dbo.WH_PurchaseOrder PO ON PO.PoID = DL.PoID
                JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
                JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID = D.DeliveryID
                JOIN dbo.SCM_DeliveryNote N ON N.NoteID = ND.NoteID
                WHERE B.BoxNumber = @Barcode
                  AND B.ActiveFlag = 1
                  AND D.Status IN ('Shipped', 'Received')
                ORDER BY N.NoteID DESC;

                IF @PortalDeliveryLineID IS NULL
                    THROW 51421, 'Portal box is not linked to a shipped delivery note.', 1;

                DECLARE @PortalExistingQty decimal(18,3);
                SELECT @PortalExistingQty=Qty
                FROM dbo.WH_Inventory WITH (UPDLOCK, HOLDLOCK)
                WHERE LotNo=@Barcode;
                IF @PortalExistingQty <> 0
                    THROW 51416, 'LOT is already received.', 1;

                -- Legacy cancellations may retain a zero-quantity row; depleted stock is not a cancelled receipt.
                IF @PortalExistingQty = 0 AND NOT EXISTS
                (
                    SELECT 1 FROM
                    (
                        SELECT TOP(1) ReasonCode,SourceType,SourceID
                        FROM dbo.WH_InventoryTransaction WITH (UPDLOCK, HOLDLOCK)
                        WHERE LotNo=@Barcode AND (TransactionType='IN' OR ReasonCode='INBOUND_CANCEL')
                        ORDER BY TransactionID DESC
                    ) LastReceipt
                    WHERE ReasonCode='INBOUND_CANCEL'
                      AND SourceType='SCM_DeliveryLine' AND SourceID=@PortalDeliveryLineID
                )
                    THROW 51416, 'This barcode was already received. Zero stock does not cancel a receipt. Check its transaction history.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.MD_Location L
                    OUTER APPLY
                    (
                        SELECT SUM(W.Qty) AS CurrentQty
                        FROM dbo.WH_Inventory W WITH (UPDLOCK, HOLDLOCK)
                        WHERE W.LocationNo = L.LocationID COLLATE DATABASE_DEFAULT
                    ) S
                    WHERE @Location IS NOT NULL
                      AND L.LocationID = @Location
                      AND COALESCE(L.Capacity, 0) > 0
                      AND COALESCE(S.CurrentQty, 0) + @PortalQty > L.Capacity
                )
                    THROW 51419, 'Location capacity would be exceeded.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.SCM_DeliveryLine
                    WHERE DeliveryLineID = @PortalDeliveryLineID
                      AND COALESCE(ReceivedQty, 0) + @PortalQty > Quantity
                )
                    THROW 51420, 'Portal delivery received quantity would exceed the remaining quantity.', 1;

                IF @PortalExistingQty IS NULL
                    INSERT dbo.WH_Inventory
                        (LotNo, UnitType, PartNo, PartName, LocationNo, Qty,
                         InvoiceNo, DeliveryNoteNo, ReceivedAt, CreatedAt, UpdatedAt)
                    VALUES
                        (@Barcode, 'BOX', @PortalItemNo, @PortalPartName, @Location, @PortalQty,
                         @PortalPoNumber, @PortalNoteNo, SYSDATETIME(), SYSDATETIME(), SYSDATETIME());
                ELSE
                    UPDATE dbo.WH_Inventory
                    SET UnitType='BOX',PartNo=@PortalItemNo,PartName=@PortalPartName,
                        LocationNo=@Location,Qty=@PortalQty,ParentLotNo=NULL,PalletNo=NULL,
                        InvoiceNo=@PortalPoNumber,DeliveryNoteNo=@PortalNoteNo,
                        ReceivedAt=SYSDATETIME(),UpdatedAt=SYSDATETIME()
                    WHERE LotNo=@Barcode AND Qty=0;

                UPDATE dbo.WH_PurchaseOrder
                   SET ReceivedQty = COALESCE(ReceivedQty, 0) + @PortalQty,
                       Status = CASE
                           WHEN COALESCE(ReceivedQty, 0) + @PortalQty >= COALESCE(OrderQty, 0) THEN 'Complete'
                           ELSE 'Open'
                       END,
                       ModifiedBy = LEFT(@User, 20),
                       ModifiedTS = SYSDATETIME()
                 WHERE PoID = @PortalPoID;

                UPDATE dbo.SCM_DeliveryLine
                   SET ReceivedQty = COALESCE(ReceivedQty, 0) + @PortalQty
                 WHERE DeliveryLineID = @PortalDeliveryLineID;

                UPDATE D
                   SET Status = 'Received',
                       ModifiedBy = LEFT(@User, 20),
                       ModifiedTS = SYSDATETIME()
                FROM dbo.SCM_Delivery D
                WHERE D.DeliveryID =
                (
                    SELECT DeliveryID
                    FROM dbo.SCM_DeliveryLine
                    WHERE DeliveryLineID = @PortalDeliveryLineID
                )
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.SCM_DeliveryLine Remaining
                      WHERE Remaining.DeliveryID = D.DeliveryID
                        AND Remaining.ReceivedQty < Remaining.Quantity
                  );

                INSERT dbo.WH_InventoryTransaction
                    (TransactionType, PartNo, LocationNo, LotNo, QtyBefore, QtyChange, QtyAfter,
                     ReasonCode, SourceType, SourceID, OperatorID, Note, CreatedBy, CreatedTS)
                VALUES
                    ('IN', @PortalItemNo, @Location, @Barcode, 0, @PortalQty, @PortalQty,
                     'INBOUND_RECEIVE', 'SCM_DeliveryLine', @PortalDeliveryLineID, @User,
                     CONCAT('PDA delivery note inbound ', @PortalNoteNo, ' / ', @Barcode),
                     LEFT(@User, 20), SYSDATETIME());

                IF @SimulateFailure = 1
                BEGIN
                    ROLLBACK TRANSACTION;
                    THROW 51518, 'Simulated receive API failure. Database transaction was rolled back.', 1;
                END;

                COMMIT TRANSACTION;


                GOTO PdaInboundResult;
            END;


            DECLARE @LotID int,@PartNo varchar(20),@PartName nvarchar(200),@Qty decimal(18,3),@PoID int,@Invoice varchar(20);
            SELECT TOP(1) @LotID=L.LotID,@PartNo=L.ItemNo,@PartName=I.ItemName,
                @Qty=COALESCE(NULLIF(L.RemainingQty,0),NULLIF(L.BatchSize,0),0)
            FROM dbo.tbl_Lot L LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
            WHERE L.LotCode COLLATE DATABASE_DEFAULT=@Barcode ORDER BY L.LotID DESC;
            IF @LotID IS NULL THROW 51414,'Barcode was not found in inbound source tables.',1;
            IF @Qty<=0 THROW 51415,'LOT quantity must be greater than zero.',1;
            BEGIN TRANSACTION;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode)
                THROW 51416,'This barcode has already been received. Check its transaction history.',1;
            IF EXISTS(
                SELECT 1 FROM (
                    SELECT TOP(1) ReasonCode,SourceType FROM dbo.WH_InventoryTransaction WITH(UPDLOCK,HOLDLOCK)
                    WHERE LotNo=@Barcode AND (TransactionType='IN' OR ReasonCode='INBOUND_CANCEL')
                    ORDER BY TransactionID DESC
                ) LastReceipt WHERE COALESCE(ReasonCode,'')<>'INBOUND_CANCEL' OR COALESCE(SourceType,'')<>'LOT')
                THROW 51416,'This LOT has a receipt history that was not cancelled. Check its transaction history.',1;
            IF EXISTS(SELECT 1 FROM dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK)
                WHERE L.LocationID=@Location AND L.Capacity>0 AND
                    (SELECT COALESCE(SUM(W.Qty),0) FROM dbo.WH_Inventory W WITH(UPDLOCK,HOLDLOCK)
                     WHERE W.LocationNo=L.LocationID COLLATE DATABASE_DEFAULT)+@Qty>L.Capacity)
                THROW 51419,'Location capacity would be exceeded.',1;
            SELECT TOP(1) @PoID=PoID,@Invoice=PoNumber FROM dbo.WH_PurchaseOrder WITH(UPDLOCK,HOLDLOCK)
                WHERE ItemNo=@PartNo AND OrderQty>COALESCE(ReceivedQty,0)
                  AND Status NOT IN('Cancelled','Canceled')
                ORDER BY DueDate,PoID;
            INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,PartName,LocationNo,Qty,InvoiceNo,ReceivedAt,CreatedAt,UpdatedAt)
                VALUES(@Barcode,'PART',@PartNo,@PartName,@Location,@Qty,@Invoice,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
            INSERT dbo.WH_InventoryTransaction(TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
            VALUES('IN',@PartNo,@Location,@Barcode,0,@Qty,@Qty,'INBOUND_RECEIVE','LOT',@PoID,@User,@Invoice,LEFT(@User,20),SYSDATETIME());
            IF @PoID IS NOT NULL
                UPDATE dbo.WH_PurchaseOrder SET ReceivedQty=COALESCE(ReceivedQty,0)+@Qty,
                    Status=CASE WHEN COALESCE(ReceivedQty,0)+@Qty>=OrderQty THEN 'Complete' ELSE 'Open' END,
                    ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME() WHERE PoID=@PoID;
            UPDATE dbo.tbl_Lot SET CurrentLocationID=@Location,RemainingQty=@Qty,ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
                WHERE LotID=@LotID;
            IF @SimulateFailure=1 THROW 51518,'Simulated receive API failure. Database transaction was rolled back.',1;
            COMMIT TRANSACTION;

        END;
        PdaInboundResult:
        """ + InboundScanResult + """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;

        """;

    public const string InboundMove = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON; SET XACT_ABORT ON;
            DECLARE @Barcode nvarchar(50)=LTRIM(RTRIM(ISNULL(@LotBarcode,N'')));
            DECLARE @Location varchar(50)=NULLIF(LTRIM(RTRIM(@LocationId)),N'');
            DECLARE @User nvarchar(40)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA');
            DECLARE @BeforeLocation varchar(50),@PartNo varchar(50),@Qty decimal(18,3),@LotID int;
            IF @Location IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.MD_Location WHERE LocationID=@Location AND COALESCE(ActiveFlag,1)=1)
                THROW 51406, 'Location barcode was not found.', 1;
            IF UPPER(LTRIM(RTRIM(@ReceiveMode))) NOT IN('LOCAL','CKD') THROW 51420,'Receive mode must be LOCAL or CKD.',1;
            IF EXISTS(SELECT 1 FROM dbo.MD_Location WHERE LocationID=@Location AND
                (COALESCE(ActiveFlag,1)=0 OR UPPER(COALESCE(LocationType,'')) IN('BLOCKED','HOLD','QUARANTINE','INACTIVE')))
                THROW 51423,'Location is blocked for put-away.',1;
            BEGIN TRANSACTION;
            SELECT @BeforeLocation=LocationNo,@PartNo=PartNo,@Qty=Qty FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode AND Qty>0;
            IF @PartNo IS NULL THROW 51408, 'This barcode has not been received yet.', 1;
            IF @BeforeLocation=@Location THROW 51426,'LOT is already in this location.',1;
            IF EXISTS(SELECT 1 FROM dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK)
                WHERE L.LocationID=@Location AND L.Capacity>0 AND
                    (SELECT COALESCE(SUM(W.Qty),0) FROM dbo.WH_Inventory W WITH(UPDLOCK,HOLDLOCK)
                     WHERE W.LocationNo=L.LocationID COLLATE DATABASE_DEFAULT)+@Qty>L.Capacity)
                THROW 51428,'Location capacity would be exceeded.',1;
            SELECT TOP(1) @LotID=LotID FROM dbo.tbl_Lot WHERE LotCode COLLATE DATABASE_DEFAULT=@Barcode ORDER BY LotID DESC;
            UPDATE dbo.WH_Inventory SET LocationNo=@Location,UpdatedAt=SYSDATETIME() WHERE LotNo=@Barcode;
            IF @LotID IS NOT NULL UPDATE dbo.tbl_Lot SET CurrentLocationID=@Location,InventoryStatus='STORED',ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME() WHERE LotID=@LotID;
            INSERT dbo.WH_InventoryTransaction
                (TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
            VALUES(SYSDATETIME(),'MOVE',@PartNo,@Location,@Barcode,@Qty,0,@Qty,'PUT_AWAY','LOT',@User,
                   CONCAT('Moved from ',COALESCE(@BeforeLocation,'(unassigned)'),' to ',@Location),LEFT(@User,20),SYSDATETIME());
            COMMIT TRANSACTION;

        END;
        PdaInboundResult:
        """ + InboundScanResult + """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;

        """;

    public const string InboundCancel = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @Mode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
            DECLARE @Barcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));
            DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
            DECLARE @LotID int;
            DECLARE @ActualMode nvarchar(10);
            DECLARE @Qty decimal(14,3);
            DECLARE @PoID int;

            IF @Mode NOT IN (N'LOCAL', N'CKD')
                THROW 51430, 'Receive mode must be LOCAL or CKD.', 1;
            IF @Barcode = N''
                THROW 51431, 'LOT barcode is required.', 1;

            IF EXISTS
               (
                   SELECT 1
                   FROM dbo.SCM_DeliveryBox
                   WHERE BoxNumber = @Barcode
                     AND ActiveFlag = 1
               )
            BEGIN
                DECLARE
                    @PortalDeliveryLineID int,
                    @PortalItemNo varchar(20),
                    @PortalLocation varchar(50),
                    @PortalQty decimal(18,3),
                    @PortalReceiptQty decimal(18,3),
                    @PortalPoID int,
                    @PortalNoteNo nvarchar(30);

                BEGIN TRANSACTION;

                SELECT TOP (1)
                    @PortalDeliveryLineID = B.DeliveryLineID,
                    @PortalItemNo = W.PartNo,
                    @PortalLocation = W.LocationNo,
                    @PortalQty = W.Qty,
                    @PortalReceiptQty = B.Quantity,
                    @PortalNoteNo = W.DeliveryNoteNo
                FROM dbo.WH_Inventory W WITH (UPDLOCK, HOLDLOCK)
                JOIN dbo.SCM_DeliveryBox B ON B.BoxNumber = W.LotNo AND B.ActiveFlag = 1
                JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
                JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID = DL.DeliveryID
                JOIN dbo.SCM_DeliveryNote N ON N.NoteID = ND.NoteID
                  AND N.NoteNumber COLLATE DATABASE_DEFAULT = W.DeliveryNoteNo COLLATE DATABASE_DEFAULT
                WHERE W.LotNo = @Barcode
                  AND W.DeliveryNoteNo IS NOT NULL
                  AND W.Qty > 0;

                IF @PortalDeliveryLineID IS NULL
                    THROW 51433, 'LOT is not received yet.', 1;

                SELECT @PortalPoID = PoID
                FROM dbo.SCM_DeliveryLine
                WHERE DeliveryLineID = @PortalDeliveryLineID;

                DELETE FROM dbo.WH_Inventory
                WHERE LotNo = @Barcode
                  AND DeliveryNoteNo = @PortalNoteNo;

                UPDATE PO
                   SET ReceivedQty = X.NewReceivedQty,
                       Status = CASE
                           WHEN X.NewReceivedQty <= 0 THEN 'Open'
                           WHEN X.NewReceivedQty >= COALESCE(PO.OrderQty, 0) THEN 'Received'
                           ELSE 'Partial'
                       END,
                       ModifiedBy = LEFT(@User, 20),
                       ModifiedTS = SYSDATETIME()
                FROM dbo.WH_PurchaseOrder PO
                CROSS APPLY (VALUES
                (
                    CASE WHEN COALESCE(PO.ReceivedQty, 0) < @PortalReceiptQty
                         THEN 0 ELSE PO.ReceivedQty - @PortalReceiptQty END
                )) X(NewReceivedQty)
                WHERE PO.PoID = @PortalPoID;

                UPDATE dbo.SCM_DeliveryLine
                   SET ReceivedQty = CASE
                           WHEN COALESCE(ReceivedQty, 0) - @PortalReceiptQty < 0 THEN 0
                           ELSE COALESCE(ReceivedQty, 0) - @PortalReceiptQty
                       END
                 WHERE DeliveryLineID = @PortalDeliveryLineID;

                UPDATE D
                   SET Status = 'Shipped',
                       ModifiedBy = LEFT(@User, 20),
                       ModifiedTS = SYSDATETIME()
                FROM dbo.SCM_Delivery D
                WHERE D.Status = 'Received'
                  AND D.DeliveryID =
                  (
                      SELECT DeliveryID
                      FROM dbo.SCM_DeliveryLine
                      WHERE DeliveryLineID = @PortalDeliveryLineID
                  );

                INSERT dbo.WH_InventoryTransaction
                    (TransactionType, PartNo, LocationNo, LotNo, QtyBefore, QtyChange, QtyAfter,
                     ReasonCode, SourceType, SourceID, OperatorID, Note, CreatedBy, CreatedTS)
                VALUES
                    ('ADJ', @PortalItemNo, @PortalLocation, @Barcode, @PortalQty, -@PortalQty, 0,
                     'INBOUND_CANCEL', 'SCM_DeliveryLine', @PortalDeliveryLineID, @User,
                     CONCAT('PDA delivery note inbound cancel ', @PortalNoteNo, ' / ', @Barcode),
                     LEFT(@User, 20), SYSDATETIME());

                COMMIT TRANSACTION;


                GOTO PdaInboundResult;
            END;


            DECLARE @PartNo varchar(20),@Location varchar(50),@Invoice varchar(20),@Po int,
                @ReceiptID bigint,@ReceiptQty decimal(18,3),@ReceiptPart varchar(20),@ExpectedQty decimal(18,3);
            BEGIN TRANSACTION;
            SELECT @PartNo=PartNo,@Location=LocationNo,@Qty=Qty,@Invoice=InvoiceNo
                FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode AND Qty>=0;
            IF @PartNo IS NULL THROW 51433,'LOT is not received yet.',1;
            SELECT TOP(1) @ReceiptID=TransactionID,@Po=SourceID,@ReceiptQty=QtyChange,@ReceiptPart=PartNo
            FROM dbo.WH_InventoryTransaction WITH(UPDLOCK,HOLDLOCK)
            WHERE LotNo=@Barcode AND TransactionType='IN' AND SourceType='LOT' AND ReasonCode='INBOUND_RECEIVE'
            ORDER BY TransactionID DESC;
            IF @ReceiptID IS NULL OR @ReceiptQty IS NULL OR @ReceiptQty<=0 OR @ReceiptPart IS NULL OR @ReceiptPart<>@PartNo
                THROW 51434,'The original LOT receipt could not be verified. Cancellation was not saved.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction WITH(UPDLOCK,HOLDLOCK)
                WHERE LotNo=@Barcode AND TransactionID>@ReceiptID
                  AND (ReasonCode='INBOUND_CANCEL' OR (QtyChange<>0 AND COALESCE(TransactionType,'')<>'ADJ')))
                THROW 51434,'This LOT was released, consumed or received again. Reverse those transactions before cancelling its receipt.',1;
            SELECT @ExpectedQty=@ReceiptQty+COALESCE(SUM(QtyChange),0)
            FROM dbo.WH_InventoryTransaction
            WHERE LotNo=@Barcode AND TransactionID>@ReceiptID AND TransactionType='ADJ';
            IF @ExpectedQty<>@Qty
                THROW 51435,'LOT stock does not match its receipt and adjustment history. Cancellation was not saved.',1;
            IF @Po IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.WH_PurchaseOrder WITH(UPDLOCK,HOLDLOCK)
                WHERE PoID=@Po AND COALESCE(ReceivedQty,0)>=@ReceiptQty)
                THROW 51435,'The PO receipt balance is inconsistent. Cancellation was not saved.',1;
            DELETE dbo.WH_Inventory WHERE LotNo=@Barcode;
            INSERT dbo.WH_InventoryTransaction(TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
                VALUES('OUT',@PartNo,@Location,@Barcode,@Qty,-@Qty,0,'INBOUND_CANCEL','LOT',@Po,@User,
                    CONCAT('Inbound receipt cancelled; PO receipt reversed: ',@ReceiptQty),LEFT(@User,20),SYSDATETIME());
            IF @Po IS NOT NULL UPDATE dbo.WH_PurchaseOrder SET
                ReceivedQty=ReceivedQty-@ReceiptQty,
                Status=CASE WHEN Status IN('Cancelled','Canceled') THEN Status
                    WHEN ReceivedQty-@ReceiptQty>=OrderQty THEN 'Complete' ELSE 'Open' END,
                ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME() WHERE PoID=@Po;
            UPDATE dbo.tbl_Lot SET CurrentLocationID=NULL,ModifiedBy=LEFT(@User,20),ModifiedTS=SYSDATETIME()
                WHERE LotCode COLLATE DATABASE_DEFAULT=@Barcode;
            COMMIT TRANSACTION;

        END;
        PdaInboundResult:
        """ + InboundScanResult + """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;

        """;

    public const string AdjustSave = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @Scan nvarchar(100) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
            DECLARE @Reason nvarchar(30) = UPPER(LTRIM(RTRIM(ISNULL(@ReasonCode, N''))));
            DECLARE @Note nvarchar(500) = NULLIF(LTRIM(RTRIM(@ReasonNote)), N'');
            DECLARE @User nvarchar(40) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
            DECLARE @LotNo nvarchar(50);

            IF @Scan = N''
                THROW 51510, 'Barcode is required.', 1;
            IF EXISTS
               (SELECT 1
                FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo
                WHERE UPPER(W.LotNo) = UPPER(@Scan)
                  AND UPPER(COALESCE(L.AreaCode, N'')) = N'FG_AREA')
                THROW 51519, 'Finished goods cannot be adjusted in Warehouse Adjust.', 1;
            IF COALESCE(@DeltaQty, 0) = 0
                THROW 51511, 'Adjustment quantity must be different from zero.', 1;
            IF @Reason = N''
                THROW 51512, 'Reason code is required.', 1;
            IF @Reason IN (N'DAMAGED', N'DEFECT', N'DEFECTIVE')
                THROW 51521, 'Use Inventory Defect to record defective stock, not quantity adjustment.', 1;
            IF NOT EXISTS
               (SELECT 1 FROM dbo.MD_CodeItem
                WHERE GroupCode='INV_ADJUST_REASON' AND CodeValue=@Reason AND ISNULL(UseFlag,1)=1)
                THROW 51520, 'Unsupported inventory adjustment reason.', 1;

            DECLARE
                @ItemNo varchar(20),
                @LocationID varchar(50),
                @BeforeQty decimal(18,3),
                @AfterQty decimal(18,3);

            BEGIN TRAN;

            SELECT TOP (1)
                @LotNo = W.LotNo,
                @ItemNo = LEFT(W.PartNo, 20),
                @LocationID = W.LocationNo,
                @BeforeQty = COALESCE(W.Qty, 0)
            FROM dbo.WH_Inventory W WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo
            WHERE UPPER(W.LotNo) = UPPER(@Scan)
              AND UPPER(COALESCE(L.AreaCode, N'')) <> N'FG_AREA'
            ORDER BY CASE WHEN W.Qty > 0 THEN 0 ELSE 1 END, W.ReceivedAt DESC, W.LotNo;

            IF @LotNo IS NULL
                THROW 51516, 'The specified barcode could not be found in Warehouse inventory.', 1;

            SET @AfterQty = @BeforeQty + @DeltaQty;

            IF @AfterQty < 0
                THROW 51517, 'After Qty cannot be below zero.', 1;
            IF @AfterQty <> FLOOR(@AfterQty) OR @AfterQty > 999999999
                THROW 51521, 'New quantity must be a whole number from 0 to 999999999.', 1;

            UPDATE dbo.WH_Inventory
               SET Qty = @AfterQty,
                   UpdatedAt = SYSDATETIME()
             WHERE LotNo = @LotNo;

            IF @SimulateFailure = 1
                THROW 51520, 'Simulated Adjust API failure. Database transaction was rolled back.', 1;

            INSERT INTO dbo.WH_InventoryTransaction
                (TransactionType, PartNo, LocationNo, LotNo, QtyBefore, QtyChange, QtyAfter,
                 ReasonCode, SourceType, SourceID, OperatorID, ApproverID, Note, CreatedBy, CreatedTS)
            VALUES
                (N'ADJ', @ItemNo, @LocationID, @LotNo, @BeforeQty, @DeltaQty, @AfterQty,
                 CONVERT(varchar(30), @Reason), N'LOT', NULL,
                 @User, NULL, @Note, LEFT(@User, 20), SYSDATETIME());

            COMMIT TRAN;
        """ + AdjustScanResult + """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        END;
        """;

    private const string InboundScanResult = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            DECLARE @ResultMode nvarchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@ReceiveMode, N''))));
            DECLARE @ResultBarcode nvarchar(50) = LTRIM(RTRIM(ISNULL(@LotBarcode, N'')));

            IF @ResultMode NOT IN (N'LOCAL', N'CKD') THROW 51400, 'Receive mode must be LOCAL or CKD.', 1;
            IF @ResultBarcode = N'' THROW 51401, 'Barcode is required.', 1;

            IF EXISTS (SELECT 1 FROM dbo.SCM_DeliveryBox WHERE BoxNumber = @ResultBarcode AND ActiveFlag = 1)
            BEGIN
                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.SCM_DeliveryBox B
                    JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
                    JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
                    WHERE B.BoxNumber = @ResultBarcode AND B.ActiveFlag = 1
                      AND D.Status IN ('Shipped', 'Received')
                )
                    THROW 51404, 'Only boxes from a shipped delivery note can be received.', 1;

                SELECT TOP (1)
                    @ResultMode AS RECEIVE_TYPE, CASE WHEN W.LotNo IS NULL THEN N'Y' ELSE N'N' END AS YN,
                    COALESCE(NULLIF(DL.VendorLotNo, N''), B.BoxNumber) AS LOTNO,
                    B.BoxNumber AS BARCODE, N'dbo.SCM_DeliveryBox' AS SOURCE_TABLE,
                    N.NoteNumber AS NOTENO, CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
                    CAST(NULL AS nvarchar(50)) AS CASE_NO, PO.PoNumber AS INVOICE_NO,
                    CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
                    B.ItemNo AS PARTNO, B.ItemName AS PARTNM,
                    COALESCE(W.Qty, B.Quantity) AS QTY, B.UnitCode AS UNIT,
                    PO.PoNumber AS PONO, PO.PoLineNo AS PONO_SEQ,
                    D.VendorID AS VENDCD, COALESCE(V.VendorName, D.VendorID) AS VENDNM,
                    DL.ProductionDate AS PROD_DATE, D.DeliveryDate AS DELI_DATE,
                    D.DeliveryDate AS ARRIV_DATE, D.ShipDate AS SHIP_DATE,
                    CAST(NULL AS date) AS PACK_DATE, W.LocationNo AS RECEIVED_LOCATION,
                    CASE WHEN W.LotNo IS NULL THEN N'Open' ELSE N'Received' END AS RECEIVED_STATUS
                FROM dbo.SCM_DeliveryBox B
                JOIN dbo.SCM_DeliveryLine DL ON DL.DeliveryLineID = B.DeliveryLineID
                JOIN dbo.SCM_Delivery D ON D.DeliveryID = DL.DeliveryID
                JOIN dbo.SCM_DeliveryNoteDelivery ND ON ND.DeliveryID = D.DeliveryID
                JOIN dbo.SCM_DeliveryNote N ON N.NoteID = ND.NoteID
                LEFT JOIN dbo.WH_PurchaseOrder PO ON PO.PoID = DL.PoID
                LEFT JOIN dbo.MD_Vendor V ON V.VendorID = D.VendorID
                LEFT JOIN dbo.WH_Inventory W ON W.LotNo = B.BoxNumber AND W.Qty > 0
                WHERE B.BoxNumber = @ResultBarcode AND B.ActiveFlag = 1
                ORDER BY N.NoteID DESC;
                RETURN;
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.tbl_Lot WHERE LotCode COLLATE DATABASE_DEFAULT = @ResultBarcode)
                THROW 51402, 'Barcode was not found in inbound source tables.', 1;

            SELECT TOP (1)
                @ResultMode AS RECEIVE_TYPE, CASE WHEN W.LotNo IS NULL THEN N'Y' ELSE N'N' END AS YN,
                L.LotCode AS LOTNO, L.LotCode AS BARCODE, N'dbo.tbl_Lot' AS SOURCE_TABLE,
                PO.PoNumber AS NOTENO, CAST(NULL AS nvarchar(50)) AS CASE_BARCODE,
                CAST(NULL AS nvarchar(50)) AS CASE_NO, PO.PoNumber AS INVOICE_NO,
                CAST(NULL AS nvarchar(50)) AS CONTAINER_NO,
                L.ItemNo AS PARTNO, I.ItemName AS PARTNM,
                COALESCE(W.Qty, NULLIF(L.RemainingQty, 0), NULLIF(L.BatchSize, 0), PO.OrderQty, 0) AS QTY,
                COALESCE(PO.UnitCode, I.DefaultUOM, 'EA') AS UNIT,
                PO.PoNumber AS PONO, PO.PoLineNo AS PONO_SEQ,
                PO.VendorID AS VENDCD, COALESCE(V.VendorName, PO.VendorID) AS VENDNM,
                CONVERT(date, L.ProducedAt) AS PROD_DATE, PO.DueDate AS DELI_DATE,
                PO.DueDate AS ARRIV_DATE, CAST(NULL AS date) AS SHIP_DATE,
                CAST(NULL AS date) AS PACK_DATE, W.LocationNo AS RECEIVED_LOCATION,
                CASE WHEN W.LotNo IS NULL THEN N'Open' ELSE N'Received' END AS RECEIVED_STATUS
            FROM dbo.tbl_Lot L
            LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT = L.ItemNo COLLATE DATABASE_DEFAULT
            OUTER APPLY
            (
                SELECT TOP (1) P.* FROM dbo.WH_PurchaseOrder P
                WHERE P.ItemNo COLLATE DATABASE_DEFAULT = L.ItemNo COLLATE DATABASE_DEFAULT
                ORDER BY CASE WHEN COALESCE(P.OrderQty,0) > COALESCE(P.ReceivedQty,0) THEN 0 ELSE 1 END,
                         P.DueDate, P.PoID
            ) PO
            LEFT JOIN dbo.MD_Vendor V ON V.VendorID = PO.VendorID
            LEFT JOIN dbo.WH_Inventory W ON W.LotNo COLLATE DATABASE_DEFAULT = L.LotCode COLLATE DATABASE_DEFAULT AND W.Qty > 0
            WHERE L.LotCode COLLATE DATABASE_DEFAULT = @ResultBarcode
            ORDER BY L.LotID DESC;
        END;
        """;

    private const string AdjustScanResult = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;

            DECLARE @ResultScan nvarchar(100) = LTRIM(RTRIM(ISNULL(@ScanText, N'')));
            DECLARE @ResultLotNo nvarchar(50);

            IF @ResultScan = N''
                THROW 51500, 'Barcode is required.', 1;

            IF EXISTS
               (SELECT 1
                FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo
                WHERE UPPER(W.LotNo) = UPPER(@ResultScan)
                  AND UPPER(COALESCE(L.AreaCode, N'')) = N'FG_AREA')
                THROW 51505, 'Finished goods cannot be adjusted in Warehouse Adjust.', 1;

            SELECT TOP (1)
                @ResultLotNo = W.LotNo
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Location L ON L.LocationID COLLATE DATABASE_DEFAULT = W.LocationNo
            WHERE UPPER(W.LotNo) = UPPER(@ResultScan)
              AND UPPER(COALESCE(L.AreaCode, N'')) <> N'FG_AREA'
            ORDER BY CASE WHEN W.Qty > 0 THEN 0 ELSE 1 END, W.ReceivedAt DESC, W.LotNo;

            IF @ResultLotNo IS NULL
                THROW 51501, 'The specified barcode could not be found in Warehouse inventory.', 1;

            SELECT
                N'LOCAL' AS RECEIVE_TYPE,
                N'N' AS YN,
                W.LotNo AS LOTNO,
                W.LotNo AS BARCODE,
                N'dbo.WH_Inventory' AS SOURCE_TABLE,
                W.DeliveryNoteNo AS NOTENO,
                W.PalletNo AS CASE_BARCODE,
                W.PalletNo AS CASE_NO,
                W.InvoiceNo AS INVOICE_NO,
                W.ParentLotNo AS CONTAINER_NO,
                W.PartNo AS PARTNO,
                COALESCE(NULLIF(W.PartName, N''), I.ItemName COLLATE DATABASE_DEFAULT) AS PARTNM,
                W.Qty AS QTY,
                COALESCE(NULLIF(I.DefaultUOM, N''), N'EA') AS UNIT,
                W.InvoiceNo AS PONO,
                CAST(NULL AS int) AS PONO_SEQ,
                CAST(NULL AS nvarchar(30)) AS VENDCD,
                CAST(NULL AS nvarchar(100)) AS VENDNM,
                CAST(NULL AS date) AS PROD_DATE,
                CAST(NULL AS date) AS DELI_DATE,
                CONVERT(date, W.ReceivedAt) AS ARRIV_DATE,
                CAST(NULL AS date) AS SHIP_DATE,
                CAST(NULL AS date) AS PACK_DATE,
                W.LocationNo AS RECEIVED_LOCATION,
                N'Received' AS RECEIVED_STATUS
            FROM dbo.WH_Inventory W
            LEFT JOIN dbo.MD_Item I ON I.ItemNo COLLATE DATABASE_DEFAULT = W.PartNo
            WHERE W.LotNo = @ResultLotNo;
        END;
        """;

}
