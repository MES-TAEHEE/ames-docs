using AMES.InjAgent.Plc;

namespace AMES.InjAgent.Core;

/// <summary>호기 1대의 폴링 상태 스냅샷 (UI 표시용).</summary>
public sealed record MachineStatus(
    string EquipId, bool MachineConnected, bool RobotConnected,
    long? ShotCount, string MoldRaw, string? LotLh, string? LotRh, string LastInspection);

/// <summary>
/// 사출기 1호기 폴링 파이프라인. 원본 Main.cs tmrRunModbus 의
/// 샷 감지 → LOT 생성 → 로봇 송신 → 검사 수신 → 조건 수집 흐름을
/// 주입된 인터페이스 위에서 재구성한 것. PollOnce 단위로 단위테스트 가능.
/// </summary>
public sealed class MachinePoller : IDisposable
{
    private readonly MachineConfig     _cfg;
    private readonly IInjectionMachine _machine;
    private readonly IRobotLink        _robot;
    private readonly IInjAgentStore    _store;
    private readonly Action<string>    _log;

    private long?  _lastShotCount;
    private string _moldRaw = string.Empty;
    private readonly CavityLot?[] _slots = new CavityLot?[2];      // [0]=CavityNo1(LH/1st), [1]=CavityNo2(RH/2nd)
    private readonly bool[]    _inspectionArmed  = new bool[2];    // 샷 후 클리어(all-null) 관측 전까지 이전 사이클 잔존 판정 무시
    private readonly string?[] _lastInspectionKey = new string?[2];
    private readonly string[]  _inspectionText   = { "—", "—" };
    private volatile MachineStatus? _statusSnapshot;               // UI는 불변 스냅샷만 읽음 (C1)

    private sealed record CavityLot(int LotId, string LotCode, string ItemNo, string CavityPos);

    public MachinePoller(MachineConfig cfg, IInjectionMachine machine, IRobotLink robot,
                         IInjAgentStore store, Action<string> log)
    {
        _cfg = cfg;
        _machine = machine;
        _robot = robot;
        _store = store;
        _log = msg => log($"[{cfg.EquipId}] {msg}");
    }

    public MachineStatus Status => _statusSnapshot
        ?? new MachineStatus(_cfg.EquipId, false, false, null, string.Empty, null, null, "—");

    public void PollOnce()
    {
        try
        {
            bool machineOk = _machine.EnsureConnected();
            bool robotOk   = _robot.EnsureConnected() && _robot.RefreshBlock();
            if (!machineOk) return;

            long shot; string moldRaw;
            try
            {
                shot    = _machine.ReadShotCount();
                moldRaw = _machine.ReadMoldCode();
            }
            catch (Exception ex)
            {
                _log($"Machine read failed: {ex.Message}");
                return;
            }

            if (_lastShotCount is null)
            {
                _lastShotCount = shot;
                _moldRaw = moldRaw;
                _log($"baseline — shot count {shot}, mold '{moldRaw}' (no LOT created on start)");
                return;
            }

            // 원본의 3중 OR 중 취출신호(D5001) 조건은 의도적으로 제외 — 샷카운트 불변 상태에서
            // 반복 감지되어 중복 LOT 를 만들 위험이 있고, 시뮬레이터도 해당 비트를 쓰지 않는다.
            bool shotDetected = shot != _lastShotCount
                             || (moldRaw != _moldRaw && moldRaw.Length > 0);

            if (shotDetected)      HandleShot(shot, moldRaw, robotOk);
            else if (robotOk)      ResendToRobot();

            if (robotOk) HandleInspection();
        }
        finally
        {
            PublishSnapshot();
        }
    }

    /// <summary>STOP→START 대비 상태 초기화. 다음 PollOnce 는 baseline 만 잡는다 (LOT 미생성).</summary>
    public void ResetForRestart()
    {
        _lastShotCount = null;
        _moldRaw = string.Empty;
        Array.Clear(_slots);
        _lastInspectionKey[0] = _lastInspectionKey[1] = null;
        _inspectionArmed[0] = _inspectionArmed[1] = false;
        _inspectionText[0] = _inspectionText[1] = "—";
        PublishSnapshot();
    }

    /// <summary>STOP 시 소켓 해제 (재시작 가능 — Dispose 아님).</summary>
    public void DisconnectClients()
    {
        _machine.Disconnect();
        _robot.Disconnect();
        PublishSnapshot();
    }

    void PublishSnapshot()
        => _statusSnapshot = new MachineStatus(
            _cfg.EquipId, _machine.Connected, _robot.Connected,
            _lastShotCount, _moldRaw,
            _slots[0]?.LotCode, _slots[1]?.LotCode,
            $"{_inspectionText[0]} / {_inspectionText[1]}");

    void HandleShot(long shot, string moldRaw, bool robotOk)
    {
        _lastShotCount = shot;
        if (moldRaw.Length > 0) _moldRaw = moldRaw;
        _log($"Shot detected — count {shot}, mold '{_moldRaw}'");

        Array.Clear(_slots);
        _lastInspectionKey[0] = _lastInspectionKey[1] = null;
        _inspectionArmed[0] = _inspectionArmed[1] = false;

        // 조건수집은 LOT 생성과 독립 — 기준정보 미등록 호기도 조건 이력은 남긴다.
        CollectConditions(shot);

        var (mold, color) = PlcCodec.SplitMoldColor(_moldRaw);
        if (mold.Length == 0) { _log("No mold code — skipping LOT creation"); return; }

        List<Contracts.Dto.MoldItemDto> map;
        try { map = _store.GetMoldItems(mold, color); }
        catch (Exception ex) { _log($"Mold mapping lookup failed: {ex.Message} — LOT for this shot is lost (same policy as original, manual entry required)"); return; }
        if (map.Count == 0) { _log($"Mold code not found in master data: {mold}+{color} — no LOT created"); return; }

        foreach (var m in map.OrderBy(x => x.CavityNo))
        {
            int slot = m.CavityNo - 1;
            if (slot is < 0 or > 1) { _log($"CavityNo {m.CavityNo} out of range — ignored"); continue; }
            try
            {
                var (lotId, lotCode) = _store.CreateRawLot(_cfg.LineId, _cfg.EquipId, m, shot);
                _slots[slot] = new CavityLot(lotId, lotCode, m.ItemNo, m.CavityPos);
                _log($"{m.CavityPos} LOT created: {lotCode}");
            }
            catch (Exception ex)
            {
                // 해당 캐비티 LOT 유실 — 원본 에이전트와 동일하게 재시도하지 않는다 (수동 등록 필요).
                // 슬롯을 비워두므로 로봇 송신·검사 매핑이 다른 캐비티와 교차 오염되지 않는다.
                _log($"{m.CavityPos} LOT creation failed: {ex.Message}");
            }
        }

        if (robotOk) ResendToRobot();
    }

    /// <summary>로봇 미확인(D5002.0==0) 동안 금형/LOT/품번을 반복 송신 (원본 625-670행).</summary>
    void ResendToRobot()
    {
        if (_robot.ReadBit(5002, 0) != 0) return;
        if (_moldRaw.Length == 0) return;
        _robot.WriteString(5700, _moldRaw);
        if (_slots[0] is { } first)
        {
            _robot.WriteString(5100, first.LotCode);
            _robot.WriteString(5800, first.ItemNo);
        }
        if (_slots[1] is { } second)
        {
            _robot.WriteString(5200, second.LotCode);
            _robot.WriteString(5900, second.ItemNo);
        }
        _robot.WriteBit(32, true);
    }

    /// <summary>D5007(LH)/D5008(RH) 검사 4항목이 모두 도착하면 1회 저장. NG 는 스캔 차단.</summary>
    void HandleInspection()
    {
        for (int slot = 0; slot < 2; slot++)
        {
            var lot = _slots[slot];
            if (lot is null) continue;
            int word = slot == 0 ? 5007 : 5008;
            var results = ReadJudgements(word);
            bool allNull = results.All(r => r is null);

            // 샷 직후 이전 사이클 판정이 아직 유지 중일 수 있다 — 클리어(all-null)를 한 번
            // 관측한 뒤에 도착하는 판정만 이 LOT의 것으로 수용한다.
            if (!_inspectionArmed[slot])
            {
                if (allNull) _inspectionArmed[slot] = true;
                continue;
            }
            if (results.Any(r => r is null)) continue;

            string key = string.Join(',', results);
            if (_lastInspectionKey[slot] == key) continue;   // 같은 판정 유지 중 재저장 방지 (판정 변경 시 재검사로 보고 추가 저장 허용)

            bool ng = results.Contains("NG");
            try
            {
                _store.SaveInspection(lot.LotId, _cfg.EquipId, lot.CavityPos,
                                      results[0]!, results[1]!, results[2]!, results[3]!, ng);
                if (ng)
                {
                    _store.MarkNgBlocked(lot.LotId);
                    _log($"{lot.LotCode} inspection NG ({key}) → scan blocked");
                }
                else
                {
                    _log($"{lot.LotCode} inspection done: {key}");
                }
            }
            catch (Exception ex)
            {
                // 저장 실패 시 key/bit33 을 커밋하지 않는다 → 로봇이 판정을 유지하는 동안 다음 폴에서 재시도.
                _log($"Inspection save failed ({lot.LotCode}): {ex.Message} — will retry");
                continue;
            }
            _lastInspectionKey[slot] = key;
            _inspectionText[slot] = $"{lot.CavityPos}: {key}";
            _robot.WriteBit(33, true);
        }
    }

    /// <summary>항목 4종(미성형/웰드라인/가스/중량) × 3비트(OK/NG/PASS). 미도착 항목은 null.</summary>
    string?[] ReadJudgements(int word)
    {
        var results = new string?[4];
        for (int item = 0; item < 4; item++)
        {
            if      (_robot.ReadBit(word, item * 3)     == 1) results[item] = "OK";
            else if (_robot.ReadBit(word, item * 3 + 1) == 1) results[item] = "NG";
            else if (_robot.ReadBit(word, item * 3 + 2) == 1) results[item] = "PASS";
        }
        return results;
    }

    /// <summary>샷 시점 사출조건(세팅/실제) 수집 → PR_InjCondLog. 항목별 실패는 개별 로그.</summary>
    void CollectConditions(long shot)
    {
        List<Contracts.Dto.InjCondItemDto> items;
        try { items = _store.GetCondItems(_cfg.LineId); }
        catch (Exception ex) { _log($"Condition master lookup failed: {ex.Message}"); return; }

        foreach (var item in items)
        {
            try
            {
                decimal? setV = ReadCond(item.DataType, item.SetAddress);
                decimal? actV = ReadCond(item.DataType, item.ActualAddress);
                if (setV is null && actV is null) continue;
                _store.InsertCondLog(_cfg.LineId, item.ItemCode, shot, setV, actV);
            }
            catch (Exception ex)
            {
                _log($"Condition collection failed ({item.ItemCode}): {ex.Message}");
            }
        }
    }

    decimal? ReadCond(string dataType, int? address)
    {
        if (address is null) return null;
        return dataType == "FLOAT"
            ? (decimal)_machine.ReadFloat(address.Value)
            : _machine.ReadLong(address.Value);
    }

    public void Dispose()
    {
        (_machine as IDisposable)?.Dispose();
        (_robot as IDisposable)?.Dispose();
    }
}
