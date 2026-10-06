using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// APS 마스터 gap 컬럼 — MD_Item.BoxQty 가 MD-003 리포지토리 CRUD 를 왕복하는지(MD_MoldItem.PartCavityCount 는 09-30 폐지).
/// AMES_DEV 통합 테스트(migrate_aps.sql 적용 전제), DB 미기동 시 skip.
/// </summary>
// PlanScheduleTests·MoldPlanningTests 와 같은 AMES_DEV 인스턴스를 쓰므로 같은 컬렉션으로 묶어 직렬화한다(ITEST-APS-% 는 겹치지 않음).
[Collection("AMES_DEV plan week")]
public class ApsMasterColumnsTests
{
    const string Item  = "ITEST-APS-BOX";
    const string Mold  = "ITEST-APS-MOLD";
    const string Actor = "ITEST";

    static void Cleanup(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE FROM dbo.MD_MoldItem WHERE MoldID = @M OR ItemNo = @I;
            DELETE FROM dbo.MD_Mold     WHERE MoldID = @M;
            DELETE FROM dbo.MD_Item     WHERE ItemNo = @I;
            """, ("@I", Item), ("@M", Mold));
    }

    [SkippableFact]
    public void Item_BoxQty_roundtrips_through_insert_list_update()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f!);
        try
        {
            var repo = new MasterDataRepository(f!);
            repo.InsertItem(Item, "ITEST APS box item", "FINISHED", null, null, "EA", "A",
                null, null, 10m, null, null, null, null,
                palletQty: 12, maxPalletQty: null, toteFlag: false, boxQty: 36,
                activeFlag: true, createdBy: Actor);

            var row = Assert.Single(repo.ListItems(Item));
            Assert.Equal(36, row.BoxQty);
            Assert.Equal(12, row.PalletQty);
            Assert.False(row.ToteFlag);

            repo.UpdateItem(Item, "ITEST APS box item", "FINISHED", null, null, "EA", "A",
                null, null, 10m, null, null, null, null,
                palletQty: 12, maxPalletQty: null, toteFlag: false, boxQty: null,
                activeFlag: true, modifiedBy: Actor);

            row = Assert.Single(repo.ListItems(Item));
            Assert.Null(row.BoxQty);
            Assert.Equal(Actor, row.ModifiedBy);
            Assert.Equal(DBNull.Value, Scalar(f!, "SELECT BoxQty FROM dbo.MD_Item WHERE ItemNo = @I;", ("@I", Item)));
        }
        finally { Cleanup(f!); }
    }

}
