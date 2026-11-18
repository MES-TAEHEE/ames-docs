using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// LOT 작업지시 이동(WorkOrderRepository.MoveLots) — 로컬 DB(AMES_TEST_CONN)의 실제 LOT 으로 옮겼다가 시험 전 값으로 되돌린다.
public class LotMoveDbTests
{
    // 시험이 건드리는 단계·헤더 행의 값을 통째로 저장했다가 되돌린다(상태·시각·수정자까지)
    sealed record RowSnap(string Table, string KeyCol, int Key, object?[] V);
    static readonly string[] Cols = ["CompletedQty", "Status", "ActualStart", "ActualEnd", "ModifiedBy", "ModifiedTS"];

    static RowSnap Snap(AmesConnectionFactory f, string table, string keyCol, int key)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT {string.Join(",", Cols)} FROM dbo.{table} WHERE {keyCol}=@K", conn);
        cmd.Parameters.AddWithValue("@K", key);
        using var r = cmd.ExecuteReader(); r.Read();
        var v = new object?[Cols.Length]; r.GetValues(v!);
        return new(table, keyCol, key, v);
    }

    static void Restore(AmesConnectionFactory f, RowSnap s)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"UPDATE dbo.{s.Table} SET {string.Join(",", Cols.Select((c, i) => $"{c}=@{i}"))} WHERE {s.KeyCol}=@K", conn);
        for (var i = 0; i < Cols.Length; i++) { var pr = cmd.Parameters.AddWithValue($"@{i}", s.V[i] ?? DBNull.Value); if (s.V[i] is DateTime) pr.SqlDbType = System.Data.SqlDbType.DateTime2; }
        cmd.Parameters.AddWithValue("@K", s.Key);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void Open_wo_lot_can_be_detached_to_stock_and_attached_back_with_step_counts_following()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        // 열린(Released·In Progress) 단계가 있고 확정 LOT 이 묶인 WO 하나
        var pick = Scalar(f!, """
            SELECT TOP 1 CAST(l.LotID AS varchar(20)) + '|' + l.LotCode + '|' + CAST(l.WoID AS varchar(20)) + '|' + CAST(r.RoutingLineID AS varchar(20))
            FROM dbo.tbl_Lot l
            JOIN dbo.PP_WorkOrder w ON w.WoID = l.WoID AND w.Status NOT IN ('Completed','Stocked','Closed','Cancelled')
            JOIN dbo.PP_WorkOrderRouting r ON r.WoID = l.WoID AND r.ProcessCode = l.ProcessCode AND r.LineID = l.LineID AND COALESCE(r.ItemNo, w.ItemNo) = l.ItemNo AND r.Status IN ('Released','In Progress')
            WHERE l.Status = 'CONFIRMED' AND r.CompletedQty >= 1 AND w.OrderQty > r.CompletedQty + 1
            """) as string;
        Skip.If(pick is null, "no open WO with a confirmed LOT");
        var (lotId, lot, wo, step) = (int.Parse(pick!.Split('|')[0]), pick.Split('|')[1], int.Parse(pick.Split('|')[2]), int.Parse(pick.Split('|')[3]));
        decimal Cq() => Convert.ToDecimal(Scalar(f!, "SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID=@R", ("@R", step))!);
        object? LotWo() => Scalar(f!, "SELECT WoID FROM dbo.tbl_Lot WHERE LotID=@L", ("@L", lotId));
        object? ResWo() => Scalar(f!, "SELECT TOP 1 WoID FROM dbo.PR_ProductionResult WHERE LotID=@L ORDER BY ResultID", ("@L", lotId));
        var repo = new WorkOrderRepository(f!);
        var before = Cq();
        var snaps = new[] { Snap(f!, "PP_WorkOrderRouting", "RoutingLineID", step), Snap(f!, "PP_WorkOrder", "WoID", wo) };
        var good = Convert.ToDecimal(Scalar(f!, "SELECT ISNULL(SUM(GoodQty),0) FROM dbo.PR_ProductionResult WHERE LotID=@L", ("@L", lotId))!);
        try
        {
            // 같은 WO 로는 이동할 수 없고, 확정이 아닌/없는 LOT 도 거부 — 거부되면 아무것도 바뀌지 않는다
            Assert.Equal("SameWo", Assert.Single(repo.MoveLots([lot], wo, "T").Errors).Code);
            Assert.Equal("NotFound", Assert.Single(repo.MoveLots(["NO-SUCH-LOT-ZZZ"], null, "T").Errors).Code);
            Assert.Equal(before, Cq());

            // 미리보기 — 실제로 옮기기 전에 보여 주는 값이 옮긴 뒤 값과 같아야 한다
            var k = ((string)Scalar(f!, "SELECT ProcessCode + '|' + LineID + '|' + ItemNo FROM dbo.tbl_Lot WHERE LotID=@L", ("@L", lotId))!).Split('|');
            var pv = repo.PreviewLotMove([lot], wo, null, k[0], k[1], k[2]);
            Assert.Equal(1, pv.LotCount); Assert.Equal(good, pv.GoodQty); Assert.Null(pv.Target);
            Assert.Equal(before, pv.Source!.Before); Assert.Equal(before - good, pv.Source.After);

            // WO 에서 풀기 → 재고: 양품만큼 단계 실적이 줄고 LOT·실적의 WoID 가 비워진다
            var res = repo.MoveLots([lot], null, "T");
            Assert.True(res.Ok); Assert.Equal(1, res.Moved);
            Assert.Equal(before - good, Cq()); Assert.Equal(pv.Source.After, Cq());
            Assert.True(LotWo() is DBNull or null); Assert.True(ResWo() is DBNull or null);
            Assert.Equal("NoSource", Assert.Single(repo.MoveLots([lot], null, "T").Errors).Code);   // WO 없는 LOT 을 또 풀 수 없다

            // 다시 같은 WO 로 붙이기 → 실적이 원래대로
            res = repo.MoveLots([lot], wo, "T");
            Assert.True(res.Ok);
            Assert.Equal(before, Cq());
            Assert.Equal(wo, LotWo()); Assert.Equal(wo, ResWo());
        }
        finally
        {
            Exec(f!, "UPDATE dbo.tbl_Lot SET WoID=@W WHERE LotID=@L; UPDATE dbo.PR_ProductionResult SET WoID=@W WHERE LotID=@L", ("@W", wo), ("@L", lotId));
            foreach (var sn in snaps) Restore(f!, sn);
        }
    }

    [SkippableFact]
    public void Moving_out_of_a_finished_wo_lowers_its_count_but_keeps_it_closed_and_target_must_be_open()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        // 끝난 WO 에 묶인 확정 LOT 과, 같은 공정·라인·품번의 열린 WO
        var pick = Scalar(f!, """
            SELECT TOP 1 CAST(l.LotID AS varchar(20)) + '|' + l.LotCode + '|' + CAST(l.WoID AS varchar(20)) + '|' + CAST(rs.RoutingLineID AS varchar(20)) + '|' + CAST(t.WoID AS varchar(20)) + '|' + CAST(rt.RoutingLineID AS varchar(20))
            FROM dbo.tbl_Lot l
            JOIN dbo.PP_WorkOrder ws ON ws.WoID = l.WoID AND ws.Status IN ('Completed','Stocked','Closed')
            JOIN dbo.PP_WorkOrderRouting rs ON rs.WoID = l.WoID AND rs.ProcessCode = l.ProcessCode AND rs.LineID = l.LineID AND COALESCE(rs.ItemNo, ws.ItemNo) = l.ItemNo
            JOIN dbo.PP_WorkOrderRouting rt ON rt.ProcessCode = l.ProcessCode AND rt.LineID = l.LineID AND rt.Status IN ('Released','In Progress') AND rt.WoID <> l.WoID
            JOIN dbo.PP_WorkOrder t ON t.WoID = rt.WoID AND t.Status NOT IN ('Completed','Stocked','Closed','Cancelled') AND COALESCE(rt.ItemNo, t.ItemNo) = l.ItemNo
            WHERE l.Status = 'CONFIRMED' AND rs.CompletedQty >= 1 AND t.OrderQty > rt.CompletedQty + 1
            """) as string;
        Skip.If(pick is null, "no finished-WO LOT with an open target");
        var q = pick!.Split('|'); var lot = q[1];
        var (lotId, src, srcStep, tgt, tgtStep) = (int.Parse(q[0]), int.Parse(q[2]), int.Parse(q[3]), int.Parse(q[4]), int.Parse(q[5]));
        string Txt(int step) => Convert.ToString(Scalar(f!, "SELECT CAST(r.CompletedQty AS varchar(30)) + '|' + r.Status + '|' + ISNULL(CAST(r.ActualEnd AS varchar(40)),'') + '|' + w.Status + '|' + ISNULL(CAST(w.CompletedQty AS varchar(30)),'') FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID=r.WoID WHERE r.RoutingLineID=@R", ("@R", step)));
        var (s0, t0) = (Txt(srcStep), Txt(tgtStep));
        var snaps = new[] { Snap(f!, "PP_WorkOrderRouting", "RoutingLineID", srcStep), Snap(f!, "PP_WorkOrderRouting", "RoutingLineID", tgtStep), Snap(f!, "PP_WorkOrder", "WoID", src), Snap(f!, "PP_WorkOrder", "WoID", tgt) };
        var srcCq = Convert.ToDecimal(Scalar(f!, "SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID=@R", ("@R", srcStep))!);
        var tgtCq = Convert.ToDecimal(Scalar(f!, "SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE RoutingLineID=@R", ("@R", tgtStep))!);
        var repo = new WorkOrderRepository(f!);
        try
        {
            var res = repo.MoveLots([lot], tgt, "T");
            Assert.True(res.Ok);
            // 끝난 WO: 수량만 1 줄고 상태·종료 시각은 그대로, 받는 WO: 1 늘고, 이동한 LOT 은 새 WO 소속
            var s1 = Txt(srcStep).Split('|'); var s0p = s0.Split('|');
            Assert.Equal(srcCq - 1, decimal.Parse(s1[0])); Assert.Equal(s0p[1], s1[1]); Assert.Equal(s0p[2], s1[2]); Assert.Equal(s0p[3], s1[3]);
            Assert.Equal(tgtCq + 1, decimal.Parse(Txt(tgtStep).Split('|')[0]));
            Assert.Equal(tgt, Scalar(f!, "SELECT WoID FROM dbo.tbl_Lot WHERE LotID=@L", ("@L", lotId)));
            // 끝난 WO 는 받는 쪽이 될 수 없다
            Assert.Equal("NoTargetStep", Assert.Single(repo.MoveLots([lot], src, "T").Errors).Code);
        }
        finally
        {
            Exec(f!, "UPDATE dbo.tbl_Lot SET WoID=@W WHERE LotID=@L; UPDATE dbo.PR_ProductionResult SET WoID=@W WHERE LotID=@L", ("@W", src), ("@L", lotId));
            foreach (var sn in snaps) Restore(f!, sn);
        }
    }

    [SkippableFact]
    public void Move_groups_follow_lot_number_sequence_exclude_the_source_wo_and_reject_other_items()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, "set AMES_TEST_CONN to run");
        var f = TryFactory(); Skip.If(f is null, "no dev db");
        var pick = Scalar(f!, """
            SELECT TOP 1 l.LotCode + '|' + CAST(l.WoID AS varchar(20)) + '|' + l.ProcessCode + '|' + l.LineID + '|' + l.ItemNo
            FROM dbo.tbl_Lot l WHERE l.Status = 'CONFIRMED' AND l.WoID IS NOT NULL AND l.LineID IS NOT NULL AND l.ItemNo IS NOT NULL
              AND (SELECT COUNT(*) FROM dbo.tbl_Lot x WHERE x.WoID = l.WoID AND x.ProcessCode = l.ProcessCode AND x.LineID = l.LineID AND x.ItemNo = l.ItemNo AND x.Status = 'CONFIRMED') >= 3
            ORDER BY l.LotID DESC
            """) as string;
        Skip.If(pick is null, "no WO with 3 confirmed LOTs in one group");
        var q = pick!.Split('|'); var (lot, wo, proc, line, item) = (q[0], int.Parse(q[1]), q[2], q[3], q[4]);
        var repo = new WorkOrderRepository(f!);

        // 묶음 = 작업지시·공정·라인·품번별 확정 LOT 수
        var g = Assert.Single(repo.ListLotMoveGroups([lot]), x => x.WoId == wo && x.ProcessCode == proc && x.LineId == line && x.ItemNo == item);
        Assert.True(g.LotCount >= 3);

        // 순번 — 번호순으로 수량만큼, 시작 LOT 을 주면 그 번호부터
        var all = repo.ListLotMoveSequence(wo, proc, line, item, null, 1000);
        Assert.Equal(g.LotCount, all.Count);
        Assert.Equal(all.OrderBy(c => c.Length).ThenBy(c => c, StringComparer.Ordinal).ToList(), all);
        Assert.Equal(all.Take(2), repo.ListLotMoveSequence(wo, proc, line, item, null, 2));
        Assert.Equal(all.Skip(1).Take(2), repo.ListLotMoveSequence(wo, proc, line, item, all[1], 2));

        // 받는 후보에서 원래 작업지시는 빠진다
        Assert.DoesNotContain(repo.ListLotMoveTargets(proc, line, item, wo), t => t.WoId == wo);

        // 품목이 다른 작업지시로는 이동 불가 — 아무것도 바뀌지 않는다
        var other = Scalar(f!, """
            SELECT TOP 1 w.WoID FROM dbo.PP_WorkOrder w
            WHERE w.Status NOT IN ('Completed','Stocked','Closed','Cancelled') AND w.WoID <> @W
              AND NOT EXISTS (SELECT 1 FROM dbo.PP_WorkOrderRouting r WHERE r.WoID = w.WoID AND r.ItemNo = @I) AND w.ItemNo <> @I
            """, ("@W", wo), ("@I", item));
        Skip.If(other is null, "no open WO of a different item");
        var res = repo.MoveLots([all[0]], (int)other!, "T");
        Assert.Equal("ItemMismatch", Assert.Single(res.Errors).Code);
        Assert.Equal(wo, Scalar(f!, "SELECT WoID FROM dbo.tbl_Lot WHERE LotCode=@L", ("@L", all[0])));
    }
}
