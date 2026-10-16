using System.IO.Ports;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

public class SerialScannerReaderTests
{
    // 실제 포트가 없는 이름 — 열기 실패 경로(로그·재시도·종료)만 검증한다.
    const string MissingPort = "COM250";

    [Fact]
    public void Missing_port_logs_once_stays_disconnected_and_disposes_promptly()
    {
        if (SerialPort.GetPortNames().Contains(MissingPort, StringComparer.OrdinalIgnoreCase))
            return;

        var log    = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var frames = new List<string>();
        var conn   = new List<bool>();
        var reader = new SerialScannerReader(MissingPort, reconnectMs: 50, frames.Add, log.Enqueue);
        reader.ConnectionChanged += conn.Add;

        reader.Start();
        Thread.Sleep(400);   // 50ms 재시도가 여러 번 돌 시간

        var sw = System.Diagnostics.Stopwatch.StartNew();
        reader.Dispose();
        sw.Stop();

        Assert.False(reader.IsConnected);
        Assert.Empty(frames);
        Assert.Empty(conn);
        Assert.Single(log, m => m.StartsWith(MissingPort));   // 같은 오류는 한 번만
        Assert.True(sw.ElapsedMilliseconds < 1500, $"dispose took {sw.ElapsedMilliseconds}ms");
    }
}
