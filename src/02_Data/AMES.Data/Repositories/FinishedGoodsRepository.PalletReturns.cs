using System.Data;
using System.Text.Json;
using AMES.Contracts.Dto;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public static string ValidatePalletReturnBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Trim().Length > 50)
            throw new ArgumentException("Scan a registered pallet barcode within 50 characters.");
        return barcode.Trim();
    }

    public PalletReturnDto ScanEmptyPalletReturn(string barcode)
    {
        barcode = ValidatePalletReturnBarcode(barcode);
        using var conn = _factory.OpenConnection();
        return ReadPalletReturn(conn, null, barcode);
    }

    public PalletReturnDto ReceiveEmptyPalletReturn(PalletReturnRequest request, string actor)
    {
        CheckActor(actor);
        var barcode = ValidatePalletReturnBarcode(request.Barcode);
        if (request.ExpectedShippedAt == default)
            throw new ArgumentException("Scan the pallet again before confirming its return.");
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        Gate(conn, tx);
        var before = ReadPalletReturn(conn, tx, barcode);
        if (before.ShippedAt != request.ExpectedShippedAt)
            throw new InvalidOperationException("The pallet shipment changed. Scan the pallet again.");

        using var cmd = new SqlCommand("""
            DECLARE @Occupied bit=0;
            ;WITH Contents AS
            (
                SELECT LotNo,Qty FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK)
                WHERE LotNo=@Pallet OR ParentLotNo=@Pallet
                   OR (PalletNo=@Pallet AND ParentLotNo IS NULL AND UnitType IN('PALLET','CASE'))
                UNION ALL
                SELECT C.LotNo,C.Qty FROM dbo.WH_Inventory C WITH(UPDLOCK,HOLDLOCK)
                JOIN Contents P ON C.ParentLotNo=P.LotNo
            )
            SELECT @Occupied=1 FROM Contents WHERE Qty>0 OPTION(MAXRECURSION 100);
            IF @Occupied=1 THROW 52021,'This pallet still contains inventory. Receive returned products separately before returning an empty pallet.',1;
            UPDATE dbo.MD_Pallet SET ShippedFlag=0,ReturnedAt=SYSDATETIME(),
                ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            OUTPUT inserted.ReturnedAt
            WHERE PalletNo=@Pallet;
            """, conn, tx);
        Add(cmd, "@Pallet", SqlDbType.VarChar, 50, barcode);
        Add(cmd, "@Actor", SqlDbType.VarChar, 20, actor);
        var after = before with { ReturnedAt = (DateTime)cmd.ExecuteScalar()! };

        // Empty-pallet recovery has no product quantity or storage location.
        using var movement = new SqlCommand("""
            INSERT dbo.WH_InventoryTransaction(TransactionTime,TransactionType,PartNo,LocationNo,LotNo,
                QtyBefore,QtyChange,QtyAfter,ReasonCode,SourceType,OperatorID,Note,CreatedBy,CreatedTS)
            VALUES(@ReturnedAt,'IN',NULL,NULL,@Pallet,0,0,0,'PALLET_RETURN','PALLET_RETURN',@Actor,
                N'Empty pallet returned; location unassigned; product inventory unchanged.',@Actor,@ReturnedAt);
            """, conn, tx);
        Add(movement, "@Pallet", SqlDbType.NVarChar, 50, barcode);
        Add(movement, "@Actor", SqlDbType.VarChar, 20, actor);
        movement.Parameters.Add("@ReturnedAt", SqlDbType.DateTime2).Value = after.ReturnedAt!.Value;
        movement.ExecuteNonQuery();

        // State, transaction history, and audit commit together.
        using var audit = new SqlCommand("""
            INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ActionType,TargetEntity,TargetID,
                BeforeValueJSON,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
            VALUES(SYSDATETIME(),@Actor,'FG','UPDATE','MD_Pallet',CASE WHEN LEN(@Pallet)<=40 THEN @Pallet END,@Before,@After,
                'OK',N'Empty pallet returned; product inventory unchanged.',@Actor,SYSDATETIME());
            """, conn, tx);
        Add(audit, "@Actor", SqlDbType.VarChar, 20, actor);
        Add(audit, "@Pallet", SqlDbType.VarChar, 50, barcode);
        Add(audit, "@Before", SqlDbType.NVarChar, -1, JsonSerializer.Serialize(new { before.PalletNo, ShippedFlag = true, before.ShippedAt, before.ReturnedAt }));
        Add(audit, "@After", SqlDbType.NVarChar, -1, JsonSerializer.Serialize(new { after.PalletNo, ShippedFlag = false, after.ShippedAt, after.ReturnedAt }));
        audit.ExecuteNonQuery();
        tx.Commit();
        return after;
    }

    private static PalletReturnDto ReadPalletReturn(SqlConnection conn, SqlTransaction? tx, string barcode)
    {
        using var cmd = new SqlCommand("""
            SELECT P.PalletNo,P.ShippedCustomerID,C.CustomerName,P.ShippedAt,P.ReturnedAt,P.ShippedFlag,P.ActiveFlag
            FROM dbo.MD_Pallet P WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.MD_Customer C ON C.CustomerID=P.ShippedCustomerID
            WHERE P.PalletNo=@Pallet;
            """, conn, tx);
        Add(cmd, "@Pallet", SqlDbType.VarChar, 50, barcode);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Pallet not found in Pallet Master. Scan the physical pallet label, not a product or box label.");
        if (!r.GetBoolean(6)) throw new InvalidOperationException("This pallet is inactive in Pallet Master.");
        if (!r.GetBoolean(5)) throw new InvalidOperationException(r.IsDBNull(4)
            ? "This pallet has not been shipped. An empty-pallet return is not required."
            : "This pallet has already been returned.");
        if (r.IsDBNull(3)) throw new InvalidOperationException("The pallet shipment date is missing. Check Pallet Master before receiving this pallet.");
        return new(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.GetDateTime(3), r.IsDBNull(4) ? null : r.GetDateTime(4));
    }
}
