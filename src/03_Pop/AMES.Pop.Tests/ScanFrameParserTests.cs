using System.Text;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class ScanFrameParserTests
{
    static byte[] B(string s) => Encoding.ASCII.GetBytes(s);

    [Theory]
    [InlineData("A91I10001\r")]
    [InlineData("A91I10001\n")]
    [InlineData("A91I10001\r\n")]
    public void Terminator_Cr_Lf_CrLf_all_yield_one_frame(string wire)
    {
        var p = new ScanFrameParser();
        var frames = p.Feed(B(wire));
        Assert.Equal(new[] { "A91I10001" }, frames);
    }

    [Fact]
    public void Two_frames_in_one_chunk()
    {
        var p = new ScanFrameParser();
        Assert.Equal(new[] { "AAA", "BBB" }, p.Feed(B("AAA\rBBB\r")));
    }

    [Fact]
    public void Frame_split_across_chunks_is_reassembled()
    {
        var p = new ScanFrameParser();
        Assert.Empty(p.Feed(B("A91I")));
        Assert.Equal(new[] { "A91I10001" }, p.Feed(B("10001\r")));
    }

    [Fact]
    public void Empty_and_whitespace_only_frames_are_dropped()
    {
        var p = new ScanFrameParser();
        Assert.Empty(p.Feed(B("\r\n\r  \r")));
        Assert.Equal(new[] { "X" }, p.Feed(B(" X \r")));
    }

    [Fact]
    public void Oversized_frame_is_discarded_then_parser_recovers()
    {
        var p = new ScanFrameParser();
        var junk = new string('Z', ScanFrameParser.MaxFrameBytes + 100) + "\r";
        Assert.Empty(p.Feed(B(junk)));
        Assert.Equal(1, p.OverflowCount);
        Assert.Equal(new[] { "OK" }, p.Feed(B("OK\r")));
    }

    [Fact]
    public void Frame_exactly_at_limit_is_kept()
    {
        var p = new ScanFrameParser();
        var s = new string('Z', ScanFrameParser.MaxFrameBytes);
        Assert.Equal(new[] { s }, p.Feed(B(s + "\r")));
        Assert.Equal(0, p.OverflowCount);
    }
}
