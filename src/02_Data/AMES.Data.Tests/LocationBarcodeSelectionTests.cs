using AMES.Data.Repositories;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LocationBarcodeSelectionTests
{
    [Fact]
    public void Parent_print_uses_all_active_children_without_printing_parent_or_other_area()
    {
        MasterDataRepository.LocationRow Row(string id, string? parent=null, bool active=true, byte level=2) =>
            new(id,id,"EOS","AREA",null,null,null,null,null,null,null,active,null,null,null,null,ParentLocationID:parent,LocationLevel:level);
        var parent = Row("PRIMARY",level:1);
        var children = new[] { Row("CHILD-2","PRIMARY"), Row("CHILD-1","PRIMARY"),Row("INACTIVE","PRIMARY",false),Row("OTHER","OTHER-PARENT") };
        Assert.Equal(new[] { "CHILD-1","CHILD-2" },LocationBarcodeSelection.Codes(parent,children));
        Assert.Equal(new[] { "CHILD-2" },LocationBarcodeSelection.Codes(children[0],children));
        Assert.Empty(LocationBarcodeSelection.Codes(parent,[]));
    }

}
