using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using AMES.Contracts.Dto;
using AMES.Contracts.Exceptions;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public const string PickingRequestActor = "WH-REQUEST";
    public sealed record PickingRequestInput(string Destination, string PartNo, decimal Qty, DateTime RequiredAt, bool LineLocationOnly = false, bool ConfirmAdditional = false);
    public sealed record PickingRequestOption(string Code, string Display, string Unit = "");

    public bool HasPickingRequestAccess(string employeeNo, char permission) => Query("""
        SELECT TOP(1) 1
        FROM dbo.SYS_UserProfile P JOIN dbo.AspNetUsers U ON U.Id=P.UserID
        JOIN dbo.AspNetUserRoles UR ON UR.UserId=U.Id
        JOIN dbo.AspNetRoles R ON R.Id=UR.RoleId
        JOIN dbo.SYS_RolePermission RP ON RP.RoleID=R.Id
            OR (NULLIF(RP.RoleID,'') IS NULL AND RP.RoleName=R.Name)
        JOIN dbo.SYS_Screen S ON S.ScreenCode=RP.ScreenCode
        WHERE P.EmployeeNo=@Employee AND P.AccountStatus='ACTIVE'
          AND (U.LockoutEnd IS NULL OR U.LockoutEnd<=SYSDATETIME())
          AND TRIM('/' FROM S.HRef)='wh/picking-requests' AND S.ModuleCode='WEB'
          AND RP.ModuleCode='WEB' AND ISNULL(S.IsVisible,1)=1
          AND RP.PermissionLevel LIKE @Permission;
        """, r=>r.GetInt32(0), ("@Employee","SYS_UserProfile.EmployeeNo",employeeNo),
            ("@Permission",null,$"%{permission}%")).Count>0;

    public List<PickingRequestOption> ListPickingRequestDestinations(bool lineLocationsOnly = false) => Query("""
        SELECT L.LocationID Code,CONCAT(L.WhCode,' / ',L.AreaCode,' / ',L.LocationID,' - ',L.LocationName) Display
        FROM dbo.MD_Location L WHERE L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1
          AND UPPER(COALESCE(L.LocationType,'')) NOT IN('FG','BLOCKED','HOLD','QUARANTINE','INACTIVE')
          AND COALESCE(L.AreaCode,'') NOT IN('FG','FG_AREA','MNT_AREA','SP_AREA')
          AND (@LineOnly=0 OR EXISTS(SELECT 1 FROM dbo.MD_Line M WHERE M.LineID=L.LineID AND COALESCE(M.Status,'ACTIVE')='ACTIVE'))
        UNION ALL
        SELECT M.LineID,CONCAT('LINE / ',M.LineID,' - ',COALESCE(M.LineNameEn,M.LineID))
        FROM dbo.MD_Line M WHERE COALESCE(M.Status,'ACTIVE')='ACTIVE'
          AND @LineOnly=0
          AND EXISTS(SELECT 1 FROM dbo.MD_Location L WHERE L.LineID=M.LineID AND L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1)
        ORDER BY Display;
        """, r => new PickingRequestOption(r.GetString(0),r.GetString(1)), ("@LineOnly",null,lineLocationsOnly));

    public List<PickingRequestOption> ListPickingRequestParts() => Query("""
        SELECT ItemNo,CONCAT(ItemNo,' - ',ItemName),COALESCE(NULLIF(DefaultUOM,''),'EA')
        FROM dbo.MD_Item WHERE COALESCE(ActiveFlag,1)=1 AND UPPER(COALESCE(ItemType,''))<>'ASSY'
        ORDER BY ItemNo;
        """, r => new PickingRequestOption(r.GetString(0),r.GetString(1),r.GetString(2)));

    public List<ExistingPickingRequestDto> ListExistingPickingRequests(string destination,string partNo)
    {
        if(string.IsNullOrWhiteSpace(destination)||destination.Length>20||string.IsNullOrWhiteSpace(partNo)||partNo.Length>20)
            throw new ArgumentException("Scan a valid line location and part number.");
        using var connection=_factory.OpenConnection();
        return ListExistingPickingRequests(connection,null,destination.Trim(),partNo.Trim());
    }

    internal static List<ExistingPickingRequestDto> ListExistingPickingRequests(SqlConnection connection,SqlTransaction? transaction,string destination,string partNo)
    {
        using var command=new SqlCommand("""
            SELECT S.PickSlipNo,S.ReqLocation,S.ItemNo,SUM(S.DemandQty),SUM(COALESCE(S.PickedQty,0)),
                SUM(S.DemandQty-COALESCE(S.PickedQty,0)),
                CASE WHEN S.CreatedBy IN('WH-AUTO','WH-REQUEST') THEN COALESCE(NULLIF(I.DefaultUOM,''),'EA') ELSE 'BOX' END,
                CASE WHEN MAX(CASE WHEN S.Status='Partial' THEN 1 ELSE 0 END)=1 THEN 'Partial' ELSE 'Open' END
            FROM dbo.WH_PickSlip S LEFT JOIN dbo.MD_Item I ON I.ItemNo=S.ItemNo
            WHERE S.ReqLocation=@Destination AND S.ItemNo=@Part
              AND S.Status IN('Open','Partial') AND S.DemandQty>COALESCE(S.PickedQty,0)
            GROUP BY S.PickSlipNo,S.ReqLocation,S.ItemNo,
                CASE WHEN S.CreatedBy IN('WH-AUTO','WH-REQUEST') THEN COALESCE(NULLIF(I.DefaultUOM,''),'EA') ELSE 'BOX' END
            ORDER BY S.PickSlipNo;
            """,connection,transaction);
        command.Parameters.Add("@Destination",SqlDbType.VarChar,20).Value=destination;
        command.Parameters.Add("@Part",SqlDbType.VarChar,20).Value=partNo;
        using var reader=command.ExecuteReader();
        var result=new List<ExistingPickingRequestDto>();
        while(reader.Read())result.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),
            reader.GetDecimal(4),reader.GetDecimal(5),reader.GetString(6),reader.GetString(7)));
        return result;
    }

    internal static PickingRequestInput ValidatePickingRequest(PickingRequestInput input, string actor)
    {
        input=input with{Destination=input.Destination?.Trim()??"",PartNo=input.PartNo?.Trim()??""};
        if(input.Destination.Length is 0 or >20 || input.PartNo.Length is 0 or >20 ||
            string.IsNullOrWhiteSpace(actor) || actor.Length>20 || input.Qty<=0 || input.Qty>99999999999m ||
            decimal.Round(input.Qty,3)!=input.Qty)
            throw new ArgumentException("Select a location and part; enter a positive quantity with up to three decimal places.");
        return input;
    }

    public string CreatePickingRequest(PickingRequestInput input, string actor)
    {
        input=ValidatePickingRequest(input,actor);
        using var connection=_factory.OpenConnection();
        using var transaction=connection.BeginTransaction();
        var slip=CreatePickingRequest(connection,transaction,input,actor);
        transaction.Commit();
        return slip;
    }

    internal static string CreatePickingRequest(SqlConnection connection,SqlTransaction transaction,PickingRequestInput input,string actor)
    {
        input=ValidatePickingRequest(input,actor);
        LockLinePicking(connection,transaction);
        using var validate=new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.MD_Item WITH(UPDLOCK,HOLDLOCK) WHERE ItemNo=@Part AND COALESCE(ActiveFlag,1)=1 AND UPPER(COALESCE(ItemType,''))<>'ASSY')
                THROW 51831,'The requested part is inactive, missing, or is an ASSY finished good.',1;
            SELECT TOP(1) L.LocationID,L.LineID
            FROM dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.MD_Line M ON M.LineID=L.LineID
            WHERE (L.LocationID=@Destination OR L.LineID=@Destination)
              AND (@LineOnly=0 OR (L.LocationID=@Destination AND M.LineID IS NOT NULL))
              AND L.LocationLevel=2 AND COALESCE(L.ActiveFlag,1)=1
              AND (NULLIF(L.LineID,'') IS NULL OR COALESCE(M.Status,'ACTIVE')='ACTIVE')
              AND UPPER(COALESCE(L.LocationType,'')) NOT IN('FG','BLOCKED','HOLD','QUARANTINE','INACTIVE')
              AND COALESCE(L.AreaCode,'') NOT IN('FG','FG_AREA','MNT_AREA','SP_AREA')
            ORDER BY CASE WHEN L.LocationID=@Destination THEN 0 ELSE 1 END,L.LocationID;
            """,connection,transaction);
        validate.Parameters.Add("@Destination",SqlDbType.VarChar,20).Value=input.Destination;
        validate.Parameters.Add("@LineOnly",SqlDbType.Bit).Value=input.LineLocationOnly;
        validate.Parameters.Add("@Part",SqlDbType.VarChar,20).Value=input.PartNo;
        string location;string? line;
        using(var reader=validate.ExecuteReader())
        {
            if(!reader.Read()) throw new InvalidOperationException(input.LineLocationOnly
                ? "Scan an active production-line detail location." : "Select an active warehouse or production-line detail location.");
            location=reader.GetString(0);line=reader[1] as string;
        }
        var existing=ListExistingPickingRequests(connection,transaction,location,input.PartNo);
        if(existing.Count>0 && !input.ConfirmAdditional)throw new PickingRequestExistsException(existing);
        string slip;
        if(!string.IsNullOrWhiteSpace(line)) slip=GeneratePickSlipNo(connection,transaction,line);
        else
        {
            using var number=new SqlCommand("""
                DECLARE @Prefix nvarchar(10)=CONCAT('PKWH',CONVERT(char(6),SYSDATETIME(),12));
                DECLARE @Seq int=(SELECT COALESCE(MAX(TRY_CONVERT(int,SUBSTRING(PickSlipNo,11,30))),0)+1
                    FROM dbo.WH_PickSlip WITH(UPDLOCK,HOLDLOCK) WHERE PickSlipNo LIKE @Prefix+'[0-9][0-9][0-9]%');
                SELECT CONCAT(@Prefix,CASE WHEN @Seq<1000 THEN RIGHT(CONCAT('000',@Seq),3) ELSE CONVERT(varchar(10),@Seq) END);
                """,connection,transaction);
            slip=Convert.ToString(number.ExecuteScalar())!;
        }
        using var insert=new SqlCommand("""
            INSERT dbo.WH_PickSlip(PickSlipNo,ReqLocation,ReqSeqNo,ReqUserId,ItemNo,DemandQty,PickedQty,RequiredAt,Priority,Status,CreatedBy,CreatedTS)
            VALUES(@Slip,@Location,1,@Actor,@Part,@Qty,0,@Due,1,'Open','WH-REQUEST',SYSDATETIME());
            INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,AfterValueJSON,Result,CreatedBy,CreatedTS)
            VALUES(SYSDATETIME(),@Actor,'WH','WH-05','CREATE','WH_PickSlip',@Slip,@After,'OK',@Actor,SYSDATETIME());
            """,connection,transaction);
        insert.Parameters.Add("@Slip",SqlDbType.NVarChar,40).Value=slip;
        insert.Parameters.Add("@Location",SqlDbType.VarChar,20).Value=location;
        insert.Parameters.Add("@Actor",SqlDbType.VarChar,20).Value=actor;
        insert.Parameters.Add("@Part",SqlDbType.VarChar,20).Value=input.PartNo;
        AddQtyDecimal(insert,"@Qty",input.Qty);
        insert.Parameters.Add("@Due",SqlDbType.DateTime2).Value=input.RequiredAt.Date;
        insert.Parameters.Add("@After",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(new{PickSlipNo=slip,Location=location,input.PartNo,input.Qty,input.RequiredAt,RequestedBy=actor});
        insert.ExecuteNonQuery();
        return slip;
    }
}
