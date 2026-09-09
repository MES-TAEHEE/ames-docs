using AMES.Devices;
using Xunit;

namespace AMES.Pop.Tests;

public class BadgeScanParserTests
{
    [Fact]
    public void Eos_badge_yields_worker_no_and_name()
    {
        var scan = BadgeScanParser.Parse("EOS*W004*Bae Yu-na");
        Assert.True(scan.IsEosFormat);
        Assert.Equal("W004", scan.WorkerNo);
        Assert.Equal("Bae Yu-na", scan.WorkerName);
    }

    [Fact]
    public void Prefix_match_is_case_insensitive()
        => Assert.True(BadgeScanParser.Parse("eos*W004*Bae Yu-na").IsEosFormat);

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_every_token()
    {
        var scan = BadgeScanParser.Parse("  EOS* W004 * Bae Yu-na \r");
        Assert.Equal("W004", scan.WorkerNo);
        Assert.Equal("Bae Yu-na", scan.WorkerName);
    }

    [Fact]
    public void Star_in_the_name_is_kept_because_only_the_first_two_separators_split()
        => Assert.Equal("Kim *Star* Lee",
            BadgeScanParser.Parse("EOS*W010*Kim *Star* Lee").WorkerName);

    [Fact]
    public void Empty_name_field_yields_null_name_but_still_parses()
    {
        var scan = BadgeScanParser.Parse("EOS*W004*");
        Assert.True(scan.IsEosFormat);
        Assert.Equal("W004", scan.WorkerNo);
        Assert.Null(scan.WorkerName);
    }

    [Fact]
    public void Eos_badge_without_a_worker_no_is_not_an_eos_badge()
    {
        // 사번이 없으면 로그인할 대상이 없다 — 통째로 사번 취급해 인증에서 떨어뜨린다.
        var scan = BadgeScanParser.Parse("EOS**Bae Yu-na");
        Assert.False(scan.IsEosFormat);
        Assert.Equal("EOS**Bae Yu-na", scan.WorkerNo);
    }

    [Fact]
    public void Eos_badge_missing_the_name_separator_is_not_an_eos_badge()
        => Assert.False(BadgeScanParser.Parse("EOS*W004").IsEosFormat);

    [Fact]
    public void Plain_badge_number_passes_through_as_the_worker_no()
    {
        // 이 포맷 이전에 뽑아둔 사번-only QR. 로그인은 되고 자동 등록만 안 된다.
        var scan = BadgeScanParser.Parse("  W004 \r");
        Assert.False(scan.IsEosFormat);
        Assert.Equal("W004", scan.WorkerNo);
        Assert.Null(scan.WorkerName);
    }

    [Fact]
    public void Web_account_badge_still_passes_through_untouched()
        => Assert.Equal("E005", BadgeScanParser.Parse("E005").WorkerNo);

    [Fact]
    public void Word_starting_with_eos_is_not_mistaken_for_the_prefix()
        => Assert.False(BadgeScanParser.Parse("EOSW004").IsEosFormat);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    public void Blank_scan_yields_an_empty_worker_no(string raw)
        => Assert.Equal(string.Empty, BadgeScanParser.Parse(raw).WorkerNo);
}
