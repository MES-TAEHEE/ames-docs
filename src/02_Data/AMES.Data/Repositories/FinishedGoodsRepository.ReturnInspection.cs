using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public static bool IsReturnAwaitingInspection(string? status) =>
        string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "Inspecting", StringComparison.OrdinalIgnoreCase);

    public static string ValidateReturnInspectionNote(bool accepted, string? note)
    {
        var value = (note ?? "").Trim();
        if (value.Length > 500 || (!accepted && value.Length == 0))
            throw new ArgumentException("Enter an inspection reason within 500 characters. A reason is required for defective returns.");
        return value;
    }

    public static void EnsureReturnInspected(SqlConnection conn, SqlTransaction tx, string lotNo)
    {
        using var cmd = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Lot AND Status IN('Open','Inspecting'))
                THROW 52614,'This customer return is awaiting inspection. Stock movement or adjustment is not allowed.',1;
            """, conn, tx);
        Add(cmd, "@Lot", SqlDbType.NVarChar, 50, lotNo);
        cmd.ExecuteNonQuery();
    }

    public void InspectCustomerReturn(int returnId, bool accepted, string? note, string userId, string actor)
    {
        CheckActor(actor);
        var reason = ValidateReturnInspectionNote(accepted, note);
        if (returnId <= 0 || string.IsNullOrWhiteSpace(userId) || userId.Length > 450)
            throw new ArgumentException("Select a return with a signed-in user.");
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        Gate(conn, tx);
        using var cmd = new SqlCommand(ReturnInspectionSql, conn, tx);
        cmd.Parameters.Add("@Return", SqlDbType.Int).Value = returnId;
        cmd.Parameters.Add("@Accepted", SqlDbType.Bit).Value = accepted;
        Add(cmd, "@Note", SqlDbType.NVarChar, 500, reason);
        Add(cmd, "@User", SqlDbType.NVarChar, 450, userId);
        Add(cmd, "@Actor", SqlDbType.VarChar, 20, actor);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    // Reuse Inventory Defect approval; return receipt alone never authorizes a disposition.
    internal const string ReturnInspectionSql = """
        IF NOT EXISTS(SELECT 1 FROM dbo.AspNetUserRoles U WITH(HOLDLOCK)
            JOIN dbo.SYS_RolePermission P WITH(HOLDLOCK) ON P.RoleID=U.RoleId
            JOIN dbo.SYS_Screen S ON S.ScreenCode=P.ScreenCode AND S.ModuleCode='WEB'
            JOIN dbo.AspNetUsers A ON A.Id=U.UserId
            LEFT JOIN dbo.SYS_UserProfile UP ON UP.UserID=A.Id
            WHERE U.UserId=@User AND P.ModuleCode='WEB' AND P.ScreenCode='FG-DEFECT'
                AND P.PermissionLevel LIKE '%A%' AND SUBSTRING(S.PermissionCriteria,3,1)<>'X'
                AND (A.LockoutEnd IS NULL OR A.LockoutEnd<=SYSDATETIMEOFFSET())
                AND COALESCE(UP.AccountStatus,'ACTIVE')='ACTIVE')
            THROW 52610,'You do not have the Inventory Defect approval permission for return inspection.',1;
        DECLARE @Lot nvarchar(50),@Part varchar(20),@Qty decimal(18,3),@LotID int,@Status varchar(15),
            @Location varchar(50),@Stock decimal(18,3),@Now datetime2=SYSDATETIME();
        SELECT @Lot=LotNo,@Part=PartNo,@Qty=ReturnQty,@LotID=LotID,@Status=Status
        FROM dbo.FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK) WHERE ReturnID=@Return;
        IF @Lot IS NULL OR @Status NOT IN('Open','Inspecting')
            THROW 52611,'This return has already been inspected or no longer exists. Refresh the list.',1;
        SELECT @Stock=Qty,@Location=LocationNo FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK)
            WHERE LotNo=@Lot AND PartNo=@Part;
        IF @Stock IS NULL OR @Stock<>@Qty OR @Qty<=0
            THROW 52612,'Returned inventory changed. Resolve the quantity before inspection.',1;
        IF EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad WITH(UPDLOCK,HOLDLOCK) WHERE LotNo=@Lot AND ShipmentID IS NULL)
            THROW 52613,'Remove this returned LOT from trailer loading before inspection.',1;
        IF @Accepted=0
        BEGIN
            UPDATE dbo.WH_Inventory SET Qty=0,UpdatedAt=@Now WHERE LotNo=@Lot;
            INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
            VALUES(@Now,'ADJ',@Part,@Location,@Lot,@Qty,-@Qty,0,'DEFECT','FG_DEFECT',@Return,@Actor,@Note,@Actor,@Now);
        END
        ELSE
        BEGIN
            -- Inspection releases the hold, not a second physical receipt.
            INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,QtyBefore,QtyChange,QtyAfter,
                ReasonCode,SourceType,SourceID,OperatorID,Note,CreatedBy,CreatedTS)
            VALUES(@Now,'ADJ',@Part,@Location,@Lot,@Qty,0,@Qty,'INSPECT_OK','FG_RETURN_INSPECT',@Return,@Actor,@Note,@Actor,@Now);
        END;
        UPDATE dbo.FG_CustomerReturn SET Status=CASE WHEN @Accepted=1 THEN 'Closed' ELSE 'Rejected' END,
            ClosedAt=@Now,ClosedBy=@Actor,ModifiedTS=@Now,ModifiedBy=@Actor WHERE ReturnID=@Return;
        UPDATE dbo.tbl_Lot SET RemainingQty=CASE WHEN @Accepted=1 THEN @Qty ELSE 0 END,
            InventoryStatus=CASE WHEN @Accepted=1 THEN 'RECEIVED' ELSE 'REJECTED' END,
            ModifiedTS=@Now,ModifiedBy=@Actor WHERE LotID=@LotID;
        INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,
            BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
        VALUES(@Now,@User,'FG','FG-01','UPDATE','FG_CustomerReturn',CONVERT(varchar(20),@Return),
            (SELECT @Lot LotNo,@Qty Qty,@Status Status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
            (SELECT @Lot LotNo,CASE WHEN @Accepted=1 THEN 'Closed' ELSE 'Rejected' END Status,
                @Accepted Accepted,@Note InspectionNote FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),'OK',@Note,@Actor,@Now);
        """;
}
