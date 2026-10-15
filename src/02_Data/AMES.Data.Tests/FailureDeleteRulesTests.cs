using AMES.Data.Services;
using Xunit;
using B = AMES.Data.Services.FailureDeleteRules.Block;

namespace AMES.Data.Tests;

// MNT-002 고장 삭제는 열린 웹 등록 고장 + 착수 전 작업지시일 때만(10-13 사용자 결정)
public class FailureDeleteRulesTests
{
    [Theory]
    [InlineData("OPEN",        "WEB",   1,    "ISSUED",      B.None)]
    [InlineData("OPEN",        null,    null, null,          B.None)]
    [InlineData(null,          "WEB",   1,    "ASSIGNED",    B.None)]
    [InlineData("REGISTERED",  "WEB",   1,    "ISSUED",      B.None)]
    [InlineData("OPEN",        "WEB",   1,    null,          B.None)]      // 연결 WO 행이 없어진 경우
    [InlineData("IN_PROGRESS", "WEB",   1,    "IN_PROGRESS", B.NotOpen)]
    [InlineData("RESOLVED",    "WEB",   1,    "COMPLETED",   B.NotOpen)]
    [InlineData("CLOSED",      "WEB",   null, null,          B.NotOpen)]
    [InlineData("OPEN",        "ANDON", null, null,          B.FromAndon)]
    [InlineData("OPEN",        "andon", 1,    "ISSUED",      B.FromAndon)]
    [InlineData("OPEN",        "WEB",   1,    "IN_PROGRESS", B.WorkStarted)]
    [InlineData("OPEN",        "WEB",   1,    "COMPLETED",   B.WorkStarted)]
    public void Only_open_web_failures_with_unstarted_work_can_be_deleted(string? status, string? source, int? wo, string? woStatus, B expected)
    {
        Assert.Equal(expected, FailureDeleteRules.Check(status, source, wo, woStatus));
        Assert.Equal(expected == B.None, FailureDeleteRules.CanDelete(status, source, wo, woStatus));
    }
}
