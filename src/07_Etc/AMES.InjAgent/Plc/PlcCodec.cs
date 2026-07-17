namespace AMES.InjAgent.Plc;

/// <summary>
/// LS산전 사출기 Modbus 레지스터 디코더.
/// 원본 Ax.Injection_Agent(Main.cs ReadLongInt/ReadFloat/ReadAscii)와
/// PLC_Simulator(core/encoders.py)가 합의한 워드 페어-스왑 규칙 기준:
/// NModbus 가 돌려주는 ushort[] 에서는 "레지스터 0 = 최하위 워드"가 된다.
/// </summary>
public static class PlcCodec
{
    public static long ToInt64(ReadOnlySpan<ushort> regs)
    {
        if (regs.Length < 4) throw new ArgumentException("LONG needs 4 registers", nameof(regs));
        return regs[0]
             | (long)regs[1] << 16
             | (long)regs[2] << 32
             | (long)regs[3] << 48;
    }

    public static float ToFloat(ReadOnlySpan<ushort> regs)
    {
        if (regs.Length < 2) throw new ArgumentException("FLOAT needs 2 registers", nameof(regs));
        uint bits = regs[0] | (uint)regs[1] << 16;
        return BitConverter.UInt32BitsToSingle(bits);
    }

    public static string ToAscii(ReadOnlySpan<ushort> regs)
    {
        var chars = new char[regs.Length * 2];
        for (int i = 0; i < regs.Length; i++)
        {
            chars[i * 2]     = (char)(regs[i] & 0xFF);
            chars[i * 2 + 1] = (char)(regs[i] >> 8);
        }
        return new string(chars).TrimEnd('\0');
    }

    /// <summary>
    /// 금형코드 원문 → (금형코드, 색상코드). 색상 = 뒤 3자리,
    /// 금형 = '-' 제거 후 색상 문자열 제거 (원본 Main.cs:452-453 규칙 유지).
    /// 주의: 색상 문자열이 금형코드 부분에 부분일치하면 그 부분까지 함께 제거된다
    /// (원본과 동일한 동작 — 정합성 유지를 위해 고치지 말 것).
    /// </summary>
    public static (string MoldCode, string ColorCode) SplitMoldColor(string raw)
    {
        raw = raw.Trim();
        if (raw.Length <= 3) return (string.Empty, string.Empty);
        string color = raw[^3..];
        string mold  = raw.Replace("-", "").Replace(color, "");
        return (mold, color);
    }
}
