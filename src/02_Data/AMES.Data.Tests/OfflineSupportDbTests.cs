using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// POP 오프라인 작업이 복구 뒤 쓰는 저장소 기능 — LotNo 구간 예약·지정 번호 LOT 삽입·발생 시각 확정/불량.
/// AMES_DEV(AMES_TEST_CONN) 가 닿을 때만 돈다. 번호는 먼 미래 날짜(2036-01-05)의 머리를 써서 실제 채번과 섞이지 않게 하고 끝에 지운다.
/// </summary>
public class OfflineSupportDbTests
{
    const string Line = "LINE-INJ-09";
    const string Item = "ITEST-OFFLINE";
    static readonly DateTime FarDay = new(2036, 1, 5);

    static string Header(AmesConnectionFactory f)
    {
        var prefix = (string)AmesDevDb.Scalar(f, "SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L", ("@L", Line))!;
        return LotNoGenerator.BuildHeader(FarDay, prefix.Trim());
    }

    static void CleanHeader(AmesConnectionFactory f)
    {
        var h = Header(f);
        AmesDevDb.Exec(f, """
            DECLARE @ids TABLE (LotID int);
            INSERT @ids SELECT LotID FROM dbo.tbl_Lot WHERE LotCode LIKE @H + '%';
            DELETE dbo.PR_DefectDetail WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.PR_ProductionResult WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.PR_InjLot WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.tbl_Lot WHERE LotID IN (SELECT LotID FROM @ids);
            DELETE dbo.SYS_LotSeq WHERE Header = @H;
            """, ("@H", h));
    }

    static string NewLot(InjLotRepository repo, AmesConnectionFactory f, DateTime at, int printed = 0)
    {
        var block = repo.ReserveLotNos(Line, FarDay, 1);
        var code = block.Codes.Single();
        repo.CreateManualRawLotsWithCodes(Line, Item, null, "ITEST", new[] { (code, at, printed) });
        return code;
    }

    [SkippableFact]
    public void Reserved_block_is_skipped_by_later_numbering()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        try
        {
            var b1 = repo.ReserveLotNos(Line, FarDay, 5);
            Assert.Equal(5, b1.Count);
            Assert.Equal(1, b1.FirstSeq);
            Assert.Equal(Header(f!) + "0001", b1.Codes.First());
            Assert.Equal(Header(f!) + "0005", b1.Codes.Last());

            var b2 = repo.ReserveLotNos(Line, FarDay, 3);
            Assert.Equal(6, b2.FirstSeq);   // 겹치지 않고 이어 붙는다

            // 평소 채번은 두 구간 뒤에서 이어진다 — 롤백하므로 카운터는 그대로
            using var conn = f!.OpenConnection();
            using var tx = conn.BeginTransaction();
            var next = LotNoGenerator.NextLotNo(conn, tx, Line, FarDay);
            tx.Rollback();
            Assert.Equal(Header(f) + "0009", next);
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Reserve_never_goes_past_9999()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        try
        {
            var all = repo.ReserveLotNos(Line, FarDay, 20000);
            Assert.Equal(9999, all.Count);
            var none = repo.ReserveLotNos(Line, FarDay, 10);
            Assert.Equal(0, none.Count);
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Manual_lot_with_a_reserved_code_keeps_code_time_and_printed_count_and_is_idempotent()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        var at = new DateTime(2036, 1, 5, 14, 30, 0);
        try
        {
            var block = repo.ReserveLotNos(Line, FarDay, 1);
            var code = block.Codes.Single();
            var made = repo.CreateManualRawLotsWithCodes(Line, Item, null, "ITEST", new[] { (code, at, 1) });
            Assert.Single(made);

            // 복구 중 재전송 — 이미 있는 번호는 건너뛴다
            Assert.Empty(repo.CreateManualRawLotsWithCodes(Line, Item, null, "ITEST", new[] { (code, at, 1) }));
            Assert.Equal(1, AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", code)));

            Assert.Equal(at, AmesDevDb.Scalar(f!, "SELECT CreatedTS FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", code)));
            Assert.Equal(at, AmesDevDb.Scalar(f!, "SELECT ProducedAt FROM dbo.tbl_Lot WHERE LotCode = @C", ("@C", code)));
            Assert.Equal(1, AmesDevDb.Scalar(f!, "SELECT e.PrintedCount FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
            Assert.Equal("RAW", AmesDevDb.Scalar(f!, "SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Confirm_with_occurredAt_stamps_the_result_with_that_time_production_day_and_shift()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        var at = new DateTime(2036, 1, 5, 2, 10, 0);   // 새벽 — 전기일 경계(보통 07:00) 앞이라 전날 생산분이 된다
        try
        {
            var code = NewLot(repo, f!, at);
            var (outcome, resultId, _, _) = repo.ConfirmByLotCode(code, Line, "ITEST", null, "ITEST", null, at, out _);
            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);

            (DateTime Now, DateTime ProdDate, string? Shift) expected;
            using (var conn = f!.OpenConnection()) expected = ProdCalendar.ResolveNow(conn, null, at);
            Assert.Equal(at, expected.Now);

            using var c2 = f!.OpenConnection();
            using var cmd = new SqlCommand("SELECT EntryAt, ProdDate, ShiftCode FROM dbo.PR_ProductionResult WHERE ResultID = @R", c2);
            cmd.Parameters.AddWithValue("@R", resultId);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(at, r.GetDateTime(0));
            Assert.Equal(expected.ProdDate, r.GetDateTime(1));
            Assert.Equal(expected.Shift, r.IsDBNull(2) ? null : r.GetString(2));
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Confirm_without_occurredAt_still_uses_the_server_clock()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        try
        {
            var code = NewLot(repo, f!, new DateTime(2036, 1, 5, 9, 0, 0));
            var before = DateTime.Now.AddMinutes(-5);
            var (outcome, resultId, _, _) = repo.ConfirmByLotCode(code, Line, "ITEST", null, "ITEST");
            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            var entryAt = (DateTime)AmesDevDb.Scalar(f!, "SELECT EntryAt FROM dbo.PR_ProductionResult WHERE ResultID = @R", ("@R", resultId))!;
            Assert.True(entryAt > before.AddYears(-1), "종전 경로는 서버 시각 — 2036 이 아니다");
            Assert.True(entryAt.Year < 2036);
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Defect_with_occurredAt_stamps_the_detail_and_the_reversal()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        var t1 = new DateTime(2036, 1, 5, 10, 0, 0);
        var t2 = new DateTime(2036, 1, 5, 11, 30, 0);
        try
        {
            var code = NewLot(repo, f!, t1);
            Assert.Equal(InjConfirmOutcome.Confirmed, repo.ConfirmByLotCode(code, Line, "ITEST", null, "ITEST", null, t1, out _).Outcome);

            var (outcome, defectId, _) = repo.RegisterDefect(code, Line, "ITEST-D01", "ITEST", null, "ITEST", t2, out _);
            Assert.Equal(DefectRegisterOutcome.Registered, outcome);

            Assert.Equal(t2, AmesDevDb.Scalar(f!, "SELECT DetectedAt FROM dbo.PR_DefectDetail WHERE DefectID = @D", ("@D", defectId)));
            Assert.Equal(t2, AmesDevDb.Scalar(f!, """
                SELECT TOP 1 r.EntryAt FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID
                WHERE l.LotCode = @C AND r.GoodQty < 0
                """, ("@C", code)));
            Assert.Equal("DEFECT", AmesDevDb.Scalar(f!, "SELECT e.ConfirmStatus FROM dbo.PR_InjLot e JOIN dbo.tbl_Lot l ON l.LotID = e.LotID WHERE l.LotCode = @C", ("@C", code)));
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Defect_on_a_raw_lot_with_occurredAt_stamps_only_the_detail()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        CleanHeader(f!);
        var t = new DateTime(2036, 1, 5, 12, 0, 0);
        try
        {
            var code = NewLot(repo, f!, t);
            var (outcome, defectId, _) = repo.RegisterDefect(code, Line, "ITEST-D01", "ITEST", null, "ITEST", t.AddMinutes(5), out _);
            Assert.Equal(DefectRegisterOutcome.Registered, outcome);
            Assert.Equal(t.AddMinutes(5), AmesDevDb.Scalar(f!, "SELECT DetectedAt FROM dbo.PR_DefectDetail WHERE DefectID = @D", ("@D", defectId)));
            Assert.Equal(0, AmesDevDb.Scalar(f!, "SELECT COUNT(*) FROM dbo.PR_ProductionResult r JOIN dbo.tbl_Lot l ON l.LotID = r.LotID WHERE l.LotCode = @C", ("@C", code)));
        }
        finally { CleanHeader(f!); }
    }

    [SkippableFact]
    public void Offline_snapshot_queries_run_against_the_real_schema()
    {
        var f = AmesDevDb.TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        var lots = repo.ListForOffline("LINE-INJ-01", DateTime.Today.AddDays(-2));
        Assert.NotNull(lots);
        var ctx = repo.GetManualContext("LINE-INJ-01", Item, null);
        Assert.Null(ctx.MoldCode);

        var pallets = new PalletStockRepository(f!);
        var rows = pallets.ListForOffline();
        Assert.All(rows, p => { Assert.True(p.Active); Assert.False(p.Shipped); });
        var rules = pallets.ReadItemRules(new[] { Item, "" });
        Assert.True(rules.ContainsKey(Item));
        Assert.Null(rules[Item].PalletQty);
    }
}
