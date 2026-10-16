using System.Text.RegularExpressions;

namespace AMES.Devices;

/// <summary>
/// IMG 완제품 라벨 스캔 문자열에서 우리 LotNo(9자)를 뽑는다.
/// 스캐너는 DataMatrix 내용 전체를 보내므로 그 안의 T 토큰 끝 9자가 LotNo 다.
///   · 시리얼(USB CDC): RS/GS/EOT 제어문자가 그대로 온다 → GS 로 나눠 T 토큰을 찾는다.
///   · HID 키보드 웨지: 제어문자가 사라진 채 붙어서 온다 → T + yyMMdd + part4M + LotNo 패턴으로 찾는다.
///   · "[)>" 헤더가 없으면 단순 LotNo 라벨(INJ 양식)로 보고 그대로 돌려준다.
/// 인식 못 하면 null — 호출자가 "LOT 없음" 으로 처리한다.
/// </summary>
public static class ImgScanParser
{
    const char RS = '\u001E', GS = '\u001D', EOT = '\u0004';
    public const int LotCodeLength = 9;

    // T + 날짜6 + part4M("1" + 2자 + 교대 1자) + LotNo 9자
    static readonly Regex TToken = new(@"T\d{6}1[A-Z0-9]{2}[A-Z]([A-Z0-9]{9})", RegexOptions.Compiled);

    public static string? ExtractLotCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().TrimEnd(EOT, RS, GS).Trim();
        if (s.Length == 0) return null;

        var hdr = s.IndexOf("[)>", StringComparison.Ordinal);
        if (hdr < 0) return s;                      // 단순 LotNo 라벨
        s = s[(hdr + 3)..];

        if (s.IndexOf(GS) >= 0)
        {
            foreach (var field in s.Split(GS, RS, EOT))
            {
                if (field.Length > LotCodeLength && field[0] == 'T')
                    return field[^LotCodeLength..];
            }
        }

        var m = TToken.Match(s);
        return m.Success ? m.Groups[1].Value : null;
    }
}
