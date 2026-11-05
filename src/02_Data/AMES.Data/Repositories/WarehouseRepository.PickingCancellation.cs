using System.Data;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public void CancelPickingSlip(string pickSlipNo, string cancelledBy)
    {
        pickSlipNo = pickSlipNo.Trim();
        cancelledBy = cancelledBy.Trim();
        if (pickSlipNo.Length is 0 or > 40) throw new ArgumentException("A picking order number of up to 40 characters is required.", nameof(pickSlipNo));
        if (cancelledBy.Length is 0 or > 20) throw new ArgumentException("An actor code of up to 20 characters is required.", nameof(cancelledBy));
        using var connection = _factory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        CancelPickingSlip(connection, transaction, pickSlipNo, cancelledBy);
        transaction.Commit();
    }

    internal static void CancelPickingSlip(SqlConnection connection, SqlTransaction transaction, string slip, string actor)
    {
        // Serialize with PDA Release and automatic regeneration; released stock remains in transit.
        LockLinePicking(connection, transaction);
        using var command = new SqlCommand("""
            DECLARE @Before nvarchar(max)=(SELECT PickSlipID,Status,DemandQty,PickedQty,CloseDate,CloseUserId
                FROM dbo.WH_PickSlip WITH(UPDLOCK,HOLDLOCK)
                WHERE COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID))=@Slip
                ORDER BY PickSlipID FOR JSON PATH,INCLUDE_NULL_VALUES);
            UPDATE dbo.WH_PickSlip SET Status='Cancelled',CloseDate=SYSDATETIME(),CloseUserId=@Actor,
                ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            WHERE COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID))=@Slip AND CloseDate IS NULL
              AND UPPER(COALESCE(Status,'OPEN')) NOT IN('CLOSED','RELEASED','PICKED','CANCELLED','CANCELED')
              AND COALESCE(DemandQty,0)>COALESCE(PickedQty,0);
            DECLARE @Changed int=@@ROWCOUNT;
            IF @Changed>0
            BEGIN
                DECLARE @After nvarchar(max)=(SELECT PickSlipID,Status,DemandQty,PickedQty,CloseDate,CloseUserId
                    FROM dbo.WH_PickSlip WHERE COALESCE(NULLIF(PickSlipNo,N''),CONCAT(N'RS-',PickSlipID))=@Slip
                    ORDER BY PickSlipID FOR JSON PATH,INCLUDE_NULL_VALUES);
                DECLARE @Screen varchar(20)=COALESCE((SELECT TOP(1) ScreenCode FROM dbo.SYS_Screen
                    WHERE ModuleCode='WEB' AND TRIM('/' FROM HRef)='wh/picking-orders'),'WH-003');
                INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,
                    BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
                VALUES(SYSDATETIME(),@Actor,'WH',@Screen,'CANCEL','WH_PickSlip',@Slip,@Before,@After,'OK',
                    N'Cancel outstanding picking quantities; retain picked quantities and stock. Shortages may be regenerated.',
                    @Actor,SYSDATETIME());
            END;
            SELECT @Changed;
            """, connection, transaction);
        command.Parameters.Add("@Slip", SqlDbType.NVarChar, 40).Value = slip;
        command.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
            throw new InvalidOperationException("Picking order was not found, is already cancelled, or has no unissued quantity left.");
    }
}
