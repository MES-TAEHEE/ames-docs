using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

// Permission Matrix-protected Web harness over the same SQL and pallet receipt used by PDA; never seeds or resets data.
public sealed class WarehouseInboundTestRepository(AmesConnectionFactory factory)
{
    public record Row(string Barcode,string PartNo,string? PartName,decimal Qty,string? Unit,bool Received,bool ScanRequired);
    public record ScanResult(string Barcode,string Kind,List<Row> Rows,IReadOnlyDictionary<string,string>? Fields=null);
    public record StorageRow(string Mode,string Barcode,string LotNo,string? PartNo,string? PartName,decimal Qty,string? Unit,
        string? DeliveryNoteNo,DateTime? ReceivedAt,string? LocationNo,string? ParentLotNo);
    public record StorageSelection(string Barcode,string Kind,List<StorageRow> Rows,List<string> Selected,bool RequiresRelocation=false);
    public record StorageLocation(string LocationNo,string? Name,string? Warehouse,string? Area,string? Aisle,decimal? Capacity,decimal Qty);
    public const string PermissionCode="WH-PDA-TEST";
    public const string PermissionHref="wh/picking-orders/inbound-test";
    public bool CanRead(string userId)=>HasPermission(userId,false);
    public bool CanReceive(string userId)=>HasPermission(userId,true);
    private bool HasPermission(string userId,bool receive)
    {
        if(string.IsNullOrWhiteSpace(userId)||userId.Length>450)return false;
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT CAST(CASE WHEN MAX(CASE WHEN P.PermissionLevel LIKE '%R%' THEN 1 ELSE 0 END)=1
                AND (@Receive=0 OR MAX(CASE WHEN P.PermissionLevel LIKE '%E%'
                    AND SUBSTRING(S.PermissionCriteria,2,1)<>'X' THEN 1 ELSE 0 END)=1) THEN 1 ELSE 0 END AS bit)
            FROM dbo.AspNetUserRoles U
            JOIN dbo.SYS_RolePermission P ON P.RoleID=U.RoleId AND P.ModuleCode='WEB'
            JOIN dbo.SYS_Screen S ON S.ScreenCode=P.ScreenCode AND S.ModuleCode='WEB'
            JOIN dbo.AspNetUsers A ON A.Id=U.UserId
            LEFT JOIN dbo.SYS_UserProfile UP ON UP.UserID=A.Id
            WHERE U.UserId=@User AND S.ScreenCode=@Screen
                AND (A.LockoutEnd IS NULL OR A.LockoutEnd<=SYSDATETIMEOFFSET())
                AND COALESCE(UP.AccountStatus,'ACTIVE')='ACTIVE';
            """,conn);
        cmd.Parameters.Add("@User",SqlDbType.NVarChar,450).Value=userId;
        cmd.Parameters.Add("@Screen",SqlDbType.VarChar,20).Value=PermissionCode;
        cmd.Parameters.Add("@Receive",SqlDbType.Bit).Value=receive;
        return cmd.ExecuteScalar() is true;
    }
    private void RequireAccess(string userId,bool receive=false)
    {if(!HasPermission(userId,receive))throw new UnauthorizedAccessException("WH PDA Test "+(receive?"read/edit":"read")+" permission is required.");}
    private static string Barcode(string value)
    {if(string.IsNullOrWhiteSpace(value)||value.Trim().Length>50)throw new ArgumentException("Scan a barcode within 50 characters.");return value.Trim();}
    private static void Mode(string mode)
    {if(mode is not("LOCAL" or "CKD"))throw new ArgumentException("Select LOCAL or CKD.");}

    public ScanResult Scan(string userId,string mode,string barcode)
    {
        RequireAccess(userId);Mode(mode);barcode=Barcode(barcode);
        using var conn=factory.OpenConnection();
        using var kind=new SqlCommand("""
            SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.SCM_DeliveryCase WHERE CaseNo=@Barcode) THEN 'PALLET'
                WHEN EXISTS(SELECT 1 FROM dbo.SCM_DeliveryNote WHERE NoteNumber=@Barcode)
                  OR EXISTS(SELECT 1 FROM dbo.SCM_Delivery WHERE DeliveryNumber=@Barcode) THEN 'DELIVERY_NOTE' ELSE 'LOT' END;
            """,conn);
        kind.Parameters.Add("@Barcode",SqlDbType.NVarChar,50).Value=barcode;
        var type=(string)kind.ExecuteScalar()!;
        if(type=="PALLET")
        {
            var pallet=new ScmRepository(factory).GetDeliveryCase(barcode)??throw new InvalidOperationException("Pallet not found.");
            if(!pallet.ReadyToReceive)throw new InvalidOperationException("This pallet has not been shipped or is cancelled.");
            using var required=new SqlCommand("SELECT ItemNo FROM dbo.MD_Item WHERE COALESCE(ScanRequired,1)=1",conn);
            var parts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var r=required.ExecuteReader())while(r.Read())parts.Add(r.GetString(0));
            return new(barcode,type,pallet.Boxes.Select(b=>new Row(b.Number,b.Item,b.Name,b.Quantity,b.Unit,b.Received,parts.Contains(b.Item))).ToList());
        }
        using var cmd=new SqlCommand(type=="DELIVERY_NOTE"?PdaWarehouseSql.InboundDocument:PdaWarehouseSql.InboundScan,conn){CommandTimeout=15};
        cmd.Parameters.Add("@ReceiveMode",SqlDbType.VarChar,10).Value=mode;
        cmd.Parameters.Add(type=="DELIVERY_NOTE"?"@DocumentBarcode":"@LotBarcode",SqlDbType.NVarChar,50).Value=barcode;
        using var reader=cmd.ExecuteReader();var rows=new List<Row>();
        if(!reader.Read())throw new InvalidOperationException("Inbound barcode was not found.");
        var fields=Enumerable.Range(0,reader.FieldCount).ToDictionary(reader.GetName,
            index=>reader.IsDBNull(index)?"-":reader.GetValue(index) is DateTime date?date.ToString("yyyy-MM-dd"):Convert.ToString(reader.GetValue(index))??"-");
        if(type=="DELIVERY_NOTE")
        {
            var names=new Dictionary<string,string?>();
            if(reader.NextResult())while(reader.Read())names[reader["PARTNO"].ToString()!]=reader["PARTNM"] as string;
            if(reader.NextResult())while(reader.Read())rows.Add(new(reader["BOX_BARCODE"].ToString()!,reader["PARTNO"].ToString()!,names.GetValueOrDefault(reader["PARTNO"].ToString()!),
                Convert.ToDecimal(reader["QTY"]),reader["UNIT"] as string,reader["YN"].ToString()=="Y",false));
        }
        else rows.Add(new(barcode,reader["PARTNO"].ToString()!,reader["PARTNM"] as string,
            Convert.ToDecimal(reader["QTY"]),reader["UNIT"] as string,reader["YN"].ToString()=="N",false));
        return new(barcode,type,rows,fields);
    }

    public void Receive(string userId,string actor,string mode,ScanResult scan,IReadOnlyCollection<string> scannedBoxes)
    {
        RequireAccess(userId,receive:true);Mode(mode);var barcode=Barcode(scan.Barcode);
        if(string.IsNullOrWhiteSpace(actor)||actor.Length>20)throw new ArgumentException("A signed-in operator is required.");
        // Reclassify from DB: neither client state nor a hidden menu is authorization or receipt validation.
        var current=Scan(userId,mode,barcode);
        if(current.Kind=="DELIVERY_NOTE")throw new InvalidOperationException("Delivery notes are view-only. Scan the pallet, box or LOT label.");
        if(current.Kind=="PALLET")
        {new ScmRepository(factory).ReceiveDeliveryCase(barcode,mode,actor,scannedBoxes);return;}
        using var conn=factory.OpenConnection();using var tx=conn.BeginTransaction();
        using var cmd=new SqlCommand(PdaWarehouseSql.InboundReceive,conn,tx){CommandTimeout=30};
        cmd.Parameters.Add("@ReceiveMode",SqlDbType.VarChar,10).Value=mode;
        cmd.Parameters.Add("@LotBarcode",SqlDbType.NVarChar,50).Value=barcode;
        cmd.Parameters.Add("@LocationId",SqlDbType.VarChar,50).Value="";
        cmd.Parameters.Add("@UserId",SqlDbType.VarChar,20).Value=actor;
        cmd.Parameters.Add("@SimulateFailure",SqlDbType.Bit).Value=false;
        cmd.ExecuteNonQuery();ScmRepository.LinkReceivedCase(conn,tx,barcode);tx.Commit();
    }

    public void CancelIncoming(string userId,string actor,string mode,string barcode)
    {
        RequireAccess(userId,receive:true);Mode(mode);barcode=Barcode(barcode);CheckActor(actor);
        var current=Scan(userId,mode,barcode);
        if(current.Kind!="LOT" || !current.Rows.Any(r=>r.Received))throw new InvalidOperationException("Scan a received box or LOT first.");
        using var conn=factory.OpenConnection();using var cmd=new SqlCommand(PdaWarehouseSql.InboundCancel,conn){CommandTimeout=30};
        cmd.Parameters.Add("@ReceiveMode",SqlDbType.VarChar,10).Value=mode;
        cmd.Parameters.Add("@LotBarcode",SqlDbType.NVarChar,50).Value=barcode;
        ColumnParams.Add(cmd,"@UserId","WH_InventoryTransaction.CreatedBy",actor);
        cmd.ExecuteNonQuery();
    }

    public StorageSelection ScanPutAway(string userId,string barcode)
    {
        RequireAccess(userId);barcode=Barcode(barcode);
        using var conn=factory.OpenConnection();
        return BuildStorageSelection(barcode,ReadStorage(conn,null,barcode))
            ??throw new InvalidOperationException("Received inventory was not found.");
    }

    public static StorageSelection? BuildStorageSelection(string barcode,List<StorageRow> rows)
    {
        var box=rows.FirstOrDefault(r=>Same(r.Barcode,barcode)||Same(r.LotNo,barcode));
        if(box is not null)return new(barcode,"BOX",[box],[box.Barcode],!string.IsNullOrWhiteSpace(box.LocationNo));
        var grouped=rows.Where(r=>Same(r.ParentLotNo,barcode)).ToList();
        if(grouped.Count>0)return new(barcode,"CASE",grouped,grouped.Where(r=>string.IsNullOrWhiteSpace(r.LocationNo)).Select(r=>r.Barcode).ToList());
        var document=rows.Where(r=>Same(r.DeliveryNoteNo,barcode)).ToList();
        return document.Count>0?new(barcode,"DELIVERY_NOTE",document,[]):null;
    }

    static List<StorageRow> ReadStorage(SqlConnection conn,SqlTransaction? tx,string barcode)
    {
        using var cmd=new SqlCommand(PdaWarehouseSql.PutAwayInventory,conn,tx){CommandTimeout=15};
        cmd.Parameters.Add("@Barcode",SqlDbType.NVarChar,80).Value=barcode;
        cmd.Parameters.Add("@OnlyUnassigned",SqlDbType.Bit).Value=false;
        using var reader=cmd.ExecuteReader();var rows=new List<StorageRow>();
        string? Text(string name)=>reader[name] as string;
        while(reader.Read())rows.Add(new(Text("Mode")??"LOCAL",Text("Barcode")??"",Text("LotNo")??"",Text("PartNo"),Text("PartName"),
            Convert.ToDecimal(reader["Qty"]),Text("Unit"),Text("DeliveryNoteNo"),reader["ReceivedAt"] is DateTime date?date:null,Text("LocationNo"),Text("ParentLotNo")));
        return rows;
    }

    public StorageLocation ScanPutAwayLocation(string userId,string barcode)
    {
        RequireAccess(userId);barcode=Barcode(barcode);
        using var conn=factory.OpenConnection();using var cmd=new SqlCommand("""
            SELECT L.LocationID,L.LocationName,L.WhCode,L.AreaCode,L.Aisle,L.Capacity,L.LocationType,
                (SELECT COALESCE(SUM(I.Qty),0) FROM dbo.WH_Inventory I WHERE I.LocationNo=L.LocationID AND I.Qty>0) Qty
            FROM dbo.MD_Location L WHERE L.LocationID=@Location AND COALESCE(L.ActiveFlag,1)=1;
            """,conn);
        cmd.Parameters.Add("@Location",SqlDbType.VarChar,50).Value=barcode;
        using var reader=cmd.ExecuteReader();
        if(!reader.Read())throw new InvalidOperationException("Location was not found or is inactive.");
        if((reader["LocationType"] as string)?.ToUpperInvariant() is "BLOCKED" or "HOLD" or "QUARANTINE" or "INACTIVE")
            throw new InvalidOperationException("This location is blocked.");
        return new(reader.GetString(0),reader[1] as string,reader[2] as string,reader[3] as string,reader[4] as string,
            reader[5] is decimal capacity?capacity:null,Convert.ToDecimal(reader["Qty"]));
    }

    public void PutAway(string userId,string actor,IReadOnlyCollection<string> barcodes,string location,bool relocate=false)
    {
        RequireAccess(userId,receive:true);CheckActor(actor);location=Barcode(location);
        if(barcodes.Count is <1 or >1000)throw new ArgumentException("Scan 1 to 1,000 boxes.");
        var selected=barcodes.Select(Barcode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        using var conn=factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
        foreach(var barcode in selected)
        {
            using(var check=new SqlCommand("SELECT LocationNo FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Barcode AND Qty>0",conn,tx))
            {
                check.Parameters.Add("@Barcode",SqlDbType.NVarChar,50).Value=barcode;
                using var reader=check.ExecuteReader();
                if(!reader.Read())throw new InvalidOperationException($"Received inventory {barcode} was not found.");
                if(!relocate && reader[0] is string current && !string.IsNullOrWhiteSpace(current))
                    throw new InvalidOperationException($"{barcode} is already put away at {current}.");
            }
            var row=ReadStorage(conn,tx,barcode).SingleOrDefault(r=>Same(r.Barcode,barcode))
                ??throw new InvalidOperationException("Scan an individual received box or LOT.");
            using var cmd=new SqlCommand(PdaWarehouseSql.InboundMove,conn,tx){CommandTimeout=30};
            cmd.Parameters.Add("@ReceiveMode",SqlDbType.VarChar,10).Value=row.Mode;
            cmd.Parameters.Add("@LotBarcode",SqlDbType.NVarChar,50).Value=barcode;
            cmd.Parameters.Add("@LocationId",SqlDbType.VarChar,50).Value=location;
            ColumnParams.Add(cmd,"@UserId","WH_InventoryTransaction.CreatedBy",actor);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    static void CheckActor(string actor)
    {if(string.IsNullOrWhiteSpace(actor)||actor.Length>20)throw new ArgumentException("A signed-in operator is required.");}
    static bool Same(string? left,string? right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
}
