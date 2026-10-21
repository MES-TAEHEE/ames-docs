using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-22 — 로케이션 2단계: 1차(SAP 수신 또는 MES 등록, 재고 없음)가 상세(재고 위치)를 묶는다.
// 상세의 상위는 1차만, 1차에는 상위가 없고, 재고가 있는 로케이션은 상위가 될 수 없으며, 하위가 있는 로케이션은 지울 수 없다
public class LocationHierarchyTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test locations and one inventory row";
    const string Actor = "itest-loc";
    const string P1 = "ITEST-LOC-P1";
    const string P2 = "ITEST-LOC-P2";
    const string D1 = "ITEST-LOC-D1";
    const string D2 = "ITEST-LOC-D2";
    const string D3 = "ITEST-LOC-D3";
    const string Lot = "ITEST-LOC-LOT";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.WH_Inventory WHERE LotNo = @Lot;
        DELETE dbo.MD_Location WHERE LocationID IN (@P1, @P2, @D1, @D2, @D3);
        """, ("@Lot", Lot), ("@P1", P1), ("@P2", P2), ("@D1", D1), ("@D2", D2), ("@D3", D3));

    static void SetParent(MasterDataRepository md, string id, string? parent) =>
        md.UpdateLocation(id, "itest", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor,
            setParent: true, parentLocationId: parent);

    [SkippableFact]
    public void Details_hang_under_primary_locations_only()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        MasterDataRepository.LocationRow Row(string id) => md.ListLocations().Single(l => l.LocationID == id);
        try
        {
            Cleanup(f!);
            md.InsertPrimaryLocation(P1, "itest primary", "EOS", "MAT_AREA", null, Actor);
            md.InsertPrimaryLocation(P2, "itest primary 2", "EOS", "MAT_AREA", null, Actor);
            md.InsertLocation(D1, "itest", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor);
            md.InsertLocation(D2, "itest", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor);

            Assert.Equal(MasterDataRepository.LocationLevelPrimary, Row(P1).LocationLevel);
            Assert.False(Row(P1).SAPFlag);
            Assert.Equal(MasterDataRepository.LocationLevelDetail, Row(D1).LocationLevel);
            Assert.Null(Row(D1).ParentLocationID);

            SetParent(md, D1, P1);
            Assert.Equal(P1, Row(D1).ParentLocationID);
            Assert.Equal(1, Row(P1).ChildCount);

            // 상위를 넘기지 않는 저장은 상위를 그대로 둔다
            md.UpdateLocation(D1, "itest 2", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor);
            Assert.Equal(P1, Row(D1).ParentLocationID);

            Assert.Equal(52073, Assert.Throws<SqlException>(() => SetParent(md, D2, D1)).Number);       // 상세는 상위가 될 수 없다
            Assert.Equal(52073, Assert.Throws<SqlException>(() => SetParent(md, D2, D2)).Number);       // 자기 자신
            Assert.Equal(52073, Assert.Throws<SqlException>(() => SetParent(md, D2, "ITEST-LOC-NONE")).Number);
            Assert.Equal(52074, Assert.Throws<SqlException>(() => SetParent(md, P2, P1)).Number);       // 1차에는 상위 없음

            Exec(f!, "INSERT dbo.WH_Inventory (LotNo, PartNo, LocationNo, Qty, ReceivedAt) VALUES (@Lot, 'ITEST-LOC-PART', @P, 3, SYSDATETIME())",
                ("@Lot", Lot), ("@P", P2));
            Assert.Equal(52075, Assert.Throws<SqlException>(() => SetParent(md, D2, P2)).Number);       // 재고가 있는 로케이션은 상위 불가
            Assert.Null(Row(D2).ParentLocationID);

            // 하위 등록 — 창고·구역은 상위를 따르고, 상위는 상위 로케이션만, 재고 있는 로케이션 아래는 안 된다
            Exec(f!, "UPDATE dbo.MD_Location SET AreaCode = 'FG_AREA' WHERE LocationID = @P", ("@P", P1));
            var part = (string)Scalar(f!, "SELECT TOP (1) ItemNo FROM dbo.MD_Item WHERE COALESCE(ActiveFlag, 1) = 1 ORDER BY ItemNo")!;
            Assert.Equal(51050, Assert.Throws<SqlException>(() => md.InsertChildLocation(D3, null, P1, null, Actor, "ITEST-NO-SUCH-PART")).Number);
            md.InsertChildLocation(D3, "itest child", P1, 12, Actor, part);
            var child = Row(D3);
            Assert.Equal((P1, MasterDataRepository.LocationLevelDetail, "FG_AREA", "FG", 12m, part),
                (child.ParentLocationID, child.LocationLevel, child.AreaCode, child.LocationType, child.Capacity!.Value, child.PartNo));
            Assert.Equal(52073, Assert.Throws<SqlException>(() => md.InsertChildLocation("ITEST-LOC-X", null, D1, null, Actor)).Number);
            Assert.Equal(52075, Assert.Throws<SqlException>(() => md.InsertChildLocation("ITEST-LOC-X", null, P2, null, Actor)).Number);
            Assert.DoesNotContain(md.ListLocations(), l => l.LocationID == "ITEST-LOC-X");
            Exec(f!, "DELETE dbo.MD_Location WHERE LocationID = @D", ("@D", D3));

            Assert.Equal(52076, Assert.Throws<SqlException>(() => md.DeleteLocation(P1)).Number);     // 하위가 있으면 삭제 불가

            SetParent(md, D1, null);
            Assert.Null(Row(D1).ParentLocationID);
            Assert.Equal(0, Row(P1).ChildCount);
            md.DeleteLocation(P1);
            Assert.DoesNotContain(md.ListLocations(), l => l.LocationID == P1);
        }
        finally { Cleanup(f!); }
    }
}
