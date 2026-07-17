using AMES.Contracts.Dto;
using AMES.InjAgent.Core;
using Xunit;

namespace AMES.InjAgent.Tests;

public class PollerRunnerTests
{
    sealed class NopMachine : IInjectionMachine
    {
        public int DisconnectCount;
        public bool Connected => true;
        public bool EnsureConnected() => true;
        public long ReadShotCount() => 0;
        public string ReadMoldCode() => string.Empty;
        public long ReadLong(int address) => 0;
        public float ReadFloat(int address) => 0f;
        public void Disconnect() => DisconnectCount++;
    }

    sealed class NopRobot : IRobotLink
    {
        public int DisconnectCount;
        public bool Connected => true;
        public bool EnsureConnected() => true;
        public bool RefreshBlock() => true;
        public int ReadBit(int word, int bit) => 0;
        public bool WriteBit(int point, bool on) => true;
        public bool WriteString(int wordAddr, string value) => true;
        public void Disconnect() => DisconnectCount++;
    }

    sealed class SlowMachine : IInjectionMachine
    {
        public bool Connected => true;
        public bool EnsureConnected() { Thread.Sleep(3000); return true; }   // Wait(2000) 초과 유도
        public long ReadShotCount() => 0;
        public string ReadMoldCode() => string.Empty;
        public long ReadLong(int address) => 0;
        public float ReadFloat(int address) => 0f;
        public void Disconnect() { }
    }

    sealed class NopStore : IInjAgentStore
    {
        public List<MoldItemMapDto> GetMoldItems(string m, string c) => new();
        public (int, string) CreateRawLot(string l, string e, MoldItemMapDto m, long s) => (1, "L");
        public void SaveInspection(int a, string b, string c, string d, string e, string f, string g, bool h) { }
        public void MarkNgBlocked(int lotId) { }
        public List<InjCondItemDto> GetCondItems(string lineId) => new();
        public void InsertCondLog(string l, string i, long s, decimal? a, decimal? b) { }
    }

    sealed class NopPrinter : ILabelPrinter
    {
        public void PrintLabel(string a, string b, string? c, string? d, string? e, string f) { }
    }

    static (PollerRunner Runner, NopMachine M, NopRobot R) Build()
    {
        var m = new NopMachine();
        var r = new NopRobot();
        var cfg = new MachineConfig { EquipId = "EQ", LineId = "LN", ModbusIp = "x", FenetIp = "y" };
        var poller = new MachinePoller(cfg, m, r, new NopStore(), new NopPrinter(), _ => { });
        return (new PollerRunner(poller, pollingMs: 10, _ => { }), m, r);
    }

    [Fact]
    public void Start_is_idempotent_and_stop_disconnects()
    {
        var (runner, m, r) = Build();
        Assert.False(runner.IsRunning);

        runner.Start();
        runner.Start();                            // 중복 Start 무시
        Assert.True(runner.IsRunning);

        runner.Stop();
        Assert.False(runner.IsRunning);
        Assert.True(m.DisconnectCount >= 1);
        Assert.True(r.DisconnectCount >= 1);

        runner.Stop();                             // 중복 Stop 무시 (예외 없음)
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void Restart_after_stop_works()
    {
        var (runner, _, _) = Build();
        runner.Start();
        runner.Stop();
        runner.Start();                            // 재시작 가능해야 함
        Assert.True(runner.IsRunning);
        runner.Dispose();
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public void Stop_timeout_keeps_running_flag_until_loop_exits()
    {
        var cfg = new MachineConfig { EquipId = "EQ", LineId = "LN", ModbusIp = "x", FenetIp = "y" };
        var poller = new MachinePoller(cfg, new SlowMachine(), new NopRobot(), new NopStore(), new NopPrinter(), _ => { });
        var runner = new PollerRunner(poller, pollingMs: 10, _ => { });

        runner.Start();
        Thread.Sleep(100);                          // 루프가 느린 PollOnce 에 진입
        runner.Stop();                              // Wait(2000) 타임아웃 경로
        Assert.True(runner.IsRunning);              // 구 루프 생존 중 — 재시작 차단

        runner.Start();                             // 무시되어야 함 (이중 루프 방지)
        Assert.True(runner.IsRunning);

        // 구 루프가 취소 플래그로 종료될 때까지 대기 (느린 호출 3s + 여유)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (runner.IsRunning && sw.ElapsedMilliseconds < 6000) Thread.Sleep(100);
        Assert.False(runner.IsRunning);             // 이제 재시작 가능

        runner.Dispose();
    }
}
