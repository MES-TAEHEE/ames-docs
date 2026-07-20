using AMES.InjAgent.Plc;
using Xunit;

namespace AMES.InjAgent.Tests;

/// <summary>
/// 검증 벡터는 PLC_Simulator core/encoders.py 의 encode_* 출력과 동일
/// (encoders.py __main__ 의 왕복 assert 로 원본 에이전트 디코더와 정합 검증된 값).
/// </summary>
public class PlcCodecTests
{
    [Fact]
    public void ToInt64_decodes_simulator_long_words()
    {
        Assert.Equal(105L,       PlcCodec.ToInt64(new ushort[] { 105, 0, 0, 0 }));
        Assert.Equal(123456789L, PlcCodec.ToInt64(new ushort[] { 0xCD15, 0x075B, 0, 0 }));
        Assert.Equal(0x0001_0000_0000L, PlcCodec.ToInt64(new ushort[] { 0, 0, 1, 0 }));
    }

    [Fact]
    public void ToFloat_decodes_simulator_float_words()
    {
        Assert.Equal(235.5f, PlcCodec.ToFloat(new ushort[] { 0x8000, 0x436B }), 3);
    }

    [Fact]
    public void ToAscii_decodes_simulator_mold_code()
    {
        var words = new ushort[] { 0x454D, 0x4441, 0x5254, 0x5443, 0x4E4E, 0x0042 };
        Assert.Equal("MEADTRCTNNB", PlcCodec.ToAscii(words));
    }

    [Fact]
    public void ToAscii_trims_trailing_nulls()
    {
        // "AB" + 패딩 → 워드 [ ('B'<<8)|'A', 0 ]
        Assert.Equal("AB", PlcCodec.ToAscii(new ushort[] { 0x4241, 0x0000 }));
    }

    [Theory]
    [InlineData("MEADTRCTNNB", "MEADTRCT", "NNB")]
    [InlineData("NEAFUCNNB",   "NEAFUC",   "NNB")]
    [InlineData("LQ2DTMDCBK",  "LQ2DTMD",  "CBK")]
    [InlineData("LQ2DTRUCBK",  "LQ2DTRU",  "CBK")]
    public void SplitMoldColor_follows_original_rule(string raw, string mold, string color)
    {
        var (m, c) = PlcCodec.SplitMoldColor(raw);
        Assert.Equal(mold, m);
        Assert.Equal(color, c);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("ABC")]
    public void SplitMoldColor_returns_empty_for_short_input(string raw)
    {
        var (m, c) = PlcCodec.SplitMoldColor(raw);
        Assert.Equal(string.Empty, m);
        Assert.Equal(string.Empty, c);
    }

    [Fact]
    public void SplitMoldColor_removes_color_substring_everywhere_like_original()
    {
        // 원본 Main.cs 동작 고정: 색상("CBK")이 앞쪽에도 등장하면 함께 제거됨.
        // 이 quirk 를 "고치면" 원본 에이전트와 디코딩 정합이 깨진다.
        var (m, c) = PlcCodec.SplitMoldColor("CBKDTMDCBK");
        Assert.Equal("DTMD", m);
        Assert.Equal("CBK", c);
    }
}
