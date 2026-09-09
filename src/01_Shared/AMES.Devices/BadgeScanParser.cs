namespace AMES.Devices;

/// <summary>One badge scan, already split into the parts POP login needs.</summary>
/// <param name="WorkerNo">사번. EOS 포맷이면 두 번째 토큰, 아니면 스캔값 전체.</param>
/// <param name="WorkerName">EOS 포맷의 세 번째 토큰. 비어 있거나 EOS 포맷이 아니면 null.</param>
/// <param name="IsEosFormat">우리가 발행한 EOS 배지로 해석됐는지. 자동 등록 여부를 가른다.</param>
public readonly record struct BadgeScan(string WorkerNo, string? WorkerName, bool IsEosFormat);

/// <summary>
/// 사원증 QR 파서. 발행 양식은 <c>EOS*사번*이름</c> 세 토큰이다.
///
/// 그 양식이 아니면 스캔값 전체를 사번으로 본다 — 이 양식 이전에 뽑아둔 사번-only
/// QR 과 웹 계정 배지가 계속 로그인돼야 하기 때문이다. 다만 그 경로는 이름을 모르므로
/// 자동 등록 대상이 아니다.
/// </summary>
public static class BadgeScanParser
{
    private const string Prefix = "EOS*";

    public static BadgeScan Parse(string? raw)
    {
        var scan = (raw ?? string.Empty).Trim();

        if (scan.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            // 3개로만 자른다 — 이름에 '*' 가 들어와도 이름 쪽에 그대로 남는다.
            var parts = scan.Split('*', 3);
            if (parts.Length == 3)
            {
                var no   = parts[1].Trim();
                var name = parts[2].Trim();
                // 사번이 없으면 로그인할 대상이 없다. EOS 로 인정하지 않고
                // 통째로 사번 취급해 인증에서 떨어지게 둔다.
                if (no.Length > 0)
                    return new BadgeScan(no, name.Length > 0 ? name : null, true);
            }
        }

        return new BadgeScan(scan, null, false);
    }
}
