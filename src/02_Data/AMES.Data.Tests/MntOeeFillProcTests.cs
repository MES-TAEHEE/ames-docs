using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-23 — dbo.SP_MNT_OEELog_Fill(에이전트 작업 "[AMES] MNT OEE Fill", dist/migrate_mnt_oeelog_fill.sql)이
// 라인 OEE 스냅샷(PP_LineOEE 교대 행)을 그 라인의 유일한 활성 설비 행으로 MNT_OEELog 에 옮기는지(비율은 백분율로),
// 활성 설비가 둘 이상인 라인은 건너뛰는지, 값이 그대로인 행은 다시 쓰지 않는지. 쓰기라서 AMES_TEST_CONN 을 지정했을 때만 돈다.
public class MntOeeFillProcTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test equipment, line OEE and equipment OEE rows";
    const string Actor   = "itest-moef";
    const string Line1   = "ITEST-MOEF-L1";   // 설비 1대 → 채운다
    const string Line2   = "ITEST-MOEF-L2";   // 설비 2대 → 건너뛴다
    const string Equip1  = "ITEST-MOEF-E1";
    const string Equip2a = "ITEST-MOEF-E2A";
    const string Equip2b = "ITEST-MOEF-E2B";
    const string Shift   = "A";
    static readonly DateTime Day = new(2099, 3, 2);

    [SkippableFact]
    public void Fill_copies_line_oee_to_the_single_machine_as_percent_and_rewrites_only_changed_rows()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT OBJECT_ID('dbo.SP_MNT_OEELog_Fill');") is DBNull or null, "dbo.SP_MNT_OEELog_Fill not applied (dist/migrate_mnt_oeelog_fill.sql)");

        try
        {
            Cleanup(f!);
            Exec(f!, """
                INSERT dbo.MD_Equipment (EquipID, EquipName, EquipType, LineID, ActiveFlag, CreatedBy, CreatedTS) VALUES
                    (@E1, N'itest 1', 'INJ', @L1, 1, @By, SYSDATETIME()),
                    (@E2A, N'itest 2a', 'INJ', @L2, 1, @By, SYSDATETIME()),
                    (@E2B, N'itest 2b', 'INJ', @L2, 1, @By, SYSDATETIME());
                INSERT dbo.PP_LineOEE (LineID, PeriodDate, ShiftCode, LoadingMin, PlannedDownMin, UnplannedDownMin, OperatingMin,
                                       TotalProducedQty, GoodQty, Availability, Performance, Quality, OEE, CreatedBy, CreatedTS) VALUES
                    (@L1, @D, @S, 570, 95, 60, 415, 120, 110, 0.7281, 0.9500, 0.9167, 0.6341, @By, SYSDATETIME()),
                    (@L2, @D, @S, 570, 95, 60, 415, 120, 110, 0.7281, 0.9500, 0.9167, 0.6341, @By, SYSDATETIME()),
                    (@L1, @D, NULL, 570, 95, 60, 415, 120, 110, 0.7281, 0.9500, 0.9167, 0.6341, @By, SYSDATETIME());
                """, ("@E1", Equip1), ("@E2A", Equip2a), ("@E2B", Equip2b), ("@L1", Line1), ("@L2", Line2),
                     ("@D", Day), ("@S", Shift), ("@By", Actor));

            var r1 = Run(f!);
            Assert.Equal((1, 1, 0), (r1.Computed, r1.Inserted, r1.Updated));   // 교대 행만, 설비 1대 라인만
            Assert.True(r1.SkippedLines >= 1);                                 // 설비 2대 라인

            var rows = new MntRepository(f!).ListOeeRange(Day, Day).Where(o => o.EquipId!.StartsWith("ITEST-MOEF")).ToList();
            var row = Assert.Single(rows);
            Assert.Equal((Equip1, Line1, "SHIFT", Shift), (row.EquipId, row.LineId, row.AggLevel, row.ShiftCode));
            Assert.Equal((570, 155), (row.PlannedTimeMin, row.DowntimeMin));              // 가동 분 = 570 − 155 = 라인 OperatingMin 415
            Assert.Equal((72.81m, 95.00m, 91.67m, 63.41m), (row.Availability, row.Performance, row.Quality, row.Oee));
            Assert.Equal((110m, 120m), (row.GoodQty, row.TotalQty));
            Assert.Equal("OEE-990302-ITEST-MOEF-E1-A", row.OeeRecordNumber);
            Assert.Equal("itest 1", row.EquipName);

            // 같은 값이면 다시 쓰지 않는다
            var r2 = Run(f!);
            Assert.Equal((1, 0, 0, 1), (r2.Computed, r2.Inserted, r2.Updated, r2.Unchanged));

            // 라인 OEE 가 다시 집계되면 그 행만 덮어쓴다(같은 행 ID)
            Exec(f!, "UPDATE dbo.PP_LineOEE SET OperatingMin = 450, UnplannedDownMin = 25, Availability = 0.7895, OEE = 0.6876 WHERE LineID = @L AND ShiftCode = @S;",
                 ("@L", Line1), ("@S", Shift));
            var r3 = Run(f!);
            Assert.Equal((0, 1), (r3.Inserted, r3.Updated));
            var row2 = Assert.Single(new MntRepository(f!).ListOeeRange(Day, Day), o => o.EquipId == Equip1);
            Assert.Equal((row.OeeLogId, 120, 78.95m, 68.76m), (row2.OeeLogId, row2.DowntimeMin, row2.Availability, row2.Oee));

            Assert.Equal("OK", Scalar(f!, "SELECT ConnStatus FROM dbo.SYS_InterfaceMonitor WHERE InterfaceCode = 'MNTOEEFILL';"));
        }
        finally { Cleanup(f!); }
    }

    sealed record FillResult(int Computed, int Inserted, int Updated, int Unchanged, int SkippedLines);

    static FillResult Run(AMES.Data.Connection.AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "EXEC dbo.SP_MNT_OEELog_Fill @Date = @D, @DaysBack = 0, @Actor = @A;", conn);
        cmd.Parameters.Add("@D", System.Data.SqlDbType.Date).Value = Day;
        cmd.Parameters.Add("@A", System.Data.SqlDbType.VarChar, 20).Value = Actor;
        using var rdr = cmd.ExecuteReader();
        Assert.True(rdr.Read());
        return new((int)rdr["Computed"], (int)rdr["Inserted"], (int)rdr["Updated"], (int)rdr["Unchanged"], (int)rdr["SkippedLines"]);
    }

    static void Cleanup(AMES.Data.Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MNT_OEELog WHERE EquipID IN (@E1, @E2A, @E2B);
        DELETE dbo.PP_LineOEE WHERE LineID IN (@L1, @L2);
        DELETE dbo.MD_Equipment WHERE EquipID IN (@E1, @E2A, @E2B);
        """, ("@E1", Equip1), ("@E2A", Equip2a), ("@E2B", Equip2b), ("@L1", Line1), ("@L2", Line2));
}
