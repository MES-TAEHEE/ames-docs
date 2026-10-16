using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-16 — MD_Item·MD_Vendor·MD_Location.SAPTransFlag 가 MD-003·MD-009·MD-018 저장 경로로 왕복하고,
// 값을 넘기지 않은 수정(null)은 저장된 값을 그대로 두는지
public class SapTransFlagTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item, vendor and location";
    const string Actor = "itest-sap";
    const string Item  = "ITEST-SAP-ITEM";
    const string Vend  = "ITEST-SAP-V";
    const string Loc   = "ITEST-SAP-LOC";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Item     WHERE ItemNo = @I;
        DELETE dbo.MD_Vendor   WHERE VendorID = @V;
        DELETE dbo.MD_Location WHERE LocationID = @L;
        """, ("@I", Item), ("@V", Vend), ("@L", Loc));

    static bool Stored(Connection.AmesConnectionFactory f, string table, string key, string id) =>
        (bool)Scalar(f, $"SELECT SAPTransFlag FROM dbo.{table} WHERE {key} = @K", ("@K", id))!;

    [SkippableFact]
    public void Flag_roundtrips_and_a_null_update_keeps_the_stored_value()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);

            md.InsertItem(Item, "itest sap", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor, sapTransFlag: true);
            Assert.True(Assert.Single(md.ListItems(Item)).SAPTransFlag);
            md.UpdateItem(Item, "itest sap 2", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            Assert.True(Stored(f!, "MD_Item", "ItemNo", Item));
            md.UpdateItem(Item, "itest sap 2", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor, sapTransFlag: false);
            Assert.False(Assert.Single(md.ListItems(Item)).SAPTransFlag);

            md.InsertVendor(Vend, "itest sap", null, null, null, null, null, null, null, false, null, null, true, Actor, sapTransFlag: true);
            Assert.True(md.ListVendors(Vend).Single(v => v.VendorID == Vend).SAPTransFlag);
            md.UpdateVendor(Vend, "itest sap 2", null, null, null, null, null, null, null, false, null, null, true, Actor);
            Assert.True(Stored(f!, "MD_Vendor", "VendorID", Vend));
            md.UpdateVendor(Vend, "itest sap 2", null, null, null, null, null, null, null, false, null, null, true, Actor, sapTransFlag: false);
            Assert.False(md.ListVendors(Vend).Single(v => v.VendorID == Vend).SAPTransFlag);

            md.InsertLocation(Loc, "itest sap", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor, sapTransFlag: true);
            Assert.True(md.ListLocations().Single(l => l.LocationID == Loc).SAPTransFlag);
            md.UpdateLocation(Loc, "itest sap 2", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor);
            Assert.True(Stored(f!, "MD_Location", "LocationID", Loc));
            md.UpdateLocation(Loc, "itest sap 2", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor, sapTransFlag: false);
            Assert.False(md.ListLocations().Single(l => l.LocationID == Loc).SAPTransFlag);

            // 화면을 거치지 않고 넣은 행은 0(전송 안 함)
            Exec(f!, "UPDATE dbo.MD_Item SET SAPTransFlag = DEFAULT WHERE ItemNo = @I", ("@I", Item));
            Assert.False(Stored(f!, "MD_Item", "ItemNo", Item));
        }
        finally { Cleanup(f!); }
    }
}
