using System.Text;

namespace AMES.Pop.Services;

/// <summary>
/// 시리얼 바이트 스트림을 스캔 프레임으로 자른다. 스캐너 suffix 는 CR 로 맞췄지만
/// LF 도 종결자로 받아 CRLF 설정 실수를 흡수한다.
/// 종결자 없이 상한을 넘는 입력은 다음 종결자까지 통째로 버린다 — 잘린 앞부분만
/// 프레임으로 내보내면 존재하지 않는 LOT 코드로 확정 시도가 나간다.
/// </summary>
internal sealed class ScanFrameParser
{
    public const int MaxFrameBytes = 512;

    private readonly byte[] _buf = new byte[MaxFrameBytes];
    private int  _len;
    private bool _discarding;

    public int OverflowCount { get; private set; }

    public List<string> Feed(ReadOnlySpan<byte> chunk)
    {
        var frames = new List<string>();
        foreach (var b in chunk)
        {
            if (b is (byte)'\r' or (byte)'\n')
            {
                if (!_discarding && _len > 0)
                {
                    var s = Encoding.ASCII.GetString(_buf, 0, _len).Trim();
                    if (s.Length > 0) frames.Add(s);
                }
                _len        = 0;
                _discarding = false;
                continue;
            }
            if (_discarding) continue;
            if (_len == MaxFrameBytes)
            {
                OverflowCount++;
                _len        = 0;
                _discarding = true;
                continue;
            }
            _buf[_len++] = b;
        }
        return frames;
    }
}
