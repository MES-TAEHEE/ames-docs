using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 10-15 사용자 결정 — 보전 작업지시 상태 MNT_STATUS: 발행 → 배정 → 진행 중 → 완료, 끝나기 전 어느 단계에서든 종료
public class MwoStatusRulesTests
{
    [Theory]
    [InlineData(null,        "ISSUED")]
    [InlineData("",          "ISSUED")]
    [InlineData("  ",        "ISSUED")]
    [InlineData("guid-tech", "ASSIGNED")]
    public void New_work_orders_are_assigned_only_with_a_technician(string? tech, string expected)
        => Assert.Equal(expected, MwoStatusRules.Initial(tech));

    [Theory]
    //          status         notStarted ended  assign start  complete close
    [InlineData("ISSUED",      true,      false, true,  true,  true,    true)]
    [InlineData("ASSIGNED",    true,      false, true,  true,  true,    true)]
    [InlineData("assigned",    true,      false, true,  true,  true,    true)]
    [InlineData("IN_PROGRESS", false,     false, true,  false, true,    true)]
    [InlineData("COMPLETED",   false,     true,  false, false, false,   false)]
    [InlineData("CLOSED",      false,     true,  false, false, false,   false)]
    [InlineData("OPEN",        false,     false, true,  false, true,    true)]   // 폐지된 동의어 — 마이그레이션이 정리한다, 착수 대상 아님
    public void Transitions(string status, bool notStarted, bool ended, bool assign, bool start, bool complete, bool close)
    {
        Assert.Equal(notStarted, MwoStatusRules.IsNotStarted(status));
        Assert.Equal(ended,      MwoStatusRules.IsEnded(status));
        Assert.Equal(assign,     MwoStatusRules.CanAssign(status));
        Assert.Equal(start,      MwoStatusRules.CanStart(status));
        Assert.Equal(complete,   MwoStatusRules.CanComplete(status));
        Assert.Equal(close,      MwoStatusRules.CanClose(status));
    }

    [Theory]
    [InlineData("ISSUED",      "ASSIGNED")]
    [InlineData("ASSIGNED",    "ASSIGNED")]
    [InlineData("IN_PROGRESS", "IN_PROGRESS")]   // 진행 중 재배정은 담당자만 바꾼다
    public void Assigning_keeps_work_in_progress(string status, string expected)
        => Assert.Equal(expected, MwoStatusRules.AfterAssign(status));
}
