using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — MD-004 엑셀 올리기 한 버전 등록(ImportBomVersion): 새 버전 + 라인 한 트랜잭션, 이미 생긴 버전·초안 아닌 버전은 null, 초안엔 없는 라인만
public class BomImportDbTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test items, a BOM version and lines";
    const string Actor = "itest-bomimp";
    const string Root = "ITEST-BOMIMP-P", C1 = "ITEST-BOMIMP-C1", C2 = "ITEST-BOMIMP-C2";
    const string Ver = "ITEST-BOMIMP-V";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Bom        WHERE VersionID = @V;
        DELETE dbo.MD_BomVersion WHERE VersionID = @V;
        DELETE dbo.MD_Item       WHERE ItemNo IN (@P, @C1, @C2);
        """, ("@V", Ver), ("@P", Root), ("@C1", C1), ("@C2", C2));

    static BomImport.Version Plan(bool isNew, params string[] comps) =>
        new(Ver, isNew, Root, "V1.0", new DateOnly(2099, 1, 1), null, "ECO", "itest",
            comps.Select((c, i) => new BomImport.Line(i + 2, c, 1.5m, null, (i + 1) * 10, null)).ToList());

    static int Lines(Connection.AmesConnectionFactory f) => (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Bom WHERE VersionID = @V", ("@V", Ver))!;

    [SkippableFact]
    public void Imports_new_version_with_lines_and_adds_only_missing_lines_to_a_draft()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            foreach (var (no, type) in new[] { (Root, "ASSY"), (C1, "MATERIAL"), (C2, "MATERIAL") })
                md.InsertItem(no, "itest bom import", type, null, false, null, "EA", null, null, null, null, null, null, null, null,
                    null, null, false, true, Actor);

            Assert.Equal(1, md.ImportBomVersion(Plan(true, C1), Actor));
            var v = md.ListBomVersions().Single(x => x.VersionID == Ver);
            Assert.Equal(("DRAFT", Root, "V1.0", false, "itest"), (v.Status, v.RootItemNo, v.VersionNo, v.SAPFlag, v.ChangeReason));   // SAPFlag 는 엑셀이 쓰지 않는다(SAP 수신만)
            var line = Assert.Single(md.ListBomLines(Ver));
            Assert.Equal((Root, C1, 1.5m, "EA", 1, 10, Actor), (line.ParentItemNo, line.CompItemNo, line.QtyPer, line.UOM, line.BOMLevel, line.Position, line.CreatedBy));

            Assert.Null(md.ImportBomVersion(Plan(true, C2), Actor));        // 그사이 같은 버전이 생겼다 — 아무것도 쓰지 않음
            Assert.Equal(1, Lines(f!));

            Assert.Equal(1, md.ImportBomVersion(Plan(false, C1, C2), Actor)); // 초안 — 없는 C2 만 더함
            Assert.Equal(2, Lines(f!));

            Exec(f!, "UPDATE dbo.MD_BomVersion SET Status = 'PENDING' WHERE VersionID = @V", ("@V", Ver));
            Assert.Null(md.ImportBomVersion(Plan(false, C2), Actor));       // 초안이 아니면 거부
            Assert.Equal(2, Lines(f!));
        }
        finally { Cleanup(f!); }
    }
}
