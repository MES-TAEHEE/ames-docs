using AMES.Data.Connection;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-22 — dbo.SP_PP_LineStateLog_Fill(에이전트 작업 "[AMES] Line State Fill", dist/migrate_pp_linestatelog_fill.sql)이
// 비가동 로그 → POP 활동(실적·LOT) → 라인 시간 패턴·PM 슬롯 순으로 분마다 PP_LineStateLog 를 분류하고, 값이 그대로인 분은 다시 쓰지 않는지.
// 쓰기라서 AMES_TEST_CONN 을 지정했을 때만 돈다. 시험 라인은 MD_Line 에 없다(@LineID 로 직접 지정 — 패턴은 전역 패턴을 따른다).
public class LineStateFillProcTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test state logs, results, lots, slots and downtime";
    const string Line  = "ITEST-LSF";
    const string Actor = "itest-lsf";
    static readonly DateTime Day  = new(2099, 3, 3);
    static readonly DateTime From = Day.AddHours(9);
    static readonly DateTime To   = Day.AddHours(14);
    const int TestWo = 999999;

    sealed record Row(DateTime MinuteTs, string State, string PlanState, bool RunFlag, int? WoId);

    [SkippableFact]
    public void Fill_classifies_minutes_from_downtime_activity_and_pattern_and_rewrites_only_changed_rows()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT OBJECT_ID('dbo.SP_PP_LineStateLog_Fill');") is DBNull or null, "dbo.SP_PP_LineStateLog_Fill not applied (dist/migrate_pp_linestatelog_fill.sql)");

        // 시험 라인에는 BOP 가 없으므로 가동 창 = PP_LINE_RUN_GAP_MIN(기본 3분)
        int gapMin = Scalar(f!, "SELECT TRY_CAST(ConfigValue AS int) FROM dbo.SYS_Config WHERE ConfigKey = 'PP_LINE_RUN_GAP_MIN';") is int g && g >= 1 && g <= 60 ? g : 3;
        var operating = ReadGlobalOperatingSegments(f!, Day);
        Skip.If(operating.Count == 0, "no global ACTIVE line time pattern with OPERATING segments");

        try
        {
            Cleanup(f!);
            Exec(f!, """
                INSERT dbo.PP_LineDowntimeLog (LineID, StartTS, EndTS, DurationMin, ReasonCode, CreatedBy)
                VALUES (@L, DATEADD(HOUR, 10, @D), DATEADD(MINUTE, 30, DATEADD(HOUR, 10, @D)), 30, 'MACHINE', @By);
                INSERT dbo.PR_ProductionResult (LineID, WoID, GoodQty, EntryAt, CreatedBy) VALUES
                    (@L, @Wo, 1, DATEADD(HOUR, 11, @D), @By),
                    (@L, @Wo, 1, DATEADD(MINUTE, 2, DATEADD(HOUR, 11, @D)), @By),
                    (@L, NULL, 1, DATEADD(MINUTE, 10, DATEADD(HOUR, 11, @D)), @By);
                INSERT dbo.tbl_Lot (LotCode, ItemNo, LineID, ProcessCode, CreatedBy, CreatedTS)
                VALUES ('ITEST-LSF-LOT1', 'ITEST', @L, 'INJ', @By, DATEADD(HOUR, 13, @D));
                INSERT dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, StartMin, EndMin, EntryType, Status, CreatedBy) VALUES
                    (@L, @D, @Wo, 660, 720, 'WO', 'DRAFT', @By),        -- 11:00–12:00
                    (@L, @D, NULL, 810, 825, 'PM', 'DRAFT', @By);       -- 13:30–13:45 계획 정지
                """, ("@L", Line), ("@D", Day), ("@By", Actor), ("@Wo", TestWo));

            var r1 = Run(f!);
            Assert.Equal((1, 300, 300, 0), (r1.Lines, r1.Minutes, r1.Inserted, r1.Updated));

            var rows = Read(f!);
            Assert.Equal(300, rows.Count);
            foreach (var r in rows)
            {
                int mod = (int)(r.MinuteTs - Day).TotalMinutes;
                bool down = r.MinuteTs >= Day.AddHours(10) && r.MinuteTs < Day.AddHours(10.5);
                bool run  = InWindow(r.MinuteTs, Day.AddHours(11), gapMin) || InWindow(r.MinuteTs, Day.AddHours(11).AddMinutes(2), gapMin)
                         || InWindow(r.MinuteTs, Day.AddHours(11).AddMinutes(10), gapMin) || InWindow(r.MinuteTs, Day.AddHours(13), gapMin);
                string expState = down ? "DOWN" : run ? "RUN" : "IDLE";
                bool pm = mod >= 810 && mod < 825;
                string expPlan = !pm && operating.Any(s => s.Start <= mod && mod < s.End) ? "PLAN-RUN" : "PLAN-STOP";
                int? expWo = mod >= 660 && mod < 720 ? TestWo
                           : (InWindow(r.MinuteTs, Day.AddHours(11), gapMin) || InWindow(r.MinuteTs, Day.AddHours(11).AddMinutes(2), gapMin)) ? TestWo
                           : null;
                Assert.True(expState == r.State && expPlan == r.PlanState && (expState == "RUN") == r.RunFlag && expWo == r.WoId,
                    $"{r.MinuteTs:HH:mm}: expected {expState}/{expPlan}/wo {expWo?.ToString() ?? "-"} but got {r.State}/{r.PlanState}/{r.RunFlag}/wo {r.WoId?.ToString() ?? "-"}");
            }
            Assert.Contains(rows, r => r.State == "DOWN");
            Assert.Contains(rows, r => r.State == "RUN");
            Assert.Contains(rows, r => r.PlanState == "PLAN-STOP");

            // 같은 값이면 다시 쓰지 않는다
            var r2 = Run(f!);
            Assert.Equal((0, 0, 300), (r2.Inserted, r2.Updated, r2.Unchanged));

            // 비가동을 늦게 닫으면(PP-DTL 과거 수정) 그 분만 다시 쓴다 — 10:30 → 10:45 로 15분 연장
            Exec(f!, "UPDATE dbo.PP_LineDowntimeLog SET EndTS = DATEADD(MINUTE, 45, DATEADD(HOUR, 10, @D)), DurationMin = 45 WHERE LineID = @L;", ("@L", Line), ("@D", Day));
            var r3 = Run(f!);
            Assert.Equal((0, 15), (r3.Inserted, r3.Updated));
            Assert.Equal(45, Read(f!).Count(r => r.State == "DOWN"));

            var mon = Scalar(f!, "SELECT ConnStatus FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = 'STATEFILL';");
            Assert.Equal("OK", mon);
        }
        finally { Cleanup(f!); }
    }

    static bool InWindow(DateTime minute, DateTime activity, int gapMin)
    {
        var m0 = activity.AddMinutes(-(activity.Minute % 1)).AddSeconds(-activity.Second);
        return minute >= m0 && minute < m0.AddMinutes(gapMin);
    }

    sealed record FillResult(int Lines, int Minutes, int Inserted, int Updated, int Unchanged);

    static FillResult Run(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand("dbo.SP_PP_LineStateLog_Fill", conn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@From", From);
        cmd.Parameters.AddWithValue("@To", To);
        cmd.Parameters.AddWithValue("@LineID", Line);
        cmd.Parameters.AddWithValue("@Actor", Actor);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        return new FillResult(Convert.ToInt32(r["Lines"]), Convert.ToInt32(r["Minutes"]), Convert.ToInt32(r["Inserted"]),
                              Convert.ToInt32(r["Updated"]), Convert.ToInt32(r["Unchanged"]));
    }

    static List<Row> Read(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand("SELECT MinuteTS, State, PlanState, ISNULL(RunFlag,0) AS RunFlag, WoID FROM dbo.PP_LineStateLog WHERE LineID = @L ORDER BY MinuteTS;", conn);
        cmd.Parameters.AddWithValue("@L", Line);
        using var r = cmd.ExecuteReader();
        var list = new List<Row>();
        while (r.Read())
            list.Add(new Row((DateTime)r["MinuteTS"], (string)r["State"], (string)r["PlanState"], Convert.ToBoolean(r["RunFlag"]), r["WoID"] as int?));
        return list;
    }

    // 프로시저와 같은 해석: 시험 라인은 PP_LineSchedule 패턴·라인 전용 패턴이 없으므로 전역 ACTIVE 패턴의 OPERATING 구간
    static List<(int Start, int End)> ReadGlobalOperatingSegments(AmesConnectionFactory f, DateTime day)
    {
        using var conn = f.OpenConnection();
        using var cmd  = new SqlCommand("""
            DECLARE @Pat varchar(20) = (SELECT TOP (1) p.PatternID FROM dbo.MD_LineTimePattern p
                                        WHERE p.LineID IS NULL AND ISNULL(p.Status,'ACTIVE') = 'ACTIVE'
                                          AND (p.EffectiveFrom IS NULL OR p.EffectiveFrom <= @D) AND (p.EffectiveTo IS NULL OR p.EffectiveTo >= @D)
                                        ORDER BY p.PatternID);
            SELECT StartMin, EndMin FROM dbo.MD_LineTimeSegment WHERE PatternID = @Pat AND SegmentState = 'OPERATING' AND EndMin > StartMin;
            """, conn);
        cmd.Parameters.AddWithValue("@D", day.Date);
        using var r = cmd.ExecuteReader();
        var list = new List<(int, int)>();
        while (r.Read()) list.Add((Convert.ToInt32(r["StartMin"]), Convert.ToInt32(r["EndMin"])));
        return list;
    }

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.PP_LineStateLog WHERE LineID = @L;
        DELETE dbo.PR_ProductionResult WHERE LineID = @L;
        DELETE dbo.tbl_Lot WHERE LineID = @L;
        DELETE dbo.PP_LineSchedule WHERE LineID = @L;
        DELETE dbo.PP_LineDowntimeLog WHERE LineID = @L;
        """, ("@L", Line));
}
