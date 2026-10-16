using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// BOM 라인은 버전이 DRAFT 일 때만 바뀐다 — 승인 요청(PENDING)·승인(APPROVED) 뒤의 추가·수정·삭제·재활성화는 0행(false).
// 전용 버전·라인 ID(품목 마스터 불필요 — FK 없음)만 쓰고 끝에 지운다.
public class BomLineDraftOnlyTests
{
    const string Ver = "ITEST-BOMV-1010";
    const string Line1 = "ITEST-BOML-1010-1", Line2 = "ITEST-BOML-1010-2";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Bom        WHERE VersionID = @V OR BOMID IN (@L1, @L2);
        DELETE dbo.MD_BomVersion WHERE VersionID = @V;
        """, ("@V", Ver), ("@L1", Line1), ("@L2", Line2));

    static void SetStatus(AmesConnectionFactory f, string status) =>
        Exec(f, "UPDATE dbo.MD_BomVersion SET Status = @S WHERE VersionID = @V", ("@S", status), ("@V", Ver));

    static string Lines(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            SELECT STRING_AGG(BOMID + ':' + CompItemNo + ':' + CAST(CAST(QtyPer AS decimal(18,3)) AS varchar(30)) + ':' + CAST(ISNULL(ActiveFlag,1) AS varchar(1)), ',')
                   WITHIN GROUP (ORDER BY BOMID)
            FROM dbo.MD_Bom WHERE VersionID = @V
            """, conn);
        cmd.Parameters.AddWithValue("@V", Ver);
        return cmd.ExecuteScalar() as string ?? "";
    }

    [SkippableTheory]
    [InlineData("PENDING")]
    [InlineData("APPROVED")]
    public void Lines_of_a_non_draft_version_cannot_change(string status)
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var md = new MasterDataRepository(f);
            md.InsertBomVersion(Ver, "ITEST-ROOT", "V1.0", null, null, null, null, "itest");
            Assert.True(md.InsertBomLine(Line1, "ITEST-ROOT", "ITEST-COMP-A", 1, 2m, "EA", null, Ver, 10, null, "itest"));
            Assert.True(md.UpdateBomLine(Line1, "ITEST-ROOT", "ITEST-COMP-A", 1, 3m, "EA", null, 10, null, "itest"));
            var draft = Lines(f);
            Assert.Equal("ITEST-BOML-1010-1:ITEST-COMP-A:3.000:1", draft);

            SetStatus(f, status);

            Assert.False(md.InsertBomLine(Line2, "ITEST-ROOT", "ITEST-COMP-B", 1, 1m, "EA", null, Ver, 20, null, "itest"));
            Assert.False(md.UpdateBomLine(Line1, "ITEST-ROOT", "ITEST-COMP-X", 1, 9m, "EA", null, 10, null, "itest"));
            Assert.False(md.DeleteBomLine(Line1, "itest"));
            Assert.Equal(draft, Lines(f));

            // 재활성화도 막힌다 — 비활성 라인을 만들어 둔 뒤 확인
            SetStatus(f, "DRAFT");
            Assert.True(md.DeleteBomLine(Line1, "itest"));
            SetStatus(f, status);
            Assert.False(md.ReactivateBomLine(Line1, "itest"));
            Assert.Equal("ITEST-BOML-1010-1:ITEST-COMP-A:3.000:0", Lines(f));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Missing_line_or_version_returns_false()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        var md = new MasterDataRepository(f);
        Assert.False(md.InsertBomLine(Line1, "ITEST-ROOT", "ITEST-COMP-A", 1, 1m, "EA", null, Ver, 10, null, "itest"));
        Assert.False(md.UpdateBomLine(Line1, "ITEST-ROOT", "ITEST-COMP-A", 1, 1m, "EA", null, 10, null, "itest"));
        Assert.Equal("", Lines(f));
    }
}
