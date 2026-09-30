using System.Data;
using System.Text.Json;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class FinishedGoodsRepository
{
    private readonly AmesConnectionFactory _factory;

    public FinishedGoodsRepository(AmesConnectionFactory factory) => _factory = factory;

    public record ReturnRow(
        int ReturnId,
        string ReturnNumber,
        string? RmaNo,
        string? CustomerCode,
        int? ShipmentOrderId,
        string? ShipmentOrderNumber,
        string? CustomerPo,
        string? ItemNo,
        decimal Qty,
        string? ReturnReason,
        string? Status,
        DateTime? ReceivedAt,
        string? ReceivedBy,
        bool CapaTriggered,
        string? ItemsJson);

    public record ShipmentRow(
        long TransactionId,
        DateTime ShippedAt,
        string OutboundBarcode,
        string UnitType,
        string LotNo,
        string? PartNo,
        string? PartName,
        string? LocationNo,
        decimal Qty,
        string? OperatorId,
        string Status);

    public record ShipmentPlanSourceRow(
        int SoId,
        string? SoNumber,
        int? SoLineNo,
        string? CustomerCode,
        string ItemNo,
        string? ItemName,
        decimal OrderQty,
        decimal ShippedQty,
        decimal PlannedQty,
        decimal RemainingQty,
        DateTime? RequestedDeliveryDate);

    public record ShipmentPlanRow(
        int ShipmentOrderId,
        string? PlanNumber,
        string? CustomerCode,
        string? SourceOrderNumber,
        DateTime? ShipDate,
        string? Status,
        int LineCount,
        decimal PlannedQty,
        DateTime? CreatedAt);

    public record ShipmentPlanLineRow(
        int ShipmentOrderLineId,
        int SourceSoId,
        string? SourceOrderNumber,
        int? SourceLineNo,
        string ItemNo,
        string? ItemName,
        decimal PlannedQty,
        DateTime? RequestedDeliveryDate);

    public record ShipmentPlanInput(int SoId, decimal Qty);

    public record ShipmentPlanCreateResult(
        IReadOnlyList<string> PlanNumbers,
        int LineCount,
        decimal TotalQty);

    public record ShipmentDocument(
        int ShipmentOrderId, string? ShipOrderNumber, string? CustomerCode, string? CustomerName,
        string? CustomerAddress, string? CustomerPo, string? DestPlant, string? DestDock,
        DateTime? ShipDate, int? LoadingId, string? LoadingNumber, string? LicensePlate,
        string? DriverName, DateTime? ArrivalAt, DateTime? ConfirmedAt,
        string? DeliveryNoteNumber, DateTime? DeliveryNoteIssuedAt, string? EdiStatus,
        IReadOnlyList<ShipmentDocumentLine> Lines);

    public record ShipmentDocumentLine(
        int LineSeq, string ItemNo, string? ItemName, string? CustomerItemNo,
        decimal OrderedQty, decimal DeliveryQty, decimal UnitPackQty, string? Unit,
        string? LotNo, string? StockNumber, string? Location, DateTime? ProducedAt);


    public record HistoryRow(
        DateTime EventAt,
        string EventType,
        string? ReferenceNo,
        string? ItemOrOrder,
        decimal Qty,
        string? Location,
        string? WorkerId,
        string? Status,
        string? Details);

    public List<ReturnRow> ListReturns(
        string? search = null,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        const string sql = """
            SELECT
                R.ReturnID,
                R.ReturnNumber,
                R.RMANo,
                R.CustomerCode,
                R.OriginalShipmentOrderID,
                O.ShipOrderNumber,
                O.CustomerPO,
                COALESCE(R.ItemNo, JSON_VALUE(R.ItemsJSON, '$[0].itemNo')) AS ItemNo,
                COALESCE(R.ReturnQty, TRY_CONVERT(decimal(14,3), JSON_VALUE(R.ItemsJSON, '$[0].qty'))) AS Qty,
                R.ReturnReason,
                R.Status,
                R.ReceivedAt,
                R.ReceivedBy,
                CAST(ISNULL(R.CapaTriggered, 0) AS bit) AS CapaTriggered,
                R.ItemsJSON
            FROM dbo.FG_CustomerReturn R
            LEFT JOIN dbo.FG_ShipmentOrder O ON O.ShipmentOrderID = R.OriginalShipmentOrderID
            WHERE (@Status IS NULL OR UPPER(R.Status) = UPPER(@Status))
              AND (@From IS NULL OR R.ReceivedAt >= @From)
              AND (@To IS NULL OR R.ReceivedAt < DATEADD(day, 1, @To))
              AND (@Search IS NULL
                   OR R.ReturnNumber LIKE @Search
                   OR R.RMANo LIKE @Search
                   OR R.CustomerCode LIKE @Search
                   OR R.ReturnReason LIKE @Search
                   OR R.ReceivedBy LIKE @Search
                   OR O.ShipOrderNumber LIKE @Search
                   OR R.ItemsJSON LIKE @Search)
            ORDER BY R.ReceivedAt DESC, R.ReturnID DESC;
            """;

        return Query(sql, r => new ReturnRow(
            GetInt(r, "ReturnID"),
            GetString(r, "ReturnNumber") ?? "",
            GetString(r, "RMANo"),
            GetString(r, "CustomerCode"),
            GetNullableInt(r, "OriginalShipmentOrderID"),
            GetString(r, "ShipOrderNumber"),
            GetString(r, "CustomerPO"),
            GetString(r, "ItemNo"),
            GetDecimal(r, "Qty"),
            GetString(r, "ReturnReason"),
            GetString(r, "Status"),
            GetDate(r, "ReceivedAt"),
            GetString(r, "ReceivedBy"),
            GetBool(r, "CapaTriggered"),
            GetString(r, "ItemsJSON")),
            ("@Search", Like(search)),
            ("@Status", NullIfBlank(status)),
            ("@From", from?.Date),
            ("@To", to?.Date));
    }

    public List<ShipmentPlanSourceRow> ListShipmentPlanSources(string? search = null)
    {
        const string sql = """
            WITH Planned AS
            (
                SELECT L.LineSeq AS SoID, SUM(ISNULL(L.OrderedQty, 0)) AS PlannedQty
                FROM dbo.FG_ShipmentOrder O
                CROSS APPLY OPENJSON(COALESCE(O.ItemsJSON, N'[]')) WITH
                (
                    LineSeq int '$.lineSeq',
                    OrderedQty decimal(14,3) '$.orderedQty'
                ) L
                WHERE UPPER(ISNULL(O.Source, '')) = 'PP'
                  AND UPPER(ISNULL(O.Status, '')) NOT IN ('CANCELED', 'CANCELLED')
                GROUP BY L.LineSeq
            )
            SELECT
                S.SoID,
                S.SoNumber,
                S.SoLineNo,
                S.CustomerID,
                S.ItemNo,
                I.ItemName,
                CAST(ISNULL(S.OrderQty, 0) AS decimal(14,3)) AS OrderQty,
                CAST(ISNULL(S.ShippedQty, 0) AS decimal(14,3)) AS ShippedQty,
                CAST(ISNULL(P.PlannedQty, 0) AS decimal(14,3)) AS PlannedQty,
                CAST(ISNULL(S.OrderQty, 0) - ISNULL(S.ShippedQty, 0) - ISNULL(P.PlannedQty, 0) AS decimal(14,3)) AS RemainingQty,
                S.RequestedDeliveryDate
            FROM dbo.PP_CustomerOrder S
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = S.ItemNo
            LEFT JOIN Planned P ON P.SoID = S.SoID
            WHERE UPPER(ISNULL(S.Status, 'OPEN')) NOT IN ('CANCELED', 'CANCELLED')
              AND ISNULL(S.OrderQty, 0) - ISNULL(S.ShippedQty, 0) - ISNULL(P.PlannedQty, 0) > 0
              AND (@Search IS NULL
                   OR S.SoNumber LIKE @Search
                   OR S.CustomerID LIKE @Search
                   OR S.ItemNo LIKE @Search
                   OR I.ItemName LIKE @Search)
            ORDER BY COALESCE(S.RequestedDeliveryDate, CONVERT(date, '99991231')),
                     S.SoNumber, S.SoLineNo, S.SoID;
            """;

        return Query(sql, r => new ShipmentPlanSourceRow(
            GetInt(r, "SoID"),
            GetString(r, "SoNumber"),
            GetNullableInt(r, "SoLineNo"),
            GetString(r, "CustomerID"),
            GetString(r, "ItemNo") ?? "",
            GetString(r, "ItemName"),
            GetDecimal(r, "OrderQty"),
            GetDecimal(r, "ShippedQty"),
            GetDecimal(r, "PlannedQty"),
            GetDecimal(r, "RemainingQty"),
            GetDate(r, "RequestedDeliveryDate")),
            ("@Search", Like(search)));
    }

    public List<ShipmentPlanRow> ListShipmentPlans(string? search = null)
    {
        const string sql = """
            SELECT TOP (100)
                O.ShipmentOrderID,
                O.ShipOrderNumber,
                O.CustomerCode,
                O.CustomerPO,
                O.ShipDate,
                O.Status,
                ISNULL(J.LineCount, 0) AS LineCount,
                CAST(ISNULL(J.PlannedQty, 0) AS decimal(14,3)) AS PlannedQty,
                O.CreatedTS
            FROM dbo.FG_ShipmentOrder O
            OUTER APPLY
            (
                SELECT COUNT(*) AS LineCount, SUM(ISNULL(L.OrderedQty, 0)) AS PlannedQty
                FROM OPENJSON(COALESCE(O.ItemsJSON, N'[]')) WITH
                (
                    OrderedQty decimal(14,3) '$.orderedQty'
                ) L
            ) J
            WHERE UPPER(ISNULL(O.Source, '')) = 'PP'
              AND (@Search IS NULL
                   OR O.ShipOrderNumber LIKE @Search
                   OR O.CustomerCode LIKE @Search
                   OR O.CustomerPO LIKE @Search)
            ORDER BY O.CreatedTS DESC, O.ShipmentOrderID DESC;
            """;

        return Query(sql, r => new ShipmentPlanRow(
            GetInt(r, "ShipmentOrderID"),
            GetString(r, "ShipOrderNumber"),
            GetString(r, "CustomerCode"),
            GetString(r, "CustomerPO"),
            GetDate(r, "ShipDate"),
            GetString(r, "Status"),
            GetInt(r, "LineCount"),
            GetDecimal(r, "PlannedQty"),
            GetDate(r, "CreatedTS")),
            ("@Search", Like(search)));
    }

    public List<ShipmentPlanLineRow> ListShipmentPlanLines(int shipmentOrderId)
    {
        const string sql = """
            SELECT
                ISNULL(L.ShipmentOrderLineID, L.ArrayIndex + 1) AS ShipmentOrderLineID,
                L.LineSeq AS SourceSoID,
                S.SoNumber,
                S.SoLineNo,
                L.ItemNo,
                I.ItemName,
                CAST(ISNULL(L.OrderedQty, 0) AS decimal(14,3)) AS PlannedQty,
                S.RequestedDeliveryDate
            FROM dbo.FG_ShipmentOrder O
            CROSS APPLY OPENJSON(COALESCE(O.ItemsJSON, N'[]')) J
            CROSS APPLY
            (
                SELECT
                    TRY_CONVERT(int, J.[key]) AS ArrayIndex,
                    X.ShipmentOrderLineID,
                    X.LineSeq,
                    X.ItemNo,
                    X.OrderedQty
                FROM OPENJSON(J.[value]) WITH
                (
                    ShipmentOrderLineID int '$.shipmentOrderLineId',
                    LineSeq int '$.lineSeq',
                    ItemNo varchar(20) '$.itemNo',
                    OrderedQty decimal(14,3) '$.orderedQty'
                ) X
            ) L
            LEFT JOIN dbo.PP_CustomerOrder S ON S.SoID = L.LineSeq
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = L.ItemNo
            WHERE O.ShipmentOrderID = @ShipmentOrderID
            ORDER BY S.SoNumber, S.SoLineNo, L.ArrayIndex;
            """;

        return Query(sql, r => new ShipmentPlanLineRow(
            GetInt(r, "ShipmentOrderLineID"),
            GetInt(r, "SourceSoID"),
            GetString(r, "SoNumber"),
            GetNullableInt(r, "SoLineNo"),
            GetString(r, "ItemNo") ?? "",
            GetString(r, "ItemName"),
            GetDecimal(r, "PlannedQty"),
            GetDate(r, "RequestedDeliveryDate")),
            ("@ShipmentOrderID", shipmentOrderId));
    }

    public string DeleteShipmentPlan(int shipmentOrderId)
    {
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            using var readCmd = new SqlCommand("""
                SELECT ShipOrderNumber, Source, Status,
                       CASE WHEN EXISTS (SELECT 1 FROM dbo.FG_CustomerReturn WHERE OriginalShipmentOrderID=@ID)
                             THEN 1 ELSE 0 END AS HasDependencies
                FROM dbo.FG_ShipmentOrder WITH (UPDLOCK, HOLDLOCK)
                WHERE ShipmentOrderID=@ID;
                """, conn, tx);
            readCmd.Parameters.Add("@ID", SqlDbType.Int).Value = shipmentOrderId;
            using var reader = readCmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Shipment plan was not found.");

            var planNumber = GetString(reader, "ShipOrderNumber") ?? shipmentOrderId.ToString();
            var source = GetString(reader, "Source");
            var status = GetString(reader, "Status");
            var hasDependencies = GetBool(reader, "HasDependencies");
            reader.Close();
            ValidateShipmentPlanDelete(source, status, hasDependencies);

            using var deleteCmd = new SqlCommand("""
                DELETE dbo.FG_ShipmentOrder WHERE ShipmentOrderID=@ID;
                """, conn, tx);
            deleteCmd.Parameters.Add("@ID", SqlDbType.Int).Value = shipmentOrderId;
            deleteCmd.ExecuteNonQuery();
            tx.Commit();
            return planNumber;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public ShipmentPlanCreateResult CreateShipmentPlans(
        DateTime shipDate,
        string customerCode,
        IReadOnlyList<ShipmentPlanInput> inputs,
        string actor)
    {
        var requested = NormalizeShipmentPlanInputs(inputs);

        if (requested.Count == 0)
            throw new InvalidOperationException("Select at least one supply plan line and enter a quantity greater than zero.");
        if (string.IsNullOrWhiteSpace(customerCode))
            throw new InvalidOperationException("Select the destination company.");
        customerCode = customerCode.Trim();

        actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Trim();
        if (actor.Length > 20) actor = actor[..20];

        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var sources = new List<PlanSource>();
            foreach (var input in requested)
            {
                using var sourceCmd = new SqlCommand("""
                    SELECT
                        S.SoID, S.SoNumber, S.CustomerID, S.ItemNo,
                        CAST(ISNULL(S.OrderQty, 0) - ISNULL(S.ShippedQty, 0) - ISNULL(P.PlannedQty, 0) AS decimal(14,3)) AS RemainingQty
                    FROM dbo.PP_CustomerOrder S WITH (UPDLOCK, HOLDLOCK)
                    OUTER APPLY
                    (
                        SELECT SUM(ISNULL(L.OrderedQty, 0)) AS PlannedQty
                        FROM dbo.FG_ShipmentOrder O WITH (UPDLOCK, HOLDLOCK)
                        CROSS APPLY OPENJSON(COALESCE(O.ItemsJSON, N'[]')) WITH
                        (
                            LineSeq int '$.lineSeq',
                            OrderedQty decimal(14,3) '$.orderedQty'
                        ) L
                        WHERE UPPER(ISNULL(O.Source, '')) = 'PP'
                          AND UPPER(ISNULL(O.Status, '')) NOT IN ('CANCELED', 'CANCELLED')
                          AND L.LineSeq = S.SoID
                    ) P
                    WHERE S.SoID = @SoID
                      AND UPPER(ISNULL(S.Status, 'OPEN')) NOT IN ('CANCELED', 'CANCELLED');
                    """, conn, tx);
                sourceCmd.Parameters.Add("@SoID", SqlDbType.Int).Value = input.SoId;
                using var reader = sourceCmd.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException($"Supply plan line {input.SoId} is not available.");

                var source = new PlanSource(
                    reader.GetInt32(reader.GetOrdinal("SoID")),
                    GetString(reader, "SoNumber"),
                    GetString(reader, "CustomerID"),
                    GetString(reader, "ItemNo") ?? "",
                    GetDecimal(reader, "RemainingQty"),
                    input.Qty);
                reader.Close();

                if (string.IsNullOrWhiteSpace(source.SoNumber) || string.IsNullOrWhiteSpace(source.ItemNo))
                    throw new InvalidOperationException($"Supply plan line {input.SoId} is missing its order or part number.");
                ValidateShipmentPlanQuantity(source.ItemNo, source.Qty, source.RemainingQty);
                sources.Add(source);
            }

            if (sources.Any(x => !string.Equals(x.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Every selected Supply Plan line must belong to the selected company.");

            var itemsJson = JsonSerializer.Serialize(sources.Select((line, index) => new
            {
                shipmentOrderLineId = index + 1,
                lineSeq = line.SoId,
                itemNo = line.ItemNo,
                orderedQty = line.Qty,
                allocatedQty = 0m
            }));

            using var headerCmd = new SqlCommand("""
                INSERT dbo.FG_ShipmentOrder
                    (ShipOrderNumber, CustomerCode, CustomerPO, Source, ShipDate, Status, ItemsJSON, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ShipmentOrderID
                VALUES (NULL, @CustomerCode, @CustomerPO, 'PP', @ShipDate, 'PLAN', @ItemsJSON, @Actor, SYSDATETIME());
                """, conn, tx);
            AddText(headerCmd, "@CustomerCode", SqlDbType.VarChar, 20, customerCode, false);
            AddText(headerCmd, "@CustomerPO", SqlDbType.VarChar, 40,
                GetShipmentPlanHeaderValue(sources.Select(x => x.SoNumber)), false);
            headerCmd.Parameters.Add("@ShipDate", SqlDbType.Date).Value = shipDate.Date;
            headerCmd.Parameters.Add("@ItemsJSON", SqlDbType.NVarChar, -1).Value = itemsJson;
            AddText(headerCmd, "@Actor", SqlDbType.VarChar, 20, actor, false);
            var orderId = Convert.ToInt32(headerCmd.ExecuteScalar());
            var planNumber = $"FGP-{shipDate:yyMMdd}-{orderId:D6}";

            using var numberCmd = new SqlCommand(
                "UPDATE dbo.FG_ShipmentOrder SET ShipOrderNumber=@PlanNumber, OutgoingSlipNumber=@PlanNumber WHERE ShipmentOrderID=@OrderID;",
                conn, tx);
            AddText(numberCmd, "@PlanNumber", SqlDbType.VarChar, 24, planNumber, false);
            numberCmd.Parameters.Add("@OrderID", SqlDbType.Int).Value = orderId;
            numberCmd.ExecuteNonQuery();

            tx.Commit();
            return new ShipmentPlanCreateResult([planNumber], sources.Count, sources.Sum(x => x.Qty));
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }


    public List<ShipmentRow> ListShipments(string? search = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            SELECT
                T.TransactionID,
                T.TransactionTime AS ShippedAt,
                T.LotNo AS OutboundBarcode,
                REPLACE(T.ReasonCode, '_OUTBOUND', '') AS UnitType,
                T.LotNo,
                T.PartNo,
                I.PartName,
                T.LocationNo,
                ABS(T.QtyChange) AS Qty,
                T.OperatorID,
                CAST('SHIPPED' AS varchar(20)) AS Status
            FROM dbo.WH_InventoryTransaction T
            LEFT JOIN dbo.WH_Inventory I ON I.LotNo = T.LotNo
            WHERE T.TransactionType = 'OUT'
              AND T.SourceType = 'FG_OUTBOUND'
              AND (@From IS NULL OR T.TransactionTime >= @From)
              AND (@To IS NULL OR T.TransactionTime < DATEADD(day, 1, @To))
              AND (@Search IS NULL
                   OR T.PartNo LIKE @Search
                   OR T.LotNo LIKE @Search
                   OR I.PartName LIKE @Search
                   OR T.LocationNo LIKE @Search
                   OR T.OperatorID LIKE @Search)
            ORDER BY T.TransactionTime DESC, T.TransactionID DESC;
            """;

        return Query(sql, r => new ShipmentRow(
            Convert.ToInt64(r["TransactionID"]),
            GetDate(r, "ShippedAt") ?? DateTime.MinValue,
            GetString(r, "OutboundBarcode") ?? "-",
            GetString(r, "UnitType") ?? "UNIT",
            GetString(r, "LotNo") ?? "-",
            GetString(r, "PartNo"),
            GetString(r, "PartName"),
            GetString(r, "LocationNo"),
            GetDecimal(r, "Qty"),
            GetString(r, "OperatorID"),
            GetString(r, "Status") ?? "SHIPPED"),
            ("@Search", Like(search)),
            ("@From", from?.Date),
            ("@To", to?.Date));
    }

    public ShipmentDocument? GetShipmentDocument(int shipmentOrderId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            SELECT TOP (1)
                O.ShipmentOrderID, O.ShipOrderNumber, O.CustomerCode,
                COALESCE(NULLIF(C.CustomerNameEn, ''), NULLIF(C.CustomerName, ''), O.CustomerCode) AS CustomerName,
                SD.Address AS CustomerAddress, O.CustomerPO, O.DestPlant, O.DestDock, O.ShipDate,
                NULL AS LoadingID, O.LoadingNumber, O.LicensePlate, O.DriverName, O.ArrivalAt AS ArrivalTS, O.LoadingConfirmedAt AS ConfirmedAt,
                COALESCE(NULLIF(O.ShipmentDocumentNo, ''), O.ShipOrderNumber) AS DnNumber,
                COALESCE(O.ShippedAt, O.DepartureAt, O.LoadingConfirmedAt, O.ConfirmedAt, O.ModifiedTS, O.CreatedTS) AS IssuedAt,
                O.Status AS EdiStatus
            FROM dbo.FG_ShipmentOrder O
            OUTER APPLY
            (
                SELECT TOP (1) CustomerID, CustomerName, CustomerNameEn
                FROM dbo.MD_Customer
                WHERE CustomerID = O.CustomerCode OR CustomerCode = O.CustomerCode
                ORDER BY CASE WHEN CustomerCode = O.CustomerCode THEN 0 ELSE 1 END
            ) C
            OUTER APPLY
            (
                SELECT TOP (1) D.Address
                FROM dbo.MD_ShipmentDest D
                WHERE D.CustomerID = C.CustomerID
                  AND (D.ShipDestID = O.DestPlant OR D.DestName = O.DestPlant OR O.DestPlant IS NULL)
                ORDER BY CASE WHEN D.ShipDestID = O.DestPlant THEN 0 ELSE 1 END, D.ShipDestID
            ) SD
            WHERE O.ShipmentOrderID = @ShipmentOrderID;

            SELECT
                SL.LineSeq, SL.ItemNo, I.ItemName,
                COALESCE(NULLIF(I.CustItemNoSAV, ''), NULLIF(I.CustItemNoGEO, ''), SL.ItemNo) AS CustomerItemNo,
                CAST(ISNULL(SL.OrderedQty, 0) AS decimal(14,3)) AS OrderedQty,
                CAST(COALESCE(NULLIF(SL.AllocatedQty, 0), S.Qty, SL.OrderedQty, 0) AS decimal(14,3)) AS DeliveryQty,
                CAST(CASE WHEN ISNULL(PK.QtyPerInner, 0) > 0 THEN PK.QtyPerInner
                          ELSE COALESCE(NULLIF(SL.AllocatedQty, 0), S.Qty, SL.OrderedQty, 1) END AS decimal(14,3)) AS UnitPackQty,
                I.DefaultUOM AS Unit, COALESCE(SL.LotNo, S.LotNo) AS LotNo,
                COALESCE(SL.LotNo, S.LotNo) AS StockNumber,
                COALESCE(SL.Location, S.LocationNo) AS Location, LOT.ProducedAt
            FROM dbo.FG_ShipmentOrder O
            CROSS APPLY OPENJSON(COALESCE(O.ItemsJSON, N'[]')) WITH
            (
                LineSeq int '$.lineSeq',
                ItemNo varchar(20) '$.itemNo',
                OrderedQty decimal(14,3) '$.orderedQty',
                AllocatedQty decimal(14,3) '$.allocatedQty',
                LotNo nvarchar(50) '$.lotNo',
                LotID int '$.lotId',
                Location varchar(20) '$.location'
            ) SL
            LEFT JOIN dbo.WH_Inventory S ON S.LotNo = SL.LotNo
            LEFT JOIN dbo.tbl_Lot LOT ON LOT.LotID = SL.LotID OR LOT.LotCode = COALESCE(SL.LotNo, S.LotNo)
            LEFT JOIN dbo.MD_Item I ON I.ItemNo = SL.ItemNo
            OUTER APPLY
            (
                SELECT TOP (1) P.QtyPerInner
                FROM dbo.MD_PackagingSpec P
                WHERE P.ItemID = SL.ItemNo AND ISNULL(P.ActiveFlag, 1) = 1
                ORDER BY P.PackSpecID
            ) PK
            WHERE O.ShipmentOrderID = @ShipmentOrderID
            ORDER BY SL.LineSeq, COALESCE(SL.LotNo, S.LotNo);
            """, conn);
        cmd.Parameters.Add("@ShipmentOrderID", SqlDbType.Int).Value = shipmentOrderId;

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var header = new
        {
            ShipmentOrderId = GetInt(reader, "ShipmentOrderID"),
            ShipOrderNumber = GetString(reader, "ShipOrderNumber"),
            CustomerCode = GetString(reader, "CustomerCode"),
            CustomerName = GetString(reader, "CustomerName"),
            CustomerAddress = GetString(reader, "CustomerAddress"),
            CustomerPo = GetString(reader, "CustomerPO"),
            DestPlant = GetString(reader, "DestPlant"),
            DestDock = GetString(reader, "DestDock"),
            ShipDate = GetDate(reader, "ShipDate"),
            LoadingId = GetNullableInt(reader, "LoadingID"),
            LoadingNumber = GetString(reader, "LoadingNumber"),
            LicensePlate = GetString(reader, "LicensePlate"),
            DriverName = GetString(reader, "DriverName"),
            ArrivalAt = GetDate(reader, "ArrivalTS"),
            ConfirmedAt = GetDate(reader, "ConfirmedAt"),
            DeliveryNoteNumber = GetString(reader, "DnNumber"),
            DeliveryNoteIssuedAt = GetDate(reader, "IssuedAt"),
            EdiStatus = GetString(reader, "EdiStatus")
        };

        var lines = new List<ShipmentDocumentLine>();
        if (reader.NextResult())
        {
            while (reader.Read())
            {
                lines.Add(new ShipmentDocumentLine(
                    GetInt(reader, "LineSeq"),
                    GetString(reader, "ItemNo") ?? "-",
                    GetString(reader, "ItemName"),
                    GetString(reader, "CustomerItemNo"),
                    GetDecimal(reader, "OrderedQty"),
                    GetDecimal(reader, "DeliveryQty"),
                    GetDecimal(reader, "UnitPackQty"),
                    GetString(reader, "Unit"),
                    GetString(reader, "LotNo"),
                    GetString(reader, "StockNumber"),
                    GetString(reader, "Location"),
                    GetDate(reader, "ProducedAt")));
            }
        }

        return new ShipmentDocument(
            header.ShipmentOrderId, header.ShipOrderNumber, header.CustomerCode, header.CustomerName,
            header.CustomerAddress, header.CustomerPo, header.DestPlant, header.DestDock, header.ShipDate,
            header.LoadingId, header.LoadingNumber, header.LicensePlate, header.DriverName, header.ArrivalAt,
            header.ConfirmedAt, header.DeliveryNoteNumber, header.DeliveryNoteIssuedAt, header.EdiStatus, lines);
    }

    public List<HistoryRow> ListHistory(string? search = null, string? eventType = null, DateTime? from = null, DateTime? to = null)
    {
        const string sql = """
            SELECT TOP 1000 H.EventAt, H.EventType, H.ReferenceNo, H.ItemOrOrder, H.Qty,
                   H.Location, H.WorkerID, H.Status, H.Details
            FROM
            (
                SELECT
                    COALESCE(P.ModifiedTS, P.CreatedTS) AS EventAt,
                    CAST('PUT AWAY' AS varchar(20)) AS EventType,
                    CAST(CONCAT('PA-', P.PutAwayID) AS nvarchar(80)) AS ReferenceNo,
                    CAST(P.ItemNo AS nvarchar(80)) AS ItemOrOrder,
                    CAST(ISNULL(P.Qty, 0) AS decimal(14,3)) AS Qty,
                    CAST(P.ActualLoc AS nvarchar(80)) AS Location,
                    CAST(COALESCE(P.OperatorID, P.CreatedBy) AS nvarchar(120)) AS WorkerID,
                    CAST(P.Status AS nvarchar(40)) AS Status,
                    CAST(CONCAT('Suggested ', ISNULL(P.SuggestedLoc, '-'), ' / Actual ', ISNULL(P.ActualLoc, '-')) AS nvarchar(300)) AS Details
                FROM dbo.FG_PutAway P

                UNION ALL

                SELECT
                    COALESCE(O.ShippedAt, O.DepartureAt, O.LoadingConfirmedAt, O.ConfirmedAt, O.ModifiedTS, O.CreatedTS),
                    'SHIPPED',
                    CAST(O.ShipOrderNumber AS nvarchar(80)),
                    CAST(O.CustomerCode AS nvarchar(80)),
                    CAST(ISNULL(S.OrderedQty, 0) AS decimal(14,3)),
                    CAST(O.DestDock AS nvarchar(80)),
                    CAST(COALESCE(O.ShipmentOperatorID, O.ConfirmedBy, O.ModifiedBy, O.CreatedBy) AS nvarchar(120)),
                    CAST(O.Status AS nvarchar(40)),
                    CAST(CONCAT('Destination ', ISNULL(O.DestPlant, '-'), ' / Truck ', ISNULL(O.LicensePlate, '-')) AS nvarchar(300))
                FROM dbo.FG_ShipmentOrder O
                OUTER APPLY
                (
                    SELECT SUM(ISNULL(SL.OrderedQty, 0)) AS OrderedQty
                    FROM OPENJSON(COALESCE(O.ItemsJSON, N'[]')) WITH
                    (OrderedQty decimal(14,3) '$.orderedQty') SL
                ) S
                WHERE UPPER(ISNULL(O.Status, '')) = 'SHIPPED'

                UNION ALL

                SELECT
                    COALESCE(R.ReceivedAt, R.CreatedTS),
                    'RETURN',
                    CAST(COALESCE(R.ReturnNumber, R.RMANo, CONCAT('RETURN-', R.ReturnID)) AS nvarchar(80)),
                    CAST(R.CustomerCode AS nvarchar(80)),
                    CAST(COALESCE(R.ReturnQty, TRY_CONVERT(decimal(14,3), JSON_VALUE(R.ItemsJSON, '$[0].qty')), 0) AS decimal(14,3)),
                    NULL,
                    CAST(COALESCE(R.ReceivedBy, R.CreatedBy) AS nvarchar(120)),
                    CAST(R.Status AS nvarchar(40)),
                    CAST(R.ReturnReason AS nvarchar(300))
                FROM dbo.FG_CustomerReturn R
            ) H
            WHERE H.EventAt IS NOT NULL
              AND (@EventType IS NULL OR H.EventType = @EventType)
              AND (@From IS NULL OR H.EventAt >= @From)
              AND (@To IS NULL OR H.EventAt < DATEADD(day, 1, @To))
              AND (@Search IS NULL
                   OR H.ReferenceNo LIKE @Search
                   OR H.ItemOrOrder LIKE @Search
                   OR H.Location LIKE @Search
                   OR H.WorkerID LIKE @Search
                   OR H.Details LIKE @Search)
            ORDER BY H.EventAt DESC, H.ReferenceNo DESC;
            """;

        return Query(sql, r => new HistoryRow(
            GetDate(r, "EventAt") ?? DateTime.MinValue,
            GetString(r, "EventType") ?? "EVENT",
            GetString(r, "ReferenceNo"),
            GetString(r, "ItemOrOrder"),
            GetDecimal(r, "Qty"),
            GetString(r, "Location"),
            GetString(r, "WorkerID"),
            GetString(r, "Status"),
            GetString(r, "Details")),
            ("@Search", Like(search)),
            ("@EventType", NullIfBlank(eventType)?.ToUpperInvariant()),
            ("@From", from?.Date),
            ("@To", to?.Date));
    }

    private List<T> Query<T>(string sql, Func<SqlDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    private static void AddText(SqlCommand cmd, string name, SqlDbType type, int size, string? value, bool nullable = true)
    {
        var parameter = cmd.Parameters.Add(name, type, size);
        parameter.Value = nullable && string.IsNullOrWhiteSpace(value) ? DBNull.Value : (value ?? "").Trim();
    }

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? GetString(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToString(reader[name]);
    private static bool GetBool(SqlDataReader reader, string name) => reader[name] != DBNull.Value && Convert.ToBoolean(reader[name]);
    private static int GetInt(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? 0 : Convert.ToInt32(reader[name]);
    private static int? GetNullableInt(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToInt32(reader[name]);
    private static decimal GetDecimal(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? 0 : Convert.ToDecimal(reader[name]);
    private static DateTime? GetDate(SqlDataReader reader, string name) => reader[name] == DBNull.Value ? null : Convert.ToDateTime(reader[name]);
    internal static List<ShipmentPlanInput> NormalizeShipmentPlanInputs(IReadOnlyList<ShipmentPlanInput> inputs) =>
        inputs.GroupBy(x => x.SoId)
            .Select(g => new ShipmentPlanInput(g.Key, g.Sum(x => x.Qty)))
            .Where(x => x.Qty > 0)
            .ToList();

    internal static void ValidateShipmentPlanQuantity(string itemNo, decimal qty, decimal remainingQty)
    {
        if (qty <= 0)
            throw new InvalidOperationException($"{itemNo} requires a quantity greater than zero.");
        if (qty != decimal.Truncate(qty))
            throw new InvalidOperationException($"{itemNo} requires a whole-number quantity.");
        if (qty > remainingQty)
            throw new InvalidOperationException($"{itemNo} exceeds the remaining quantity ({remainingQty:N0}).");
    }

    internal static string? GetShipmentPlanHeaderValue(IEnumerable<string?> values)
    {
        var distinct = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return distinct.Count switch { 0 => null, 1 => distinct[0], _ => "MULTI" };
    }

    internal static void ValidateShipmentPlanDelete(string? source, string? status, bool hasDependencies)
    {
        if (!string.Equals(source, "PP", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only Shipment Plans can be deleted here.");
        if (!string.Equals(status, "PLAN", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, "PLANNED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only plans in PLAN status can be deleted.");
        if (hasDependencies)
            throw new InvalidOperationException("This plan is already connected to picking, loading, or return data and cannot be deleted.");
    }

    private sealed record PlanSource(
        int SoId,
        string? SoNumber,
        string? CustomerCode,
        string ItemNo,
        decimal RemainingQty,
        decimal Qty);
}
