using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 10-16 사용자 결정 — 설비 상태 자동 판정: 안돈·CRITICAL 고장 → DOWN, 착수된 PM 작업지시 → PM, 그 밖은 수동(가동·유휴)
public class EquipStatusRulesTests
{
    [Theory]
    [InlineData("ANDON",    "MINOR",    true)]
    [InlineData("andon",    null,       true)]
    [InlineData("WEB",      "CRITICAL", true)]
    [InlineData("OPERATOR", "critical", true)]
    [InlineData("WEB",      "MAJOR",    false)]
    [InlineData("QC",       "LOW",      false)]
    [InlineData(null,       null,       false)]
    public void Andon_or_critical_failures_stop_the_equipment(string? source, string? severity, bool expected)
        => Assert.Equal(expected, EquipStatusRules.StopsEquipment(source, severity));

    // 10-16 — CRITICAL 이 아니어도 사용자가 「라인 정지」로 등록한(비가동에 연결된) 고장은 중지
    [Fact]
    public void Line_stop_failures_stop_the_equipment()
        => Assert.True(EquipStatusRules.StopsEquipment("WEB", "MINOR", lineStopped: true));

    [Theory]
    [InlineData(true,  true,  "IDLE", "DOWN")]   // 고장이 PM 보다 앞선다
    [InlineData(false, true,  "IDLE", "PM")]
    [InlineData(false, false, "IDLE", "IDLE")]
    [InlineData(false, false, "RUN",  "RUN")]
    [InlineData(false, false, "DOWN", "RUN")]    // 손으로 넣은 DOWN·PM 은 자동 판정이 대신한다
    [InlineData(false, false, "PM",   "RUN")]
    [InlineData(false, false, null,   "RUN")]
    [InlineData(false, false, "setup", "IDLE")]
    public void Effective_status(bool stop, bool pm, string? manual, string expected)
        => Assert.Equal(expected, EquipStatusRules.Effective(stop, pm, manual));
}
