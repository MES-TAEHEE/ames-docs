using AMES.Data.Repositories;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class InventoryTargetsTests
{
    [Fact]
    public void Legacy_material_targets_are_visible_without_mixing_categories_or_units()
    {
        RptRepository.InventoryTarget[] targets=[new("WH","RESIN","G",200),new("FG","RESIN","G",50),
            new("WH","RESIN","KG",20),new("WH","TRIM","EA",500)];
        Assert.Equal(250m,InventoryTargets.Quantity(targets,"RESIN","G"));
        Assert.Equal(20m,InventoryTargets.Quantity(targets,"RESIN","KG"));
        Assert.Equal(0m,InventoryTargets.Quantity(targets,"OTHER","EA"));
    }

    [Fact]
    public void Shared_category_setting_takes_precedence_over_older_separate_inventory_settings()
    {
        RptRepository.InventoryTarget[] targets=[new("WH","RESIN","G",200),new("ALL","RESIN","G",300)];
        Assert.Equal(300m,InventoryTargets.Quantity(targets,"RESIN","G"));
    }
}
