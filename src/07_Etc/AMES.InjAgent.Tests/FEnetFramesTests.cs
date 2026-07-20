using System.Text;
using AMES.InjAgent.Plc;
using Xunit;

namespace AMES.InjAgent.Tests;

public class FEnetFramesTests
{
    [Fact]
    public void SelectCpu_frame_matches_original_26_bytes()
    {
        var f = FEnetFrames.BuildSelectCpu();
        Assert.Equal(26, f.Length);
        Assert.Equal("LGIS-GLOFA", Encoding.ASCII.GetString(f, 0, 10));
        Assert.Equal(0x33, f[13]);
        Assert.Equal(0x06, f[16]);
        Assert.Equal(0xb0, f[20]);
    }

    [Fact]
    public void BitDeviceXgb_encodes_point_as_word_plus_hex_bit()
    {
        Assert.Equal("%DX50020", FEnetFrames.BitDeviceXgb(5000, 32)); // D5002.0 (LOT 전송완료)
        Assert.Equal("%DX50021", FEnetFrames.BitDeviceXgb(5000, 33)); // D5002.1 (수집완료)
        Assert.Equal("%DX5000F", FEnetFrames.BitDeviceXgb(5000, 15));
    }

    [Fact]
    public void ContinuousDevice_doubles_word_address()
    {
        Assert.Equal("%DB10000", FEnetFrames.ContinuousDevice(5000)); // 블록 베이스
        Assert.Equal("%DB10200", FEnetFrames.ContinuousDevice(5100)); // 1st LOT 문자열
    }

    [Fact]
    public void BuildRead_produces_continuous_read_frame()
    {
        var f = FEnetFrames.BuildRead("%DB10000", 96); // 48워드 = 96바이트
        Assert.Equal("LSIS-XGT", Encoding.ASCII.GetString(f, 0, 8));
        Assert.Equal(0x54, f[20]);                       // Read
        Assert.Equal(0x14, f[22]);                       // Continue
        Assert.Equal(0x01, f[26]);                       // 블록 수
        int addrLen = f[28] | f[29] << 8;
        Assert.Equal(8, addrLen);
        Assert.Equal("%DB10000", Encoding.ASCII.GetString(f, 30, addrLen));
        Assert.Equal(96, f[30 + addrLen] | f[31 + addrLen] << 8); // 데이터 길이
        Assert.Equal(20 + 12 + addrLen, f.Length);
        // 헤더 길이 필드 = 명령부 길이
        Assert.Equal(12 + addrLen, f[16] | f[17] << 8);
    }

    [Fact]
    public void BuildWrite_bit_frame_carries_one_data_byte()
    {
        var f = FEnetFrames.BuildWrite("%DX50020", FEnetFrames.DataType.Bit, new byte[] { 1 });
        Assert.Equal(0x58, f[20]);                       // Write
        Assert.Equal(0x00, f[22]);                       // Bit
        int addrLen = f[28] | f[29] << 8;
        Assert.Equal("%DX50020", Encoding.ASCII.GetString(f, 30, addrLen));
        Assert.Equal(1, f[30 + addrLen] | f[31 + addrLen] << 8);
        Assert.Equal(1, f[^1]);                          // 데이터 본문
        Assert.Equal(20 + 12 + addrLen + 1, f.Length);
    }

    [Fact]
    public void Header_bcc_is_mod256_sum_of_first_18_bytes()
    {
        var f = FEnetFrames.BuildRead("%DB10000", 96);
        int sum = 0;
        for (int i = 0; i < 18; i++) sum = (sum + f[i]) & 0xFF;
        Assert.Equal(sum, f[19]);
    }

    // ── 응답 파싱: PLC_Simulator core/fenet_server.py 의 응답 형식을 그대로 재현 ──
    static byte[] SimReadResponse(byte[] data)
    {
        var hdr = new byte[20];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(hdr, 0);
        var instr = new byte[12];
        instr[0] = 0x55;
        instr[2] = 0x14;
        instr[8] = 0x01;
        instr[10] = (byte)(data.Length & 0xFF);
        instr[11] = (byte)(data.Length >> 8);
        return hdr.Concat(instr).Concat(data).ToArray();
    }

    static byte[] SimWriteResponse()
    {
        var hdr = new byte[20];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(hdr, 0);
        var instr = new byte[10];
        instr[0] = 0x59;
        return hdr.Concat(instr).ToArray();
    }

    [Fact]
    public void TryParseReadResponse_extracts_payload()
    {
        var payload = new byte[96];
        payload[0] = 0xAB;
        Assert.True(FEnetFrames.TryParseReadResponse(SimReadResponse(payload), out var data, out var err));
        Assert.Equal(96, data.Length);
        Assert.Equal(0xAB, data[0]);
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void TryParseReadResponse_rejects_error_status()
    {
        var rsp = SimReadResponse(new byte[4]);
        rsp[26] = 0xFF; rsp[27] = 0xFF; rsp[28] = 0x11;  // Address Format Error
        Assert.False(FEnetFrames.TryParseReadResponse(rsp, out _, out var err));
        Assert.Contains("Address", err);
    }

    [Fact]
    public void TryParseWriteResponse_accepts_simulator_ack()
    {
        Assert.True(FEnetFrames.TryParseWriteResponse(SimWriteResponse(), out var err));
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void TryParseWriteResponse_rejects_oversized_response()
    {
        var rsp = SimWriteResponse().Concat(new byte[4]).ToArray();  // TCP 병합 프레임 가정
        Assert.False(FEnetFrames.TryParseWriteResponse(rsp, out var err));
        Assert.Contains("Length", err);
    }

    [Fact]
    public void TryParseWriteResponse_rejects_wrong_command()
    {
        var rsp = SimWriteResponse();
        rsp[20] = 0x55;
        Assert.False(FEnetFrames.TryParseWriteResponse(rsp, out _));
    }
}
