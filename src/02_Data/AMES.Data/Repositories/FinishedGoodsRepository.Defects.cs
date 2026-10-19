using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public const string DefectPermissionHref = "wh/inventory/defect";
    public const string DefectPermissionCode = "FG-DEFECT";

    public HashSet<string> ListDefectLots()
    {
        using var conn=_factory.OpenConnection();
        using var cmd=new SqlCommand("""
            SELECT W.LotNo FROM dbo.WH_Inventory W
            WHERE W.Qty>0 AND NULLIF(LTRIM(RTRIM(W.PartNo)),'') IS NOT NULL
                AND NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory C WHERE C.ParentLotNo=W.LotNo);
            """,conn);
        using var r=cmd.ExecuteReader();var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(r.Read())result.Add(r.GetString(0));return result;
    }

    public static string ValidateDefectReason(string reason)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length>500)
            throw new ArgumentException("Enter a defect reason within 500 characters.");
        return reason.Trim();
    }

    public static decimal ValidateDefectQuantity(decimal quantity,decimal available,int digits=3)
    {
        if(quantity<=0 || quantity>available || quantity>999999999999999.999m
            || decimal.Round(quantity,Math.Clamp(digits,0,3))!=quantity)
            throw new ArgumentException("Enter a defect quantity greater than zero, within the available stock and unit precision.");
        if(quantity!=available)
            throw new ArgumentException("The entire LOT must be marked defective.");
        return quantity;
    }

    public void MarkInventoryDefective(string lotNo,string partNo,decimal expectedQty,string? location,decimal defectQty,string reason,string userId,string actor)
    {
        CheckActor(actor);reason=ValidateDefectReason(reason);
        ValidateDefectQuantity(defectQty,expectedQty);
        if(string.IsNullOrWhiteSpace(lotNo)||lotNo.Length>50||string.IsNullOrWhiteSpace(partNo)||partNo.Length>20
            ||expectedQty<=0||(location?.Length??0)>50||string.IsNullOrWhiteSpace(userId)||userId.Length>450)
            throw new ArgumentException("Select current inventory and refresh before retrying.");
        using var conn=_factory.OpenConnection();using var tx=conn.BeginTransaction(IsolationLevel.Serializable);Gate(conn,tx);
        using var cmd=new SqlCommand("""
            SET XACT_ABORT ON;
            IF NOT EXISTS(SELECT 1 FROM dbo.AspNetUserRoles U WITH(HOLDLOCK)
                JOIN dbo.SYS_RolePermission P WITH(HOLDLOCK) ON P.RoleID=U.RoleId
                JOIN dbo.SYS_Screen S ON S.ScreenCode=P.ScreenCode AND S.ModuleCode='WEB'
                JOIN dbo.AspNetUsers A ON A.Id=U.UserId
                LEFT JOIN dbo.SYS_UserProfile UP ON UP.UserID=A.Id
                WHERE U.UserId=@User AND P.ModuleCode='WEB' AND P.ScreenCode='FG-DEFECT'
                    AND P.PermissionLevel LIKE '%A%' AND SUBSTRING(S.PermissionCriteria,3,1)<>'X'
                    AND (A.LockoutEnd IS NULL OR A.LockoutEnd<=SYSDATETIMEOFFSET())
                    AND COALESCE(UP.AccountStatus,'ACTIVE')='ACTIVE')
                THROW 52600,'You do not have the Inventory Defect permission.',1;
            DECLARE @Qty decimal(18,3),@After decimal(18,3),@Now datetime2=SYSDATETIME(),@Unit varchar(10)='EA',@Digits int;
            SELECT @Qty=W.Qty FROM dbo.WH_Inventory W WITH(UPDLOCK,HOLDLOCK)
                WHERE W.LotNo=@Lot AND W.PartNo=@Part AND W.Qty>0 AND ISNULL(W.LocationNo,'')=ISNULL(@Location,'');
            IF @Qty IS NULL OR @Qty<>@Expected THROW 52602,'LOT quantity or location changed. Refresh inventory and select the LOT again.',1;
            IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(HOLDLOCK) WHERE ParentLotNo=@Lot)
                THROW 52601,'Select the individual box or LOT, not a pallet or group.',1;
            IF EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Lot AND ShipmentID IS NULL)
                THROW 52603,'Remove this LOT from trailer loading before marking it defective.',1;
            SELECT @Unit=COALESCE(NULLIF(DefaultUOM,''),'EA')
            FROM dbo.MD_Item WITH(HOLDLOCK) WHERE ItemNo=@Part;
            IF @Defect<>@Qty
                THROW 52605,'The entire LOT must be marked defective.',1;
            SELECT @Digits=DecimalPrec FROM dbo.MD_Uom WHERE UOMCode=@Unit;
            SET @Digits=CASE WHEN @Digits IS NULL THEN CASE WHEN @Unit='EA' THEN 0 ELSE 3 END
                WHEN @Digits<0 THEN 0 WHEN @Digits>3 THEN 3 ELSE @Digits END;
            IF @Defect<=0 OR @Defect>@Qty OR ROUND(@Defect,@Digits)<>@Defect
                THROW 52604,'Defect quantity must be positive, within the available stock and unit precision.',1;
            SET @After=@Qty-@Defect;
            UPDATE dbo.WH_Inventory SET Qty=@After,UpdatedAt=@Now WHERE LotNo=@Lot;
            INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
            VALUES(@Now,'ADJ',@Part,@Location,@Lot,@Qty,-@Defect,@After,'DEFECT','INV_DEFECT',NULL,@Actor,@Reason,@Actor,@Now);
            INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,
                BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
            VALUES(@Now,@User,'WH','FG-DEFECT','UPDATE','WH_Inventory',CASE WHEN LEN(@Lot)<=40 THEN @Lot END,
                (SELECT @Lot LotNo,@Qty Qty,@Location LocationNo FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
                (SELECT @Lot LotNo,@After Qty,@Defect DefectQty,@Reason DefectReason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),'OK',@Reason,@Actor,@Now);
            """,conn,tx);
        Add(cmd,"@Lot",SqlDbType.NVarChar,50,lotNo);Add(cmd,"@Part",SqlDbType.VarChar,20,partNo);
        Add(cmd,"@Location",SqlDbType.VarChar,50,string.IsNullOrEmpty(location)?null:location);
        Add(cmd,"@Reason",SqlDbType.NVarChar,500,reason);Add(cmd,"@User",SqlDbType.NVarChar,450,userId);
        Add(cmd,"@Actor",SqlDbType.VarChar,20,actor);Qty(cmd,"@Expected",expectedQty);Qty(cmd,"@Defect",defectQty);
        cmd.ExecuteNonQuery();tx.Commit();
    }
}
