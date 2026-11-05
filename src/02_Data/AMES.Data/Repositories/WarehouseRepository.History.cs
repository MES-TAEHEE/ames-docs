using System.Data;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public List<OperationLogRow> ListOperationLogs(string? search = null, string? eventType = null,
        DateTime? from = null, DateTime? to = null)
    {
        if (search?.Length > 120 || from?.Date > to?.Date || to?.Date == DateTime.MaxValue.Date)
            throw new ArgumentException("Invalid history search or date range.");
        var type = NullIfBlank(eventType)?.ToUpperInvariant();
        var rows = Query(OperationHistorySql, r => new OperationLogRow(
            GetLong(r,"OperationLogID"),GetDateTime(r,"EventTime"),GetString(r,"EventType") ?? "",
            null,GetString(r,"WorkerID"),null,GetString(r,"WorkerID"),null,null,null,
            "LOT",GetString(r,"LotNo"),GetString(r,"Status") ?? "SUCCESS",GetString(r,"Message"),null,
            GetString(r,"SourceType"),GetNullableInt(r,"SourceID"),GetString(r,"LotNo"),GetString(r,"PartNo"),
            GetString(r,"LocationID"),GetNullableDecimal(r,"Qty"),
            HistoryKey:GetString(r,"RowKey"),ReferenceNo:GetString(r,"ReferenceNo"),
            Customer:GetString(r,"Customer"),Unit:GetString(r,"Unit")),
            ("@Like",null,Like(search)),("@OperationType",null,type),
            ("@DateFrom",null,from?.Date),("@DateTo",null,to?.Date));

        if (type is null or "OUTBOUND")
            rows.AddRange(new FinishedGoodsRepository(_factory).ListShipmentHistory(search,from,to,1000)
                .Select(s => new OperationLogRow(s.ShipmentId,s.ShippedAt,"SHIPPED",null,s.ShippedBy,null,
                    s.ShippedBy,null,null,null,null,null,"SHIPPED",null,null,"FG_OUTBOUND",s.ShipmentId,
                    null,null,null,s.Qty,HistoryKey:s.Key,ReferenceNo:s.ReferenceNo,DeliveryId:s.DeliveryId,
                    ShipmentId:s.ShipmentId,PalletCount:s.PalletCount,ItemCount:s.ItemCount,
                    Customer:string.IsNullOrWhiteSpace(s.CustomerName) ? s.CustomerId : $"{s.CustomerId} · {s.CustomerName}")));
        return rows.OrderByDescending(row => row.EventTime)
            .ThenByDescending(row => row.RowKey,StringComparer.Ordinal).Take(1000).ToList();
    }

    internal const string OperationHistorySql = """
        WITH Entries AS
        (
            SELECT T.TransactionID OperationLogID,CONCAT('TX:',T.TransactionID) COLLATE DATABASE_DEFAULT RowKey,
                T.TransactionTime EventTime,X.EventType,X.OperationType,
                T.LotNo COLLATE DATABASE_DEFAULT ReferenceNo,T.LotNo COLLATE DATABASE_DEFAULT LotNo,
                T.PartNo COLLATE DATABASE_DEFAULT PartNo,T.LocationNo COLLATE DATABASE_DEFAULT LocationID,
                CASE WHEN T.TransactionType='MOVE' THEN COALESCE(T.QtyAfter,0) ELSE T.QtyChange END Qty,
                T.OperatorID COLLATE DATABASE_DEFAULT WorkerID,
                CAST('SUCCESS' AS nvarchar(30)) COLLATE DATABASE_DEFAULT Status,
                CASE WHEN X.EventType='MOVE_LOCATION' THEN
                    COALESCE(NULLIF(T.Note,N''),CONCAT(N'Location Change to ',T.LocationNo))
                    ELSE CONCAT(COALESCE(NULLIF(T.Note,N''),COALESCE(T.ReasonCode,T.TransactionType)),
                        N' (',COALESCE(T.QtyBefore,0),N' -> ',COALESCE(T.QtyAfter,0),N')') END COLLATE DATABASE_DEFAULT Message,
                T.SourceType COLLATE DATABASE_DEFAULT SourceType,T.SourceID,
                CAST(NULL AS nvarchar(300)) COLLATE DATABASE_DEFAULT Customer,
                M.DefaultUOM COLLATE DATABASE_DEFAULT Unit,M.ItemName COLLATE DATABASE_DEFAULT PartName
            FROM dbo.WH_InventoryTransaction T
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            CROSS APPLY(SELECT CASE
                WHEN T.ReasonCode='INBOUND_CANCEL' OR T.TransactionType='CANCEL' THEN 'CANCEL_RECEIPT'
                WHEN T.SourceType='PALLET_RETURN' OR T.ReasonCode='PALLET_RETURN' THEN 'PALLET_RETURN'
                WHEN T.TransactionType='MOVE' OR (T.SourceType='FG_PUTAWAY' AND T.TransactionType='IN') THEN 'MOVE_LOCATION'
                WHEN T.SourceType='FG_RETURN' AND T.TransactionType='IN' THEN 'CUSTOMER_RETURN'
                WHEN T.TransactionType='IN' THEN 'RECEIVE'
                WHEN T.TransactionType='OUT' AND (T.SourceType IN('PICK_SLIP','DIRECT_OUTGOING') OR T.ReasonCode='RELEASE_PICK') THEN 'RELEASE_PICK'
                WHEN T.TransactionType='OUT' THEN 'OUTBOUND'
                WHEN T.TransactionType='ADJ' THEN 'ADJUST_SAVE'
                ELSE UPPER(T.TransactionType) END EventType,
                CASE
                WHEN T.ReasonCode='INBOUND_CANCEL' OR T.TransactionType='CANCEL' THEN 'INBOUND'
                WHEN T.SourceType='PALLET_RETURN' OR T.ReasonCode='PALLET_RETURN' THEN 'PALLET_RETURN'
                WHEN T.TransactionType='MOVE' OR (T.SourceType='FG_PUTAWAY' AND T.TransactionType='IN') THEN 'RELOCATION'
                WHEN T.SourceType='FG_RETURN' AND T.TransactionType='IN' THEN 'RETURN'
                WHEN T.TransactionType='IN' THEN 'INBOUND'
                WHEN T.TransactionType='OUT' AND (T.SourceType IN('PICK_SLIP','DIRECT_OUTGOING') OR T.ReasonCode='RELEASE_PICK') THEN 'RELEASE'
                WHEN T.TransactionType='OUT' THEN 'OUTBOUND'
                WHEN T.TransactionType='ADJ' THEN 'ADJUSTMENT' END OperationType) X
            WHERE NOT (COALESCE(T.SourceType,'')='FG_OUTBOUND' AND T.TransactionType='OUT'
                AND EXISTS(SELECT 1 FROM dbo.FG_Shipment S WHERE S.ShipmentID=T.SourceID))
              AND NOT (COALESCE(T.SourceType,'')='FG_RETURN' AND T.TransactionType='IN'
                AND EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.ReturnID=T.SourceID))
            UNION ALL
            SELECT CONVERT(bigint,R.ReturnID),CONCAT('RETURN:',R.ReturnID) COLLATE DATABASE_DEFAULT,
                COALESCE(R.ReceivedAt,R.CreatedTS),'CUSTOMER_RETURN','RETURN',
                CONVERT(nvarchar(80),R.ReturnID) COLLATE DATABASE_DEFAULT,R.LotNo COLLATE DATABASE_DEFAULT,
                R.PartNo COLLATE DATABASE_DEFAULT,NULL,R.ReturnQty,
                COALESCE(R.ReceivedBy,R.CreatedBy) COLLATE DATABASE_DEFAULT,R.Status COLLATE DATABASE_DEFAULT,
                CONCAT(R.ReturnReason,CASE WHEN NULLIF(R.Note,N'') IS NOT NULL THEN CONCAT(N' · ',R.Note) END) COLLATE DATABASE_DEFAULT,
                'FG_RETURN',R.ReturnID,CONCAT(R.CustomerID,N' · ',C.CustomerName) COLLATE DATABASE_DEFAULT,
                M.DefaultUOM COLLATE DATABASE_DEFAULT,R.PartName COLLATE DATABASE_DEFAULT
            FROM dbo.FG_CustomerReturn R
            LEFT JOIN dbo.MD_Customer C ON C.CustomerID=R.CustomerID
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=R.PartNo
            UNION ALL
            SELECT CONVERT(bigint,P.PickSlipID),CONCAT('REQUEST:',P.PickSlipID) COLLATE DATABASE_DEFAULT,
                P.CreatedTS,'PICKING_REQUEST','PICKING_REQUEST',
                COALESCE(NULLIF(P.PickSlipNo,N''),CONCAT(N'RS-',P.PickSlipID)) COLLATE DATABASE_DEFAULT,NULL,
                P.ItemNo COLLATE DATABASE_DEFAULT,P.ReqLocation COLLATE DATABASE_DEFAULT,P.DemandQty,
                COALESCE(NULLIF(P.ReqUserId,N''),P.CreatedBy) COLLATE DATABASE_DEFAULT,'SUCCESS',
                CONCAT(N'Picking Request ',COALESCE(NULLIF(P.PickSlipNo,N''),CONCAT(N'RS-',P.PickSlipID)),
                    N' · ',P.ItemNo,N' · ',P.DemandQty,N' ',M.DefaultUOM,N' · ',P.ReqLocation) COLLATE DATABASE_DEFAULT,
                'PICKING_REQUEST',P.PickSlipID,NULL,M.DefaultUOM COLLATE DATABASE_DEFAULT,M.ItemName COLLATE DATABASE_DEFAULT
            FROM dbo.WH_PickSlip P
            LEFT JOIN dbo.MD_Item M ON M.ItemNo=P.ItemNo
            WHERE P.CreatedBy='WH-REQUEST'
        )
        SELECT TOP(1000) * FROM Entries H
        WHERE (@OperationType IS NULL OR H.OperationType=@OperationType)
          AND (@DateFrom IS NULL OR H.EventTime>=@DateFrom)
          AND (@DateTo IS NULL OR H.EventTime<DATEADD(day,1,@DateTo))
          AND (@Like IS NULL OR H.ReferenceNo LIKE @Like OR H.LotNo LIKE @Like OR H.PartNo LIKE @Like
            OR H.PartName LIKE @Like OR H.LocationID LIKE @Like OR H.WorkerID LIKE @Like
            OR H.Message LIKE @Like OR H.Customer LIKE @Like OR H.EventType LIKE @Like
            OR H.SourceType LIKE @Like OR CONVERT(nvarchar(80),H.SourceID) LIKE @Like)
        ORDER BY H.EventTime DESC,H.RowKey DESC;
        """;
}
