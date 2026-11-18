using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// LOT↔팔레트 매핑 이력(PR_LotPallet) — LOT 조회 페이징·팔레트 열, 작업지시 팔레트 단계 목록. 로컬 DB(AMES_TEST_CONN) 필요, 시험 행은 끝에 지운다.
public class LotPalletMappingTests
{
    [SkippableFact]
    public void Lot_search_pages_and_shows_stage_pallets_from_mapping_stock_and_shipment()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        Skip.If(Scalar(f!, "SELECT OBJECT_ID(N'dbo.PR_LotPallet', N'U')") is null or DBNull, "apply dist/migrate_lot_pallet.sql first");
        var pick = Scalar(f!, "SELECT TOP 1 l.LotCode + '|' + CAST(l.WoID AS varchar(20)) FROM dbo.tbl_Lot l WHERE l.WoID IS NOT NULL ORDER BY l.LotID DESC") as string;
        Skip.If(pick is null, "no LOT with a work order");
        var (lot, woId) = (pick!.Split('|')[0], int.Parse(pick.Split('|')[1]));
        const string P1 = "ZZ-TEST-PLT-1", P2 = "ZZ-TEST-PLT-2";
        var repo = new WorkOrderRepository(f!);
        Exec(f!, $"DELETE dbo.PR_LotPallet WHERE PalletNo IN ('{P1}','{P2}')");
        try
        {
            // 공정(INJ)은 P1, 같은 LOT 의 IMG 는 다른 팔레트 P2 — 단계마다 다를 수 있다
            Exec(f!, $"INSERT dbo.PR_LotPallet(LotCode,PalletNo,Stage,CreatedBy) VALUES ('{lot}','{P1}','INJ','T'),('{lot}','{P2}','IMG','T'),('{lot}','{P1}','PNT','T')");

            var (rows, total) = repo.SearchLotsPage(lot, null, null, null, null, null, null, 0, 50);
            Assert.True(total >= 1);
            var row = Assert.Single(rows, r => r.LotCode == lot);
            Assert.Contains($"{P1} (INJ/PNT)", row.PalletNos);   // 같은 팔레트의 여러 단계는 한 번만, 단계를 묶어 보인다
            Assert.Contains($"{P2} (IMG)", row.PalletNos);

            var pallets = repo.ListWoPallets(woId);
            Assert.Contains(pallets, p => p.Stage == "INJ" && p.PalletNo == P1);
            Assert.Contains(pallets, p => p.Stage == "IMG" && p.PalletNo == P2);

            // 페이징 — 1건씩 끊어도 전체 건수는 같고, 다음 페이지는 겹치지 않는다
            var (p0, t0) = repo.SearchLotsPage(null, null, null, null, null, null, null, 0, 1);
            var (p1, t1) = repo.SearchLotsPage(null, null, null, null, null, null, null, 1, 1);
            Assert.Equal(t0, t1);
            Assert.Single(p0);
            if (t0 > 1) Assert.NotEqual(p0[0].LotCode, Assert.Single(p1).LotCode);
        }
        finally { Exec(f!, $"DELETE dbo.PR_LotPallet WHERE PalletNo IN ('{P1}','{P2}')"); }
    }

    [SkippableFact]
    public void Lot_filter_suggestions_match_typed_text_for_lot_wo_and_item()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        var pick = Scalar(f!, "SELECT TOP 1 l.LotCode + '|' + w.WoNumber + '|' + l.ItemNo FROM dbo.tbl_Lot l JOIN dbo.PP_WorkOrder w ON w.WoID = l.WoID WHERE l.ItemNo IS NOT NULL ORDER BY l.LotID DESC") as string;
        Skip.If(pick is null, "no LOT with a work order");
        var (lot, wo, item) = (pick!.Split('|')[0], pick.Split('|')[1], pick.Split('|')[2]);
        var repo = new WorkOrderRepository(f!);
        Assert.Contains(lot, repo.SuggestLotFilter("lot", lot[1..^1]));          // 가운데 글자만 쳐도 나온다
        Assert.Contains(lot, repo.SuggestLotFilter("lot", lot.ToLowerInvariant()));   // 대소문자 무관
        Assert.Contains(wo, repo.SuggestLotFilter("wo", wo[3..]));
        Assert.Contains(item, repo.SuggestLotFilter("item", item[..3]));
        Assert.True(repo.SuggestLotFilter("lot", lot[..2], 5).Count <= 5);        // 건수 제한
        Assert.Empty(repo.SuggestLotFilter("lot", ""));                           // 입력이 없으면 제안 없음
        Assert.Empty(repo.SuggestLotFilter("lot", "NO-SUCH-LOT-ZZZ"));
    }

    static void Exec(AMES.Data.Connection.AmesConnectionFactory f, string sql)
    {
        using var conn = f.OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }
}
