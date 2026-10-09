using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// PP-LSB 「적용」·「초기화」 동시 변경 거부 — 보드가 읽은 뒤 다른 쪽(PP-003·APS·다른 보드)이 행을 바꿨으면 아무것도 지우지 않는다.
// 전용 라인 코드(마스터 없음 — PP_LineSchedule 은 FK 가 없다)와 +500일 날짜만 쓰고 끝에 지운다.
public class LineScheduleConcurrencyTests
{
    const string Line  = "ITEST-LSB-CC";
    const int    WoA   = 999_999_901;
    const int    WoB   = 999_999_902;
    static readonly DateTime Day = DateTime.Today.AddDays(500);

    static readonly (int StartMin, int EndMin, string? Title, string? RefType, int? RefId)[] NoPm = [];

    static void Cleanup(AmesConnectionFactory f) =>
        Exec(f, "DELETE FROM dbo.PP_LineSchedule WHERE LineID = @L;", ("@L", Line));

    // PP-003·APS 가 보드 밖에서 슬롯을 넣는 것과 같은 효과
    static void InsertOutside(AmesConnectionFactory f, int woId, int start, int end, string status = "DRAFT") =>
        Exec(f, """
            INSERT INTO dbo.PP_LineSchedule (LineID, ScheduleDate, WoID, StartMin, EndMin, PlannedQty, EntryType, Status, CreatedBy, CreatedTS)
            VALUES (@L, @D, @W, @S, @E, 10, 'WO', @St, 'ITEST', SYSDATETIME());
            """, ("@L", Line), ("@D", Day.Date), ("@W", woId), ("@S", start), ("@E", end), ("@St", status));

    static List<int?> WoIds(LineScheduleRepository lsb) =>
        lsb.GetSchedule(Line, Day).Select(r => r.WoId).OrderBy(x => x).ToList();

    [Fact]
    public void VersionOf_ignores_row_order_and_changes_with_content()
    {
        var a = new LineScheduleRepository.ScheduleRow(1, "P", 10, null, null, 480, 540, 5m, "DRAFT", null, null, "WO", null, null, null);
        var b = a with { ScheduleId = 2, WoId = 11, StartMin = 540, EndMin = 600 };

        Assert.Equal(LineScheduleRepository.VersionOf([a, b]), LineScheduleRepository.VersionOf([b, a]));
        Assert.NotEqual(LineScheduleRepository.VersionOf([a, b]), LineScheduleRepository.VersionOf([a, b with { EndMin = 610 }]));
        Assert.NotEqual(LineScheduleRepository.VersionOf([a]), LineScheduleRepository.VersionOf([a, b]));
        Assert.NotEqual(LineScheduleRepository.VersionOf([a]), LineScheduleRepository.VersionOf([a with { Status = "PUBLISHED" }]));
    }

    [SkippableFact]
    public void Save_with_stale_version_is_rejected_and_keeps_slot_added_outside()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var lsb = new LineScheduleRepository(f);
            var seen = LineScheduleRepository.VersionOf(lsb.GetSchedule(Line, Day));   // 보드를 연 시점(빈 날)
            InsertOutside(f, WoB, 600, 660);                                          // 그 사이 PP-003 이 슬롯을 넣음

            Assert.Throws<LineScheduleRepository.ScheduleChangedException>(() =>
                lsb.SaveSchedule(Line, Day, null, [(WoA, 480, 540, 5m, null)], NoPm, "itest", expectedVersion: seen));

            Assert.Equal([WoB], WoIds(lsb));   // 밖에서 넣은 슬롯은 남고 보드 값은 저장되지 않았다
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Save_with_current_version_replaces_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var lsb = new LineScheduleRepository(f);
            InsertOutside(f, WoB, 600, 660);
            var seen = LineScheduleRepository.VersionOf(lsb.GetSchedule(Line, Day));   // 보드가 WoB 를 보고 있음

            lsb.SaveSchedule(Line, Day, null, [(WoA, 480, 540, 5m, null), (WoB, 600, 660, 10m, null)], NoPm, "itest", expectedVersion: seen);

            Assert.Equal([WoA, WoB], WoIds(lsb));

            // 같은 지문으로 다시 적용하면(두 번째 보드가 옛 화면으로 누른 경우) 거부
            Assert.Throws<LineScheduleRepository.ScheduleChangedException>(() =>
                lsb.SaveSchedule(Line, Day, null, [(WoA, 480, 540, 5m, null)], NoPm, "itest", expectedVersion: seen));
            Assert.Equal([WoA, WoB], WoIds(lsb));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Reset_checks_version_and_deletes_only_draft_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var lsb = new LineScheduleRepository(f);
            InsertOutside(f, WoA, 480, 540);
            InsertOutside(f, WoB, 600, 660, status: "PUBLISHED");
            var seen = LineScheduleRepository.VersionOf(lsb.GetSchedule(Line, Day));

            InsertOutside(f, WoB + 1, 700, 760);   // 보드를 연 뒤 들어온 슬롯
            Assert.Throws<LineScheduleRepository.ScheduleChangedException>(() => lsb.DeleteSchedule(Line, Day, seen));
            Assert.Equal(3, WoIds(lsb).Count);

            lsb.DeleteSchedule(Line, Day, LineScheduleRepository.VersionOf(lsb.GetSchedule(Line, Day)));
            Assert.Equal([WoB], WoIds(lsb));       // 발행된 행만 남음
        }
        finally { Cleanup(f); }
    }
}
