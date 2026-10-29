using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ShippingPolicyReadOnlyTests
{
    [SkippableFact]
    public void Deployed_schema_supports_due_risk_and_fifo_queries_without_writes()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_SHIPPING_READ_TEST_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Read-only schema validation requires an explicit connection.");
        var factory = new AmesConnectionFactory(connection!);
        var snapshot = new FinishedGoodsRepository(factory).ListShippingDueRisks();
        Assert.True(snapshot.NextWorkday > snapshot.AsOf);
        Assert.All(snapshot.Rows, row => Assert.NotEqual(ShippingDueRiskLevel.None, row.Risk));
        using var conn = factory.OpenConnection();
        Assert.Empty(FinishedGoodsFifo.Check(conn, null, "__NO_SUCH_POLICY_TEST_BARCODE__", null));
    }
}
