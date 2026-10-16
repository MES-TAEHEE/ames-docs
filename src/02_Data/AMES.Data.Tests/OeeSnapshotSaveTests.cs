using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

[CollectionDefinition(nameof(OeeSnapshotSaveTests), DisableParallelization = true)]
public sealed class OeeSnapshotSaveCollection { }

// 10-14 — PP-OEE 스냅샷은 (라인, 일자, 교대) 키당 1행: 저장소가 키를 잠그고 확인하며, 유니크 인덱스가 마지막 안전망
[Collection(nameof(OeeSnapshotSaveTests))]
public class OeeSnapshotSaveTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — writes and removes test OEE snapshots";
    const string Line = "ITEST-OEE";
    static readonly DateTime Day = new(2099, 1, 15);

    static OeeSnapshotDto Snap(decimal oee, string? shift = "A") => new()
    {
        LineId = Line, PeriodDate = Day, ShiftCode = shift,
        LoadingMin = 480, PlannedDownMin = 30, UnplannedDownMin = 20, OperatingMin = 430,
        TotalProducedQty = 100, GoodQty = 95, Availability = 89.58m, Performance = 90m, Quality = 95m, Oee = oee,
    };

    static Connection.AmesConnectionFactory? WriteFactory()
        => Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null ? null : TryFactory();

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, "DELETE dbo.PP_LineOEE WHERE LineID = @L;", ("@L", Line));

    static int Count(Connection.AmesConnectionFactory f, string? shift)
        => Convert.ToInt32(Scalar(f, "SELECT COUNT(*) FROM dbo.PP_LineOEE WHERE LineID = @L AND PeriodDate = @D AND ((ShiftCode IS NULL AND @S IS NULL) OR ShiftCode = @S);",
            ("@L", Line), ("@D", Day), ("@S", (object?)shift ?? DBNull.Value)));

    [SkippableFact]
    public void Concurrent_saves_of_one_key_leave_one_row_and_one_creation()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var repo = new OeeRepository(f!);
        Cleanup(f!);
        try
        {
            for (var round = 0; round < 3; round++)
            {
                Cleanup(f!);
                var results = new System.Collections.Concurrent.ConcurrentBag<OeeRepository.SnapshotSaveResult>();
                Parallel.For(0, 6, i => results.Add(repo.SaveSnapshot(Snap(70 + i), "itest")));

                Assert.Equal(1, Count(f!, "A"));
                Assert.Single(results, r => r.Created);
                Assert.Single(results.Select(r => r.Id).Distinct());
            }
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Overwrite_returns_the_previous_values()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        var repo = new OeeRepository(f!);
        Cleanup(f!);
        try
        {
            var first  = repo.SaveSnapshot(Snap(71.5m), "itest");
            var second = repo.SaveSnapshot(Snap(80m), "itest");

            Assert.True(first.Created);
            Assert.Null(first.Before);
            Assert.False(second.Created);
            Assert.Equal(first.Id, second.Id);
            Assert.Equal(71.5m, second.Before!.Oee);
            Assert.Equal(80m, repo.GetSnapshots(Day, Day, "A", Line).Single().Oee);

            // 교대 NULL(일 단위)은 교대 A 와 다른 키이고, 그 키도 1행
            repo.SaveSnapshot(Snap(60m, null), "itest");
            repo.SaveSnapshot(Snap(61m, null), "itest");
            Assert.Equal(1, Count(f!, null));
            Assert.Equal(1, Count(f!, "A"));
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Unique_index_rejects_a_direct_duplicate_insert()
    {
        var f = WriteFactory(); Skip.If(f is null, WriteSkip);
        Skip.If(Scalar(f!, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_PP_LineOEE_Line_Date_Shift';") is 0,
            "dist/migrate_pp_lineoee_unique.sql not applied");
        var repo = new OeeRepository(f!);
        Cleanup(f!);
        try
        {
            repo.SaveSnapshot(Snap(70m), "itest");
            var ex = Assert.Throws<SqlException>(() => Exec(f!, """
                INSERT dbo.PP_LineOEE (LineID, PeriodDate, ShiftCode, CreatedBy, CreatedTS)
                VALUES (@L, @D, 'A', 'itest', SYSDATETIME());
                """, ("@L", Line), ("@D", Day)));
            Assert.Contains(ex.Number, new[] { 2601, 2627 });
        }
        finally { Cleanup(f!); }
    }
}
