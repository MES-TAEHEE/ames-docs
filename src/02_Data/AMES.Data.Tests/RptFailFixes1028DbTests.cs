using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-28 RPT 단위테스트 결과서 FAIL 조치 — DB 경로(로컬 DB 쓰기, AMES_TEST_CONN 지정 시)
public class RptFailFixes1028DbTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — inserts and removes test results, defects and items";
    const string Actor = "itest-rpt1028";
    const string Line = "ITEST-RPT1028";
    const string Item = "ITEST-RPT1028";
    static readonly DateTime Day = new(2001, 3, 15);

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.PR_DefectDetail WHERE CreatedBy = @A;
        DELETE dbo.PR_ProductionResult WHERE CreatedBy = @A;
        DELETE dbo.MD_Item WHERE ItemNo = @I AND CreatedBy = @A;
        """, ("@A", Actor), ("@I", Item));

    // RPT001-TC-021·E2E-TC-005·RPT007 — 실적에 붙은 불량은 그 실적의 저장 전기일로 묶여 파레토·빌더·월간 지표·생산 집계가 같은 하루를 쓴다
    [SkippableFact]
    public void Result_linked_defects_follow_the_stored_production_date_on_every_report()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var rpt = new RptRepository(f!);
        try
        {
            Cleanup(f!);
            var other = Scalar(f!, """
                SELECT (SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE EntryAt < '2001-06-01' OR ProdDate < '2001-06-01')
                     + (SELECT COUNT(*) FROM dbo.PR_DefectDetail WHERE DetectedAt < '2001-06-01')
                """);
            Skip.If(Convert.ToInt32(other) > 0, "results or defects already exist before 2001-06");

            // 실적: 전기일 03-15(저장값), 입력은 03-15 23:00 — 불량은 다음 날 10:00 에 등록(실적 연결 3 EA) + 실적 없는 LOT 불량 2 EA(03-16 10:00)
            var rid = Convert.ToInt32(Scalar(f!, """
                INSERT dbo.PR_ProductionResult (LineID, ProcessCode, GoodQty, DefectFlag, EntryAt, ProdDate, CreatedBy, CreatedTS)
                OUTPUT INSERTED.ResultID VALUES (@L, 'INJ', 10, 0, '2001-03-15 23:00', '2001-03-15', @A, SYSDATETIME());
                """, ("@L", Line), ("@A", Actor)));
            Exec(f!, """
                INSERT dbo.PR_DefectDetail (ResultID, ProcessCode, DefectCode, Qty, DetectedAt, CreatedBy, CreatedTS)
                VALUES (@R, 'INJ', 'ITEST-D', 3, '2001-03-16 10:00', @A, SYSDATETIME()),
                       (NULL, 'INJ', 'ITEST-D', 2, '2001-03-16 10:00', @A, SYSDATETIME());
                """, ("@R", rid), ("@A", Actor));

            Assert.Equal(3, rpt.ListDefectAgg(Day, Day).Sum(r => r.Qty));                       // RPT-002 파레토 = 실적의 전기일
            Assert.Equal(rpt.ProductionTotals(Day, Day, Line).Defect, rpt.ListDefectAgg(Day, Day, Line).Sum(r => r.Qty));
            Assert.Equal(2, rpt.ListDefectAgg(Day.AddDays(1), Day.AddDays(1)).Sum(r => r.Qty)); // 실적 없는 불량은 발생 시각 − 경계

            var byDate = rpt.AdhocQuery("DEFECT", "Date", Day, Day.AddDays(1)).ToDictionary(r => r.Key, r => r.M["DefectQty"]);
            Assert.Equal(3m, byDate["2001-03-15"]);
            Assert.Equal(2m, byDate["2001-03-16"]);

            var month = rpt.ListMonthlyMetrics(new DateTime(2001, 3, 1), new DateTime(2001, 3, 1)).Single();
            Assert.Equal(5m, month.DefectQty);
            Assert.Equal(new DateTime(2001, 3, 1), rpt.LastMetricMonth(new DateTime(2001, 5, 1)));   // 6개월 창과 무관하게 찾는다
            Assert.Null(rpt.LastMetricMonth(new DateTime(2001, 2, 1)));
        }
        finally { Cleanup(f!); }
    }

    // RPT005-TC-010·014 — 재고 행이 없는 활성 품목도 안전·최대재고가 있으면 수량 0 으로 나온다
    [SkippableFact]
    public void Items_with_safety_stock_but_no_stock_rows_are_listed_at_zero()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var rpt = new RptRepository(f!);
        try
        {
            Cleanup(f!);
            Exec(f!, """
                INSERT dbo.MD_Item (ItemNo, ItemName, ItemType, SafetyStock, ActiveFlag, CreatedBy, CreatedTS)
                VALUES (@I, N'RPT 1028 test', 'MATERIAL', 5, 1, @A, SYSDATETIME());
                """, ("@I", Item), ("@A", Actor));
            var row = rpt.ListInventorySku().Single(r => r.ItemNo == Item);
            Assert.Equal(0m, row.Qty);
            Assert.Equal("WH", row.Source);
            Assert.Equal(5m, row.SafetyStock);

            Exec(f!, "UPDATE dbo.MD_Item SET ActiveFlag = 0 WHERE ItemNo = @I", ("@I", Item));
            Assert.DoesNotContain(rpt.ListInventorySku(), r => r.ItemNo == Item);   // 사용 안 함 품목은 빠진다
        }
        finally { Cleanup(f!); }
    }

    // RPT010-TC-019 — 출하 원본의 주문 수량은 값이 없으므로 측정값 키가 없다(화면 「—」)
    [SkippableFact]
    public void Builder_ship_source_has_no_ordered_quantity_value()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV 접속 불가");
        var rows = new RptRepository(f!).AdhocQuery("SHIP", "Customer", new DateTime(2020, 1, 1), new DateTime(2035, 12, 31));
        Skip.If(rows.Count == 0, "no shipments");
        Assert.All(rows, r => Assert.False(r.M.ContainsKey("OrderedQty")));
        Assert.All(rows, r => Assert.True(r.M.ContainsKey("Orders")));
    }
}
