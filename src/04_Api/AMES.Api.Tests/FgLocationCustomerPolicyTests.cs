using AMES.Data.Repositories;
using Xunit;

namespace AMES.Api.Tests;

public sealed class FgLocationCustomerPolicyTests
{
    [Fact]
    public void FG_locations_allow_different_customers_but_outbound_units_keep_customer_validation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src/04_Api/AMES.Api"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
        Assert.DoesNotContain("Customer mix is blocked", source);
        Assert.DoesNotContain("Location already contains mixed customer stock", source);
        Assert.Contains("row.Capacity > 0 && row.AvailableQty < qty", source);
        Assert.Contains("This unit contains products for different customers.", PdaFinishedGoodsSql.OutboundScan);
        Assert.Contains("This unit contains products for different customers.", PdaFinishedGoodsSql.OutboundComplete);
    }
}
