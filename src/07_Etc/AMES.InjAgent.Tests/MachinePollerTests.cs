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
        public Func<int, bool>? FailBit;      // true 를 돌려주면 그 비트 쓰기는 실패한다
        public Func<int, bool>? FailString;   // true 를 돌려주면 그 주소의 문자열 쓰기는 실패한다
        public bool WriteBit(int point, bool on)
        {
            if (FailBit?.Invoke(point) == true) return false;
            BitWrites.Add((point, on));
            return true;
        }
        public bool WriteString(int addr, string value)
        {
            if (FailString?.Invoke(addr) == true) return false;
            StringWrites.Add((addr, value));
            return true;
        }
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
        public List<MoldItemDto> Map = new()
        {
            new() { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 1, CavityPos = "LH", ItemNo = "ITEM-LH", MoldId = "MOLD-1" },
            new() { MoldCode = "LQ2DTMD", ColorCode = "CBK", CavityNo = 2, CavityPos = "RH", ItemNo = "ITEM-RH", MoldId = "MOLD-1" },
        };
        public List<InjCondItemDto> CondItems = new();
        public List<(string LineId, string EquipId, MoldItemDto Map, long Shot)> Created = new();
        public List<(int LotId, bool Ng)>     Inspections = new();
        public List<int>                      NgBlocked   = new();
        public List<(string Item, long Seq, decimal? Set, decimal? Act)> CondLogs = new();
        public Func<MoldItemDto, bool>? FailCreateFor;
        public bool FailSaveInspection;
        int _nextLotId = 100;

        public List<MoldItemDto> GetMoldItems(string moldCode, string colorCode)
            => Map.Where(m => m.MoldCode == moldCode && m.ColorCode == colorCode).ToList();
        public (int, string) CreateRawLot(string lineId, string equipId, MoldItemDto map, long shot)
        {
            if (FailCreateFor?.Invoke(map) == true) throw new InvalidOperationException("db down");
            Created.Add((lineId, equipId, map, shot));
            int id = _nextLotId++;
            return (id, $"LOT-{id}-{map.CavityPos}");
        }
        public bool ThrowAfterSave;     // 커밋은 됐는데 응답만 예외
        public bool FailMarkNg;
        public void SaveInspection(int lotId, string equipId, string cavityPos,
            string p1, string p2, string p3, string p4, bool overallNg)
        {
            if (FailSaveInspection) throw new InvalidOperationException("db down");
            Inspections.Add((lotId, overallNg));
            if (ThrowAfterSave) throw new InvalidOperationException("commit done, reply lost");
        }
        public void MarkNgBlocked(int lotId)
        {
            if (FailMarkNg) throw new InvalidOperationException("ng mark db error");
            NgBlocked.Add(lotId);
        }
        public int CountInspections(int lotId, string cavityPos) => Inspections.Count(i => i.LotId == lotId);
        public List<InjCondItemDto> GetCondItems(string lineId) => CondItems;
        public void InsertCondLog(string lineId, string itemCode, long shotSeq, decimal? s, decimal? a)
            => CondLogs.Add((itemCode, shotSeq, s, a));
    }

    static (MachinePoller Poller, FakeMachine M, FakeRobot R, FakeStore S) Build()
    {
        var m = new FakeMachine();
        var r = new FakeRobot();
        var s = new FakeStore();
        var cfg = new MachineConfig { EquipId = "INJ-01-01-1800", LineId = "LINE-INJ-01", ModbusIp = "x", FenetIp = "y" };
        var poller = new MachinePoller(cfg, m, r, s, _ => { });
        return (poller, m, r, s);
    }

    [Fact]
    public void First_poll_sets_baseline_without_creating_lot()
    {
        var (poller, m, _, s) = Build();
        m.ShotCount = 55;
        poller.PollOnce();
        Assert.Empty(s.Created);

        poller.PollOnce();               // 변화 없음 → 여전히 생성 없음
        Assert.Empty(s.Created);
    }

    [Fact]
    public void Shot_count_change_creates_lot_per_cavity()
    {
        var (poller, m, r, s) = Build();
        m.ShotCount = 55;
        poller.PollOnce();               // baseline

        m.ShotCount = 56;
        poller.PollOnce();

        Assert.Equal(2, s.Created.Count);                       // LH + RH
        Assert.Equal("LH", s.Created[0].Map.CavityPos);
        Assert.Equal("RH", s.Created[1].Map.CavityPos);
        Assert.Equal(56, s.Created[0].Shot);
        // 로봇 송신: 금형(5700) + 1st LOT(5100)/품번(5800) + 2nd LOT(5200)/품번(5900)
        Assert.Contains(r.StringWrites, w => w.Addr == 5700 && w.Value == "LQ2DTMDCBK");
        Assert.Contains(r.StringWrites, w => w.Addr == 5100);
        Assert.Contains(r.StringWrites, w => w.Addr == 5200);
        Assert.Contains(r.StringWrites, w => w.Addr == 5800 && w.Value == "ITEM-LH");
        Assert.Contains(r.StringWrites, w => w.Addr == 5900 && w.Value == "ITEM-RH");
        Assert.Contains(r.BitWrites, w => w.Point == 32 && w.On);
    }

    [Fact]
    public void Shot_count_jump_creates_lot_for_every_skipped_shot()
    {
        var (poller, m, _, s) = Build();
        m.ShotCount = 55;
        poller.PollOnce();               // baseline

        m.ShotCount = 58;                // 56·57·58 세 샷이 한 폴링 사이에 지나감
        poller.PollOnce();

        Assert.Equal(6, s.Created.Count);                                         // 3샷 × 2캐비티
        Assert.Equal(new long[] { 56, 56, 57, 57, 58, 58 }, s.Created.Select(c => c.Shot).ToArray());
    }

    [Fact]
    public void Mold_code_change_alone_creates_no_lot()
    {
        var (poller, m, r, s) = Build();
        m.ShotCount = 55;
        poller.PollOnce();              // baseline
        m.MoldRaw = "LQ2DTMDCBK2";      // 샷은 그대로, 금형 코드만 바뀜
        poller.PollOnce();

        Assert.Empty(s.Created);        // LOT 은 만들지 않는다
    }

    [Fact]
    public void Mold_code_change_keeps_the_in_progress_lot_and_its_judgment()
    {
        var (poller, m, r, s) = Build();
        m.ShotCount = 55; poller.PollOnce();
        m.ShotCount = 56; poller.PollOnce();      // LOT 생성, 검사 armed
        m.MoldRaw = "LQ2DTMDCBK2";                // 금형 코드만 바뀜 — 진행 중 LOT 은 그대로
        poller.PollOnce();

        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "OK");
        poller.PollOnce();                        // 진행 중 LOT 의 판정은 버려지지 않는다
        Assert.Single(s.Inspections);

        // 로봇에 다시 보내는 금형 코드도 그 LOT 의 것(옛 금형)이어야 한다
        Assert.All(r.StringWrites.Where(w => w.Addr == 5700), w => Assert.Equal("LQ2DTMDCBK", w.Value));
    }

    [Fact]
    public void Counter_reset_creates_no_lot_and_counts_from_the_new_baseline()
    {
        var (poller, m, _, s) = Build();
        m.ShotCount = 55; poller.PollOnce();
        m.ShotCount = 56; poller.PollOnce();    // 정상 샷 → LOT 2개(LH·RH)
        Assert.Equal(2, s.Created.Count);

        m.ShotCount = 3; poller.PollOnce();     // 리셋 → LOT 추가 없음
        Assert.Equal(2, s.Created.Count);

        m.ShotCount = 4; poller.PollOnce();     // 새 기준선 3 에서 +1 → LOT 2개 추가
        Assert.Equal(4, s.Created.Count);
        Assert.All(s.Created.Skip(2), c => Assert.Equal(4, c.Shot));
    }

    [Fact]
    public void Unknown_mold_map_creates_nothing()
    {
        var (poller, m, _, s) = Build();
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
        var (poller, m, r, _) = Build();
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
        var (poller, m, r, s) = Build();
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
        var (poller, m, r, s) = Build();
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
        var (poller, m, r, s) = Build();
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
        var (poller, m, _, s) = Build();
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
    public void Failed_lot_creation_is_retried_and_succeeds()
    {
        var (poller, m, _, s) = Build();
        m.ShotCount = 1; poller.PollOnce();
        int tries = 0;
        s.FailCreateFor = map => map.CavityPos == "LH" && tries++ < 1;   // 첫 시도만 실패
        m.ShotCount = 2; poller.PollOnce();

        Assert.Contains(s.Created, c => c.Map.CavityPos == "LH");
        Assert.Equal(0, poller.Status.MissingLots);
    }

    [Fact]
    public void Lot_still_failing_after_retries_is_reported_as_missing()
    {
        var (poller, m, _, s) = Build();
        m.ShotCount = 1; poller.PollOnce();
        int tries = 0;
        s.FailCreateFor = map => { if (map.CavityPos == "LH") tries++; return map.CavityPos == "LH"; };
        m.ShotCount = 2; poller.PollOnce();

        Assert.Equal(3, tries);                                   // 3번 시도 후 포기
        Assert.Equal(1, poller.Status.MissingLots);               // 화면에 LOT 없음으로 표시
        Assert.Contains(s.Created, c => c.Map.CavityPos == "RH"); // RH 는 영향 없음
    }

    [Fact]
    public void Failed_robot_lot_write_keeps_ack_bit_off_until_all_values_are_sent()
    {
        var (poller, m, r, _) = Build();
        m.ShotCount = 1; poller.PollOnce();
        int tries = 0;
        r.FailString = addr => addr == 5100 && tries++ < 1;      // 1st LOT 쓰기가 한 번 실패
        m.ShotCount = 2; poller.PollOnce();

        Assert.DoesNotContain(r.BitWrites, w => w.Point == 32);  // 값이 빠진 채 켜지 않는다
        poller.PollOnce();                                       // 다음 주기에 다시 보낸다
        Assert.Contains(r.StringWrites, w => w.Addr == 5100);
        Assert.Contains(r.BitWrites, w => w.Point == 32 && w.On);
        Assert.Equal(0, poller.Status.RobotWriteFailures);
    }

    [Fact]
    public void Failed_inspection_ack_is_retried_without_saving_again()
    {
        var (poller, m, r, s) = Build();
        m.ShotCount = 1; poller.PollOnce();
        m.ShotCount = 2; poller.PollOnce();                      // LOT 생성, 검사 armed
        int tries = 0;
        r.FailBit = point => point == 33 && tries++ < 1;         // 첫 응답 쓰기만 실패
        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "OK");
        poller.PollOnce();                                       // 저장은 되고 응답은 실패

        Assert.Single(s.Inspections);
        Assert.DoesNotContain(r.BitWrites, w => w.Point == 33);
        Assert.Equal(1, poller.Status.RobotWriteFailures);

        poller.PollOnce();                                       // 저장 없이 응답만 다시 쓴다
        Assert.Single(s.Inspections);
        Assert.Contains(r.BitWrites, w => w.Point == 33 && w.On);
        Assert.Equal(0, poller.Status.RobotWriteFailures);
    }

    [Fact]
    public void Ng_mark_failure_after_save_does_not_save_the_inspection_again()
    {
        var (poller, m, r, s) = Build();
        m.ShotCount = 1; poller.PollOnce();
        m.ShotCount = 2; poller.PollOnce();
        s.FailMarkNg = true;
        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "NG");
        poller.PollOnce();                       // 저장 OK, NG 차단 실패
        s.FailMarkNg = false;
        poller.PollOnce();                       // 차단만 다시 한다

        Assert.Single(s.Inspections);
        Assert.Single(s.NgBlocked);
        Assert.Contains(r.BitWrites, w => w.Point == 33 && w.On);
    }

    [Fact]
    public void Save_that_committed_but_threw_is_not_saved_twice()
    {
        var (poller, m, r, s) = Build();
        m.ShotCount = 1; poller.PollOnce();
        m.ShotCount = 2; poller.PollOnce();
        s.ThrowAfterSave = true;
        for (int item = 0; item < 4; item++) r.SetJudgement(5007, item, "OK");
        poller.PollOnce();                       // 저장은 커밋됐는데 예외
        s.ThrowAfterSave = false;
        poller.PollOnce();                       // 이미 들어간 것으로 보고 다시 저장하지 않는다

        Assert.Single(s.Inspections);
        Assert.Contains(r.BitWrites, w => w.Point == 33 && w.On);
    }

    [Fact]
    public void Partial_cavity_failure_keeps_rh_in_second_slot()
    {
        var (poller, m, r, s) = Build();
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
        var (poller, m, r, s) = Build();
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
        var (poller, m, r, s) = Build();
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
        var (poller, m, _, s) = Build();
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
