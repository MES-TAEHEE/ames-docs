using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class FinishedGoodsRepository
{
    public bool CanRegisterPdaDefect(string employeeNo)
    {
        using var conn = _factory.OpenConnection();
        return PdaDefectUser(conn, null, employeeNo) is not null;
    }

    private static string? PdaDefectUser(SqlConnection conn, SqlTransaction? tx, string employeeNo)
    {
        using var cmd = new SqlCommand("""
            SELECT TOP(1) UP.UserID
            FROM dbo.SYS_UserProfile UP JOIN dbo.AspNetUsers A ON A.Id=UP.UserID
            JOIN dbo.AspNetUserRoles U ON U.UserId=A.Id
            JOIN dbo.SYS_RolePermission P ON P.RoleID=U.RoleId AND P.ModuleCode='WEB'
            JOIN dbo.SYS_Screen S ON S.ScreenCode=P.ScreenCode AND S.ModuleCode=P.ModuleCode
            WHERE UP.EmployeeNo=@Employee AND COALESCE(UP.AccountStatus,'ACTIVE')='ACTIVE'
              AND (A.LockoutEnd IS NULL OR A.LockoutEnd<=SYSDATETIMEOFFSET())
              AND P.ScreenCode='FG-DEFECT' AND P.PermissionLevel LIKE '%A%'
              AND SUBSTRING(S.PermissionCriteria,3,1)<>'X';
            """, conn, tx);
        ColumnParams.AddFilter(cmd, "@Employee", "SYS_UserProfile.EmployeeNo", employeeNo);
        return cmd.ExecuteScalar() as string;
    }

    public PdaFgDefectLot ScanPdaDefect(string lotNo)
    {
        ValidatePdaDefectLotNo(lotNo);
        using var conn = _factory.OpenConnection();
        return ReadPdaDefectLot(conn, null, lotNo);
    }

    internal static void ValidatePdaDefectLotNo(string lotNo)
    {
        if (string.IsNullOrWhiteSpace(lotNo) || lotNo.Length > 40 || lotNo.Any(char.IsControl))
            throw new ArgumentException("Scan a finished-goods LOT barcode within 40 characters.");
    }

    internal static void ValidatePdaDefectStock(PdaFgDefectLot row, string? itemType,
        bool returnPending, bool onDelivery)
    {
        if (!string.Equals(itemType, "ASSY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only ASSY finished-goods LOTs can be registered here. Scan an individual product LOT, not a pallet.");
        if (row.DefectId.HasValue) return;
        if (returnPending)
            throw new InvalidOperationException("This customer return is awaiting inspection. Record its decision in Customer Returns.");
        if (onDelivery)
            throw new InvalidOperationException("This LOT is prepared for dispatch or loaded on a DCR. Remove it from dispatch/trailer loading before registering a defect.");
        if (row.Qty != 1)
            throw new InvalidOperationException("This LOT does not have one available finished product in inventory. Check its shipment and inventory history.");
        if (row.ProcessCode is not ("INJ" or "IMG" or "PNT") || row.ConfirmStatus != LotDefectRules.Confirmed)
            throw new InvalidOperationException("This LOT is not a confirmed production LOT eligible for defect registration.");
    }

    private static PdaFgDefectLot ReadPdaDefectLot(SqlConnection conn, SqlTransaction? tx, string lotNo)
    {
        if (tx is not null)
        {
            // Match production registration: reject DEFECT before reading its locked rework row.
            using var state = new SqlCommand("""
                SELECT COALESCE(P.ConfirmStatus,M.ConfirmStatus,N.ConfirmStatus,'')
                FROM dbo.tbl_Lot L WITH(UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.PR_PntLot P WITH(UPDLOCK,HOLDLOCK) ON P.LotID=L.LotID
                LEFT JOIN dbo.PR_ImgLot M WITH(UPDLOCK,HOLDLOCK) ON M.LotID=L.LotID
                LEFT JOIN dbo.PR_InjLot N WITH(UPDLOCK,HOLDLOCK) ON N.LotID=L.LotID
                WHERE L.LotCode=@Lot;
                """, conn, tx);
            ColumnParams.AddFilter(state, "@Lot", "tbl_Lot.LotCode", lotNo);
            if (state.ExecuteScalar() as string is LotDefectRules.Defect or LotDefectRules.Scrapped or LotDefectRules.LegacyNgConfirmed)
                throw new InvalidOperationException("This LOT is already awaiting defect disposition or has been scrapped. Scan it again to view its current status.");
        }
        var sql = """
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SELECT L.LotID,L.LotCode,L.ItemNo,I.ItemName,I.ItemType,I.DefaultUOM,
                W.Qty,W.LocationNo,W.PalletNo,
                CASE WHEN P.LotID IS NOT NULL THEN 'PNT' WHEN M.LotID IS NOT NULL THEN 'IMG'
                     WHEN N.LotID IS NOT NULL THEN 'INJ' ELSE '' END ProcessCode,
                COALESCE(P.ConfirmStatus,M.ConfirmStatus,N.ConfirmStatus,'') ConfirmStatus,
                D.DefectID,D.ReasonNote,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WITH(HOLDLOCK)
                    WHERE R.LotNo=L.LotCode COLLATE DATABASE_DEFAULT AND R.Status IN('Open','Inspecting')) THEN 1 ELSE 0 END AS bit) ReturnPending,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.FG_DeliveryLoad X WITH(UPDLOCK,HOLDLOCK)
                    WHERE X.LotNo=L.LotCode COLLATE DATABASE_DEFAULT AND X.ShipmentID IS NULL) THEN 1 ELSE 0 END AS bit) OnDelivery
            FROM dbo.tbl_Lot L WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo
            LEFT JOIN dbo.PR_PntLot P WITH(UPDLOCK,HOLDLOCK) ON P.LotID=L.LotID
            LEFT JOIN dbo.PR_ImgLot M WITH(UPDLOCK,HOLDLOCK) ON M.LotID=L.LotID
            LEFT JOIN dbo.PR_InjLot N WITH(UPDLOCK,HOLDLOCK) ON N.LotID=L.LotID
            LEFT JOIN dbo.WH_Inventory W WITH(UPDLOCK,HOLDLOCK) ON W.LotNo=L.LotCode COLLATE DATABASE_DEFAULT
            OUTER APPLY(SELECT TOP(1) DefectID,ReasonNote FROM dbo.PR_DefectDetail WITH(HOLDLOCK)
                WHERE LotID=L.LotID AND Disposition IS NULL ORDER BY DefectID DESC) D
            WHERE L.LotCode=@Lot;
            """;
        if (tx is null) sql = sql.Replace(" WITH(UPDLOCK,HOLDLOCK)", "").Replace(" WITH(HOLDLOCK)", "");
        using var cmd = new SqlCommand(sql, conn, tx);
        ColumnParams.AddFilter(cmd, "@Lot", "tbl_Lot.LotCode", lotNo);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("Production LOT was not found. Scan an individual ASSY LOT barcode.");
        var row = new PdaFgDefectLot(r.GetString(1), r["ItemNo"] as string ?? "", r["ItemName"] as string,
            r["Qty"] is decimal qty ? qty : 0, r["DefaultUOM"] as string ?? "EA",
            r["LocationNo"] as string, r["PalletNo"] as string, r.GetInt32(0), r["ProcessCode"] as string ?? "",
            r["ConfirmStatus"] as string ?? "", r["DefectID"] as int?, r["ReasonNote"] as string);
        ValidatePdaDefectStock(row, r["ItemType"] as string, (bool)r["ReturnPending"], (bool)r["OnDelivery"]);
        return row;
    }

    public PdaFgDefectResult RegisterPdaDefect(PdaFgDefectRequest input, string employeeNo, int sessionId)
    {
        ValidatePdaDefectLotNo(input.LotNo);
        var reason = ValidateDefectReason(input.Reason);
        CheckActor(employeeNo);
        if (input.RequestId == Guid.Empty || input.Photo is not { Length: > 0 and <= 3145728 }
            || input.PhotoType is not ("image/jpeg" or "image/png"))
            throw new ArgumentException("Select the defect photo again.");
        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        Gate(conn, tx);
        var userId = PdaDefectUser(conn, tx, employeeNo)
            ?? throw new UnauthorizedAccessException("You do not have the Inventory Defect permission.");

        // A retry after a lost response returns the original registration without reversing production twice.
        using (var retry = new SqlCommand("""
            SELECT D.DefectID,L.LotCode,D.RegisteredBy,COALESCE(NULLIF(D.Disposition,''),'PENDING') Status,
                D.ReasonNote,P.ContentType,P.Photo
            FROM dbo.FG_DefectPhoto P WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.PR_DefectDetail D ON D.DefectID=P.DefectID
            JOIN dbo.tbl_Lot L ON L.LotID=D.LotID WHERE P.RequestID=@Request;
            """, conn, tx))
        {
            retry.Parameters.Add("@Request", SqlDbType.UniqueIdentifier).Value = input.RequestId;
            using var r = retry.ExecuteReader();
            if (r.Read())
            {
                if (r.GetString(1) != input.LotNo || r["RegisteredBy"] as string != employeeNo
                    || r["ReasonNote"] as string != reason || r["ContentType"] as string != input.PhotoType
                    || !((byte[])r["Photo"]).AsSpan().SequenceEqual(input.Photo))
                    throw new InvalidOperationException("This photo request has already been used. Scan the LOT again.");
                var result = new PdaFgDefectResult(r.GetInt32(0), input.LotNo, r.GetString(3));
                r.Close(); tx.Commit(); return result;
            }
        }
        var row = ReadPdaDefectLot(conn, tx, input.LotNo);
        if (row.DefectId.HasValue) throw new InvalidOperationException("This LOT is already awaiting defect disposition.");
        if (row.PartNo != input.PartNo || row.Qty != input.ExpectedQty
            || (row.LocationNo ?? "") != (input.ExpectedLocation ?? "") || (row.PalletNo ?? "") != (input.ExpectedPallet ?? ""))
            throw new InvalidOperationException("LOT quantity, pallet or location changed. Scan the LOT again before registering the defect.");

        string line;
        using (var cmd = new SqlCommand("SELECT LineID FROM dbo.tbl_Lot WHERE LotID=@Id;", conn, tx))
        {
            ColumnParams.AddFilter(cmd, "@Id", "tbl_Lot.LotID", row.LotId);
            line = cmd.ExecuteScalar() as string ?? "";
        }
        var (resultId, reversalId, woId) = LotDefectWriter.ReverseConfirmedResult(conn, tx, row.LotId,
            line, employeeNo, sessionId, employeeNo);
        using var insert = new SqlCommand("""
            INSERT dbo.PR_DefectDetail(ResultID,WoID,LotID,ProcessCode,Qty,ReasonNote,
                PriorStatus,ReversalResultID,DetectedAt,RegisteredBy,StationCode,CreatedBy,CreatedTS)
            OUTPUT INSERTED.DefectID
            SELECT @Result,@Wo,@Lot,@Process,1,@Reason,'CONFIRMED',@Reversal,SYSDATETIME(),@Actor,
                StationCode,@Actor,SYSDATETIME() FROM dbo.tbl_Lot WHERE LotID=@Lot;
            UPDATE dbo.tbl_Lot SET Status='DEFECT',QualityFlag='NG',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE LotID=@Lot;
            UPDATE dbo.PR_InjLot SET ConfirmStatus='DEFECT',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE LotID=@Lot AND @Process='INJ';
            UPDATE dbo.PR_ImgLot SET ConfirmStatus='DEFECT',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE LotID=@Lot AND @Process='IMG';
            UPDATE dbo.PR_PntLot SET ConfirmStatus='DEFECT',ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE LotID=@Lot AND @Process='PNT';
            """, conn, tx);
        ColumnParams.Add(insert, "@Result", "PR_DefectDetail.ResultID", (object?)resultId ?? DBNull.Value);
        ColumnParams.Add(insert, "@Wo", "PR_DefectDetail.WoID", (object?)woId ?? DBNull.Value);
        ColumnParams.Add(insert, "@Lot", "PR_DefectDetail.LotID", row.LotId);
        ColumnParams.Add(insert, "@Process", "PR_DefectDetail.ProcessCode", row.ProcessCode);
        ColumnParams.Add(insert, "@Reason", "PR_DefectDetail.ReasonNote", reason);
        ColumnParams.Add(insert, "@Reversal", "PR_DefectDetail.ReversalResultID", (object?)reversalId ?? DBNull.Value);
        ColumnParams.Add(insert, "@Actor", "PR_DefectDetail.CreatedBy", employeeNo);
        var id = Convert.ToInt32(insert.ExecuteScalar());
        using (var image = new SqlCommand("""
            INSERT dbo.FG_DefectPhoto(DefectID,RequestID,ContentType,Photo,CreatedBy,CreatedTS)
            VALUES(@Id,@Request,@Type,@Photo,@Actor,SYSDATETIME());
            UPDATE dbo.PR_DefectDetail SET PhotoUrl=CONCAT('/api/fg/defects/',@Id,'/photo') WHERE DefectID=@Id;
            """, conn, tx))
        {
            ColumnParams.Add(image, "@Id", "FG_DefectPhoto.DefectID", id);
            ColumnParams.Add(image, "@Request", "FG_DefectPhoto.RequestID", input.RequestId);
            ColumnParams.Add(image, "@Type", "FG_DefectPhoto.ContentType", input.PhotoType);
            ColumnParams.Add(image, "@Photo", "FG_DefectPhoto.Photo", input.Photo);
            ColumnParams.Add(image, "@Actor", "PR_DefectDetail.CreatedBy", employeeNo);
            image.ExecuteNonQuery();
        }
        PalletStockRepository.Unload(conn, tx, row.LotId, row.LotNo, row.PartNo,
            "FG_DEFECT_REG", $"Defect #{id} awaiting disposition", employeeNo, employeeNo, transactionType: "ADJ");
        using var audit = new SqlCommand("""
            INSERT dbo.SYS_AuditLog(EventTS,ActorUserID,ModuleCode,ScreenCode,ActionType,TargetEntity,TargetID,
                Result,Note,CreatedBy,CreatedTS)
            VALUES(SYSDATETIME(),@User,'FG','FG-DEFECT','CREATE','PR_DefectDetail',CONVERT(varchar(20),@Id),
                'OK',@Reason,@Actor,SYSDATETIME());
            """, conn, tx);
        ColumnParams.Add(audit, "@User", "SYS_AuditLog.ActorUserID", userId);
        audit.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        ColumnParams.Add(audit, "@Reason", "SYS_AuditLog.Note", reason);
        ColumnParams.Add(audit, "@Actor", "SYS_AuditLog.CreatedBy", employeeNo);
        audit.ExecuteNonQuery();
        tx.Commit();
        return new(id, row.LotNo, "PENDING");
    }

    public (byte[] Bytes, string ContentType)? PdaDefectPhoto(int id)
    {
        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("SELECT Photo,ContentType FROM dbo.FG_DefectPhoto WHERE DefectID=@Id;", conn);
        ColumnParams.AddFilter(cmd, "@Id", "PR_DefectDetail.DefectID", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ((byte[])r[0], r.GetString(1)) : null;
    }
}
