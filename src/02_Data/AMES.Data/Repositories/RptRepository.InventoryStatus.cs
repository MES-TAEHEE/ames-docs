using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class RptRepository
{
    public sealed record InventoryStatusLot(string Source, string PartNo, string PartName, string LotNo,
        string LocationNo, DateTime? ReceivedAt, string Unit, decimal Qty, string ItemCategory, string VendorCode);
    public sealed record InventoryMovement(string Source, string Category, string Unit, decimal Received, decimal Issued);
    public sealed record InventoryTarget(string Scope, string Category, string Unit, decimal Qty);
    public sealed record InventoryDailyBalance(DateTime Day,string Source,string Category,string Unit,decimal Qty);

    // Include the previous day's closing balance to calculate the first day's change.
    public List<InventoryDailyBalance> ListInventoryDailyBalances(DateTime from,DateTime through)
    {
        if(from.Date>through.Date) throw new ArgumentException("From must be on or before To.");
        return Query(InventoryDailyBalancesSql,r=>new InventoryDailyBalance(Convert.ToDateTime(r[0]),(string)r[1],
            (string)r[2],(string)r[3],Convert.ToDecimal(r[4])),("@From",null,from.Date.AddDays(-1)),("@To",null,through.Date));
    }

    internal const string InventoryDailyBalancesSql="""
        WITH Days AS (
            SELECT CAST(@From AS date) Day
            UNION ALL SELECT DATEADD(day,1,Day) FROM Days WHERE Day<@To
        ), Lots AS (
            SELECT LotNo,PartNo FROM dbo.WH_Inventory WHERE PartNo IS NOT NULL
            UNION SELECT LotNo,PartNo FROM dbo.WH_InventoryTransaction WHERE PartNo IS NOT NULL
        )
        SELECT D.Day,CASE WHEN UPPER(ISNULL(M.ItemType,'')) IN('ASSY','SUB') THEN 'FG' ELSE 'WH' END Source,
            COALESCE(M.ItemCategory,'') Category,COALESCE(NULLIF(M.DefaultUOM,''),'EA') Unit,SUM(Q.Qty) Qty
        FROM Days D CROSS JOIN Lots K
        JOIN dbo.MD_Item M ON M.ItemNo=K.PartNo
        LEFT JOIN dbo.WH_Inventory I ON I.LotNo=K.LotNo AND I.PartNo=K.PartNo
        OUTER APPLY(SELECT TOP(1) TransactionID,QtyBefore FROM dbo.WH_InventoryTransaction T
            WHERE T.LotNo=K.LotNo AND T.PartNo=K.PartNo AND T.TransactionTime>=DATEADD(day,1,CAST(D.Day AS datetime2))
            ORDER BY T.TransactionTime,T.TransactionID) A
        OUTER APPLY(SELECT TOP(1) TransactionID,QtyAfter FROM dbo.WH_InventoryTransaction T
            WHERE T.LotNo=K.LotNo AND T.PartNo=K.PartNo AND T.TransactionTime<DATEADD(day,1,CAST(D.Day AS datetime2))
            ORDER BY T.TransactionTime DESC,T.TransactionID DESC) B
        CROSS APPLY(VALUES(CASE WHEN A.TransactionID IS NOT NULL THEN A.QtyBefore
            WHEN I.LotNo IS NOT NULL THEN I.Qty ELSE B.QtyAfter END)) Q(Qty)
        WHERE Q.Qty>0 AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=K.LotNo)
            AND (I.CreatedAt IS NULL OR I.CreatedAt<DATEADD(day,1,CAST(D.Day AS datetime2))
                OR A.TransactionID IS NOT NULL OR B.TransactionID IS NOT NULL)
        GROUP BY D.Day,CASE WHEN UPPER(ISNULL(M.ItemType,'')) IN('ASSY','SUB') THEN 'FG' ELSE 'WH' END,
            COALESCE(M.ItemCategory,''),COALESCE(NULLIF(M.DefaultUOM,''),'EA')
        ORDER BY D.Day,Source,Category,Unit OPTION(MAXRECURSION 0);
        """;
    public sealed record InventoryEvent(long Id,DateTime Time,string Source,string Category,string Unit,string PartNo,
        string PartName,string LotNo,string LocationNo,string Type,string Reason,string SourceType,decimal Qty,string Operator)
    {
        public string Event => Reason=="INBOUND_CANCEL" ? "CANCEL_RECEIPT" : Type=="OUT" && SourceType=="PICK_SLIP" ? "RELEASE" : Type;
        public decimal Received => Type=="IN" && Qty>0 && Event!="CANCEL_RECEIPT" ? Qty : 0;
        public decimal ReceiptChange => Event=="CANCEL_RECEIPT" ? Qty : Received;
        public decimal Issued => Type=="OUT" && Qty<0 && Event!="CANCEL_RECEIPT" ? -Qty : 0;
    }

    public List<InventoryEvent> ListInventoryEvents(DateTime from,DateTime through)
    {
        if(from.Date>through.Date) throw new ArgumentException("From must be on or before To.");
        return Query("""
            SELECT T.TransactionID,T.TransactionTime,
                CASE WHEN UPPER(ISNULL(M.ItemType,'')) IN('ASSY','SUB') THEN 'FG' ELSE 'WH' END Source,
                COALESCE(M.ItemCategory,'') Category,COALESCE(NULLIF(M.DefaultUOM,''),'EA') Unit,
                M.ItemNo,COALESCE(M.ItemName,'') PartName,T.LotNo,COALESCE(NULLIF(T.LocationNo,''),'0000') LocationNo,
                T.TransactionType,COALESCE(T.ReasonCode,'') Reason,COALESCE(T.SourceType,'') SourceType,
                T.QtyChange,COALESCE(T.OperatorID,T.CreatedBy,'') Operator
            FROM dbo.WH_InventoryTransaction T JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            WHERE T.TransactionTime>=@From AND T.TransactionTime<@End AND T.QtyChange<>0
                AND (T.TransactionType IN('IN','OUT') OR T.ReasonCode='INBOUND_CANCEL')
                AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=T.LotNo)
            ORDER BY T.TransactionTime DESC,T.TransactionID DESC;
            """,r=>new InventoryEvent(Convert.ToInt64(r[0]),Convert.ToDateTime(r[1]),(string)r[2],(string)r[3],
                (string)r[4],(string)r[5],(string)r[6],(string)r[7],(string)r[8],(string)r[9],(string)r[10],
                (string)r[11],Convert.ToDecimal(r[12]),(string)r[13]),("@From",null,from.Date),("@End",null,through.Date.AddDays(1)));
    }

    // QtyBefore on the first later event restores even a deleted or cancelled receipt.
    // MOVE has zero QtyChange and records the destination; use the last event at the cutoff for location.
    internal const string InventoryStatusSql = """
        WITH BeforeCutoff AS (
            SELECT T.*, ROW_NUMBER() OVER(PARTITION BY LotNo,PartNo ORDER BY TransactionTime DESC,TransactionID DESC) rn
            FROM dbo.WH_InventoryTransaction T WHERE TransactionTime<@End AND PartNo IS NOT NULL
        ), AfterCutoff AS (
            SELECT T.*, ROW_NUMBER() OVER(PARTITION BY LotNo,PartNo ORDER BY TransactionTime,TransactionID) rn
            FROM dbo.WH_InventoryTransaction T WHERE TransactionTime>=@End AND PartNo IS NOT NULL
        ), Keys AS (
            SELECT LotNo,PartNo FROM dbo.WH_Inventory WHERE PartNo IS NOT NULL
            UNION SELECT LotNo,PartNo FROM BeforeCutoff
            UNION SELECT LotNo,PartNo FROM AfterCutoff
        ), Balances AS (
            SELECT K.LotNo,K.PartNo,
                CASE WHEN A.TransactionID IS NOT NULL THEN A.QtyBefore
                     WHEN I.LotNo IS NOT NULL THEN I.Qty ELSE B.QtyAfter END Qty,
                CASE WHEN A.TransactionID IS NULL AND I.LotNo IS NOT NULL THEN I.LocationNo
                     ELSE B.LocationNo END LocationNo,
                COALESCE(CASE WHEN I.ReceivedAt<@End THEN I.ReceivedAt END, Receipt.ReceivedAt) ReceivedAt
            FROM Keys K
            LEFT JOIN dbo.WH_Inventory I ON I.LotNo=K.LotNo AND I.PartNo=K.PartNo
            LEFT JOIN BeforeCutoff B ON B.LotNo=K.LotNo AND B.PartNo=K.PartNo AND B.rn=1
            LEFT JOIN AfterCutoff A ON A.LotNo=K.LotNo AND A.PartNo=K.PartNo AND A.rn=1
            OUTER APPLY(SELECT MAX(TransactionTime) ReceivedAt FROM dbo.WH_InventoryTransaction R
                WHERE R.LotNo=K.LotNo AND R.PartNo=K.PartNo AND R.TransactionType='IN' AND R.QtyChange>0
                    AND R.TransactionTime<@End) Receipt
            WHERE NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=K.LotNo)
              AND (I.CreatedAt IS NULL OR I.CreatedAt<@End OR B.TransactionID IS NOT NULL OR A.TransactionID IS NOT NULL)
        )
        SELECT CASE WHEN UPPER(ISNULL(M.ItemType,'')) IN('ASSY','SUB') THEN 'FG' ELSE 'WH' END Source,
            B.PartNo, COALESCE(M.ItemName,'') PartName, B.LotNo,
            COALESCE(NULLIF(LTRIM(RTRIM(B.LocationNo)),''),'0000') LocationNo,
            B.ReceivedAt, COALESCE(NULLIF(M.DefaultUOM,''),'EA') Unit, B.Qty,
            COALESCE(M.ItemCategory,'') ItemCategory, COALESCE(D.VendorID,'') VendorCode
        FROM Balances B
        JOIN dbo.MD_Item M ON M.ItemNo=B.PartNo
        LEFT JOIN dbo.WH_Inventory I ON I.LotNo=B.LotNo AND I.PartNo=B.PartNo
        LEFT JOIN dbo.SCM_DeliveryNote D ON D.NoteNumber=I.DeliveryNoteNo
        WHERE B.Qty>0
        ORDER BY B.PartNo,CASE WHEN NULLIF(B.LocationNo,'0000') IS NULL THEN 0 ELSE 1 END,B.ReceivedAt,B.LotNo;
        """;

    public List<InventoryStatusLot> ListInventoryStatus(DateTime through)
        => Query(InventoryStatusSql, r => new InventoryStatusLot(
            (string)r["Source"], (string)r["PartNo"], (string)r["PartName"], (string)r["LotNo"],
            (string)r["LocationNo"], r["ReceivedAt"] as DateTime?, (string)r["Unit"],
            Convert.ToDecimal(r["Qty"]), (string)r["ItemCategory"], (string)r["VendorCode"]),
            ("@End", null, through.Date.AddDays(1)));

    public List<InventoryMovement> InventoryMovements(DateTime from, DateTime through)
    {
        if (from.Date > through.Date) throw new ArgumentException("From must be on or before To.");
        return Query("""
            SELECT CASE WHEN UPPER(ISNULL(M.ItemType,'')) IN('ASSY','SUB') THEN 'FG' ELSE 'WH' END Source,
                COALESCE(M.ItemCategory,'') Category,COALESCE(NULLIF(M.DefaultUOM,''),'EA') Unit,
                SUM(CASE WHEN T.TransactionType='IN' AND T.QtyChange>0 THEN T.QtyChange ELSE 0 END) Received,
                SUM(CASE WHEN T.TransactionType='OUT' AND T.QtyChange<0 AND ISNULL(T.ReasonCode,'')<>'INBOUND_CANCEL'
                    THEN -T.QtyChange ELSE 0 END) Issued
            FROM dbo.WH_InventoryTransaction T JOIN dbo.MD_Item M ON M.ItemNo=T.PartNo
            WHERE T.TransactionTime>=@From AND T.TransactionTime<@End
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=T.LotNo)
            GROUP BY CASE WHEN UPPER(ISNULL(M.ItemType,'')) IN('ASSY','SUB') THEN 'FG' ELSE 'WH' END,
                COALESCE(M.ItemCategory,''),COALESCE(NULLIF(M.DefaultUOM,''),'EA');
            """, r => new InventoryMovement((string)r["Source"],(string)r["Category"],(string)r["Unit"],
                Convert.ToDecimal(r["Received"]),Convert.ToDecimal(r["Issued"])),
            ("@From",null,from.Date),("@End",null,through.Date.AddDays(1)));
    }

    public List<InventoryTarget> ListInventoryTargets()
        => Query("""
            IF OBJECT_ID('dbo.RPT_InventoryTarget','U') IS NOT NULL
                SELECT InventoryScope,ItemCategory,Uom,TargetQty FROM dbo.RPT_InventoryTarget;
            """, r => new InventoryTarget((string)r[0],(string)r[1],(string)r[2],Convert.ToDecimal(r[3])));

    public void SaveInventoryTarget(InventoryTarget target, string actor)
    {
        if (target.Scope is not ("ALL" or "WH" or "FG") || target.Category.Length>50 || target.Unit.Length is <1 or >10
            || target.Qty<=0 || target.Qty>999999999999999m || decimal.Round(target.Qty,3)!=target.Qty
            || string.IsNullOrWhiteSpace(actor) || actor.Length>20)
            throw new ArgumentException("Invalid inventory target.");
        using var conn=_f.OpenConnection();
        using var cmd=new SqlCommand("""
            SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;
            BEGIN TRAN;
            UPDATE dbo.RPT_InventoryTarget WITH(UPDLOCK,SERIALIZABLE)
                SET TargetQty=@Qty,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
                WHERE InventoryScope=@Scope AND ItemCategory=@Category AND Uom=@Unit;
            IF @@ROWCOUNT=0 INSERT dbo.RPT_InventoryTarget(InventoryScope,ItemCategory,Uom,TargetQty,CreatedBy)
                VALUES(@Scope,@Category,@Unit,@Qty,@Actor);
            COMMIT;
            """,conn);
        ColumnParams.Add(cmd,"@Scope","RPT_InventoryTarget.InventoryScope",target.Scope);
        ColumnParams.Add(cmd,"@Category","RPT_InventoryTarget.ItemCategory",target.Category);
        ColumnParams.Add(cmd,"@Unit","RPT_InventoryTarget.Uom",target.Unit);
        ColumnParams.Add(cmd,"@Qty","RPT_InventoryTarget.TargetQty",target.Qty);
        ColumnParams.Add(cmd,"@Actor","RPT_InventoryTarget.CreatedBy",actor);
        cmd.ExecuteNonQuery();
    }
}
