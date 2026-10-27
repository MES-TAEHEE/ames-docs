using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// IMG·PNT 오프라인 작업이 복구 뒤 쓰는 저장소 기능 — 지정 번호 완제품 LOT·Core 스캔·발생 시각 확정/불량·사본용 조회.
/// AMES_DEV(AMES_TEST_CONN) 가 닿을 때만 돈다. 번호는 먼 미래 날짜(2036-01-05)의 머리를 써서 실제 채번과 섞이지 않게 하고 끝에 지운다.
/// </summary>
public class OfflineFinishedDbTests
{
    const string InjLine = "LINE-INJ-09", ImgLine = "LINE-IMG-05", PntLine = "LINE-PNT-02";
    const string Item = "ITEST-FIN", CoreItem = "ITEST-CORE";
    static readonly DateTime FarDay = new(2036, 1, 5);

    static string Header(AmesConnectionFactory f, string line)
    {
        var prefix = (string)AmesDevDb.Scalar(f, "SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L", ("@L", line))!;
        return LotNoGenerator.BuildHeader(FarDay, prefix.Trim());
    }

    static void Clean(AmesConnectionFactory f)
    {
        foreach (var line in new[] { InjLine, ImgLine, PntLine })
        {
            var h = Header(f, line);
            AmesDevDb.Exec(f, """
                DECLARE @ids TABLE (LotID int);
                INSERT @ids SELECT LotID FROM dbo.tbl_Lot WHERE LotCode LIKE @H + '%';
                DELETE dbo.PR_DefectDetail WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_ProductionResult WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_InjLot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_ImgLot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.PR_PntLot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM @ids);
                DELETE dbo.SYS_LotSeq WHERE Header = @H;
                """, ("@H", h));
        }
    }

    static string Code(AmesConnectionFactory f, string line)
        => new InjLotRepository(f).ReserveLotNos(line, FarDay, 1).Codes.Single();

    // ── PNT ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Pnt_lot_with_a_reserved_code_keeps_code_time_printed_count_and_the_printed_customer()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        var repo = new PntLotRepository(f!);
        var at = new DateTime(2036, 1, 5, 14, 30, 0);
        try
        {
            var code = Code(f!, PntLine);
            var lot = repo.CreateRawLotWithCode(PntLine, Item, "ITEST", code, at, 1, "CUST-X");
            Assert.NotNull(lot);
            Assert.Equal(code, lot!.LotCode);
            Assert.Equal("CUST-X", lot.CustomerCode);       // 라벨에 찍은 수주처가 정본 — 복구 시점의 WO 로 다시 정하지 않는다
            Assert.Equal(1, lot.PrintedCount);
            Assert.Null(repo.CreateRawLotWithCode(PntLine, Item, "ITEST", code, at, 1, "CUST-X"));   // 재전송은 무해

            Assert.Equal(at, AmesDevDb.Scalar(f!, "SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", code)));
            Assert.Equal(at, AmesDevDb.Scalar(f!, "SELECT e.CreatedTS FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
            Assert.Equal(1, AmesDevDb.Scalar(f!, "SELECT e.PrintedCount FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
            Assert.Equal("CUST-X", AmesDevDb.Scalar(f!, "SELECT e.CustomerCode FROM dbo.PR_PntLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Pnt_confirm_and_defect_with_occurredAt_stamp_the_given_times()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        var repo = new PntLotRepository(f!);
        var t1 = new DateTime(2036, 1, 5, 2, 10, 0);
        var t2 = new DateTime(2036, 1, 5, 11, 30, 0);
        try
        {
            var code = Code(f!, PntLine);
            repo.CreateRawLotWithCode(PntLine, Item, "ITEST", code, t1, 0, null);

            var (outcome, resultId, _, _) = repo.ConfirmByLotCode(code, PntLine, "ITEST", null, "ITEST", null, t1, out _);
            Assert.Equal(ImgConfirmOutcome.Confirmed, outcome);
            (DateTime Now, DateTime ProdDate, string? Shift) expected;
            using (var conn = f!.OpenConnection()) expected = ProdCalendar.ResolveNow(conn, null, t1);
            using (var c2 = f!.OpenConnection())
            using (var cmd = new SqlCommand("SELECT EntryAt, ProdDate, ShiftCode FROM dbo.PR_ProductionResult WHERE ResultID = @R", c2))
            {
                cmd.Parameters.AddWithValue("@R", resultId);
                using var r = cmd.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(t1, r.GetDateTime(0));
                Assert.Equal(expected.ProdDate, r.GetDateTime(1));
                Assert.Equal(expected.Shift, r.IsDBNull(2) ? null : r.GetString(2));
            }

            var (dOutcome, defectId, _) = repo.RegisterDefect(code, PntLine, "ITEST-D01", "ITEST", null, "ITEST", t2, out _);
            Assert.Equal(DefectRegisterOutcome.Registered, dOutcome);
            Assert.Equal(t2, AmesDevDb.Scalar(f!, "SELECT DetectedAt FROM dbo.PR_DefectDetail WHERE DefectID = @D", ("@D", defectId)));
            Assert.Equal(t2, AmesDevDb.Scalar(f!, """
                SELECT TOP 1 r.EntryAt FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID
                WHERE l.LotCode = @C AND r.GoodQty < 0
                """, ("@C", code)));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Pnt_snapshot_queries_run_against_the_real_schema()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new PntLotRepository(f!);
        Assert.NotNull(repo.ListForOffline(PntLine, DateTime.Today.AddDays(-2)));
        var ctx = repo.GetLabelContext(PntLine, Item);
        Assert.Null(ctx.ItemName);   // 없는 품번 — 조회가 예외 없이 빈 값으로 끝난다
    }

    // ── IMG ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Img_raw_lot_with_a_reserved_code_keeps_code_time_printed_count_and_the_printed_customer()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        var repo = new ImgLotRepository(f!);
        var at = new DateTime(2036, 1, 5, 9, 0, 0);
        try
        {
            var code = Code(f!, ImgLine);
            var lot = repo.CreateRawLotWithCode(ImgLine, Item, "ITEST", code, at, 1, "CUST-Z");
            Assert.NotNull(lot);
            Assert.Equal("CUST-Z", lot!.CustomerCode);
            Assert.Null(repo.CreateRawLotWithCode(ImgLine, Item, "ITEST", code, at, 1, "CUST-Z"));
            Assert.Equal(at, AmesDevDb.Scalar(f!, "SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", code)));
            Assert.Equal(1, AmesDevDb.Scalar(f!, "SELECT e.PrintedCount FROM dbo.PR_ImgLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Img_confirm_and_defect_with_occurredAt_stamp_the_given_times()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        var repo = new ImgLotRepository(f!);
        var t1 = new DateTime(2036, 1, 5, 10, 0, 0);
        var t2 = new DateTime(2036, 1, 5, 11, 45, 0);
        try
        {
            var code = Code(f!, ImgLine);
            repo.CreateRawLotWithCode(ImgLine, Item, "ITEST", code, t1, 0, null);
            var (outcome, resultId, _, _) = repo.ConfirmByLotCode(code, ImgLine, "ITEST", null, "ITEST", null, t1, out _);
            Assert.Equal(ImgConfirmOutcome.Confirmed, outcome);
            Assert.Equal(t1, AmesDevDb.Scalar(f!, "SELECT EntryAt FROM dbo.PR_ProductionResult WHERE ResultID = @R", ("@R", resultId)));

            var (dOutcome, defectId, _) = repo.RegisterDefect(code, ImgLine, "ITEST-D01", "ITEST", null, "ITEST", t2, out _);
            Assert.Equal(DefectRegisterOutcome.Registered, dOutcome);
            Assert.Equal(t2, AmesDevDb.Scalar(f!, "SELECT DetectedAt FROM dbo.PR_DefectDetail WHERE DefectID = @D", ("@D", defectId)));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Img_core_scan_replays_with_its_reserved_code_and_a_core_can_only_be_used_once()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        // BOM 이 코어를 정하지 못하는 완제품 — 그 경우 서버는 작업자가 고른 품번을 믿는다(WO 도 없어야 한다)
        string? finished = null;
        using (var conn = f!.OpenConnection())
        {
            var names = new List<string>();
            using (var cmd = new SqlCommand("SELECT TOP 80 ItemNo FROM dbo.MD_Item WHERE ISNULL(ActiveFlag,1) = 1 ORDER BY ItemNo", conn))
            using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add((string)r[0]);
            finished = names.FirstOrDefault(n => CoreItemResolver.ReadByInjFlag(conn, null, n).Outcome != CoreItemResolver.Outcome.Core);
        }
        Skip.If(finished is null, "no suitable finished item in this DB");

        var inj = new InjLotRepository(f!);
        var img = new ImgLotRepository(f!);
        var at = new DateTime(2036, 1, 5, 9, 30, 0);
        try
        {
            // 확정된 사출 LOT = Core
            var coreCode = Code(f!, InjLine);
            inj.CreateManualRawLotsWithCodes(InjLine, CoreItem, null, "ITEST", new[] { (coreCode, at.AddMinutes(-10), 0) });
            Assert.Equal(InjConfirmOutcome.Confirmed, inj.ConfirmByLotCode(coreCode, InjLine, "ITEST", null, "ITEST", null, at.AddMinutes(-9), out _).Outcome);

            var imgCode = Code(f!, ImgLine);
            var (outcome, lot, _, itemNo) = img.CreateFromCoreWithCode(coreCode, ImgLine, "ITEST", finished, imgCode, at, 1, "CUST-Y");
            Assert.Equal(ImgCoreOutcome.Created, outcome);
            Assert.NotNull(lot);
            Assert.Equal(finished, itemNo);
            Assert.Equal(imgCode, lot!.LotCode);
            Assert.Equal(coreCode, lot.CoreLotCode);
            Assert.Equal("CUST-Y", lot.CustomerCode);
            Assert.Equal(1, lot.PrintedCount);
            Assert.Equal(at, AmesDevDb.Scalar(f!, "SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", imgCode)));

            // 재전송 — 같은 번호는 새로 만든 것 없이 Created
            var again = img.CreateFromCoreWithCode(coreCode, ImgLine, "ITEST", finished, imgCode, at, 1, "CUST-Y");
            Assert.Equal(ImgCoreOutcome.Created, again.Outcome);
            Assert.Null(again.Lot);

            // 같은 Core 로 다른 번호를 또 만들려 하면 서버가 거부 — 충돌 목록으로 간다
            var other = Code(f!, ImgLine);
            var dup = img.CreateFromCoreWithCode(coreCode, ImgLine, "ITEST", finished, other, at.AddMinutes(1), 1, null);
            Assert.Equal(ImgCoreOutcome.CoreUsed, dup.Outcome);
            Assert.Equal(imgCode, dup.UsedByLotCode);
            Assert.Equal(0, AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", other)));
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Img_core_scan_with_an_unknown_core_is_refused_by_the_server()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Clean(f!);
        try
        {
            var r = new ImgLotRepository(f!).CreateFromCoreWithCode(Header(f!, InjLine) + "9998", ImgLine, "ITEST", Item,
                Header(f!, ImgLine) + "9997", new DateTime(2036, 1, 5, 9, 0, 0), 1, null);
            Assert.Equal(ImgCoreOutcome.NotFound, r.Outcome);   // 복구 뒤 NotFound — 정해진 시간 동안 다시 시도하다 충돌이 된다
        }
        finally { Clean(f!); }
    }

    [SkippableFact]
    public void Img_snapshot_queries_run_against_the_real_schema()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new ImgLotRepository(f!);
        Assert.NotNull(repo.ListForOffline(ImgLine, DateTime.Today.AddDays(-2)));
        Assert.NotNull(repo.ListCoresForOffline(DateTime.Today.AddDays(-1)));
        Assert.NotNull(repo.ListCoreMap(ImgLine));
        var boms = repo.ReadBomCores(new[] { Item, "" });
        Assert.True(boms.ContainsKey(Item));
        Assert.NotNull(repo.GetLabelContext(ImgLine, Item));
    }
}
