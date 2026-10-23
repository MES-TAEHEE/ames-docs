using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-23 — LOT 을 발행하는 공정(사출·감싸기·도장)의 라인만 LOT PREFIX 필수, 그 밖(재작업 등)은 두지 않는다.
// MD-001 저장은 LOT 공정 라인에 접두어가 없으면 52079 로 거부하고 다른 공정의 접두어는 받지 않으며,
// WO 릴리스 후보(ReadPreview)는 접두어 없는 LOT 라인을 빼고 그 라인을 NoPrefixLines 로 알린다(지정하면 릴리스가 거부)
public class LotPrefixRuleTests
{
    [Theory]
    [InlineData("INJ", true)]
    [InlineData("img", true)]
    [InlineData(" PNT ", true)]
    [InlineData("RWK", false)]
    [InlineData("QC", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Only_lot_issuing_processes_require_a_prefix(string? process, bool required)
        => Assert.Equal(required, LotPrefixRules.RequiresPrefix(process));

    [Fact]
    public void Sql_list_matches_the_process_list()
        => Assert.Equal(string.Join(",", LotPrefixRules.LotProcesses.Select(p => $"'{p}'")), LotPrefixRules.LotProcessSqlList);

    [Theory]
    [InlineData("I1", true)]
    [InlineData("W9", true)]
    [InlineData("I", false)]
    [InlineData("I-", false)]
    [InlineData(null, false)]
    public void Prefix_is_two_letters_or_digits(string? prefix, bool valid)
        => Assert.Equal(valid, LotPrefixRules.IsValid(prefix));

    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test lines";
    const string Actor   = "itest-lotrule";
    const string RwkLine = "ITEST-LPR-RWK";
    const string InjLine = "ITEST-LPR-INJ";
    const string Item    = "82371-XA000CRN";   // 라우팅 A 완제품(INJ → IMG)

    static void Cleanup(Connection.AmesConnectionFactory f) =>
        Exec(f, "DELETE dbo.MD_Line WHERE LineID IN (@R, @I)", ("@R", RwkLine), ("@I", InjLine));

    [SkippableFact]
    public void Line_save_and_release_candidates_follow_the_rule()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @I AND RoutingType = 'A'", ("@I", Item)) is null, $"{Item} (routing A) missing");
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            // 재작업 라인: 접두어를 보내도 저장하지 않는다
            md.InsertLine(RwkLine, "itest rwk", null, null, "WC-RWK", null, null, false, "ACTIVE", Actor, "ZQ");
            Assert.Null(Scalar(f!, "SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L", ("@L", RwkLine)) as string);
            md.UpdateLine(RwkLine, "itest rwk 2", null, null, "WC-RWK", null, null, false, "ACTIVE", Actor, null);

            // 사출 라인: 접두어 없으면 52079
            Assert.Equal(52079, Assert.Throws<SqlException>(() =>
                md.InsertLine(InjLine, "itest inj", null, null, "WC-INJ", null, null, false, "ACTIVE", Actor, null)).Number);
            // 재작업 → 사출로 작업장을 바꾸면 접두어가 있어야 한다
            Assert.Equal(52079, Assert.Throws<SqlException>(() =>
                md.UpdateLine(RwkLine, "itest rwk", null, null, "WC-INJ", null, null, false, "ACTIVE", Actor, null)).Number);

            // 접두어 없는 사출 라인(SQL 로 만든 경우)은 릴리스 후보에서 빠지고 NoPrefixLines 로 보인다
            Exec(f!, "INSERT dbo.MD_Line (LineID, LineName, WCID, Status, CreatedBy, CreatedTS) VALUES (@L, 'itest inj', 'WC-INJ', 'ACTIVE', @B, SYSDATETIME())",
                ("@L", InjLine), ("@B", Actor));
            var inj = new WorkOrderRepository(f!).PreviewRouting(Item, "A").First(s => s.ProcessCode == "INJ");
            Assert.DoesNotContain(inj.Candidates, c => c.LineId == InjLine);
            Assert.True(inj.LacksPrefix(InjLine));
            Assert.NotEqual(InjLine, inj.BopLineId);
            Assert.All(inj.Candidates, c => Assert.False(inj.LacksPrefix(c.LineId)));
        }
        finally { Cleanup(f!); }
    }

    const string LockLine  = "ITEST-LPR-LOCK";
    const string StockLot  = "ITEST-LPR-STK";
    const string SeqHeader = "ZZZZ9";

    static void LockCleanup(Connection.AmesConnectionFactory f)
    {
        Exec(f, "DELETE dbo.WH_Inventory WHERE LotNo = @L", ("@L", StockLot));
        Exec(f, "DELETE dbo.SYS_LotSeq WHERE Header = @H", ("@H", SeqHeader));
        Exec(f, "DELETE dbo.MD_Line WHERE LineID = @L", ("@L", LockLine));
    }

    // 10-23 — 이미 쓰인 접두어(LOT 번호·채번 헤더·라인 재고 위치)는 바꾸지 않는다(52080). 같은 값 저장·다른 칸 수정·작업장 변경은 통과
    [SkippableFact]
    public void Used_prefix_cannot_be_changed()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT 1 FROM dbo.MD_Line WHERE LotPrefix IN ('Z8', 'Z9')") is not null, "Z8/Z9 already assigned");
        var md = new MasterDataRepository(f!);
        void Save(string prefix, string wc = "WC-INJ", string name = "itest lock")
            => md.UpdateLine(LockLine, name, null, null, wc, null, null, false, "ACTIVE", Actor, prefix);
        string? Prefix() => Scalar(f!, "SELECT LotPrefix FROM dbo.MD_Line WHERE LineID = @L", ("@L", LockLine)) as string;
        try
        {
            LockCleanup(f!);
            md.InsertLine(LockLine, "itest lock", null, null, "WC-INJ", null, null, false, "ACTIVE", Actor, "Z8");
            Assert.False(md.LotPrefixInUse(LockLine));
            Save("Z9");                                   // 쓰이지 않은 접두어는 바꿀 수 있다
            Assert.Equal("Z9", Prefix());

            // 채번 헤더에 쓰이면 잠긴다
            Exec(f!, "INSERT dbo.SYS_LotSeq (Header, LastSeq, ModifiedTS) VALUES (@H, 1, SYSDATETIME())", ("@H", SeqHeader));
            Assert.True(md.LotPrefixInUse(LockLine));
            Assert.Equal(52080, Assert.Throws<SqlException>(() => Save("Z8")).Number);
            Save("Z9", name: "itest lock 2");             // 같은 값·다른 칸 수정은 통과
            Save("Z9", wc: "WC-RWK");                     // 비 LOT 공정으로 바꾸면 접두어는 보내지 않아 그대로 남는다
            Assert.Equal("Z9", Prefix());
            Save("Z9");

            // 라인 재고 위치(BF- 자리표시 포함)에 쓰여도 잠긴다
            Exec(f!, "DELETE dbo.SYS_LotSeq WHERE Header = @H", ("@H", SeqHeader));
            Assert.False(md.LotPrefixInUse(LockLine));
            Exec(f!, "INSERT dbo.WH_Inventory (LotNo, LocationNo, ReceivedAt) VALUES (@L, 'Z9', SYSDATETIME())", ("@L", StockLot));
            Assert.True(md.LotPrefixInUse(LockLine));
            Assert.Equal(52080, Assert.Throws<SqlException>(() => Save("Z8")).Number);
            Assert.Equal("Z9", Prefix());
        }
        finally { LockCleanup(f!); }
    }
}
