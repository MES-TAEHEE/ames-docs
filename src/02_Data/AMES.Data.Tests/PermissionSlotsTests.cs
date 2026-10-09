using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 화면 기본 권한(SYS_Screen.PermissionCriteria, '_'/'X') + 역할 권한 3자리(R·E·A, '_' 미부여, 'X' 기능 없음) 규칙
public class PermissionSlotsTests
{
    [Theory]
    [InlineData(null,  "___")]
    [InlineData("",    "___")]
    [InlineData("__X", "__X")]
    [InlineData("_XX", "_XX")]
    [InlineData("XXX", "_XX")]   // 읽기는 항상 있다
    [InlineData("x_x", "__X")]   // 소문자
    [InlineData("_X",  "_X_")]   // 짧으면 나머지는 기능 있음
    [InlineData("REA", "___")]   // X 가 아니면 모두 '_'
    public void NormalizeTemplate_keeps_read_and_only_X_or_underscore(string? template, string expected)
        => Assert.Equal(expected, PermissionSlots.NormalizeTemplate(template));

    [Theory]
    [InlineData(true,  true,  "___", "REA")]
    [InlineData(true,  false, "__X", "RE")]
    [InlineData(false, true,  "_X_", "RA")]
    [InlineData(false, false, "_XX", "R")]
    public void TemplateOf_and_FeaturesOf_round_trip(bool edit, bool approve, string template, string features)
    {
        Assert.Equal(template, PermissionSlots.TemplateOf(edit, approve));
        Assert.Equal(features, PermissionSlots.FeaturesOf(template));
    }

    [Theory]
    [InlineData("__X", "RE",  "REX")]   // 조회+수정 화면
    [InlineData("__X", "R",   "R_X")]
    [InlineData("__X", "REA", "REX")]   // 옛 "REA" — 없는 승인은 X
    [InlineData("_XX", "REA", "RXX")]   // 조회 전용 화면
    [InlineData("___", "RE",  "RE_")]   // 승인 있는 화면
    [InlineData("___", "RA",  "R_A")]
    [InlineData("_X_", "REA", "RXA")]   // 수정 없이 승인만 있는 화면
    [InlineData(null,  "RE",  "RE_")]   // 기본 권한을 모르면 R·E·A 모두 있다고 본다
    public void Compose_marks_missing_features_X_and_ungranted_underscore(string? template, string granted, string expected)
        => Assert.Equal(expected, PermissionSlots.Compose(template, granted));

    [Theory]
    [InlineData("__X", "")]
    [InlineData("__X", null)]
    [InlineData("_XX", "EA")]   // 조회 전용 화면에 E·A 만 주면 남는 게 없다
    public void Compose_returns_null_when_nothing_is_granted(string? template, string? granted)
        => Assert.Null(PermissionSlots.Compose(template, granted));

    [Theory]
    [InlineData("REX", null,  "RE")]
    [InlineData("R_X", null,  "R")]
    [InlineData("_EA", null,  "EA")]
    [InlineData("rea", null,  "REA")]   // 옛 형식·소문자
    [InlineData("REA", "_XX", "R")]     // 그 화면에 없는 기능 글자는 뺀다
    [InlineData("RXX", "_XX", "R")]
    public void Granted_reads_only_given_letters(string level, string? template, string expected)
        => Assert.Equal(expected, PermissionSlots.Granted(level, template));
}
