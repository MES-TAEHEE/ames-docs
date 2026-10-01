using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>
/// CoreItemResolver.Read 의 DB 판 — AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// ① 테스트 시드(ITEST-CR-*)로 유효 버전 선택 규칙 ② 개발 DB 실데이터 ASSY 77건 = 스펙 부록 A.
/// </summary>
public class CoreItemDbTests
{
    const string Fg = "ITEST-CR-FG", Core = "ITEST-CR-CORE", Old = "ITEST-CR-OLDCORE", Draft = "ITEST-CR-DRAFTCORE";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE FROM dbo.MD_Bom        WHERE ParentItemNo LIKE 'ITEST-CR-%';
        DELETE FROM dbo.MD_BomVersion WHERE RootItemNo   LIKE 'ITEST-CR-%';
        DELETE FROM dbo.MD_Item       WHERE ItemNo       LIKE 'ITEST-CR-%';
        """);

    /// <summary>FG 에 버전 3개: 만료(EffTo 어제, 자식 Old) · 유효(자식 Core) · DRAFT(자식 Draft).</summary>
    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ActiveFlag, CreatedBy) VALUES
              (@Fg,    N'ITEST cr fg',        'ASSY', 1, 'ITEST'),
              (@Core,  N'CORE-ITEST cr',      'SUB',  1, 'ITEST'),
              (@Old,   N'CORE-ITEST old',     'SUB',  1, 'ITEST'),
              (@Draft, N'CORE-ITEST draft',   'SUB',  1, 'ITEST');
            INSERT INTO dbo.MD_BomVersion (VersionID, RootItemNo, VersionNo, EffFrom, EffTo, Status, CreatedBy) VALUES
              ('ITEST-CR-V0', @Fg, 'V0', DATEADD(day,-30,CAST(GETDATE() AS date)), DATEADD(day,-1,CAST(GETDATE() AS date)), 'APPROVED', 'ITEST'),
              ('ITEST-CR-V1', @Fg, 'V1', DATEADD(day,-10,CAST(GETDATE() AS date)), NULL, 'APPROVED', 'ITEST'),
              ('ITEST-CR-V2', @Fg, 'V2', DATEADD(day,-5, CAST(GETDATE() AS date)), NULL, 'DRAFT',    'ITEST');
            INSERT INTO dbo.MD_Bom (BOMID, ParentItemNo, CompItemNo, BOMLevel, QtyPer, UOM, VersionID, ActiveFlag, CreatedBy) VALUES
              ('ITEST-CR-B0', @Fg, @Old,   1, 1, 'EA', 'ITEST-CR-V0', 1, 'ITEST'),
              ('ITEST-CR-B1', @Fg, @Core,  1, 1, 'EA', 'ITEST-CR-V1', 1, 'ITEST'),
              ('ITEST-CR-B2', @Fg, @Draft, 1, 1, 'EA', 'ITEST-CR-V2', 1, 'ITEST');
            """, ("@Fg", Fg), ("@Core", Core), ("@Old", Old), ("@Draft", Draft));
    }

    [SkippableFact]
    public void Read_ignores_non_effective_versions()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f);
        try
        {
            using var conn = f.OpenConnection();
            var r = CoreItemResolver.Read(conn, null, Fg);
            Assert.Equal((CoreItemResolver.Outcome.Core, Core), (r.Outcome, r.CoreItemNo));
            Assert.Equal(CoreItemResolver.Outcome.Self, CoreItemResolver.Read(conn, null, Core).Outcome);
            Assert.Equal(CoreItemResolver.Outcome.Self, CoreItemResolver.Read(conn, null, "ITEST-CR-NOPE").Outcome);
        }
        finally { Cleanup(f); }
    }

    /// <summary>스펙 부록 A — 2026-09-30 개발 DB ASSY 77건. 시드가 바뀌면 이 표를 같이 고친다.</summary>
    static readonly (string Assy, string Core)[] Expected =
    {
        ("83335-P8000BM1","D3133-P8000"), ("83335-P8000DNN","D3133-P8000"), ("83335-P8000JY2","D3133-P8000"), ("83335-P8000RBQ","D3133-P8000"),
        ("83345-P8000BM1","D3143-P8000"), ("83345-P8000DNN","D3143-P8000"), ("83345-P8000JY2","D3143-P8000"), ("83345-P8000RBQ","D3143-P8000"),
        ("M83371-P8000RBQ","D0133-P8000"), ("M83371-P8010RBQ","D0133-P8010"), ("M83381-P8000RBQ","D0143-P8000"), ("M83381-P8010RBQ","D0143-P8010"),
        ("M2310-FC000NNB","D0101-FC000"), ("M2310-FC000YGU","D0101-FC000"), ("M2310-FC010NNB","D0101-FC010"), ("M2310-FC010YGU","D0101-FC010"),
        ("M2320-FC000NNB","D0201-FC000"), ("M2320-FC000YGU","D0201-FC000"),
        ("M3310-FC000NNB","D4101-FC000"), ("M3310-FC000YGU","D4101-FC000"), ("M3310-FC010NNB","D4101-FC010"), ("M3310-FC010YGU","D4101-FC010"),
        ("M3320-FC000NNB","D4201-FC000"), ("M3320-FC000YGU","D4201-FC000"), ("M3320-FC010NNB","D4201-FC010"), ("M3320-FC010YGU","D4201-FC010"),
        ("M2311-TD000NNB","C2311-TD000"), ("M2311-TD000PNY","C2311-TD000"), ("M2311-TD000VKE","C2311-TD000"), ("M2311-TD000YGN","C2311-TD000"),
        ("M3311-TD000NNB","C3311-TD000"), ("M3311-TD000PNY","C3311-TD000"), ("M3311-TD000VKE","C3311-TD000"), ("M3311-TD000YGN","C3311-TD000"),
        ("M3311-TD100NNB","C3311-TD100"), ("M3311-TD100PNY","C3311-TD100"), ("M3311-TD100VKE","C3311-TD100"), ("M3311-TD100YGN","C3311-TD100"),
        ("M3321-TD000NNB","C3321-TD000"), ("M3321-TD000PNY","C3321-TD000"), ("M3321-TD000VKE","C3321-TD000"), ("M3321-TD000YGN","C3321-TD000"),
        ("M3321-TD100NNB","C3321-TD100"), ("M3321-TD100PNY","C3321-TD100"), ("M3321-TD100VKE","C3321-TD100"), ("M3321-TD100YGN","C3321-TD100"),
        ("82371-XA000CRN","D0111-XA000"), ("82371-XA000DFS","D0111-XA000"), ("82381-XA000CRN","D0121-XA000"), ("82381-XA000DFS","D0121-XA000"),
        ("83371-XA000CRN","D0131-XA000"), ("83371-XA000DFS","D0131-XA000"), ("83371-XA010CRN","D0131-XA010"), ("83371-XA010DFS","D0131-XA010"),
        ("83381-XA000CRN","D0141-XA000"), ("83381-XA000DFS","D0141-XA000"), ("83381-XA010CRN","D0141-XA010"), ("83381-XA010DFS","D0141-XA010"),
        ("83310-QI000UUG","D4101-QI000"), ("83320-QI000UUG","D4201-QI000"),
        ("M2310-PI010NNB","D0111-PI010"), ("M2310-PI010VKE","D0111-PI010"), ("M2310-PI010YGU","D0111-PI010"), ("M2310-QI000UUG","D0101-QI000"),
        ("M2320-PI010NNB","D0121-PI010"), ("M2320-PI010VKE","D0121-PI010"), ("M2320-PI010YGU","D0121-PI010"), ("M2320-QI000UUG","D0201-QI000"),
        ("M3310-PI020NNB","D0131-PI020"), ("M3310-PI020VKE","D0131-PI020"), ("M3310-PI020YGU","D0131-PI020"),
        ("M3320-PI020NNB","D0141-PI020"), ("M3320-PI020VKE","D0141-PI020"), ("M3320-PI020YGU","D0141-PI020"),
        ("M0211-P1010WK","82311-DW010"), ("M0221-P1010WK","82324-DW010"),
        ("846J0-NV010","D0103-NV010"),
    };

    [SkippableFact]
    public void Read_resolves_every_master_list_assembly_to_its_single_core()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If((int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Item WHERE ItemType='ASSY' AND CreatedBy='seed';")! != 77,
                "BOM Master List seed (77 ASSY) not applied");
        using var conn = f.OpenConnection();
        var bad = new List<string>();
        foreach (var (assy, core) in Expected)
        {
            var r = CoreItemResolver.Read(conn, null, assy);
            if (r.Outcome != CoreItemResolver.Outcome.Core || r.CoreItemNo != core)
                bad.Add($"{assy}: {r.Outcome} {r.CoreItemNo}");
        }
        Assert.Empty(bad);
        Assert.Equal(77, Expected.Length);
    }

    [SkippableFact]
    public void Dev_seed_maps_a_mold_and_inj_bop_to_exactly_the_resolver_core()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Skip.If((int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Mold WHERE CreatedBy = 'SEED-DEMO';")! == 0, "seed_inj_img_master_dev not applied");
        using var conn = f.OpenConnection();
        var bad = new List<string>();
        foreach (var (assy, core) in Expected)
        {
            var r = CoreItemResolver.Read(conn, null, assy);
            var mold = Scalar(f, "SELECT COUNT(*) FROM dbo.MD_MoldItem WHERE ItemNo = @I AND ActiveFlag = 1;", ("@I", r.CoreItemNo ?? ""));
            var bop  = Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Bop WHERE ItemNo = @I AND StationCode LIKE 'ST-INJ-%';", ("@I", r.CoreItemNo ?? ""));
            var img  = Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Bop WHERE ItemNo = @I AND StationCode LIKE 'ST-IMG-%';", ("@I", assy));
            if (r.CoreItemNo != core || (int)mold! < 1 || (int)bop! < 1 || (int)img! < 1)
                bad.Add($"{assy}: core={r.CoreItemNo} mold={mold} injBop={bop} imgBop={img}");
        }
        Assert.Empty(bad);
    }
}
