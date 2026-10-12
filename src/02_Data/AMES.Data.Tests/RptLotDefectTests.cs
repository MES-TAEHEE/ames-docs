using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// 확정 전(RAW·로봇 NG) LOT 의 불량(ResultID NULL)이 생산 집계에 LOT 의 라인·발생 전기일로 더해지는지(읽기 전용, AMES_DEV 필요).
/// 기준 = 실적 행에 붙은 불량만 세던 옛 집계 + 그 날·그 라인의 ResultID NULL 불량.
/// </summary>
public class RptLotDefectTests
{
    [SkippableFact]
    public void Unconfirmed_lot_defects_are_added_to_production_reports()
    {
        var f = AmesDevDb.TryFactory();
        Skip.If(f is null, "AMES_DEV 접속 불가");
        var rpt = new RptRepository(f!);
        var cut = (int)rpt.ReadDayCutoff().TotalMinutes;

        DateTime day; string line; int orphan, resultDefect;
        using (var conn = f!.OpenConnection())
        {
            using (var cmd = new SqlCommand("""
                SELECT TOP 1 CAST(DATEADD(MINUTE, -@Cut, d.DetectedAt) AS DATE) AS PD, l.LineID
                FROM   dbo.PR_DefectDetail d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID
                WHERE  d.ResultID IS NULL AND l.LineID IS NOT NULL
                ORDER  BY d.DefectID DESC;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@Cut", cut);
                using var rdr = cmd.ExecuteReader();
                Skip.IfNot(rdr.Read(), "확정 전 LOT 불량 데이터 없음");
                day = (DateTime)rdr["PD"]; line = (string)rdr["LineID"];
            }
            using (var cmd = new SqlCommand("""
                SELECT ISNULL(SUM(d.Qty), 0)
                FROM   dbo.PR_DefectDetail d JOIN dbo.tbl_Lot l ON l.LotID = d.LotID
                WHERE  d.ResultID IS NULL AND l.LineID = @L AND CAST(DATEADD(MINUTE, -@Cut, d.DetectedAt) AS DATE) = @D;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@L", line); cmd.Parameters.AddWithValue("@D", day); cmd.Parameters.AddWithValue("@Cut", cut);
                orphan = (int)cmd.ExecuteScalar()!;
            }
            using (var cmd = new SqlCommand("""
                SELECT ISNULL(SUM(ISNULL(d.Qty, CASE WHEN r.DefectFlag = 1 THEN 1 ELSE 0 END)), 0)
                FROM   dbo.PR_ProductionResult r
                OUTER APPLY (SELECT SUM(dd.Qty) AS Qty FROM dbo.PR_DefectDetail dd WHERE dd.ResultID = r.ResultID) d
                WHERE  COALESCE(r.ProdDate, CAST(r.EntryAt AS DATE)) = @D AND r.LineID = @L;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@L", line); cmd.Parameters.AddWithValue("@D", day);
                resultDefect = (int)cmd.ExecuteScalar()!;
            }
        }
        Assert.True(orphan > 0);
        var expected = resultDefect + orphan;

        Assert.Equal(expected, rpt.ProductionTotals(day, day, line).Defect);                       // RPT-002 불량률 분모·분자
        Assert.Equal(expected, rpt.ListProductionByHour(day, line).Sum(r => r.DefectQty));          // RPT-001
        var adhoc = rpt.AdhocQuery("PROD", "Line", day, day).Single(r => r.Key == line);           // RPT-010
        Assert.Equal(expected, adhoc.M["DefectQty"]);

        // 불량 행은 실적 건수가 아니다
        var byShift = rpt.AdhocQuery("PROD", "Shift", day, day);
        var byLine  = rpt.AdhocQuery("PROD", "Line", day, day);
        Assert.Equal(byLine.Sum(r => r.M["Entries"]), byShift.Sum(r => r.M["Entries"]));
        Assert.Equal(byLine.Sum(r => r.M["DefectQty"]), byShift.Sum(r => r.M["DefectQty"]));
    }
}
