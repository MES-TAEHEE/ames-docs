using AMES.Data.Aps;
using AMES.Data.Repositories;
using AMES.Data.Scheduling;
using Xunit;
using static AMES.Data.Repositories.PpRepository;
using ScheduleRow = AMES.Data.Repositories.LineScheduleRepository.ScheduleRow;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// PP-APS 「WO 생성」 미리보기 보드(2026-10-07) — dryRun 결과(ApsWoResult)와 기존 PP_LineSchedule 행을 날짜 탭 × 라인 줄 × 블록으로 묶는 순수 규칙.
/// 새 슬롯·새 MC·기존 WO/MC/PM 을 구분하고, 형제 0분 슬롯은 대표 블록에 붙이며, 실행 뒤에는 결과 WO 의 슬롯을 기존 행에서 뺀다(이중 표시 방지).
/// </summary>
public class ApsWoBoardTests
{
    static readonly DateOnly D0 = new(2026, 10, 12);
    static readonly DateOnly D1 = D0.AddDays(1);
    static readonly DateOnly D2 = D0.AddDays(2);
    static DateTime T(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    static ApsWoOrder Order(string wo, int woId, string item, DateOnly plan, params DeadlinePacker.Placement[] slots) =>
        new(wo, woId, 1, item, plan, null, slots.Sum(s => s.Qty), null, null, slots, Array.Empty<DeadlinePacker.StepShortfall>(), Array.Empty<DeadlinePacker.MoldChange>());

    static ScheduleRow Row(int id, string? type, int? woId, string? woNo, int start, int end, decimal qty, string? refType = null, int? refId = null, string? mold = null) =>
        new(id, "PAT", woId, woNo, null, start, end, qty, "DRAFT", null, null, type, null, refType, refId, mold);

    static readonly string[] Lines = { "LINE-INJ-01", "LINE-IMG-01" };

    [Fact]
    public void Dates_are_plan_dates_plus_placement_dates_sorted()
    {
        var result = new ApsWoResult(new()
        {
            Order("WO-1", 1, "P1", D1, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D1), 480, 600, 100, false),
                                        new DeadlinePacker.Placement(20, "LINE-IMG-01", T(D2), 480, 540, 100, false)),
        }, new(), 0, 0, true);

        var days = ApsWoBoard.Build(result, new[] { D1, D0 }, Lines, _ => Array.Empty<ScheduleRow>());

        Assert.Equal(new[] { D0, D1, D2 }, days.Select(d => d.Date).ToArray());
        Assert.All(days, d => Assert.Equal(Lines, d.Rows.Select(r => r.LineId).ToArray()));   // 모든 라인이 빈 줄로라도 나온다, 순서 보존
    }

    [Fact]
    public void New_slots_and_mold_changes_become_blocks_on_their_line_and_date()
    {
        var order = Order("WO-1", 1, "P1", D0,
            new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 500, 620, 120, false, "M1"),
            new DeadlinePacker.Placement(20, "LINE-IMG-01", T(D1), 480, 540, 120, true)) with
        {
            MoldChanges = new[] { new DeadlinePacker.MoldChange(10, "LINE-INJ-01", T(D0), 480, 500, "M0", "M1", false) },
        };
        var result = new ApsWoResult(new() { order }, new(), 0, 0, true);

        var days = ApsWoBoard.Build(result, new[] { D0 }, Lines, _ => Array.Empty<ScheduleRow>());

        var inj = days.Single(d => d.Date == D0).Rows.Single(r => r.LineId == "LINE-INJ-01");
        Assert.Collection(inj.Blocks.OrderBy(b => b.StartMin),
            mc => { Assert.Equal(ApsWoBoard.BlockKind.NewMoldChange, mc.Kind); Assert.Equal((480, 500), (mc.StartMin, mc.EndMin)); Assert.Equal("M1", mc.MoldId); },
            wo => { Assert.Equal(ApsWoBoard.BlockKind.NewWo, wo.Kind); Assert.Equal("WO-1", wo.WoNumber); Assert.Equal("P1", wo.ItemNo); Assert.Equal(120m, wo.Qty); Assert.False(wo.Late); });
        Assert.Equal(120m, inj.NewQty);
        Assert.Equal(140, inj.NewMin);   // MC 20 + WO 120 — 보드 부하는 MC 도 센다(PP-LSB 와 같다)

        var img = days.Single(d => d.Date == D1).Rows.Single(r => r.LineId == "LINE-IMG-01");
        var late = Assert.Single(img.Blocks);
        Assert.True(late.Late);
        Assert.Empty(days.Single(d => d.Date == D1).Rows.Single(r => r.LineId == "LINE-INJ-01").Blocks);
    }

    [Fact]
    public void Sibling_zero_minute_slots_attach_to_the_representative_block()
    {
        var rep = Order("WO-1", 1, "P-LH", D0, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 600, 100, false, "M1"));
        var sib = Order("WO-2", 2, "P-RH", D0, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 480, 100, false));
        var result = new ApsWoResult(new() { rep, sib }, new(), 0, 0, true);

        var row = ApsWoBoard.Build(result, new[] { D0 }, Lines, _ => Array.Empty<ScheduleRow>())[0].Rows[0];

        var block = Assert.Single(row.Blocks);
        Assert.Equal("WO-1", block.WoNumber);
        var s = Assert.Single(block.Siblings);
        Assert.Equal(("WO-2", "P-RH", 100m), (s.WoNumber, s.ItemNo, s.Qty));
        Assert.Equal(200m, row.NewQty);   // 형제 수량도 그 줄의 새 수량이다
        Assert.Equal(120, row.NewMin);
    }

    [Fact]
    public void Existing_rows_are_classified_and_result_wos_are_excluded_from_them()
    {
        var order = Order("WO-NEW", 77, "P1", D0, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 600, 660, 60, false, "M1")) with
        {
            MoldChanges = new[] { new DeadlinePacker.MoldChange(10, "LINE-INJ-01", T(D0), 580, 600, null, "M1", false) },
        };
        var result = new ApsWoResult(new() { order }, new(), 0, 0, false);
        var existing = new List<ScheduleRow>
        {
            Row(1, "WO", 5,  "WO-OLD", 480, 540, 40, mold: "M0"),
            Row(2, "WO", 77, "WO-NEW", 600, 660, 60, mold: "M1"),           // 실행 뒤 다시 읽은 자기 슬롯 — 새 블록과 겹치므로 뺀다
            Row(3, "MC", null, null, 580, 600, 0, "WO", 77, "M1"),        // 자기 MC 도
            Row(4, "MC", null, null, 460, 480, 0, "WO", 5, "M0"),
            Row(5, "PM", null, null, 700, 760, 0, "PMSCH", 9),
            Row(6, "WO", null, null, 0, 0, 0),                            // placeholder 행(패턴 저장용) — 길이 0 은 블록이 아니다
        };

        var row = ApsWoBoard.Build(result, new[] { D0 }, new[] { "LINE-INJ-01" }, key => key.Line == "LINE-INJ-01" && key.Date == D0 ? existing : Array.Empty<ScheduleRow>())[0].Rows[0];

        Assert.Equal(new[]
        {
            (ApsWoBoard.BlockKind.OldMoldChange, 460, 480),
            (ApsWoBoard.BlockKind.OldWo,         480, 540),
            (ApsWoBoard.BlockKind.NewMoldChange, 580, 600),
            (ApsWoBoard.BlockKind.NewWo,         600, 660),
            (ApsWoBoard.BlockKind.Pm,            700, 760),
        }, row.Blocks.OrderBy(b => b.StartMin).Select(b => (b.Kind, b.StartMin, b.EndMin)).ToArray());
        Assert.Equal("WO-OLD", row.Blocks.Single(b => b.Kind == ApsWoBoard.BlockKind.OldWo).WoNumber);
    }

    [Fact]
    public void Shortfalls_sit_on_the_step_line_row_of_the_plan_date()
    {
        var order = Order("WO-1", 1, "P1", D0, new DeadlinePacker.Placement(10, "LINE-INJ-01", T(D0), 480, 540, 50, false)) with
        {
            Shortfalls = new[] { new DeadlinePacker.StepShortfall(10, "LINE-INJ-01", 30), new DeadlinePacker.StepShortfall(20, "LINE-IMG-01", 80) },
        };
        var result = new ApsWoResult(new() { order }, new(), 0, 0, true);

        var day = ApsWoBoard.Build(result, new[] { D0 }, Lines, _ => Array.Empty<ScheduleRow>())[0];

        var inj = Assert.Single(day.Rows.Single(r => r.LineId == "LINE-INJ-01").Shortfalls);
        Assert.Equal(("WO-1", 30m, true), (inj.WoNumber, inj.Qty, inj.IsInjection));
        var img = Assert.Single(day.Rows.Single(r => r.LineId == "LINE-IMG-01").Shortfalls);
        Assert.Equal((80m, false), (img.Qty, img.IsInjection));
    }

    [Fact]
    public void Unknown_line_gets_an_extra_row_at_the_end()
    {
        var order = Order("WO-1", 1, "P1", D0, new DeadlinePacker.Placement(20, "LINE-PNT-09", T(D0), 480, 540, 50, false));
        var result = new ApsWoResult(new() { order }, new(), 0, 0, true);

        var day = ApsWoBoard.Build(result, new[] { D0 }, Lines, _ => Array.Empty<ScheduleRow>())[0];

        Assert.Equal(new[] { "LINE-INJ-01", "LINE-IMG-01", "LINE-PNT-09" }, day.Rows.Select(r => r.LineId).ToArray());
        Assert.Single(day.Rows[2].Blocks);
    }
}
