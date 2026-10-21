using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-21 — SAPFlag(구 SAPTransFlag) 는 SAP 에서 받은 행 표시다. MES 저장 경로(품목·협력업체·로케이션·고객사·BOM 버전·단위(10-22) 등록·수정)는
// 이 값을 쓰지 않는다 — 새 행은 0, SAP 수신이 1 로 둔 행은 화면에서 고쳐 저장해도 1 그대로이고 목록이 그 값을 읽는다
public class SapFlagTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item, vendor, location, customer, BOM version and unit";
    const string Actor = "itest-sap";
    const string Item  = "ITEST-SAP-ITEM";
    const string Vend  = "ITEST-SAP-V";
    const string Loc   = "ITEST-SAP-LOC";
    const string Cust  = "ITEST-SAP-C";
    const string Ver   = "ITEST-SAP-BV";
    const string Uom   = "ITSAPU";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_BomVersion WHERE VersionID = @B;
        DELETE dbo.MD_Item     WHERE ItemNo = @I;
        DELETE dbo.MD_Vendor   WHERE VendorID = @V;
        DELETE dbo.MD_Location WHERE LocationID = @L;
        DELETE dbo.MD_Customer WHERE CustomerID = @C;
        DELETE dbo.MD_Uom      WHERE UOMCode = @U;
        """, ("@I", Item), ("@V", Vend), ("@L", Loc), ("@C", Cust), ("@B", Ver), ("@U", Uom));

    static bool Stored(Connection.AmesConnectionFactory f, string table, string key, string id) =>
        (bool)Scalar(f, $"SELECT SAPFlag FROM dbo.{table} WHERE {key} = @K", ("@K", id))!;

    static void SetFromSap(Connection.AmesConnectionFactory f, string table, string key, string id) =>
        Exec(f, $"UPDATE dbo.{table} SET SAPFlag = 1 WHERE {key} = @K", ("@K", id));

    [SkippableFact]
    public void Mes_saves_never_write_the_flag_and_keep_what_sap_set()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);

            md.InsertItem(Item, "itest sap", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            Assert.False(Assert.Single(md.ListItems(Item)).SAPFlag);
            SetFromSap(f!, "MD_Item", "ItemNo", Item);
            md.UpdateItem(Item, "itest sap 2", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);
            Assert.True(Assert.Single(md.ListItems(Item)).SAPFlag);

            md.InsertVendor(Vend, "itest sap", null, null, null, null, null, null, null, false, null, null, true, Actor);
            Assert.False(Stored(f!, "MD_Vendor", "VendorID", Vend));
            SetFromSap(f!, "MD_Vendor", "VendorID", Vend);
            md.UpdateVendor(Vend, "itest sap 2", null, null, null, null, null, null, null, false, null, null, true, Actor);
            Assert.True(md.ListVendors(Vend).Single(v => v.VendorID == Vend).SAPFlag);

            md.InsertLocation(Loc, "itest sap", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor);
            Assert.False(Stored(f!, "MD_Location", "LocationID", Loc));
            SetFromSap(f!, "MD_Location", "LocationID", Loc);
            md.UpdateLocation(Loc, "itest sap 2", "EOS", "MAT_AREA", null, null, null, null, null, null, null, true, Actor);
            Assert.True(md.ListLocations().Single(l => l.LocationID == Loc).SAPFlag);

            md.InsertCustomer(Cust, null, "itest sap", null, null, null, null, null, null, null, false, null, "ACTIVE", Actor);
            Assert.False(Stored(f!, "MD_Customer", "CustomerID", Cust));
            SetFromSap(f!, "MD_Customer", "CustomerID", Cust);
            md.UpdateCustomer(Cust, null, "itest sap 2", null, null, null, null, null, null, null, false, null, "ACTIVE", Actor);
            Assert.True(md.ListCustomers(Cust).Single(c => c.CustomerID == Cust).SAPFlag);

            md.InsertBomVersion(Ver, Item, "V1.0", new DateOnly(2099, 1, 1), null, null, null, Actor);
            Assert.False(Stored(f!, "MD_BomVersion", "VersionID", Ver));
            SetFromSap(f!, "MD_BomVersion", "VersionID", Ver);
            Assert.True(md.UpdateBomVersion(Ver, Item, "V1.0", new DateOnly(2099, 1, 1), null, "ECO", "itest", Actor));
            Assert.True(md.ListBomVersions().Single(v => v.VersionID == Ver).SAPFlag);

            md.InsertUom(Uom, "itest sap", null, false, null, null, 0, null, true, Actor);
            Assert.False(Stored(f!, "MD_Uom", "UOMCode", Uom));
            SetFromSap(f!, "MD_Uom", "UOMCode", Uom);
            md.UpdateUom(Uom, "itest sap 2", null, false, null, null, 0, null, true, Actor);
            Assert.True(md.ListUoms(includeRetired: true).Single(u => u.UOMCode == Uom).SAPFlag);
        }
        finally { Cleanup(f!); }
    }
}
