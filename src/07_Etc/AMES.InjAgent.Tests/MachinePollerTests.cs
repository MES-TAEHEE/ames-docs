using AMES.Contracts.Dto;
using AMES.InjAgent.Core;
using Xunit;

namespace AMES.InjAgent.Tests;

public class MachinePollerTests
{
    // ── fakes ────────────────────────────────────────────────────────────
    sealed class FakeMachine : IInjectionMachine
    {
        public long ShotCount;
        public string MoldRaw = "LQ2DTMDCBK";
        public Dictionary<int, long>  Longs  = new();
        public Dictionary<int, float> Floats = new();
        public bool Connected => true;
        public bool EnsureConnected() => true;
        public long   ReadShotCount()     => ShotCount;
        public string ReadMoldCode()      => MoldRaw;
        public long   ReadLong(int addr)  => Longs.TryGetValue(addr, out var v) ? v : 0;
        public float  ReadFloat(int addr) => Floats.TryGetValue(addr, out var v) ? v : 0f;
        public void Disconnect() { }
    }

    sealed class FakeRobot : IRobotLink
    {
        public Dictionary<(int Word, int Bit), int> Bits = new();
        public List<(int Point, bool On)>           BitWrites    = new();
        public List<(int Addr, string Value)>       StringWrites = new();
        public bool Connected => true;
        public bool EnsureConnected() => true;
        public bool RefreshBlock() => true;
        public int  ReadBit(int word, int bit) => Bits.TryGetValue((word, bit), out var v) ? v : 0;
        public bool WriteBit(int point, bool on) { BitWrites.Add((point, on)); return true; }
        public bool WriteString(int addr, string value) { StringWrites.Add((addr, value)); return true; }
        public void Disconnect() { }

        public void SetJudgement(int word, int item, string result)
        {
            // item 0..3, result OK/NG/PASS → 비트 3i / 3i+1 / 3i+2
            int bit = item * 3 + result switch { "OK" => 0, "NG" => 1, _ => 2 };
            Bits[(word, bit)] = 1;
        }
    }

    sealed class FakeStore : IInjAgentStore
    {
        public List<MoldItemMapDto> Map = new()
        {
            new() { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "ITEM-LH", MoldId = "MOLD-1" },
            new() { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 2, CavityPos = "RH", ItemNo = "ITEM-RH", MoldId = "MOLD-1" },
        };
        public List<InjCondItemDto> CondItems = new();
        public List<(string LineId, string EquipId, MoldItemMapDto Map, long Shot)> Created = new();
        public List<(int LotId, bool Ng)>     Inspections = new();
        public List<int>                      NgBlocked   = new();
        public List<(string Item, long Seq, decimal? Set, decimal? Act)> CondLogs = new();
        public Func<MoldItemMapDto, bool>? FailCreateFor;
        public bool FailSaveInspection;
        int _nextLotId = 100;

        public List<MoldItemMapDto> GetMoldItems(string moldCode, string colorCode)
            => Map.Where(m => m.MoldCode == moldCode && m.ColorCode == colorCode).ToList();
        public (int, string) CreateRawLot(string lineId, string equipId, MoldItemMapDto map, long shot)
        {
            if (FailCreateFor?.Invoke(map) == true) throw new InvalidOperationException("db down");
            Created.Add((lineId, equipId, map, shot));
            int id = _nextLotId++;
            return (id, $"LOT-{id}-{map.CavityPos}");
        }
        public void SaveInspection(int lotId, string equipId, string cavityPos,
            string p1, string p2, string p3, string p4, bool overallNg)
        {
            if (FailSaveInspection) throw new InvalidOperationException("db down");
            Inspections.Add((lotId, overallNg));
        }
        public void MarkNgBlocked(int lotId) => NgBlocked.Add(lotId);
        public List<InjCondItemDto> GetCondItems(string lineId) => CondItems;
        public void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? s, decimal? a)
            => CondLogs.Add((itemCode, shotSeq, s, a));
    }

    sealed class FakePrinter : ILabelPrinter
    {
        public List<string> Printed = new();
        public void PrintLabel(string lotCode, string itemNo, string? itemName,
                               string? colorCode, string? cavityPos, string lineId)
            => Printed.Add(lotCode);
    }

    static (MachinePoller Poller, FakeMachine M, FakeRobot R, FakeStore S, FakePrinter P) Build()
    {
        var m = new FakeMachine();
        var r = new FakeRobot();
        var s = new FakeStore();
        var p = new FakePrinter();
        var cfg = new MachineConfig { EquipId = "INJ-650-01", LineId = "LINE-INJ-01", ModbusIp = "x", FenetIp = "y" };
        var poller = new MachinePoller(cfg, m, r, s, p, _ => { });
        return (poller, m, r, s, p);
    }

    [Fact]
    public void First_poll_sets_baseline_without_creating_lot()
    {
        var (poller, m, _, s, _) = Build();
        m.ShotCount = 55;
        poller.PollOnce();
        Assert.Empty(s.Created);

        poller.PollOnce();               // 변화 없음 → 여전히 생성 없음
        Assert.Empty(s.Created);
    }

    [Fact]
    public void Shot_count_change_creates_lot_per_cavity_and_prints()
    {
        var (poller, m, r, s, p) = Build();
        m.ShotCount = 55;
        poller.PollOnce();               // baseline

        m.ShotCount = 56;
        poller.PollOnce();

        Assert.Equal(2, s.Created.Count);                       // LH + RH
        Assert.Equal("LH", s.Created[0].Map.CavityPos);
        Assert.Equal("RH", s.Created[1].Map.CavityPos);
        Assert.Equal(56, s.Created[0].Shot);
        Assert.Equal(2, p.Printed.Count);
        // 로봇 송신: 금형(5700) + 1st LOT(5100)/품번(5800) + 2nd LOT(5200)/품번(5900)
        Assert.Contains(r.StringWrites, w => w.Addr == 5700 && w.Value == "LQ2DTMDCBK");
        Assert.Contains(r.StringWrites, w => w.Addr == 5100);
        Assert.Contains(r.StringWrites, w => w.Addr == 5200);
        Assert.Contains(r.StringWrites, w => w.Addr == 5800 && w.Value == "ITEM-LH");
        Assert.Contains(r.StringWrites, w => w.Addr == 5900 && w.Value == "ITEM-RH");
        Assert.Contains(r.BitWrites, w => w.Point == 32 && w.On);
    }

    [Fact]
    public void Mold_code_change_alone_triggers_shot()
    {
        var (poller, m, _, s, _) = Build();
        m.ShotCount = 55;
        m.MoldRaw = "LQ2DTMDCBK";
        poller.PollOnce();               // baseline

        m.MoldRaw = "LQ2DTRUCBK";
        // 매핑 추가
        s.Map.Add(new MoldItemMapDto { MoldCode = "LQ2DTRU", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "I3", MoldId = "MOLD-2" });
        poller.PollOnce();
        Assert.Single(s.Created);
        Assert.Equal("LQ2DTRU", s.Created[0].Map.MoldCode);
    }

    [Fact]
    public void Unknown_mold_map_creates_nothing()
    {
        var (poller, m, _, s, _) = Build();
        m.ShotCount = 1;
        m.MoldRaw = "UNKNOWNXXX";
        poller.PollOnce();               // baseline
        m.ShotCount = 2;
        poller.PollOnce();
        Assert.Empty(s.Created);
    }

    [Fact]
    public void Resends_to_robot_while_ack_bit_low()
    {
        var (poller, m, r, _, _) = Build();
        m.ShotCount = 1;
        poller.PollOnce();               // baseline
        m.ShotCount = 2;
        poller.PollOnce();               // 샷 → 송신 1회

        r.StringWrites.Clear();
        r.Bits[(5002, 0)] = 0;           // 로봇 미확인
        poller.PollOnce();               // 재송신
        Assert.Contains(r.StringWrites, w => w.Addr == 5700);

        r.StringWrites.Clear();
        r.Bits[(5002, 0)] = 1;           // 로봇 확인됨
        poller.PollOnce();
        Assert.Empty(r.StringWrites);    // 게이트 닫힘
    }

    [Fact]
    public void Inspection_all_ok_saved_once_with_bit33()
    {
        var (poller, m, r, s, _) = Build();
        m.ShotCount = 1;
        poller.PollOnce();
        m.ShotCount = 2;
        poller.PollOnce();               // LH/RH LOT 생성

        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "OK");
        poller.PollOnce();
        Assert.Single(s.Inspections);
        Assert.False(s.Inspections[0].Ng);
        Assert.Empty(s.NgBlocked);
        Assert.Contains(r.BitWrites, w => w.Point == 33 && w.On);

        poller.PollOnce();               // 같은 판정 유지 → 중복 저장 없음
        Assert.Single(s.Inspections);
    }

    [Fact]
    public void Inspection_ng_blocks_lot()
    {
        var (poller, m, r, s, _) = Build();
        m.ShotCount = 1;
        poller.PollOnce();
        m.ShotCount = 2;
        poller.PollOnce();

        r.SetJudgement(5007, 0, "OK");
        r.SetJudgement(5007, 1, "OK");
        r.SetJudgement(5007, 2, "OK");
        r.SetJudgement(5007, 3, "NG");   // 중량 NG
        poller.PollOnce();

        Assert.Single(s.Inspections);
        Assert.True(s.Inspections[0].Ng);
        Assert.Single(s.NgBlocked);
        Assert.Equal(s.Inspections[0].LotId, s.NgBlocked[0]);
    }

    [Fact]
    public void Incomplete_inspection_not_saved()
    {
        var (poller, m, r, s, _) = Build();
        m.ShotCount = 1;
        poller.PollOnce();
        m.ShotCount = 2;
        poller.PollOnce();

        r.SetJudgement(5007, 0, "OK");   // 4항목 중 1개만 도착
        poller.PollOnce();
        Assert.Empty(s.Inspections);
    }

    [Fact]
    public void Shot_collects_condition_values()
    {
        var (poller, m, _, s, _) = Build();
        s.CondItems.Add(new InjCondItemDto { ItemCode = "TEMP",  SetAddress = 5400, ActualAddress = 5404, DataType = "FLOAT" });
        s.CondItems.Add(new InjCondItemDto { ItemCode = "PRESS", SetAddress = 5410, ActualAddress = 5414, DataType = "LONG"  });
        m.Floats[5400] = 235.5f; m.Floats[5404] = 234.9f;
        m.Longs[5410] = 850;     m.Longs[5414] = 847;

        m.ShotCount = 1;
        poller.PollOnce();
        m.ShotCount = 2;
        poller.PollOnce();

        Assert.Equal(2, s.CondLogs.Count);
        var temp = s.CondLogs.Single(c => c.Item == "TEMP");
        Assert.Equal(2, temp.Seq);
        Assert.Equal(235.5m, temp.Set!.Value, 1);
        Assert.Equal(234.9m, temp.Act!.Value, 1);
        var press = s.CondLogs.Single(c => c.Item == "PRESS");
        Assert.Equal(850m, press.Set);
        Assert.Equal(847m, press.Act);
    }

    [Fact]
    public void Partial_cavity_failure_keeps_rh_in_second_slot()
    {
        var (poller, m, r, s, _) = Build();
        m.ShotCount = 1; poller.PollOnce();
        s.FailCreateFor = map => map.CavityPos == "LH";
        m.ShotCount = 2; poller.PollOnce();

        Assert.Single(s.Created);                        // RH 만 생성
        Assert.Equal("RH", s.Created[0].Map.CavityPos);
        Assert.DoesNotContain(r.StringWrites, w => w.Addr == 5100);   // 1st 슬롯 비어있음
        Assert.Contains(r.StringWrites, w => w.Addr == 5200);         // RH 는 2nd 주소 유지

        // RH 검사는 D5008 로 수신되어 RH LOT 에 귀속
        for (int item = 0; item < 4; item++) r.SetJudgement(5008, item, "OK");
        poller.PollOnce();
        Assert.Single(s.Inspections);
    }

    [Fact]
    public void Inspection_save_failure_retries_next_poll_without_bit33()
    {
        var (poller, m, r, s, _) = Build();
        m.ShotCount = 1; poller.PollOnce();
        m.ShotCount = 2; poller.PollOnce();              // 샷 + 같은 폴에서 클리어 관측(armed)

        s.FailSaveInspection = true;
        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "OK");
        r.BitWrites.Clear();
        poller.PollOnce();
        Assert.Empty(s.Inspections);
        Assert.DoesNotContain(r.BitWrites, w => w.Point == 33);

        s.FailSaveInspection = false;
        poller.PollOnce();                               // key 미소진 → 재시도 성공
        Assert.Single(s.Inspections);
        Assert.Contains(r.BitWrites, w => w.Point == 33 && w.On);
    }

    [Fact]
    public void Stale_judgement_held_over_shot_is_ignored_until_cleared()
    {
        var (poller, m, r, s, _) = Build();
        m.ShotCount = 1; poller.PollOnce();
        m.ShotCount = 2; poller.PollOnce();              // LOT 생성 + armed

        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "OK");
        poller.PollOnce();
        Assert.Single(s.Inspections);                    // 정상 저장

        m.ShotCount = 3;                                 // 로봇이 판정을 아직 유지한 채 다음 샷
        poller.PollOnce();
        poller.PollOnce();                               // 잔존 판정 — armed 전이라 무시
        Assert.Single(s.Inspections);

        r.Bits.Clear();                                  // 로봇 클리어
        poller.PollOnce();                               // armed
        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "NG");
        poller.PollOnce();                               // 새 판정 수용
        Assert.Equal(2, s.Inspections.Count);
        Assert.True(s.Inspections[1].Ng);
    }

    [Fact]
    public void Reset_for_restart_takes_fresh_baseline()
    {
        var (poller, m, _, s, _) = Build();
        m.ShotCount = 1; poller.PollOnce();
        m.ShotCount = 2; poller.PollOnce();
        Assert.Equal(2, s.Created.Count);          // LH+RH

        poller.ResetForRestart();
        m.ShotCount = 55;                          // 정지 중 카운터 변화 가정
        poller.PollOnce();                         // baseline만 — LOT 미생성
        Assert.Equal(2, s.Created.Count);
        m.ShotCount = 56;
        poller.PollOnce();                         // 이후 샷은 정상 수집
        Assert.Equal(4, s.Created.Count);
    }
}
