using System.Net;
using System.Net.Sockets;
using AMES.InjAgent.Plc;
using Xunit;

namespace AMES.InjAgent.Tests;

public class FEnetReadReassemblyTests
{
    // 읽기 응답: 헤더 20 + 명령부 12 + 데이터(48워드 = 96바이트). D5000 비트 0 = 1.
    static byte[] ReadResponse()
    {
        var rsp = new byte[20 + 12 + 96];
        rsp[20] = 0x55; rsp[21] = 0x00;
        rsp[30] = 96; rsp[31] = 0;
        rsp[32] = 0x01;           // D5000 = 0x0001
        return rsp;
    }

    [Fact]
    public void Incomplete_read_response_is_not_complete()
    {
        var rsp = ReadResponse();
        Assert.False(FEnetFrames.IsReadResponseComplete(rsp.AsSpan(0, 60).ToArray()));
        Assert.True(FEnetFrames.IsReadResponseComplete(rsp));
    }

    [Fact]
    public void Read_response_split_across_packets_is_reassembled()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var plc = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            var ns = c.GetStream();
            var buf = new byte[1024];
            await ns.ReadAsync(buf);                       // SelectCpu 요청
            await ns.WriteAsync(new byte[32]);             // 응답(기종 판별)
            await ns.ReadAsync(buf);                       // 블록 읽기 요청
            var rsp = ReadResponse();
            await ns.WriteAsync(rsp, 0, 40);               // 첫 조각만 먼저
            await Task.Delay(150);
            await ns.WriteAsync(rsp, 40, rsp.Length - 40); // 나머지
        });

        try
        {
            using var client = new FEnetClient("127.0.0.1", port);
            Assert.True(client.EnsureConnected());
            Assert.True(client.RefreshBlock());            // 두 조각을 모아 읽는다
            Assert.Equal(1, client.ReadBit(5000, 0));
            plc.Wait(5000);
        }
        finally
        {
            listener.Stop();
        }
    }
}
