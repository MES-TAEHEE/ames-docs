using AMES.Data.Repositories;
using AMES.Data.Services.SapIf;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

/// <summary>SAP B1 송신(아웃박스 → EOS_IF) — 명세·페이로드·SQL 생성은 순수 함수, 처리 순서 게이트는 로컬 DB(AMES_TEST_CONN).</summary>
public class SapOutTests
{
    static SapOutSpec Spec(string id) => SapOutSpecs.Find(id)!;

    [Fact]
    public void Specs_cover_the_seven_outbound_interfaces_with_unique_ids_and_tables()
    {
        var ids = SapOutSpecs.All.Select(s => s.IfId).ToList();
        Assert.Equal(["IF-T01", "IF-T02", "IF-T03", "IF-T05", "IF-T06", "IF-T07", "IF-T09"], ids);
        Assert.Equal(ids.Count, SapOutSpecs.All.Select(s => s.HdrTable).Distinct().Count());
        Assert.All(SapOutSpecs.All, s => Assert.Contains(s.KeyCol, s.HdrCols));
        Assert.True(Spec("IF-T01").KeyWithAction);                 // EOS_IF UQ = (MES_WO_NO, IF_ACTION, CREATED_TS)
        Assert.All(SapOutSpecs.All.Where(s => s.IfId != "IF-T01"), s => Assert.False(s.KeyWithAction));
    }

    [Fact]
    public void Specs_never_write_control_columns()
    {
        string[] control = ["IF_SEQ", "IF_ACTION", "IF_STATUS", "IF_MSG", "MES_TS", "CREATED_TS", "PROC_TS", "B1_DOCNUM", "B1_DOCTYPE", "HDR_SEQ"];
        foreach (var s in SapOutSpecs.All)
        {
            Assert.All(s.HdrCols, c => Assert.DoesNotContain(c, control));
            Assert.DoesNotContain("B1_DOCENTRY", s.HdrCols);     // 웅진이 되쓰는 컬럼 — 헤더에는 쓰지 않는다(T05 라인의 PO 참조만 예외)
            Assert.All(s.LineCols ?? [], c => Assert.DoesNotContain(c, control));
        }
        Assert.Contains("B1_DOCENTRY", Spec("IF-T05").LineCols!);  // GRPO 라인은 PO(BaseEntry)를 가리킨다
    }

    const string Good = """
        {"hdr":{"MES_ISSUE_NO":"TST-IS-0001","MES_WO_NO":"TST-WO-0001","PostDate":"2026-11-20","Remarks":"한글 비고"},
         "lines":[{"CompItemNo":"D0103-NV010","Qty":100,"WhsCode":"W1MN","ExtraFlag":0},
                  {"CompItemNo":"D0102-NV000NNB","Qty":25.5,"WhsCode":"W1MN","ExtraFlag":1,"LineRemarks":"BOM 외"}]}
        """;

    [Fact]
    public void Payload_parses_types_dates_and_lines()
    {
        var p = SapOutPayload.Parse(Spec("IF-T02"), Good);
        Assert.Equal("TST-WO-0001", p.Hdr["MES_WO_NO"]);
        Assert.Equal(new DateTime(2026, 11, 20), p.Hdr["PostDate"]);       // Date 로 끝나는 컬럼은 날짜
        Assert.Equal("한글 비고", p.Hdr["Remarks"]);
        Assert.Equal(2, p.Lines.Count);
        Assert.Equal(25.5m, p.Lines[1]["Qty"]);
        Assert.Equal(1m, p.Lines[1]["ExtraFlag"]);
    }

    [Theory]
    [InlineData("""{"hdr":{"MES_ISSUE_NO":"X","IF_STATUS":"Y"}}""", "쓸 수 없는 컬럼")]               // 제어 컬럼 주입 금지
    [InlineData("""{"hdr":{"MES_ISSUE_NO":"X","B1_DOCENTRY":5}}""", "쓸 수 없는 컬럼")]               // 웅진 결과 컬럼 주입 금지
    [InlineData("""{"hdr":{"MES_WO_NO":"X"}}""", "MES_ISSUE_NO")]                                      // 키 없음
    [InlineData("""{"hdr":{"MES_ISSUE_NO":"  "}}""", "MES_ISSUE_NO")]                                  // 키 공백
    [InlineData("""{"lines":[]}""", "hdr")]                                                            // hdr 없음
    [InlineData("""{"hdr":{"MES_ISSUE_NO":"X","PostDate":"20/11/2026"}}""", "yyyy-MM-dd")]             // 날짜 형식
    [InlineData("""{"hdr":{"MES_ISSUE_NO":"X"},"lines":[{"Bogus":1}]}""", "쓸 수 없는 컬럼")]          // 라인 컬럼 화이트리스트
    public void Payload_rejects_bad_input(string json, string expectedFragment)
    {
        var ex = Assert.Throws<FormatException>(() => SapOutPayload.Parse(Spec("IF-T02"), json));
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public void Payload_without_line_table_rejects_lines()
    {
        var ex = Assert.Throws<FormatException>(() =>
            SapOutPayload.Parse(Spec("IF-T03"), """{"hdr":{"MES_RESULT_NO":"R1"},"lines":[{"ItemNo":"X"}]}"""));
        Assert.Contains("라인이 없는", ex.Message);
    }

    [Fact]
    public void Payload_roundtrips_through_json_with_dates_as_text_and_korean_intact()
    {
        var hdr = new Dictionary<string, object?> { ["MES_ISSUE_NO"] = "R-1", ["PostDate"] = new DateTime(2026, 11, 20), ["Remarks"] = "한글" };
        var line = new Dictionary<string, object?> { ["CompItemNo"] = "X", ["Qty"] = 3.5m };
        var json = SapOutPayload.ToJson(hdr, [line]);
        Assert.Contains("\"PostDate\":\"2026-11-20\"", json);
        Assert.Contains("한글", json);                                   // 유니코드 이스케이프 없이 저장(읽기 쉽게)
        var p = SapOutPayload.Parse(Spec("IF-T02"), json);
        Assert.Equal(new DateTime(2026, 11, 20), p.Hdr["PostDate"]);
        Assert.Equal(3.5m, p.Lines[0]["Qty"]);
    }

    [Fact]
    public void Header_insert_sets_control_columns_and_returns_the_new_seq()
    {
        var p = SapOutPayload.Parse(Spec("IF-T02"), Good);
        var (sql, ps) = SapOutSql.HeaderInsert(Spec("IF-T02"), "C", new DateTime(2026, 11, 20, 9, 0, 0), p.Hdr);
        Assert.StartsWith("INSERT dbo.IF_TX_PRODISSUE (IF_ACTION, IF_STATUS, MES_TS, MES_ISSUE_NO, MES_WO_NO, PostDate, Remarks)", sql);
        Assert.Contains("OUTPUT inserted.IF_SEQ VALUES (@act, 'N', @ts, @h0, @h1, @h2, @h3)", sql);
        Assert.Equal(6, ps.Count);                                      // @act @ts + 4 업무 컬럼
        Assert.Contains(ps, x => x.Column == "MES_TS" && x.Table == "IF_TX_PRODISSUE");
    }

    [Fact]
    public void Line_insert_links_to_the_header_seq_and_skips_absent_columns()
    {
        var p = SapOutPayload.Parse(Spec("IF-T02"), Good);
        var (sql, ps) = SapOutSql.LineInsert(Spec("IF-T02"), 77, p.Lines[0]);
        Assert.Equal("INSERT dbo.IF_TX_PRODISSUE_LINE (HDR_SEQ, CompItemNo, Qty, WhsCode, ExtraFlag) VALUES (@hdr, @l0, @l1, @l2, @l3)", sql);
        Assert.Equal(4, ps.Count);
    }

    [Fact]
    public void Exists_check_uses_action_only_for_production_orders()
    {
        Assert.Equal("SELECT TOP (1) IF_SEQ FROM dbo.IF_TX_PRODORDER WHERE MES_WO_NO = @key AND IF_ACTION = @act ORDER BY IF_SEQ DESC",
            SapOutSql.ExistsSql(Spec("IF-T01")));
        Assert.Equal("SELECT TOP (1) IF_SEQ FROM dbo.IF_TX_PRODRECEIPT WHERE MES_RESULT_NO = @key ORDER BY IF_SEQ DESC",
            SapOutSql.ExistsSql(Spec("IF-T03")));
    }

    [Fact]
    public void Gate_follows_the_spec_order_create_issue_receipt_close()
    {
        Assert.Contains("g.IfID = 'IF-T01' AND g.Action = 'C'", SapIfOutboxRepository.GateSql("IF-T02"));
        Assert.Contains("g.IfID = 'IF-T02'", SapIfOutboxRepository.GateSql("IF-T03"));
        Assert.Contains("x.Action <> 'L'", SapIfOutboxRepository.GateSql("IF-T01"));
        Assert.Equal("", SapIfOutboxRepository.GateSql("IF-T05"));     // 나머지는 앞 단계가 없다
    }

    // ── 처리 순서 게이트(로컬 DB, 커밋하는 시험 행을 끝에 지운다) ─────────────────────────────
    const string Skipped = "set AMES_TEST_CONN (local DB) to run — inserts and removes ITEST-SAPOUT outbox rows";
    const string Wo = "ITEST-SAPOUT-W1";

    static string Pay(string ifId) => ifId switch
    {
        "IF-T01" => $$"""{"hdr":{"MES_WO_NO":"{{Wo}}","ItemNo":"X","PlannedQty":1,"WhsCode":"W1MN","DueDate":"2026-11-30"},"lines":[]}""",
        "IF-T02" => $$"""{"hdr":{"MES_ISSUE_NO":"ITEST-SAPOUT-I1","MES_WO_NO":"{{Wo}}","PostDate":"2026-11-20"},"lines":[]}""",
        _ => $$"""{"hdr":{"MES_RESULT_NO":"ITEST-SAPOUT-R1","MES_WO_NO":"{{Wo}}","ItemNo":"X","CompleteQty":1,"RejectQty":0,"WhsCode":"W1MN","PostDate":"2026-11-20"},"lines":[]}""",
    };

    [SkippableFact]
    public void Outbox_releases_each_step_only_after_the_previous_one_succeeded_in_b1()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, Skipped);
        var f = TryFactory(); Skip.If(f is null, Skipped);
        var repo = new SapIfOutboxRepository(f!);
        void Clean() => Exec(f!, "DELETE dbo.SAP_IfOutbox WHERE MesKey LIKE 'ITEST-SAPOUT-%'");
        List<SapIfOutboxRepository.Row> Due(string ifId) => repo.PeekDue(ifId, 50).Where(r => r.MesKey.StartsWith("ITEST-SAPOUT-")).ToList();
        Clean();
        try
        {
            using (var c = f!.OpenConnection())
            {
                var t01 = SapIfOutboxRepository.Enqueue(c, null, "IF-T01", Wo, "C", Pay("IF-T01"));
                Assert.NotNull(t01);
                Assert.Null(SapIfOutboxRepository.Enqueue(c, null, "IF-T01", Wo, "C", Pay("IF-T01")));   // 같은 이벤트는 한 번만
                SapIfOutboxRepository.Enqueue(c, null, "IF-T02", "ITEST-SAPOUT-I1", "C", Pay("IF-T02"));
                SapIfOutboxRepository.Enqueue(c, null, "IF-T03", "ITEST-SAPOUT-R1", "C", Pay("IF-T03"));
                SapIfOutboxRepository.Enqueue(c, null, "IF-T01", Wo, "L", Pay("IF-T01"));
            }

            // 1) 처음엔 생성(C)만 나갈 수 있다 — 투입·실적·종료는 기다린다
            Assert.Equal(["C"], Due("IF-T01").Select(r => r.Action));
            Assert.Empty(Due("IF-T02"));
            Assert.Empty(Due("IF-T03"));

            // 2) 생성을 EOS_IF 에 썼어도(Y) 웅진이 B1 에서 끝내기(B1Status Y) 전에는 투입이 나가지 않는다
            var first = repo.ClaimDue("IF-T01", 10).Single(r => r.MesKey == Wo && r.Action == "C");
            Assert.Equal("P", first.Status);
            Assert.Empty(repo.ClaimDue("IF-T01", 10).Where(r => r.Action == "C"));                    // 같은 행을 두 번 잡지 못한다
            repo.MarkSent(first.OutboxID, 101);
            Assert.Empty(Due("IF-T02"));
            Assert.Single(repo.PendingResults("IF-T01").Where(x => x.OutboxID == first.OutboxID));   // 웅진 결과 대기

            // 3) 웅진이 Y 를 되쓰면 투입이 열린다
            repo.UpdateB1(first.OutboxID, "Y", 5001, 6001, null);
            var issue = Assert.Single(Due("IF-T02"));
            Assert.Empty(Due("IF-T03"));

            // 4) 투입이 B1 에서 끝나면 실적, 실적이 끝나면 종료(L)
            repo.MarkSent(issue.OutboxID, 102); repo.UpdateB1(issue.OutboxID, "Y", 5002, 6002, null);
            var receipt = Assert.Single(Due("IF-T03"));
            Assert.DoesNotContain(Due("IF-T01"), r => r.Action == "L");
            repo.MarkSent(receipt.OutboxID, 103); repo.UpdateB1(receipt.OutboxID, "Y", 5003, 6003, null);
            Assert.Contains(Due("IF-T01"), r => r.Action == "L");

            // 5) 실패하면 재시도 시각 전까지는 다시 나가지 않는다
            var close = Due("IF-T01").Single(r => r.Action == "L");
            repo.MarkFailed(close.OutboxID, 1, DateTime.Now.AddMinutes(30), "boom", final: false);
            Assert.DoesNotContain(Due("IF-T01"), r => r.Action == "L");
            repo.MarkFailed(close.OutboxID, 8, null, "boom", final: true);
            Assert.Equal("E", Scalar(f!, "SELECT Status FROM dbo.SAP_IfOutbox WHERE OutboxID = @I", ("@I", close.OutboxID)) as string);
        }
        finally { Clean(); }
    }

    [SkippableFact]
    public void Dry_run_note_is_written_once_and_the_row_stays_queued()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, Skipped);
        var f = TryFactory(); Skip.If(f is null, Skipped);
        var repo = new SapIfOutboxRepository(f!);
        Exec(f!, "DELETE dbo.SAP_IfOutbox WHERE MesKey LIKE 'ITEST-SAPOUT-%'");
        try
        {
            long id;
            using (var c = f!.OpenConnection()) id = SapIfOutboxRepository.Enqueue(c, null, "IF-T01", Wo, "C", Pay("IF-T01"))!.Value;
            Assert.True(repo.SetDryNote(id, "[DRYRUN] first"));
            Assert.False(repo.SetDryNote(id, "[DRYRUN] first"));       // 같은 메모는 다시 쓰지 않는다(5분마다 쓰기 방지)
            Assert.True(repo.SetDryNote(id, "[DRYRUN] changed"));
            Assert.Equal("N", Scalar(f!, "SELECT Status FROM dbo.SAP_IfOutbox WHERE OutboxID = @I", ("@I", id)) as string);
        }
        finally { Exec(f!, "DELETE dbo.SAP_IfOutbox WHERE MesKey LIKE 'ITEST-SAPOUT-%'"); }
    }

    [SkippableFact]
    public void Released_wo_enqueues_one_T01_create_row_with_a_parsable_payload()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, Skipped);
        var f = TryFactory(); Skip.If(f is null, Skipped);
        using var conn = f!.OpenConnection();
        using var tx = conn.BeginTransaction();          // 끝에 롤백 — 아무것도 남기지 않는다
        try
        {
            int woId;
            using (var ins = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, DueDate, RoutingType, Status, SourceType, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('ITEST-SAPOUT-WO1', 'ITEST-ITEM', 12.5, 12.5, '2026-12-20', 'A', 'Released', 'MANUAL', 'ITEST', SYSDATETIME());
                """, conn, tx))
                woId = (int)ins.ExecuteScalar()!;

            Assert.True(SapOutHooks.WoReleased(conn, tx, woId));
            Assert.False(SapOutHooks.WoReleased(conn, tx, woId));     // 같은 WO 는 한 번만

            using var q = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT IfID, Action, Status, PayloadJson FROM dbo.SAP_IfOutbox WHERE MesKey = 'ITEST-SAPOUT-WO1';", conn, tx);
            using var r = q.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("IF-T01", r.GetString(0));
            Assert.Equal("C", r.GetString(1));
            Assert.Equal("N", r.GetString(2));
            var p = SapOutPayload.Parse(SapOutSpecs.Find("IF-T01")!, r.GetString(3));
            Assert.Equal("ITEST-ITEM", p.Hdr["ItemNo"]);
            Assert.Equal(12.5m, Convert.ToDecimal(p.Hdr["PlannedQty"]));
            Assert.Equal(SapOutHooks.DefaultWhsCode, p.Hdr["WhsCode"]);
            Assert.Equal(new DateTime(2026, 12, 20), p.Hdr["DueDate"]);
            Assert.False(r.Read());
        }
        finally { tx.Rollback(); }
    }

    [SkippableFact]
    public void Collector_turns_confirmed_backflush_results_and_wo_end_into_outbox_rows_once()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, Skipped);
        var f = TryFactory(); Skip.If(f is null, Skipped);
        const string Wo = "ITEST-COL-WO";
        void Clean()
        {
            Exec(f!, "DELETE dbo.SAP_IfOutbox WHERE JSON_VALUE(PayloadJson, '$.hdr.MES_WO_NO') = 'ITEST-COL-WO' OR MesKey = 'ITEST-COL-WO'");
            Exec(f!, "DELETE r FROM dbo.PR_ProductionResult r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.WoNumber = 'ITEST-COL-WO'");
            Exec(f!, "DELETE t FROM dbo.WH_InventoryTransaction t JOIN dbo.tbl_Lot l ON l.LotID = t.SourceID JOIN dbo.PP_WorkOrder w ON w.WoID = l.WoID WHERE t.SourceType = 'BACKFLUSH' AND w.WoNumber = 'ITEST-COL-WO'");
            Exec(f!, "DELETE l FROM dbo.tbl_Lot l JOIN dbo.PP_WorkOrder w ON w.WoID = l.WoID WHERE w.WoNumber = 'ITEST-COL-WO'");
            Exec(f!, "DELETE r FROM dbo.PP_WorkOrderRouting r JOIN dbo.PP_WorkOrder w ON w.WoID = r.WoID WHERE w.WoNumber = 'ITEST-COL-WO'");
            Exec(f!, "DELETE dbo.PP_WorkOrder WHERE WoNumber = 'ITEST-COL-WO'");
        }
        Clean();
        try
        {
            var woId = Convert.ToInt32(Scalar(f!, """
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, OpenQty, DueDate, RoutingType, Status, SourceType, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('ITEST-COL-WO', 'ITEST-ITEM', 10, 10, '2026-12-20', 'A', 'Released', 'MANUAL', 'ITEST', SYSDATETIME());
                """));
            Exec(f!, "INSERT dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy, CreatedTS) VALUES (@W, 1, 'IMG', 'ITEST-LINE', 'Released', 0, 'ITEST', SYSDATETIME())", ("@W", woId));
            var lotId = Convert.ToInt32(Scalar(f!, """
                INSERT INTO dbo.tbl_Lot (LotCode, ItemNo, WoID, CreatedBy, CreatedTS) OUTPUT INSERTED.LotID
                VALUES ('ITEST-COL-LOT', 'ITEST-ITEM', @W, 'ITEST', SYSDATETIME());
                """, ("@W", woId)));
            Exec(f!, """
                INSERT dbo.WH_InventoryTransaction (TransactionType, SourceType, SourceID, PartNo, LotNo, QtyChange, TransactionTime, CreatedBy, CreatedTS)
                VALUES ('OUT', 'BACKFLUSH', @L, 'ITEST-COMP', 'X', -2, DATEADD(MINUTE, -10, SYSDATETIME()), 'ITEST', SYSDATETIME());
                INSERT dbo.WH_InventoryTransaction (TransactionType, SourceType, SourceID, PartNo, LotNo, QtyChange, TransactionTime, CreatedBy, CreatedTS)
                VALUES ('OUT', 'BACKFLUSH', @L, 'ITEST-COMP', 'X', -1, DATEADD(MINUTE, -10, SYSDATETIME()), 'ITEST', SYSDATETIME());
                INSERT dbo.PR_ProductionResult (WoID, LotID, ProcessCode, GoodQty, ProdDate, ShiftCode, LineID, CreatedBy, CreatedTS)
                VALUES (@W, @L, 'IMG', 3, '2026-11-20', 'A', 'ITEST-LINE', 'ITEST', DATEADD(MINUTE, -10, SYSDATETIME()));
                """, ("@L", lotId), ("@W", woId));
            // 생성(C)이 아웃박스에 있어야 수집 대상
            using (var c = f!.OpenConnection())
                SapIfOutboxRepository.Enqueue(c, null, "IF-T01", Wo, "C", SapOutPayload.ToJson(new Dictionary<string, object?>
                { ["MES_WO_NO"] = Wo, ["ItemNo"] = "ITEST-ITEM", ["PlannedQty"] = 10m, ["WhsCode"] = "W1MN", ["DueDate"] = new DateTime(2026, 12, 20) }));

            var col = new SapOutCollector(f!, new SapIfLogRepository(f!));
            var r1 = col.Collect();
            Assert.Null(r1.Error);
            Assert.Equal(1, r1.Issues);
            Assert.Equal(1, r1.Receipts);
            Assert.Equal(0, r1.Closes);

            // 투입은 같은 구성품을 합친다(2+1=3), 실적은 3
            var issue = Scalar(f!, "SELECT PayloadJson FROM dbo.SAP_IfOutbox WHERE IfID = 'IF-T02' AND JSON_VALUE(PayloadJson, '$.hdr.MES_WO_NO') = 'ITEST-COL-WO'") as string;
            var pi = SapOutPayload.Parse(SapOutSpecs.Find("IF-T02")!, issue!);
            Assert.Single(pi.Lines);
            Assert.Equal(3m, Convert.ToDecimal(pi.Lines[0]["Qty"]));
            var rcpt = Scalar(f!, "SELECT PayloadJson FROM dbo.SAP_IfOutbox WHERE IfID = 'IF-T03' AND JSON_VALUE(PayloadJson, '$.hdr.MES_WO_NO') = 'ITEST-COL-WO'") as string;
            var pr = SapOutPayload.Parse(SapOutSpecs.Find("IF-T03")!, rcpt!);
            Assert.Equal(3m, Convert.ToDecimal(pr.Hdr["CompleteQty"]));

            Assert.Equal(0, col.Collect().Total);                      // 다시 훑어도 중복되지 않는다

            Exec(f!, "UPDATE dbo.PP_WorkOrder SET Status = 'Completed' WHERE WoID = @W", ("@W", woId));
            Assert.Equal(1, col.Collect().Closes);
            Assert.Equal(1, Convert.ToInt32(Scalar(f!, "SELECT COUNT(*) FROM dbo.SAP_IfOutbox WHERE IfID = 'IF-T01' AND Action = 'L' AND MesKey = 'ITEST-COL-WO'")));
            Assert.Equal(0, col.Collect().Total);                      // 종료된 WO 는 더 수집하지 않는다
        }
        finally { Clean(); }
    }
}
