using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using System.Data;
using Xunit;

namespace AMES.Data.Tests;

public sealed class PdaFgDefectDbTests
{
    [SkippableFact]
    public void Registration_photo_retry_and_rejection_are_atomic_on_a_local_clone()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_PDA_DEFECT_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection) || Environment.GetEnvironmentVariable("AMES_PDA_DEFECT_TEST_WRITE") != "1",
            "Requires explicit opt-in for dedicated test LOT writes on a local AMES_TEST_PDA_ clone.");
        var cs = new SqlConnectionStringBuilder(connection!);
        Assert.True(cs.DataSource is "localhost" or "localhost,1433" or @"localhost\SQLEXPRESS");
        Assert.StartsWith("AMES_TEST_PDA_", cs.InitialCatalog);
        var factory = new AmesConnectionFactory(connection!);
        var repo = new FinishedGoodsRepository(factory);
        using var conn = factory.OpenConnection();
        string actor, part;
        using (var cmd = new SqlCommand("""
            SELECT TOP(1) UP.EmployeeNo FROM dbo.SYS_UserProfile UP JOIN dbo.AspNetUsers A ON A.Id=UP.UserID
            JOIN dbo.AspNetUserRoles U ON U.UserId=A.Id JOIN dbo.SYS_RolePermission P ON P.RoleID=U.RoleId
            WHERE UP.AccountStatus='ACTIVE' AND (A.LockoutEnd IS NULL OR A.LockoutEnd<=SYSDATETIMEOFFSET())
                AND P.ModuleCode='WEB' AND P.ScreenCode='FG-DEFECT' AND P.PermissionLevel LIKE '%A%'
                AND NULLIF(UP.EmployeeNo,'') IS NOT NULL;
            """, conn)) actor = cmd.ExecuteScalar() as string ?? throw new InvalidOperationException("No existing permitted test actor is available.");
        using (var cmd = new SqlCommand("SELECT TOP(1) ItemNo FROM dbo.MD_Item WHERE ItemType='ASSY' AND ActiveFlag=1 ORDER BY ItemNo;", conn))
            part = cmd.ExecuteScalar() as string ?? throw new InvalidOperationException("No ASSY master is available.");
        var names = new[] { "T-FGD-" + Guid.NewGuid().ToString("N")[..20], "T-FGD-" + Guid.NewGuid().ToString("N")[..20] };
        var ids = new List<int>();
        try
        {
            foreach (var name in names)
            {
                using var seedTx = conn.BeginTransaction();
                using var cmd = new SqlCommand("""
                    DECLARE @Id int;
                    INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,Status,QualityFlag,CreatedBy,CreatedTS)
                    VALUES(@Code,@Part,'T-PDA-FGDEF','IMG','CONFIRMED','OK',@Actor,SYSDATETIME());
                    SET @Id=CONVERT(int,SCOPE_IDENTITY());
                    INSERT dbo.PR_ImgLot(LotID,ConfirmStatus,PrintedCount,CreatedBy,CreatedTS)
                    VALUES(@Id,'CONFIRMED',1,@Actor,SYSDATETIME());
                    INSERT dbo.WH_Inventory(LotNo,UnitType,PartNo,PartName,Qty,ReceivedAt,CreatedAt,UpdatedAt)
                    VALUES(@Code,'PART',@Part,N'PDA defect integration test',1,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
                    INSERT dbo.PR_ProductionResult(EntryNo,LotID,LineID,ProcessCode,GoodQty,DefectFlag,EntryAt,ProdDate,CreatedBy,CreatedTS)
                    VALUES(@Code,@Id,'T-PDA-FGDEF','IMG',1,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Actor,SYSDATETIME());
                    SELECT @Id;
                    """, conn, seedTx);
                cmd.Parameters.Add("@Code", SqlDbType.VarChar, 28).Value = name;
                cmd.Parameters.Add("@Part", SqlDbType.VarChar, 20).Value = part;
                cmd.Parameters.Add("@Actor", SqlDbType.VarChar, 20).Value = actor;
                ids.Add(Convert.ToInt32(cmd.ExecuteScalar()));
                seedTx.Commit();
            }
            Assert.True(repo.CanRegisterPdaDefect(actor));
            Assert.False(repo.CanRegisterPdaDefect("T-NO-DEFECT-ACCESS"));
            var stock = repo.ScanPdaDefect(names[0]);
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6Z9sAAAAASUVORK5CYII=");
            var input = new PdaFgDefectRequest(Guid.NewGuid(), stock.LotNo, stock.PartNo, stock.Qty,
                stock.LocationNo, stock.PalletNo, new string('R', 500), png, "image/png");
            var result = repo.RegisterPdaDefect(input, actor, 0);
            Assert.Equal("PENDING", result.Status);
            Assert.Equal(result.DefectId, repo.ScanPdaDefect(names[0]).DefectId);
            Assert.Equal(png, repo.PdaDefectPhoto(result.DefectId)!.Value.Bytes);
            Assert.Equal(result.DefectId, repo.RegisterPdaDefect(input, actor, 0).DefectId);
            Assert.Throws<InvalidOperationException>(() => repo.RegisterPdaDefect(input with { Reason = "Changed request" }, actor, 0));
            Assert.Throws<InvalidOperationException>(() => repo.RegisterPdaDefect(input with { RequestId = Guid.NewGuid() }, actor, 0));
            using (var cmd = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE LotID=@Id),
                    (SELECT COUNT(*) FROM dbo.WH_Inventory WHERE LotNo=@Code),
                    (SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@Code AND TransactionType='ADJ' AND SourceType='FG_DEFECT_REG'),
                    (SELECT SUM(GoodQty) FROM dbo.PR_ProductionResult WHERE LotID=@Id);
                """, conn))
            {
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = ids[0];
                cmd.Parameters.Add("@Code", SqlDbType.NVarChar, 50).Value = names[0];
                using var r = cmd.ExecuteReader(); r.Read();
                Assert.Equal(1, r.GetInt32(0)); Assert.Equal(0, r.GetInt32(1)); Assert.Equal(1, r.GetInt32(2)); Assert.Equal(0, r.GetInt32(3));
            }
            var second = repo.ScanPdaDefect(names[1]);
            var stale = input with { RequestId = Guid.NewGuid(), LotNo = second.LotNo, ExpectedLocation = "STALE-LOCATION" };
            Assert.Throws<InvalidOperationException>(() => repo.RegisterPdaDefect(stale, actor, 0));
            Assert.Equal(1, repo.ScanPdaDefect(names[1]).Qty);
            Assert.Null(repo.ScanPdaDefect(names[1]).DefectId);
            Assert.Throws<UnauthorizedAccessException>(() => repo.RegisterPdaDefect(stale, "T-NO-DEFECT-ACCESS", 0));
        }
        finally
        {
            foreach (var id in ids)
            {
                using var cmd = new SqlCommand("""
                    SET XACT_ABORT ON; BEGIN TRAN;
                    DELETE A FROM dbo.SYS_AuditLog A WHERE TargetEntity='PR_DefectDetail'
                        AND TargetID IN(SELECT CONVERT(varchar(20),DefectID) FROM dbo.PR_DefectDetail WHERE LotID=@Id);
                    DELETE P FROM dbo.FG_DefectPhoto P JOIN dbo.PR_DefectDetail D ON D.DefectID=P.DefectID WHERE D.LotID=@Id;
                    DELETE FROM dbo.PR_DefectDetail WHERE LotID=@Id;
                    DELETE FROM dbo.WH_InventoryTransaction WHERE LotNo=(SELECT LotCode FROM dbo.tbl_Lot WHERE LotID=@Id);
                    DELETE FROM dbo.WH_Inventory WHERE LotNo=(SELECT LotCode FROM dbo.tbl_Lot WHERE LotID=@Id);
                    DELETE FROM dbo.PR_ProductionResult WHERE LotID=@Id;
                    DELETE FROM dbo.PR_ImgLot WHERE LotID=@Id;
                    DELETE FROM dbo.tbl_Lot WHERE LotID=@Id;
                    COMMIT;
                    """, conn);
                cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                cmd.ExecuteNonQuery();
            }
        }
    }
}
