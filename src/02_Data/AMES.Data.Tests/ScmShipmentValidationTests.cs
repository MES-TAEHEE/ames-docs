using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmShipmentValidationTests
{
    static ScmRepository Repository()=>new(new AmesConnectionFactory("Server=unused;Database=unused;Integrated Security=True"));
    static readonly Dictionary<string,IReadOnlyDictionary<int,string>> Versions=new();

    [Fact]
    public void Empty_shipment_is_rejected_before_opening_a_connection()
        =>Assert.Throws<ArgumentException>(()=>Repository().ConfirmSupplierShipment([],new DateTime(2026,10,13),Versions,"user","actor"));

    [Fact]
    public void Duplicate_deliveries_are_rejected_before_opening_a_connection()
    {
        var delivery=new ScmRepository.ShipmentDelivery("D-1","PO-1","V1",[new(1,10)]);
        Assert.Throws<ArgumentException>(()=>Repository().ConfirmSupplierShipment([delivery,delivery with {Number="d-1"}],new DateTime(2026,10,13),Versions,"user","actor"));
    }

    [Fact]
    public void Invalid_quantities_are_rejected_before_opening_a_connection()
    {
        var delivery=new ScmRepository.ShipmentDelivery("D-1","PO-1","V1",[new(1,0)]);
        Assert.Throws<ArgumentException>(()=>Repository().ConfirmSupplierShipment([delivery],new DateTime(2026,10,13),Versions,"user","actor"));
    }
}
