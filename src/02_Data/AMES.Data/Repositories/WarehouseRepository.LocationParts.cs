using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public sealed record LocationPartSetting(string PartNo, decimal MinimumQty);
    public sealed record LocationPartRow(string LocationId, string PartNo, string? PartName, string Unit, decimal MinimumQty);

    private static string LocationPartSettingsSql(SqlConnection connection, SqlTransaction? transaction = null)
    {
        using var cmd = new SqlCommand("SELECT OBJECT_ID(N'dbo.MD_LocationPart',N'U');", connection, transaction);
        return cmd.ExecuteScalar() is int
            ? "SELECT LocationID,PartNo,MinimumQty FROM dbo.MD_LocationPart"
            : "SELECT LocationID,PartNo,MinimumQty FROM dbo.MD_Location WHERE NULLIF(PartNo,'') IS NOT NULL";
    }

    // BOP chooses the line/output; its effective BOM supplies the input parts, not the output itself.
    internal const string LineMaterialPartsSql = """
        SELECT DISTINCT R.LineID,B.CompItemNo PartNo
        FROM (
            SELECT S.LineID,P.ItemNo FROM dbo.MD_Bop P
            JOIN dbo.MD_Station S ON S.StationCode=P.StationCode
            WHERE COALESCE(P.ActiveFlag,1)=1 AND COALESCE(S.Status,'ACTIVE')='ACTIVE'
            UNION
            SELECT R.LineID,COALESCE(R.ItemNo,W.ItemNo) FROM dbo.PP_WorkOrderRouting R
            JOIN dbo.PP_WorkOrder W ON W.WoID=R.WoID
            WHERE W.Status IN('Released','In Progress') AND R.Status IN('Released','In Progress')
        ) R
        CROSS APPLY (
            SELECT TOP(1) V.VersionID FROM dbo.MD_BomVersion V
            WHERE V.Status='APPROVED'
              AND (V.EffFrom IS NULL OR V.EffFrom<=CAST(SYSDATETIME() AS date))
              AND (V.EffTo IS NULL OR V.EffTo>=CAST(SYSDATETIME() AS date))
              AND EXISTS(SELECT 1 FROM dbo.MD_Bom B WHERE B.VersionID=V.VersionID AND B.ParentItemNo=R.ItemNo AND COALESCE(B.ActiveFlag,1)=1)
            ORDER BY V.EffFrom DESC,V.VersionID DESC
        ) V
        JOIN dbo.MD_Bom B ON B.VersionID=V.VersionID AND B.ParentItemNo=R.ItemNo
        JOIN dbo.MD_Item I ON I.ItemNo=B.CompItemNo AND COALESCE(I.ActiveFlag,1)=1
        WHERE COALESCE(B.ActiveFlag,1)=1 AND B.QtyPer>0
        """;

    public static string LocationPartAssignmentsSql(SqlConnection connection, SqlTransaction? transaction = null)
        => $"""
            SELECT A.LocationID,A.PartNo,MAX(A.MinimumQty) MinimumQty FROM (
                SELECT S.LocationID,S.PartNo,S.MinimumQty FROM ({LocationPartSettingsSql(connection,transaction)}) S
                JOIN dbo.MD_Location C ON C.LocationID=S.LocationID
                WHERE NULLIF(C.LineID,'') IS NULL OR S.MinimumQty>0
                UNION ALL
                SELECT L.LocationID,P.PartNo,CONVERT(decimal(18,3),0) MinimumQty
                FROM dbo.MD_Location L JOIN ({LineMaterialPartsSql}) P ON P.LineID=L.LineID
                WHERE L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1
            ) A GROUP BY A.LocationID,A.PartNo
            """;

    public List<LocationPartRow> ListLocationParts(string? locationId = null)
    {
        using var connection = _factory.OpenConnection();
        using var cmd = new SqlCommand($"""
            SELECT A.LocationID,A.PartNo,I.ItemName,COALESCE(NULLIF(I.DefaultUOM,''),'EA') Unit,COALESCE(A.MinimumQty,0) MinimumQty
            FROM ({LocationPartAssignmentsSql(connection)}) A
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=A.PartNo
            WHERE @Location IS NULL OR A.LocationID=@Location
            ORDER BY A.LocationID,A.PartNo;
            """, connection);
        cmd.Parameters.Add("@Location", SqlDbType.VarChar, 20).Value = (object?)NullIfBlank(locationId) ?? DBNull.Value;
        using var reader = cmd.ExecuteReader();
        var result = new List<LocationPartRow>();
        while (reader.Read()) result.Add(new(reader.GetString(0),reader.GetString(1),reader[2] as string,reader.GetString(3),reader.GetDecimal(4)));
        return result;
    }

    internal static List<LocationPartSetting> ValidateLocationParts(IEnumerable<LocationPartSetting> assignments)
    {
        var rows = assignments.Select(a => a with { PartNo = a.PartNo.Trim() }).ToList();
        if (rows.Any(a => a.PartNo.Length is 0 or >20 || a.MinimumQty < 0 || a.MinimumQty > 99999999999m
            || decimal.Round(a.MinimumQty,3) != a.MinimumQty))
            throw new ArgumentException("Select a part and enter a nonnegative minimum with up to three decimal places.");
        if (rows.Select(a => a.PartNo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            throw new ArgumentException("Each part can only be assigned once to a location.");
        return rows;
    }

    internal static void DeleteLocationParts(SqlConnection connection,SqlTransaction transaction,string locationId)
    {
        using var cmd=new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.WH_PickSlip WITH(UPDLOCK,HOLDLOCK)
                WHERE ReqLocation=@Location AND CloseDate IS NULL
                  AND UPPER(COALESCE(Status,'OPEN')) NOT IN('CLOSED','RELEASED','CANCELLED','CANCELED'))
                THROW 51050,'Close outstanding picking orders before deleting the location.',1;
            IF OBJECT_ID(N'dbo.MD_LocationPart',N'U') IS NOT NULL
                EXEC sys.sp_executesql N'DELETE dbo.MD_LocationPart WHERE LocationID=@Location',N'@Location varchar(20)',@Location;
            """,connection,transaction);
        cmd.Parameters.Add("@Location",SqlDbType.VarChar,20).Value=locationId;
        cmd.ExecuteNonQuery();
    }

    internal static void SaveLocationParts(SqlConnection connection, SqlTransaction transaction, string locationId,
        IEnumerable<LocationPartSetting> assignments, string actor)
    {
        var rows = ValidateLocationParts(assignments);
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 20) throw new ArgumentException("Use an actor ID of up to 20 characters.");
        using var exists = new SqlCommand("SELECT OBJECT_ID(N'dbo.MD_LocationPart',N'U');", connection, transaction);
        if (exists.ExecuteScalar() is not int)
            throw new InvalidOperationException("Apply migrate_location_parts.sql before saving multiple assigned parts.");
        using var cmd = new SqlCommand($"""
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET NUMERIC_ROUNDABORT OFF;
            IF NOT EXISTS(SELECT 1 FROM dbo.MD_Location WITH(UPDLOCK,HOLDLOCK)
                WHERE LocationID=@Location AND LocationLevel=2 AND COALESCE(ActiveFlag,1)=1)
                THROW 52077,'Assign parts only to an active detail location.',1;
            DECLARE @Selected TABLE(PartNo varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY,MinimumQty decimal(18,3));
            INSERT @Selected SELECT PartNo,MinimumQty FROM OPENJSON(@Parts)
                WITH(PartNo varchar(20) '$.PartNo',MinimumQty decimal(18,3) '$.MinimumQty');
            IF EXISTS(SELECT 1 FROM @Selected S LEFT JOIN dbo.MD_Item I WITH(HOLDLOCK) ON I.ItemNo=S.PartNo
                WHERE I.ItemNo IS NULL OR COALESCE(I.ActiveFlag,1)<>1)
                THROW 51050,'Select active Item Master parts.',1;
            DECLARE @Allowed TABLE(PartNo varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY);
            INSERT @Allowed SELECT PartNo FROM @Selected UNION
                SELECT P.PartNo FROM ({LineMaterialPartsSql}) P JOIN dbo.MD_Location L ON L.LineID=P.LineID WHERE L.LocationID=@Location;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory W WITH(UPDLOCK,HOLDLOCK)
                WHERE W.LocationNo=@Location AND W.Qty>0 AND NOT EXISTS(SELECT 1 FROM @Selected S WHERE S.PartNo=W.PartNo)
                  AND NOT EXISTS(SELECT 1 FROM @Allowed A WHERE A.PartNo=W.PartNo)
                  AND (EXISTS(SELECT 1 FROM @Selected) OR EXISTS(SELECT 1 FROM dbo.MD_LocationPart A WHERE A.LocationID=@Location AND A.PartNo=W.PartNo)))
                THROW 51050,'Keep stocked parts assigned, or move their stock before removing them.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_PickSlip P WITH(UPDLOCK,HOLDLOCK)
                WHERE P.ReqLocation=@Location AND P.CloseDate IS NULL
                  AND UPPER(COALESCE(P.Status,'OPEN')) NOT IN('CLOSED','RELEASED','CANCELLED','CANCELED')
                  AND NOT EXISTS(SELECT 1 FROM @Allowed S WHERE S.PartNo=P.ItemNo))
                THROW 51050,'Close outstanding picking orders before removing their assigned parts.',1;
            DELETE A FROM dbo.MD_LocationPart A
                WHERE A.LocationID=@Location AND NOT EXISTS(SELECT 1 FROM @Selected S WHERE S.PartNo=A.PartNo);
            UPDATE A SET MinimumQty=S.MinimumQty,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
                FROM dbo.MD_LocationPart A JOIN @Selected S ON S.PartNo=A.PartNo WHERE A.LocationID=@Location;
            INSERT dbo.MD_LocationPart(LocationID,PartNo,MinimumQty,CreatedBy)
                SELECT @Location,S.PartNo,S.MinimumQty,@Actor FROM @Selected S
                WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_LocationPart A WHERE A.LocationID=@Location AND A.PartNo=S.PartNo);
            -- A single assignment remains readable by old clients; never pretend that the first of many is the only part.
            UPDATE dbo.MD_Location SET PartNo=CASE WHEN (SELECT COUNT(*) FROM @Selected)=1 THEN (SELECT TOP(1) PartNo FROM @Selected) END,
                MinimumQty=CASE WHEN (SELECT COUNT(*) FROM @Selected)=1 THEN (SELECT TOP(1) MinimumQty FROM @Selected) ELSE 0 END
                WHERE LocationID=@Location;
            """, connection, transaction);
        cmd.Parameters.Add("@Location",SqlDbType.VarChar,20).Value=locationId;
        cmd.Parameters.Add("@Actor",SqlDbType.VarChar,20).Value=actor;
        cmd.Parameters.Add("@Parts",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(rows);
        cmd.ExecuteNonQuery();
    }
}
