using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// 로컬 AMES_DEV(Docker) 통합 테스트. DB 미기동 시 각 테스트가 skip 된다.
/// Task 1 마이그레이션 + 시드가 선행되어야 한다.
/// </summary>
public class InjLotRepositoryTests
{
    // DB 는 AMES_TEST_CONN 으로만 지정한다(없으면 건너뜀).
    static readonly string Conn =
        Environment.GetEnvironmentVariable("AMES_TEST_CONN")
        ?? "";   // 접속 정보는 소스에 두지 않는다 — 없으면 DB 테스트는 건너뛴다

    static AmesConnectionFactory? TryFactory()
    {
        try
        {
            var f = new AmesConnectionFactory(Conn);
            using var c = f.OpenConnection();
            return f;
        }
        catch { return null; }
    }

    static MoldItemDto Lh() => new()
    { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "83335-P8000RBQ", MoldId = null };

    static void Cleanup(AmesConnectionFactory f, int lotId)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
            DELETE FROM dbo.PR_RobotInspection WHERE LotID = @L;
            DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
            DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
            """, conn);
        cmd.Parameters.AddWithValue("@L", lotId);
        cmd.ExecuteNonQuery();
    }

    [SkippableFact]
    public void GetMoldItems_returns_seeded_two_cavity_map()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        // 금형은 시험이 만든다 — 개발 DB 의 금형 시드(LQ2DTMD 등)는 마스터 재시드 때 바뀐다
        void Clean() => AmesDevDb.Exec(f, """
            DELETE FROM dbo.MD_MoldItem WHERE MoldID = 'ITEST-GMI';
            DELETE FROM dbo.MD_Mold     WHERE MoldID = 'ITEST-GMI';
            """);
        Clean();
        try
        {
            AmesDevDb.Exec(f, """
                INSERT INTO dbo.MD_Mold (MoldID, MoldName, Status, CavityCount, CreatedBy)
                VALUES ('ITEST-GMI', N'ITEST mold items', 'AVAILABLE', 2, 'ITEST');   -- MoldCodeClean = ITESTGMI(계산 열)
                INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, CavityCount, MoldCategory, ActiveFlag, CreatedBy)
                VALUES ('ITEST-GMI', 'ITEST-GMI-RH', 'CBK', 2, 'RH', 2, 'INJECTION', 1, 'ITEST'),
                       ('ITEST-GMI', 'ITEST-GMI-LH', 'CBK', 1, 'LH', 2, 'INJECTION', 1, 'ITEST'),
                       ('ITEST-GMI', 'ITEST-GMI-XX', 'NNB', 1, 'LH', 2, 'INJECTION', 1, 'ITEST');
                """);
            var map = new InjLotRepository(f).GetMoldItems("ITESTGMI", "CBK");
            Assert.Equal(2, map.Count);
            Assert.Equal(("LH", "ITEST-GMI-LH"), (map[0].CavityPos, map[0].ItemNo));
            Assert.Equal(("RH", "ITEST-GMI-RH"), (map[1].CavityPos, map[1].ItemNo));
        }
        finally { Clean(); }
    }

    [SkippableFact]
    public void CreateRawLot_then_appears_in_unconfirmed_list()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 12345);
        try
        {
            Assert.True(lotId > 0);
            var list = repo.GetUnconfirmed("LINE-INJ-01");
            Assert.Contains(list, x => x.LotId == lotId && x.ConfirmStatus == "RAW" && x.LotCode == lotCode);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void Cavity_one_shot_accumulates_the_mnt_mold_shot_counter_once_per_shot()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var ids = new List<int>();
        void Clean() => AmesDevDb.Exec(f!, """
            DELETE FROM dbo.MNT_MoldShotCount WHERE MoldID = 'ITEST-MSC';
            DELETE FROM dbo.PR_ShotCount      WHERE MoldID = 'ITEST-MSC';
            DELETE FROM dbo.MD_Mold           WHERE MoldID = 'ITEST-MSC';
            """);
        Clean();
        try
        {
            AmesDevDb.Exec(f, """
                INSERT INTO dbo.MD_Mold (MoldID, MoldName, Status, CavityCount, CurrentShots, CumulativeShots, CreatedBy)
                VALUES ('ITEST-MSC', N'ITEST shot counter', 'AVAILABLE', 2, 40, 1000, 'ITEST');
                """);
            var repo = new InjLotRepository(f);
            var lh = new MoldItemDto { MoldCode = "ITESTMSC", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "83335-P8000RBQ", MoldId = "ITEST-MSC" };
            var rh = new MoldItemDto { MoldCode = "ITESTMSC", ColorCode = "CBK", CavityNo = 2, CavityPos = "RH", ItemNo = "83335-P8000RBQ", MoldId = "ITEST-MSC" };
            ids.Add(repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", lh, 1).LotId);   // 첫 샷: MD_Mold 값(41·1001)으로 시작
            ids.Add(repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", rh, 1).LotId);   // 같은 샷의 2캐비티 — 세지 않음
            ids.Add(repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", lh, 2).LotId);
            Assert.Equal("42|1002|INJ-01-01-1800|1", (string?)AmesDevDb.Scalar(f!, """
                SELECT CONCAT(MAX(CurrentShots), '|', MAX(LifetimeShots), '|', MAX(MountedEquipID), '|', COUNT(*))
                FROM dbo.MNT_MoldShotCount WHERE MoldID = 'ITEST-MSC'
                """));
        }
        finally { foreach (var id in ids) Cleanup(f!, id); Clean(); }
    }

    [SkippableFact]
    public void IncrementPrintedCount_accumulates_and_shows_in_view()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 12347);
        try
        {
            Assert.Equal(1, repo.IncrementPrintedCount(lotId, "E-ITEST"));   // 디스패처 자동 발행
            Assert.Equal(2, repo.IncrementPrintedCount(lotId, "E-ITEST"));   // 재출력 버튼
            Assert.Equal(2, repo.GetByLotCode(lotCode)!.PrintedCount);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void DecrementPrintedCount_reverts_the_increment_and_never_goes_below_zero()
    {
        // 출력 실패 시 선행 카운트 되돌리기 — 0 아래로는 내려가지 않는다(OUTPUT 이 없으면 0 으로 읽힌다).
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 12348);
        try
        {
            Assert.Equal(1, repo.IncrementPrintedCount(lotId, "E-ITEST"));
            Assert.Equal(0, repo.DecrementPrintedCount(lotId, "E-ITEST"));
            Assert.Equal(0, repo.DecrementPrintedCount(lotId, "E-ITEST"));
            Assert.Equal(0, repo.GetByLotCode(lotCode)!.PrintedCount);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void MarkNgBlocked_then_confirm_is_rejected()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 12346);
        try
        {
            repo.SaveInspection(lotId, "INJ-01-01-1800", "LH", "OK", "OK", "OK", "NG", overallNg: true);
            repo.MarkNgBlocked(lotId);
            var (outcome, _, _, _) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", "test-op", null, "E-TEST");
            Assert.Equal(InjConfirmOutcome.NgBlocked, outcome);
        }
        finally { Cleanup(f, lotId); }
    }

    [SkippableFact]
    public void Confirm_unknown_lot_returns_notfound()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (outcome, _, _, _) = repo.ConfirmByLotCode("L-NO-SUCH-LOT", "LINE-INJ-01", "test-op", null, "E-TEST");
        Assert.Equal(InjConfirmOutcome.NotFound, outcome);
    }

    [SkippableFact]
    public void Confirm_raw_lot_creates_result_and_bumps_wo()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        int woId = 0, lotId = 0;
        try
        {
            // 테스트 전용 WO 생성
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('WO-ITEST-CONFIRM', '83335-P8000RBQ', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
                """, conn))
                woId = (int)cmd.ExecuteScalar()!;

            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
                VALUES (@W, 1, 'INJ', 'LINE-INJ-01', 'In Progress', 0, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }

            string lotCode;
            (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 99001);

            // woId 는 더 이상 전달하지 않는다 — 리포지토리가 LOT 품번으로 WO 를 찾아야 한다.
            var (outcome, resultId, itemNo, confirmedWoId) = repo.ConfirmByLotCode(
                lotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");

            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.True(resultId > 0);
            Assert.Equal("83335-P8000RBQ", itemNo);
            Assert.Equal(woId, confirmedWoId);   // 품번 매칭으로 테스트 WO 가 선택됐는지

            // 실적/상태/수량 검증
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE ResultID = @R AND LotID = @L AND GoodQty = 1) AS ResultOk,
                  (SELECT ConfirmStatus FROM dbo.PR_InjLot WHERE LotID = @L) AS Status,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID = @W AND StepSeq = 1) AS StepCompleted,
                  (SELECT Status FROM dbo.tbl_Lot WHERE LotID = @L) AS LotStatus;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@R", resultId);
                cmd.Parameters.AddWithValue("@L", lotId);
                cmd.Parameters.AddWithValue("@W", woId);
                using var rdr = cmd.ExecuteReader();
                Assert.True(rdr.Read());
                Assert.Equal(1, (int)rdr["ResultOk"]);
                Assert.Equal("CONFIRMED", (string)rdr["Status"]);
                Assert.Equal(1m, (decimal)rdr["Completed"]);
                Assert.Equal(1m, (decimal)rdr["StepCompleted"]);
                Assert.Equal("CONFIRMED", (string)rdr["LotStatus"]);
            }

            // 중복 스캔 거부
            var (again, _, _, _) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");
            Assert.Equal(InjConfirmOutcome.AlreadyConfirmed, again);
        }
        finally
        {
            if (f is not null)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                    DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;
                    DELETE FROM dbo.PR_ProductionResult WHERE LotID = @L;
                    DELETE FROM dbo.PR_RobotInspection WHERE LotID = @L;
                    DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
                    DELETE FROM dbo.tbl_Lot WHERE LotID = @L;
                    DELETE FROM dbo.PP_WorkOrder WHERE WoID = @W;
                    """, conn);
                cmd.Parameters.AddWithValue("@L", lotId);
                cmd.Parameters.AddWithValue("@W", woId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>열린 WO 가 없어도 생산은 막히지 않는다(2026-10-01 사용자 결정) — 실적·LOT 은 WoID NULL 로 남고 단계 반영만 없다.</summary>
    [SkippableFact]
    public void Confirm_without_open_wo_records_result_with_no_wo()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        const string item = "ITEST-NOWO-INJ";
        var repo = new InjLotRepository(f!);
        int lotId = 0;
        try
        {
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @I)
                    INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ActiveFlag, CreatedBy) VALUES (@I, N'CORE-ITEST no wo', 'SUB', 1, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@I", item); cmd.ExecuteNonQuery(); }

            var lot = repo.CreateManualRawLots("LINE-INJ-01", item, null, 1, "E-ITEST")[0];
            lotId = lot.LotId;

            var (outcome, resultId, itemNo, woId) = repo.ConfirmByLotCode(lot.LotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");

            Assert.Equal((InjConfirmOutcome.Confirmed, item, 0), (outcome, itemNo, woId));
            Assert.True(resultId > 0);
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT (SELECT WoID FROM dbo.PR_ProductionResult WHERE ResultID = @R) AS ResultWo,
                       (SELECT WoID FROM dbo.tbl_Lot WHERE LotID = @L) AS LotWo,
                       (SELECT ConfirmStatus FROM dbo.PR_InjLot WHERE LotID = @L) AS Status;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@R", resultId); cmd.Parameters.AddWithValue("@L", lotId);
                using var rdr = cmd.ExecuteReader(); Assert.True(rdr.Read());
                Assert.Equal(DBNull.Value, rdr["ResultWo"]);
                Assert.Equal(DBNull.Value, rdr["LotWo"]);
                Assert.Equal("CONFIRMED", (string)rdr["Status"]);
            }

            // 확정 후 불량 등록은 WoID 없는 실적을 역분개한다 — 단계가 없어도 예외 없이 등록돼야 한다
            var ng = repo.RegisterDefect(lot.LotCode, "LINE-INJ-01", "INJ-D01", "itest-op", null, "E-ITEST");
            Assert.Equal(DefectRegisterOutcome.Registered, ng.Outcome);
        }
        finally
        {
            if (f is not null)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                    DELETE FROM dbo.PR_DefectDetail     WHERE LotID = @L;
                    DELETE FROM dbo.PR_ProductionResult WHERE LotID = @L;
                    DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
                    DELETE FROM dbo.tbl_Lot   WHERE LotID = @L;
                    DELETE FROM dbo.MD_Item   WHERE ItemNo = @I AND CreatedBy = 'ITEST';
                    """, conn);
                cmd.Parameters.AddWithValue("@L", lotId); cmd.Parameters.AddWithValue("@I", item);
                cmd.ExecuteNonQuery();
            }
        }
    }

    [SkippableFact]
    public void Late_robot_ng_after_confirm_warns_without_code_and_registers_defect_with_code()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        const string item = "ITEST-LATENG-INJ";
        var repo = new InjLotRepository(f!);
        int lotId = 0;
        try
        {
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @I)
                    INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, ActiveFlag, CreatedBy) VALUES (@I, N'CORE-ITEST late ng', 'SUB', 1, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@I", item); cmd.ExecuteNonQuery(); }

            var lot = repo.CreateManualRawLots("LINE-INJ-01", item, null, 1, "E-ITEST")[0];
            lotId = lot.LotId;
            Assert.Equal(InjConfirmOutcome.Confirmed, repo.ConfirmByLotCode(lot.LotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST").Outcome);

            var warn = repo.MarkNgBlocked(lotId, null);
            Assert.Equal(InjLotRepository.NgMarkOutcome.LateAfterConfirm, warn.Outcome);
            Assert.Equal("CONFIRMED", repo.GetByLotCode(lot.LotCode)!.ConfirmStatus);

            var reg = repo.MarkNgBlocked(lotId, "INJ-D02");
            Assert.Equal(InjLotRepository.NgMarkOutcome.LateRegistered, reg.Outcome);
            Assert.Equal("DEFECT", repo.GetByLotCode(lot.LotCode)!.ConfirmStatus);

            // 이미 DEFECT 면 다시 등록하지 않는다
            Assert.Equal(InjLotRepository.NgMarkOutcome.Ignored, repo.MarkNgBlocked(lotId, "INJ-D02").Outcome);
        }
        finally
        {
            if (f is not null)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                    DELETE FROM dbo.PR_DefectDetail     WHERE LotID = @L;
                    DELETE FROM dbo.PR_ProductionResult WHERE LotID = @L;
                    DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
                    DELETE FROM dbo.tbl_Lot   WHERE LotID = @L;
                    DELETE FROM dbo.MD_Item   WHERE ItemNo = @I AND CreatedBy = 'ITEST';
                    """, conn);
                cmd.Parameters.AddWithValue("@L", lotId); cmd.Parameters.AddWithValue("@I", item);
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>
    /// WO 품번(완제품) ≠ LOT 품번(코어). INJ 단계 행의 ItemNo 가 코어면 코어 LOT 스캔이 그 WO 로 확정된다.
    /// INJ 는 마지막 라인 단계가 아니라 헤더 CompletedQty 는 그대로다.
    /// </summary>
    [SkippableFact]
    public void Confirm_core_lot_resolves_wo_whose_inj_step_makes_the_core()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);
        const string fg = "ITEST-INJ-FG";
        int woId = 0, lotId = 0;
        try
        {
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.MD_Item WHERE ItemNo = @Fg)
                    INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, RoutingType, ActiveFlag, CreatedBy)
                    VALUES (@Fg, N'ITEST inj fg', 'ASSY', 'A', 1, 'ITEST');
                DECLARE @W TABLE (WoID int);
                -- Priority 1 + In Progress: 같은 코어를 쓰는 다른 테스트 잔재 WO 보다 먼저 잡히게 한다
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, Priority, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID INTO @W
                VALUES ('WO-ITEST-CORE', @Fg, 100, 0, 'In Progress', 1, 'ITEST', SYSDATETIME());
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, ItemNo, Status, CompletedQty, CreatedBy)
                SELECT WoID, 1, 'INJ', 'LINE-INJ-01', '83335-P8000RBQ', 'In Progress', 0, 'ITEST' FROM @W
                UNION ALL
                SELECT WoID, 2, 'IMG', 'LINE-IMG-01', NULL, 'Released', 0, 'ITEST' FROM @W;
                SELECT WoID FROM @W;
                """, conn))
            { cmd.Parameters.AddWithValue("@Fg", fg); woId = (int)cmd.ExecuteScalar()!; }

            string lotCode;
            (lotId, lotCode) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 99002);   // LOT 품번 = 83335-P8000RBQ(코어 역할)

            var (outcome, resultId, itemNo, confirmedWoId) = repo.ConfirmByLotCode(lotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");

            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.Equal("83335-P8000RBQ", itemNo);
            Assert.Equal(woId, confirmedWoId);
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT (SELECT CompletedQty FROM dbo.PP_WorkOrderRouting WHERE WoID=@W AND StepSeq=1) AS StepDone,
                       (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID=@W) AS HeadDone,
                       (SELECT WoID FROM dbo.tbl_Lot WHERE LotID=@L) AS LotWo;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@W", woId); cmd.Parameters.AddWithValue("@L", lotId);
                using var rdr = cmd.ExecuteReader(); rdr.Read();
                Assert.Equal(1m, (decimal)rdr["StepDone"]);
                Assert.Equal(0m, (decimal)rdr["HeadDone"]);
                Assert.Equal(woId, (int)rdr["LotWo"]);
            }
        }
        finally
        {
            using var conn = f!.OpenConnection();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                DELETE FROM dbo.PR_ProductionResult WHERE WoID = @W;
                DELETE FROM dbo.PR_RobotInspection  WHERE LotID = @L;
                DELETE FROM dbo.PR_InjLot WHERE LotID = @L;
                DELETE FROM dbo.tbl_Lot   WHERE LotID = @L;
                DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;
                DELETE FROM dbo.PP_WorkOrder WHERE WoID = @W;
                DELETE FROM dbo.MD_Item WHERE ItemNo = @Fg AND CreatedBy = 'ITEST';
                """, conn);
            cmd.Parameters.AddWithValue("@W", woId); cmd.Parameters.AddWithValue("@L", lotId); cmd.Parameters.AddWithValue("@Fg", fg);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 수동 발행은 에이전트와 같은 원천 LOT(RAW) 만 만든다 — 1 LOT = 1 PCS, 실적/WO 수량은 건드리지 않고
    /// 금형 타수도 움직이지 않는다. 확정은 오직 스캔(ConfirmByLotCode) 경유.
    /// </summary>
    [SkippableFact]
    public void CreateManualRawLots_issues_raw_lots_only_and_confirms_by_scan()
    {
        var f = TryFactory();
        Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f!);

        string? moldId;
        using (var conn = f!.OpenConnection())
        using (var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT TOP 1 MoldID FROM dbo.MD_Mold WHERE MoldCodeClean = 'LQ2DTMD';", conn))
            moldId = cmd.ExecuteScalar() as string;
        Skip.If(moldId is null, "seed_inj_demo not applied");

        // CurrentShots 는 INT, CumulativeShots 는 BIGINT — 둘 다 long 으로 읽는다.
        (long Cur, long Cum) ReadShots()
        {
            using var conn = f!.OpenConnection();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                "SELECT ISNULL(CurrentShots,0) AS C, ISNULL(CumulativeShots,0) AS M FROM dbo.MD_Mold WHERE MoldID = @M;", conn);
            cmd.Parameters.AddWithValue("@M", moldId);
            using var rdr = cmd.ExecuteReader();
            rdr.Read();
            return (Convert.ToInt64(rdr["C"]), Convert.ToInt64(rdr["M"]));
        }

        const int qty = 3;
        var woId = 0;
        var lotIds = new List<int>();
        try
        {
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrder (WoNumber, ItemNo, OrderQty, CompletedQty, Status, CreatedBy, CreatedTS)
                OUTPUT INSERTED.WoID
                VALUES ('WO-ITEST-MANUAL', '83335-P8000RBQ', 100, 0, 'In Progress', 'ITEST', SYSDATETIME());
                """, conn))
                woId = (int)cmd.ExecuteScalar()!;

            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                INSERT INTO dbo.PP_WorkOrderRouting (WoID, StepSeq, ProcessCode, LineID, Status, CompletedQty, CreatedBy)
                VALUES (@W, 1, 'INJ', 'LINE-INJ-01', 'In Progress', 0, 'ITEST');
                """, conn))
            { cmd.Parameters.AddWithValue("@W", woId); cmd.ExecuteNonQuery(); }

            var shotsBefore = ReadShots();

            var lots = repo.CreateManualRawLots("LINE-INJ-01", "83335-P8000RBQ", moldId, qty, "E-ITEST");
            lotIds.AddRange(lots.Select(l => l.LotId));

            Assert.Equal(qty, lots.Count);
            Assert.Equal(qty, lots.Select(l => l.LotCode).Distinct().Count());   // LotCode 중복 없음
            Assert.All(lots, l => Assert.Equal("RAW", l.ConfirmStatus));
            Assert.All(lots, l => Assert.Equal("LH", l.CavityPos));              // 품번 → 캐비티 매핑
            Assert.All(lots, l => Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", l.LotCode!));

            // 발행 직후: 원천 LOT 만 있고 실적·WO 수량은 그대로여야 한다.
            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM dbo.tbl_Lot
                    WHERE LotID IN (SELECT value FROM STRING_SPLIT(@Ids, ','))
                      AND BatchSize = 1 AND RemainingQty = 1 AND WoID IS NULL
                      AND Status = 'RAW' AND QualityFlag = 'PENDING' AND ProcessCode = 'INJ') AS Lots,
                  (SELECT COUNT(*) FROM dbo.PR_InjLot
                    WHERE LotID IN (SELECT value FROM STRING_SPLIT(@Ids, ','))
                      AND ConfirmStatus = 'RAW' AND CreatedBy = 'E-ITEST'
                      AND MachineShotCount IS NULL AND ConfirmedAt IS NULL AND CavityPos = 'LH') AS InjLots,
                  (SELECT COUNT(*) FROM dbo.PR_ProductionResult
                    WHERE LotID IN (SELECT value FROM STRING_SPLIT(@Ids, ','))) AS Results,
                  (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@Ids", string.Join(",", lotIds));
                cmd.Parameters.AddWithValue("@W", woId);
                using var rdr = cmd.ExecuteReader();
                Assert.True(rdr.Read());
                Assert.Equal(qty, (int)rdr["Lots"]);
                Assert.Equal(qty, (int)rdr["InjLots"]);
                Assert.Equal(0, (int)rdr["Results"]);        // 아직 실적 아님
                Assert.Equal(0m, (decimal)rdr["Completed"]); // WO 수량도 그대로
            }

            Assert.Equal(shotsBefore, ReadShots());   // 타수는 PLC 샷카운터 전용

            // 발행분은 미확정 목록에 뜬다.
            var unconfirmed = repo.GetUnconfirmed("LINE-INJ-01");
            Assert.All(lots, l => Assert.Contains(unconfirmed, x => x.LotId == l.LotId));

            // 스캔해야 비로소 실적이 된다.
            var (outcome, resultId, _, confirmedWoId) = repo.ConfirmByLotCode(
                lots[0].LotCode, "LINE-INJ-01", "itest-op", null, "E-ITEST");
            Assert.Equal(InjConfirmOutcome.Confirmed, outcome);
            Assert.True(resultId > 0);
            Assert.Equal(woId, confirmedWoId);

            using (var conn = f!.OpenConnection())
            using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT (SELECT Status FROM dbo.tbl_Lot WHERE LotID = @L) AS LotStatus,
                       (SELECT WoID   FROM dbo.tbl_Lot WHERE LotID = @L) AS LotWo,
                       (SELECT COUNT(*) FROM dbo.PR_ProductionResult WHERE LotID = @L AND GoodQty = 1) AS Results,
                       (SELECT CompletedQty FROM dbo.PP_WorkOrder WHERE WoID = @W) AS Completed;
                """, conn))
            {
                cmd.Parameters.AddWithValue("@L", lots[0].LotId);
                cmd.Parameters.AddWithValue("@W", woId);
                using var rdr = cmd.ExecuteReader();
                Assert.True(rdr.Read());
                Assert.Equal("CONFIRMED", (string)rdr["LotStatus"]);
                Assert.Equal(woId, (int)rdr["LotWo"]);       // 확정 시점에 WO 가 붙는다
                Assert.Equal(1, (int)rdr["Results"]);
                Assert.Equal(1m, (decimal)rdr["Completed"]); // 스캔한 1건만 반영
            }

            Assert.Equal(shotsBefore, ReadShots());   // 스캔 확정도 타수를 올리지 않는다(에이전트 샷 전용)
        }
        finally
        {
            // 발행 직후 LOT 은 WoID 가 NULL 이라 WO 기준만으로는 지워지지 않는다 — LotID 로도 지운다.
            if (f is not null && woId > 0)
            {
                using var conn = f.OpenConnection();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand("""
                    DECLARE @L TABLE (LotID INT);
                    INSERT INTO @L SELECT CAST(value AS INT) FROM STRING_SPLIT(@Ids, ',') WHERE value <> '';
                    INSERT INTO @L SELECT LotID FROM dbo.tbl_Lot
                      WHERE WoID = @W AND LotID NOT IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.PP_WorkOrderRouting WHERE WoID = @W;
                    DELETE FROM dbo.PR_ProductionResult WHERE WoID = @W OR LotID IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.PR_InjLot          WHERE LotID IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.tbl_Lot            WHERE LotID IN (SELECT LotID FROM @L);
                    DELETE FROM dbo.PP_WorkOrder       WHERE WoID = @W;
                    """, conn);
                cmd.Parameters.AddWithValue("@Ids", string.Join(",", lotIds));
                cmd.Parameters.AddWithValue("@W", woId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    [SkippableFact]
    public void NextLotNo_increments_within_header_and_rolls_new_header()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();

        var d1 = new DateTime(2026, 9, 1);
        var a = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1);
        var b = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1);
        Assert.Equal(9, a.Length);
        Assert.Equal(a[..5], b[..5]);
        Assert.Equal(int.Parse(a[5..]) + 1, int.Parse(b[5..]));

        var c = AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-INJ-01", d1.AddDays(1));
        Assert.NotEqual(a[..5], c[..5]);   // 날짜가 바뀌면 새 헤더

        tx.Rollback();   // 카운터도 롤백 — 테스트 흔적 없음
    }

    [SkippableFact]
    public void NextLotNo_line_without_prefix_throws()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        using var conn = f.OpenConnection();
        using var tx = conn.BeginTransaction();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AMES.Data.Services.LotNoGenerator.NextLotNo(conn, tx, "LINE-NOPREFIX", DateTime.Now));
        Assert.Contains("LotPrefix", ex.Message);
        tx.Rollback();
    }

    [SkippableFact]
    public void CreateRawLot_uses_9char_rule_with_incrementing_seq()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var (id1, c1) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 90001);
        var (id2, c2) = repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 90002);
        try
        {
            Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", c1);
            Assert.Equal(c1[..5], c2[..5]);
            Assert.Equal(int.Parse(c1[5..]) + 1, int.Parse(c2[5..]));
        }
        finally { Cleanup(f, id1); Cleanup(f, id2); }
    }

    [SkippableFact]
    public void CreateManualRawLots_consecutive_seq_and_9char_rule()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var lots = repo.CreateManualRawLots("LINE-INJ-01", "83335-P8000RBQ", null, 3, "E-TEST");
        try
        {
            Assert.Equal(3, lots.Count);
            Assert.All(lots, l => Assert.Matches(@"^[A-Z][1-9A-C][1-9A-V]I1\d{4}$", l.LotCode!));
            var seqs = lots.Select(l => int.Parse(l.LotCode![5..])).ToList();
            Assert.Equal(seqs[0] + 1, seqs[1]);
            Assert.Equal(seqs[1] + 1, seqs[2]);
        }
        finally { foreach (var l in lots) Cleanup(f, l.LotId); }
    }

    [SkippableFact]
    public void CreateManualRawLots_line_without_prefix_throws_and_rolls_back()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            repo.CreateManualRawLots("LINE-NOPREFIX", "83335-P8000RBQ", null, 1, "E-TEST"));
        Assert.Contains("LotPrefix", ex.Message);
    }

    [SkippableFact]
    public void CreateRawLot_parallel_yields_unique_codes()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var repo = new InjLotRepository(f);
        var results = new System.Collections.Concurrent.ConcurrentBag<(int LotId, string LotCode)>();
        Parallel.For(0, 8, i =>
            results.Add(repo.CreateRawLot("LINE-INJ-01", "INJ-01-01-1800", Lh(), 91000 + i)));
        try
        {
            Assert.Equal(8, results.Count);
            Assert.Equal(8, results.Select(r => r.LotCode).Distinct().Count());
        }
        finally { foreach (var (id, _) in results) Cleanup(f, id); }
    }
}
