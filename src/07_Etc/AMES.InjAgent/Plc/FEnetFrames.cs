using System.Text;

namespace AMES.InjAgent.Plc;

/// <summary>
/// LSIS FEnet(XGB/XGT) 프레임 조립·파싱. 원본 AxFEnet.cs 의
/// TransSendDataRead/Write · PlcReadcheck/PlcWritecheck · BCC_Check 를
/// 소켓과 분리한 순수 함수로 재구성한 것. 프레임 레이아웃은 원본과 바이트 단위로 동일.
/// 주의: 읽기/쓰기 프레임 헤더는 XGB(LSIS-XGT companyId) 전용. XGT 실장비는 LGIS-GLOFA 헤더 변형이 필요하며 현재 미지원.
/// </summary>
public static class FEnetFrames
{
    public enum DataType : byte { Bit = 0x00, Byte = 0x01, Word = 0x02, Continue = 0x14 }

    const int HeaderSize = 20;

    /// <summary>기종 판별 요청 (LGIS-GLOFA, cmd 0xb0). 원본 SelectCPU 와 동일한 26바이트.</summary>
    public static byte[] BuildSelectCpu()
    {
        var req = new byte[26];
        Encoding.ASCII.GetBytes("LGIS-GLOFA").CopyTo(req, 0);
        req[13] = 0x33;
        req[16] = 0x06;
        req[20] = 0xb0;
        return req;
    }

    /// <summary>SelectCPU 응답이 XGT 인지 (res[16]==0x24 && res[20]==0xb1). 아니면 XGB.</summary>
    public static bool IsXgtSelectResponse(ReadOnlySpan<byte> res)
        => res.Length > 20 && res[16] == 0x24 && res[20] == 0xb1;

    /// <summary>XGB 비트 주소: %DX{베이스워드+point/16:0000}{point%16:X}. 예) point 32 → %DX50020 (D5002.0)</summary>
    public static string BitDeviceXgb(int baseWord, int point)
    {
        if (point < 0) throw new ArgumentOutOfRangeException(nameof(point));
        return $"%DX{baseWord + point / 16:0000}{point % 16:X}";
    }

    /// <summary>XGT 비트 주소: %DX{베이스워드*16+point:00000}</summary>
    public static string BitDeviceXgt(int baseWord, int point)
        => $"%DX{baseWord * 16 + point:00000}";

    /// <summary>연속(바이트) 주소: %DB{워드주소*2:0000}. 예) D5100 → %DB10200</summary>
    public static string ContinuousDevice(int wordAddr) => $"%DB{wordAddr * 2:0000}";

    public static byte[] BuildRead(string device, int byteLen)
    {
        var addr = Encoding.ASCII.GetBytes(device);
        var instr = new byte[addr.Length + 12];
        instr[0] = 0x54;                                 // Read
        instr[2] = (byte)DataType.Continue;
        instr[6] = 0x01;                                 // 블록 수
        instr[8] = (byte)(addr.Length & 0xFF);
        instr[9] = (byte)(addr.Length >> 8);
        addr.CopyTo(instr, 10);
        instr[10 + addr.Length] = (byte)(byteLen & 0xFF);
        instr[11 + addr.Length] = (byte)(byteLen >> 8);
        return Frame(instr.Length, instr, Array.Empty<byte>());
    }

    public static byte[] BuildWrite(string device, DataType type, byte[] data)
    {
        var addr = Encoding.ASCII.GetBytes(device);
        var instr = new byte[addr.Length + 12];
        instr[0] = 0x58;                                 // Write
        instr[2] = (byte)type;
        instr[6] = 0x01;
        instr[8] = (byte)(addr.Length & 0xFF);
        instr[9] = (byte)(addr.Length >> 8);
        addr.CopyTo(instr, 10);
        instr[10 + addr.Length] = (byte)(data.Length & 0xFF);
        instr[11 + addr.Length] = (byte)(data.Length >> 8);
        return Frame(instr.Length + data.Length, instr, data);
    }

    static byte[] Frame(int instructionLen, byte[] instr, byte[] data)
    {
        var header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(header, 0);
        header[13] = 0x33;                               // Source of Frame
        header[16] = (byte)(instructionLen & 0xFF);      // Length (LE)
        header[17] = (byte)(instructionLen >> 8);
        header[19] = Bcc(header, 0, 18);
        var frame = new byte[HeaderSize + instr.Length + data.Length];
        header.CopyTo(frame, 0);
        instr.CopyTo(frame, HeaderSize);
        data.CopyTo(frame, HeaderSize + instr.Length);
        return frame;
    }

    static byte Bcc(ReadOnlySpan<byte> b, int start, int endExclusive)
    {
        int sum = 0;
        for (int i = start; i < endExclusive; i++) sum = (sum + b[i]) & 0xFF;
        return (byte)sum;
    }

    public static bool TryParseReadResponse(byte[] rsp, out byte[] data, out string error)
    {
        data = Array.Empty<byte>();
        error = string.Empty;
        const int instrSize = 12;
        if (rsp.Length <= HeaderSize + instrSize) { error = "Receive Data Length Error"; return false; }
        if (rsp[HeaderSize] != 0x55 || rsp[HeaderSize + 1] != 0x00) { error = "Read response Error"; return false; }
        if (rsp[HeaderSize + 6] == 0xFF && rsp[HeaderSize + 7] == 0xFF)
        {
            error = ErrorText(rsp[HeaderSize + 8]);
            return false;
        }
        int dataLen = rsp[HeaderSize + 10] | rsp[HeaderSize + 11] << 8;
        if (rsp.Length != HeaderSize + instrSize + dataLen) { error = "Data Length Error"; return false; }
        data = new byte[dataLen];
        Buffer.BlockCopy(rsp, HeaderSize + instrSize, data, 0, dataLen);
        return true;
    }

    public static bool TryParseWriteResponse(byte[] rsp, out string error)
    {
        error = string.Empty;
        const int instrSize = 10;
        if (rsp.Length < HeaderSize + instrSize) { error = "Receive Data Length Error"; return false; }
        if (rsp[HeaderSize] != 0x59 || rsp[HeaderSize + 1] != 0x00) { error = "Write response Error"; return false; }
        if (rsp[HeaderSize + 6] == 0xFF && rsp[HeaderSize + 7] == 0xFF)
        {
            error = ErrorText(rsp[HeaderSize + 8]);
            return false;
        }
        if (rsp.Length != HeaderSize + instrSize) { error = "Receive Data Length Error"; return false; }
        return true;
    }

    static string ErrorText(byte code) => code switch
    {
        0x24 => "Data Type Error",
        0x75 => "Header Error",
        0x76 => "Header Length Error",
        0x77 => "Checksum Error",
        0x78 => "Unknown Command Error",
        0x10 => "Device Type Error",
        0x11 => "Address Format Error",
        0x12 => "Data Error",
        _    => $"Unknown Error - {code:X2}",
    };
}
