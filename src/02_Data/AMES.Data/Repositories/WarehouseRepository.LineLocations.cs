using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public sealed record LineLocationRow(string LocationId,string LineId,string AreaName,string X,string Y,string Floor,
        string? PartNo,string? PartName,decimal MinimumQty,decimal Qty);

    public List<LineLocationRow> ListLineLocations(string? lineId=null) => Query("""
        SELECT L.LocationID,L.LineID,COALESCE(NULLIF(M.LineNameEn,''),M.LineID) AreaName,
            L.Bay X,L.Aisle Y,L.Slot Floor,L.PartNo,I.ItemName,COALESCE(L.MinimumQty,0) MinimumQty,
            COALESCE(S.Qty,0) Qty
        FROM dbo.MD_Location L JOIN dbo.MD_Line M ON M.LineID=L.LineID
        LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.PartNo
        OUTER APPLY(SELECT SUM(W.Qty) Qty FROM dbo.WH_Inventory W
            WHERE W.LocationNo=L.LocationID AND W.PartNo=L.PartNo
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo)) S
        WHERE (@Line IS NULL OR L.LineID=@Line) AND COALESCE(L.ActiveFlag,1)=1
        ORDER BY L.LineID,L.Slot,L.Aisle,L.Bay;
        """,r=>new LineLocationRow(GetString(r,"LocationID")!,GetString(r,"LineID")!,GetString(r,"AreaName")!,
            GetString(r,"X")!,GetString(r,"Y")!,GetString(r,"Floor")!,GetString(r,"PartNo"),GetString(r,"ItemName"),
            GetDecimal(r,"MinimumQty"),GetDecimal(r,"Qty")),("@Line","MD_Line.LineID",NullIfBlank(lineId)));

    public static string LineLocationNo(string prefix,int x,int y,int floor)
    {
        if(prefix.Length!=2 || !prefix.All(char.IsAsciiLetterOrDigit) || x is <1 or >99 || y is <1 or >260 || floor is <1 or >99)
            throw new ArgumentException("Use a two-character line code, X 1–99, Y 1–260 and floor 1–99.");
        return $"{prefix.ToUpperInvariant()}{x:D2}{LocationMapNaming.YCode(y)}F{floor}";
    }

    public string SaveLineLocation(string lineId,int x,int y,int floor,string partNo,decimal minimum,string actor)
    {
        if(string.IsNullOrWhiteSpace(partNo) || partNo.Length>20 || minimum<0 || minimum>99999999999m || minimum!=decimal.Truncate(minimum)
            || string.IsNullOrWhiteSpace(actor) || actor.Length>20) throw new ArgumentException("Select a part and enter a whole minimum quantity.");
        using var conn=_factory.OpenConnection();
        using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
        string prefix,name;
        using(var read=new SqlCommand("SELECT LotPrefix,LineNameEn FROM dbo.MD_Line WITH(HOLDLOCK) WHERE LineID=@Line AND COALESCE(Status,'ACTIVE')='ACTIVE'",conn,tx))
        {
            read.Parameters.Add("@Line",SqlDbType.VarChar,20).Value=lineId;
            using var r=read.ExecuteReader();
            if(!r.Read() || r.IsDBNull(0) || r.IsDBNull(1) || string.IsNullOrWhiteSpace(r.GetString(1)))
                throw new InvalidOperationException("Set the line LotPrefix and English name first.");
            prefix=r.GetString(0).Trim(); name=r.GetString(1);
        }
        var id=LineLocationNo(prefix,x,y,floor);
        using var cmd=new SqlCommand("""
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET NUMERIC_ROUNDABORT OFF;
            IF EXISTS(SELECT 1 FROM dbo.MD_Line WHERE LotPrefix=@Prefix AND LineID<>@Line)
                THROW 51050,'The line LotPrefix must be unique.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.MD_Item WHERE ItemNo=@Part AND COALESCE(ActiveFlag,1)=1)
                THROW 51050,'Select an active Item Master part.',1;
            IF EXISTS(SELECT 1 FROM dbo.MD_Location WITH(UPDLOCK,HOLDLOCK) WHERE LocationID=@Id AND COALESCE(LineID,'')<>@Line)
                THROW 51050,'This location code is already in use.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LocationNo=@Id AND Qty>0 AND PartNo<>@Part)
                THROW 51050,'The location contains another part. Move its inventory before changing the assigned part.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_PickSlip WITH(UPDLOCK,HOLDLOCK) WHERE ReqLocation=@Id AND ItemNo<>@Part AND CloseDate IS NULL
                AND UPPER(COALESCE(Status,'OPEN')) NOT IN('CLOSED','RELEASED','CANCELLED','CANCELED'))
                THROW 51050,'Close the outstanding picking order before changing the assigned part.',1;
            DECLARE @WarehouseID varchar(50);
            SELECT @WarehouseID=CodeID FROM dbo.MD_CodeItem WITH(UPDLOCK,HOLDLOCK)
                WHERE GroupCode='WH_CODE' AND CodeValue='EOS' AND COALESCE(UseFlag,1)=1;
            IF @WarehouseID IS NULL THROW 51050,'Register the active EOS warehouse before creating line locations.',1;
            IF EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND CodeValue=@Prefix
                AND COALESCE(ParentCodeID,'')<>@WarehouseID)
                THROW 51050,'The line prefix is already used by another warehouse area.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode='WH_AREA' AND CodeValue=@Prefix)
                INSERT dbo.MD_CodeItem(CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,ParentCodeID,Attribute1,UseFlag,SortOrder,CreatedBy)
                VALUES(CONCAT('WH_AREA_',@Prefix),'WH_AREA',@Prefix,@Name,@Name,@WarehouseID,@Prefix,1,100,@Actor);
            ELSE UPDATE dbo.MD_CodeItem SET CodeName=@Name,CodeNameEn=@Name,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
                WHERE GroupCode='WH_AREA' AND CodeValue=@Prefix;
            IF EXISTS(SELECT 1 FROM dbo.MD_Location WHERE LocationID=@Id)
                UPDATE dbo.MD_Location SET WhCode='EOS',LocationName=@Name,PartNo=@Part,MinimumQty=@Min,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE LocationID=@Id;
            ELSE INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,Aisle,Bay,Slot,LocationType,LineID,PartNo,MinimumQty,ActiveFlag,CreatedBy)
                VALUES(@Id,@Name,'EOS',@Prefix,@Y,@X,@F,'LINE',@Line,@Part,@Min,1,@Actor);
            """,conn,tx);
        cmd.Parameters.Add("@Id",SqlDbType.VarChar,20).Value=id;
        cmd.Parameters.Add("@Line",SqlDbType.VarChar,20).Value=lineId;
        cmd.Parameters.Add("@Part",SqlDbType.VarChar,20).Value=partNo.Trim();
        cmd.Parameters.Add("@Prefix",SqlDbType.VarChar,2).Value=prefix;
        cmd.Parameters.Add("@Name",SqlDbType.NVarChar,50).Value=name;
        cmd.Parameters.Add("@Actor",SqlDbType.VarChar,20).Value=actor;
        cmd.Parameters.Add("@X",SqlDbType.VarChar,5).Value=x.ToString("D2");
        cmd.Parameters.Add("@Y",SqlDbType.VarChar,5).Value=LocationMapNaming.YCode(y);
        cmd.Parameters.Add("@F",SqlDbType.VarChar,5).Value=$"F{floor}";
        var min=cmd.Parameters.Add("@Min",SqlDbType.Decimal); min.Precision=18;min.Scale=3;min.Value=minimum;
        cmd.ExecuteNonQuery(); tx.Commit(); return id;
    }
}
