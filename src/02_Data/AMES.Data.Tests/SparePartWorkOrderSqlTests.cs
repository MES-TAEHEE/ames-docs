using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class SparePartWorkOrderSqlTests
{
    [Fact]
    public void Choices_filter_before_loading_and_share_the_pm_planned_rule()
    {
        var sql = PdaSparePartsSql.OpenWorkOrders;
        Assert.Contains("w.Status IN ('ISSUED','ASSIGNED','IN_PROGRESS')", sql);
        Assert.Contains("AND NOT " + PmRules.PlannedWoSql, sql);
        Assert.DoesNotContain("SourceType=", sql); // CHECK/ANDON/CM are not silently excluded.
    }

    [Fact]
    public void Release_locks_the_work_order_before_inventory_and_validates_before_stock_writes()
    {
        var sql = PdaSparePartsSql.Move;
        var woLock = sql.IndexOf("MNT_WorkOrder w WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal);
        var stockLock = sql.IndexOf("MNT_SparePartItem I WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal);
        Assert.True(woLock >= 0 && woLock < stockLock);
        Assert.Contains("@WorkStatus NOT IN ('ISSUED','ASSIGNED','IN_PROGRESS')", sql);
        Assert.Contains(PmRules.PlannedWoSql, sql);
        Assert.Contains("CASE WHEN @WorkOrder IS NULL THEN 'PDA' ELSE 'MWO' END", sql);
        Assert.Contains("COALESCE(@WorkOrder,@Location", sql);
        Assert.Contains("'SUCCESS',@MoveNote", sql);
        Assert.Contains("@WorkOrder IS NOT NULL AND @Move<>'OUT'", sql);
    }

    [Fact]
    public void Cancel_serializes_with_completion_and_rechecks_the_original_release()
    {
        var sql = PdaSparePartsSql.Cancel;
        var woLock = sql.IndexOf("MNT_WorkOrder WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal);
        var itemLock = sql.IndexOf("MNT_SparePartItem WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal);
        var write = sql.IndexOf("UPDATE dbo.MD_SparePart", StringComparison.Ordinal);
        Assert.True(woLock >= 0 && woLock < itemLock && itemLock < write);
        Assert.Contains("IF @RefType='MWO'", sql);
        Assert.Contains("@WorkStatus NOT IN ('ISSUED','ASSIGNED','IN_PROGRESS')", sql);
        Assert.Contains("@OriginalTxn<>@ExpectedTxn OR @ExpectedTxn IS NULL", sql);
        Assert.Contains("R.ReversalOfTxnID=T.SparePartsTxnID", sql);
        Assert.Contains("'SUCCESS',@CancelNote", sql);
    }

    [Fact]
    public void Cancel_rejects_a_selected_old_transaction_before_locking_a_different_work_order_or_writing_stock()
    {
        var sql = PdaSparePartsSql.Cancel;
        var selected = sql.IndexOf("IF @TransactionId IS NOT NULL AND (@ExpectedTxn IS NULL OR @ExpectedTxn<>@TransactionId)", StringComparison.Ordinal);
        Assert.True(selected > sql.IndexOf("ORDER BY T.SparePartsTxnID DESC;", StringComparison.Ordinal));
        Assert.True(selected < sql.IndexOf("IF @RefType='MWO'", StringComparison.Ordinal));
        Assert.True(selected < sql.IndexOf("UPDATE dbo.MD_SparePart", StringComparison.Ordinal));
        Assert.Contains("This transaction is no longer cancellable.", sql);
    }
}
